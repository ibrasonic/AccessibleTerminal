using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Collections.ObjectModel;

namespace AccessibleTerminal
{
    public class CommandExecutor : IDisposable
    {
        private Runspace? powerShellRunspace;
        private string currentDirectory;
        private bool disposed = false;
        private string? gitBashPath = null;

        public CommandExecutor()
        {
            currentDirectory = Environment.CurrentDirectory;
            InitializePowerShell();
            gitBashPath = FindGitBash();
        }

        private string? FindGitBash()
        {
            // Use bundled bash from bin/unix directory
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string bundledBash = Path.Combine(appDir, "bin", "unix", "bash.exe");
            
            if (File.Exists(bundledBash))
            {
                return bundledBash;
            }
            
            // Fallback to system Git Bash if bundled version not found
            string[] possiblePaths = new[]
            {
                @"C:\Program Files\Git\bin\bash.exe",
                @"C:\Program Files (x86)\Git\bin\bash.exe",
                @"C:\Program Files\Git\usr\bin\bash.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\Git\\bin\\bash.exe")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        private void InitializePowerShell()
        {
            try
            {
                // Create initial session state with default cmdlets
                InitialSessionState iss = InitialSessionState.CreateDefault2();
                powerShellRunspace = RunspaceFactory.CreateRunspace(iss);
                powerShellRunspace.Open();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize PowerShell: {ex.Message}");
                powerShellRunspace = null;
            }
        }

        public async Task<string> ExecuteAsync(string command, string shell)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return string.Empty;
            }

            if (disposed)
            {
                throw new ObjectDisposedException(nameof(CommandExecutor));
            }

            try
            {
                switch (shell)
                {
                    case "Bash":
                        return await ExecuteBashCommand(command);
                    
                    case "CMD":
                        return await ExecuteCmdCommand(command);
                    
                    case "PowerShell":
                    default:
                        return await ExecutePowerShellCommand(command);
                }
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return $"Error executing command: {ex.Message}";
            }
        }

        private async Task<string> ExecutePowerShellCommand(string command)
        {
            if (disposed) throw new ObjectDisposedException(nameof(CommandExecutor));

            return await Task.Run(() =>
            {
                try
                {
                    // Update working directory for cd commands
                    if (command.Trim().StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
                    {
                        string path = command.Substring(3).Trim().Trim('"', '\'');
                        if (Directory.Exists(path))
                        {
                            currentDirectory = Path.GetFullPath(path);
                            Environment.CurrentDirectory = currentDirectory;
                            return currentDirectory;
                        }
                        else if (path == ".." || path.StartsWith("..\\") || path.StartsWith("../"))
                        {
                            currentDirectory = Path.GetFullPath(Path.Combine(currentDirectory, path));
                            Environment.CurrentDirectory = currentDirectory;
                            return currentDirectory;
                    }
                }

                // If command starts with curl, use curl.exe directly to avoid PowerShell alias
                string effectiveCommand = command;
                if (command.Trim().StartsWith("curl ", StringComparison.OrdinalIgnoreCase))
                {
                    effectiveCommand = "curl.exe" + command.Substring(4);
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -Command \"Set-Location '{currentDirectory}'; $ProgressPreference='SilentlyContinue'; {effectiveCommand.Replace("\"", "`\"")}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = currentDirectory,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };                    using (Process process = new Process { StartInfo = startInfo })
                    {
                        process.Start();

                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();

                        bool exited = process.WaitForExit(60000); // 60 second timeout
                        
                        if (!exited)
                        {
                            try { process.Kill(); } catch { }
                            return "Command timed out after 60 seconds.";
                        }

                        // Update current directory if cd command was used
                        if (command.Trim().StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                currentDirectory = Directory.GetCurrentDirectory();
                            }
                            catch { }
                        }

                        // Only show stderr if the command failed or there's no stdout
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            string result = output.Trim();
                            
                            // Filter out curl progress output
                            var lines = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            var filteredLines = lines.Where(line =>
                            {
                                string trimmed = line.Trim();
                                return !trimmed.StartsWith("%") && 
                                       !trimmed.Contains("% Total") &&
                                       !trimmed.Contains("Dload  Upload") &&
                                       !System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+\s+\d+\s+\d+\s+\d+");
                            }).ToList();
                            
                            if (filteredLines.Any())
                            {
                                return string.Join(Environment.NewLine, filteredLines);
                            }
                            
                            return result;
                        }
                        else if (!string.IsNullOrWhiteSpace(error))
                        {
                            // If exit code is 0, stderr might just be progress/verbose info
                            if (process.ExitCode != 0)
                            {
                                return "ERROR: " + error.Trim();
                            }
                            return "";
                        }

                        return "";
                    }
                }
                catch (Exception ex)
                {
                    return $"PowerShell Error: {ex.Message}";
                }
            });
        }

        private async Task<string> ExecuteCmdCommand(string command)
        {
            if (disposed) throw new ObjectDisposedException(nameof(CommandExecutor));

            return await Task.Run(() =>
            {
                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {command}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = currentDirectory,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                    
                    // Set UTF-8 code page for CMD
                    startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                    using (Process process = new Process { StartInfo = startInfo })
                    {
                        process.Start();

                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();

                        bool exited = process.WaitForExit(60000);
                        
                        if (!exited)
                        {
                            try { process.Kill(); } catch { }
                            return "Command timed out after 60 seconds.";
                        }

                        // Only show stderr if the command failed (non-zero exit code) or there's no stdout
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            string result = output.Trim();
                            
                            // Filter out curl progress output
                            var lines = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            var filteredLines = lines.Where(line =>
                            {
                                string trimmed = line.Trim();
                                return !trimmed.StartsWith("%") && 
                                       !trimmed.Contains("% Total") &&
                                       !trimmed.Contains("Dload  Upload") &&
                                       !System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+\s+\d+\s+\d+\s+\d+");
                            }).ToList();
                            
                            if (filteredLines.Any())
                            {
                                return string.Join(Environment.NewLine, filteredLines);
                            }
                            
                            return result;
                        }
                        else if (!string.IsNullOrWhiteSpace(error))
                        {
                            // If exit code is 0, stderr might just be progress info, don't show it
                            if (process.ExitCode != 0)
                            {
                                return "ERROR: " + error.Trim();
                            }
                            return ""; // Command succeeded but produced no output
                        }

                        return "";
                    }
                }
                catch (Exception ex)
                {
                    return $"CMD Error: {ex.Message}";
                }
            });
        }

        private async Task<string> ExecuteBashCommand(string command)
        {
            if (disposed) throw new ObjectDisposedException(nameof(CommandExecutor));

            try
            {
                if (gitBashPath == null)
                {
                    return "Error: Bash binaries not found. Application may be corrupted.";
                }

                // Handle cd command to update working directory
                if (command.Trim().StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
                {
                    string path = command.Substring(3).Trim().Trim('"', '\'');
                    
                    // Convert ~ to user home directory
                    if (path.StartsWith("~"))
                    {
                        path = path.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                    }
                    
                    // Handle relative paths
                    if (!Path.IsPathRooted(path))
                    {
                        path = Path.GetFullPath(Path.Combine(currentDirectory, path));
                    }
                    
                    if (Directory.Exists(path))
                    {
                        currentDirectory = path;
                        Environment.CurrentDirectory = currentDirectory;
                        return currentDirectory;
                    }
                    else
                    {
                        return $"cd: {path}: No such file or directory";
                    }
                }

                // For Windows executables that might be in PATH, use cmd.exe directly
                // Check if this looks like a Windows command (not a Unix command from bin/unix)
                string commandName = command.Trim().Split(' ')[0].ToLower();
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string unixBinDir = Path.Combine(appDir, "bin", "unix");
                
                // List of known Unix commands that should run natively in bash
                string[] unixCommands = { "ls", "grep", "awk", "sed", "find", "cat", "head", "tail", 
                    "curl", "wget", "git", "ssh", "scp", "tar", "gzip", "gunzip", "zip", "unzip",
                    "chmod", "chown", "ps", "kill", "touch", "mkdir", "rm", "cp", "mv", "echo", "pwd" };
                
                bool isUnixCommand = unixCommands.Contains(commandName) || 
                                    File.Exists(Path.Combine(unixBinDir, commandName + ".exe"));
                
                if (!isUnixCommand)
                {
                    // Windows command - execute via CMD directly instead of through bash
                    return await ExecuteCmdCommand(command);
                }
                
                // Unix command - execute through bash
                string escapedCommand = command.Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");
                
                // Convert unix bin dir to Unix path format for PATH
                string unixBinPath = unixBinDir.Replace("\\", "/");
                if (unixBinPath.Length >= 2 && unixBinPath[1] == ':')
                {
                    char driveLetter = char.ToLower(unixBinPath[0]);
                    unixBinPath = "/cygdrive/" + driveLetter + unixBinPath.Substring(2);
                }
                
                // Convert current directory to Unix format (cygdrive format)
                string unixCwd = currentDirectory.Replace("\\", "/");
                if (unixCwd.Length >= 2 && unixCwd[1] == ':')
                {
                    char driveLetter = char.ToLower(unixCwd[0]);
                    unixCwd = "/cygdrive/" + driveLetter + unixCwd.Substring(2);
                }

                // Set up tmp directory
                string tempDir = Path.Combine(unixBinDir, "tmp");
                if (!Directory.Exists(tempDir))
                {
                    try { Directory.CreateDirectory(tempDir); } catch { }
                }
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = gitBashPath,
                        Arguments = $"-c \"export PATH='{unixBinPath}':$PATH && cd '{unixCwd}' && {escapedCommand}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                        WorkingDirectory = currentDirectory
                    }
                };
                
                // Set environment variables - PATH must include both unix bin and full system PATH
                string systemPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                // Keep Windows-style paths so Windows executables like 'code' work properly
                process.StartInfo.EnvironmentVariables["PATH"] = unixBinDir + Path.PathSeparator + systemPath;
                process.StartInfo.EnvironmentVariables["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                process.StartInfo.EnvironmentVariables["TMPDIR"] = tempDir;
                process.StartInfo.EnvironmentVariables["TEMP"] = tempDir;
                process.StartInfo.EnvironmentVariables["TMP"] = tempDir;
                process.StartInfo.EnvironmentVariables["LC_ALL"] = "C";
                process.StartInfo.EnvironmentVariables["LANG"] = "C";

                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                // Filter out bash warnings
                string result = output;
                if (!string.IsNullOrEmpty(error))
                {
                    var lines = error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var filteredLines = lines.Where(line => 
                        !line.Contains("could not find /tmp") && 
                        !line.Contains("warning: setlocale") &&
                        !line.Contains("bash.exe: warning"));
                    
                    string filteredError = string.Join(Environment.NewLine, filteredLines);
                    if (!string.IsNullOrWhiteSpace(filteredError))
                    {
                        result += (string.IsNullOrEmpty(result) ? "" : Environment.NewLine) + filteredError;
                    }
                }

                // Convert /cygdrive/ paths back to Windows format for display (with forward slashes)
                result = System.Text.RegularExpressions.Regex.Replace(result, 
                    @"/cygdrive/([a-z])((?:/[^\s]+)*)", 
                    m => m.Groups[1].Value.ToUpper() + ":" + m.Groups[2].Value);

                // Filter out curl progress bars and transfer statistics
                if (!string.IsNullOrEmpty(result))
                {
                    var lines = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var filteredLines = lines.Where(line =>
                    {
                        string trimmed = line.Trim();
                        // Filter curl progress lines that start with % or contain progress indicators
                        return !trimmed.StartsWith("%") && 
                               !trimmed.Contains("% Total") &&
                               !trimmed.Contains("Dload  Upload") &&
                               !System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+\s+\d+\s+\d+\s+\d+");
                    }).ToList();
                    
                    if (filteredLines.Any())
                    {
                        result = string.Join(Environment.NewLine, filteredLines);
                    }
                }

                // Return the result or indicate truly empty output
                if (string.IsNullOrWhiteSpace(result))
                {
                    // Check if command had an error
                    if (process.ExitCode != 0)
                    {
                        return $"Command failed with exit code {process.ExitCode}";
                    }
                    return ""; // Empty output is valid
                }
                
                return result;
            }
            catch (Exception ex)
            {
                return $"Error executing bash command: {ex.Message}";
            }
        }

        public bool IsBashAvailable()
        {
            return gitBashPath != null;
        }

        public string GetCurrentDirectory()
        {
            return currentDirectory;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    powerShellRunspace?.Dispose();
                }
                disposed = true;
            }
        }
    }
}
