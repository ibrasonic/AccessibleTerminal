using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace AccessibleTerminal
{
    // Output block for structural navigation
    public class OutputBlock
    {
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public string Command { get; set; } = string.Empty;
        public string Output { get; set; } = string.Empty;
        public bool IsError { get; set; }
        public bool IsFolded { get; set; }
        public string BlockId { get; set; } = Guid.NewGuid().ToString();
        public BlockType Type { get; set; }
    }

    public enum BlockType
    {
        Normal,
        Error,
        StackTrace,
        Json,
        LargeText,
        Heading
    }

    // Bookmark for marking important points
    public class Bookmark
    {
        public int Position { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
        public DateTime Created { get; set; } = DateTime.Now;
    }

    // Categorized output for panels
    public class CategorizedOutput
    {
        public string Content { get; set; } = string.Empty;
        public int Position { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Command { get; set; } = string.Empty;
    }

    public partial class MainWindow : Window
    {
        private bool isReadingMode = false;
        private bool isHighContrast = false;
        private double baseFontSize = 14;
        private const double MIN_FONT_SIZE = 8;
        private const double MAX_FONT_SIZE = 48;
        private int currentLine = 1;
        private int currentColumn = 1;
        private string currentPrompt = ">";
        private int promptStartPosition = 0;
        private readonly List<string> commandHistory = new List<string>();
        private const int MAX_HISTORY = 1000;
        private int historyIndex = -1;
        private CommandExecutor? commandExecutor;
        private string currentShell = "PowerShell";
        private readonly object lockObject = new object();

        // Output block tracking for navigation
        private readonly List<OutputBlock> outputBlocks = new List<OutputBlock>();
        private int currentBlockIndex = -1;

        // Folding state
        private readonly Dictionary<string, bool> foldedBlocks = new Dictionary<string, bool>();
        private const int MAX_OUTPUT_LINES_BEFORE_FOLD = 50;

        // Bookmarking system
        private readonly List<Bookmark> bookmarks = new List<Bookmark>();
        private int currentBookmarkIndex = -1;

        // Keyword monitoring
        private readonly HashSet<string> monitoredKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "error", "exception", "failed", "timeout", "warning", "success", "connected", "completed"
        };
        private bool pauseOnKeyword = false;

        // Output categorization
        private readonly List<CategorizedOutput> errors = new List<CategorizedOutput>();
        private readonly List<CategorizedOutput> warnings = new List<CategorizedOutput>();
        private readonly List<CategorizedOutput> statusMessages = new List<CategorizedOutput>();
        private const int MAX_CATEGORIZED_ITEMS = 500;

        public MainWindow()
        {
            InitializeComponent();
            InitializeTerminal();
            this.Closing += MainWindow_Closing;
            
            // Add text input handler for screen reader announcements
            TerminalTextBox.PreviewTextInput += TerminalTextBox_PreviewTextInput;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                commandExecutor?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cleanup error: {ex.Message}");
            }
        }

        private void TerminalTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Character typing - screen reader will announce naturally, no need for extra announcements
        }

        private void InitializeTerminal()
        {
            commandExecutor = new CommandExecutor();
            
            // Set initial text with welcome message
            StringBuilder welcomeMessage = new StringBuilder();
            welcomeMessage.AppendLine("╔═══════════════════════════════════════════════════════════════╗");
            welcomeMessage.AppendLine("║         ACCESSIBLE TERMINAL - Professional CLI v1.0           ║");
            welcomeMessage.AppendLine("╚═══════════════════════════════════════════════════════════════╝");
            welcomeMessage.AppendLine();
            welcomeMessage.AppendLine("Screen Reader Support: ENABLED");
            welcomeMessage.AppendLine("Unix Commands: SUPPORTED");
            welcomeMessage.AppendLine();
            welcomeMessage.AppendLine("Essential Shortcuts:");
            welcomeMessage.AppendLine("  Ctrl+R    - Reading Mode     Ctrl+L - Clear Screen");
            welcomeMessage.AppendLine("  Ctrl+H    - High Contrast    Tab    - Auto-complete");
            welcomeMessage.AppendLine("  Up/Down   - Command History");
            welcomeMessage.AppendLine();
            welcomeMessage.AppendLine("Type 'help' for full list of commands and shortcuts.");
            welcomeMessage.AppendLine("Type 'bash' to switch to bash mode for Unix commands.");
            welcomeMessage.AppendLine();
            welcomeMessage.Append(currentPrompt);

            TerminalTextBox.Text = welcomeMessage.ToString();
            promptStartPosition = TerminalTextBox.Text.Length;
            TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
            TerminalTextBox.Focus();

            // Announce to screen reader
            AnnounceToScreenReader("Accessible Terminal initialized. Type commands and press Enter to execute.");
        }

        private void TerminalTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Handle all Ctrl shortcuts first
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.R:
                        e.Handled = true;
                        ToggleReadingMode();
                        return;
                    
                    case Key.L:
                        e.Handled = true;
                        ClearScreen();
                        return;
                    
                    case Key.H:
                        e.Handled = true;
                        ToggleHighContrast();
                        return;
                    
                    case Key.N:
                        // Ctrl+N: Jump to next output block
                        e.Handled = true;
                        NavigateToNextBlock();
                        return;
                    
                    case Key.P:
                        // Ctrl+P: Jump to previous output block
                        e.Handled = true;
                        NavigateToPreviousBlock();
                        return;
                    
                    case Key.E:
                        // Ctrl+E: Jump to next error
                        e.Handled = true;
                        NavigateToNextError();
                        return;
                    
                    case Key.F:
                        // Ctrl+F: Toggle fold current block
                        e.Handled = true;
                        ToggleFoldCurrentBlock();
                        return;
                    
                    case Key.D:
                        // Ctrl+D: Unfold all blocks
                        e.Handled = true;
                        UnfoldAllBlocks();
                        return;
                    
                    case Key.B:
                        // Ctrl+B: Add bookmark at current position
                        e.Handled = true;
                        AddBookmark();
                        return;
                    
                    case Key.J:
                        // Ctrl+J: Jump to next bookmark
                        e.Handled = true;
                        JumpToNextBookmark();
                        return;
                    
                    case Key.K:
                        // Ctrl+K: Jump to previous bookmark
                        e.Handled = true;
                        JumpToPreviousBookmark();
                        return;
                    
                    case Key.M:
                        // Ctrl+M: Manage monitored keywords
                        e.Handled = true;
                        ShowKeywordManager();
                        return;
                    
                    case Key.O:
                        // Ctrl+O: Show output panels (errors, warnings, status)
                        e.Handled = true;
                        ShowOutputPanels();
                        return;
                    
                    case Key.Add:
                    case Key.OemPlus:
                        e.Handled = true;
                        IncreaseFontSize();
                        return;
                    
                    case Key.Subtract:
                    case Key.OemMinus:
                        e.Handled = true;
                        DecreaseFontSize();
                        return;
                    
                    case Key.D0:
                    case Key.NumPad0:
                        e.Handled = true;
                        ResetFontSize();
                        return;
                }
            }

            if (isReadingMode)
            {
                HandleReadingModeKeys(e);
                return;
            }

            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    ExecuteCommand();
                    break;

                case Key.Up:
                    e.Handled = true;
                    NavigateHistory(-1);
                    break;

                case Key.Down:
                    e.Handled = true;
                    NavigateHistory(1);
                    break;

                case Key.Back:
                    // Prevent backspace before or at prompt position
                    if (TerminalTextBox.SelectionStart <= promptStartPosition || 
                        TerminalTextBox.CaretIndex <= promptStartPosition)
                    {
                        e.Handled = true;
                        AnnounceToScreenReader("Cannot delete prompt");
                    }
                    else
                    {
                        // Announce character deletion for screen readers
                        if (TerminalTextBox.CaretIndex > promptStartPosition)
                        {
                            int deletePos = TerminalTextBox.CaretIndex - 1;
                            if (deletePos < TerminalTextBox.Text.Length)
                            {
                                string deletedChar = TerminalTextBox.Text[deletePos].ToString();
                                AnnounceToScreenReader($"Deleted {deletedChar}");
                            }
                        }
                    }
                    break;

                case Key.Delete:
                    // Prevent delete at or before prompt position
                    if (TerminalTextBox.CaretIndex < promptStartPosition)
                    {
                        e.Handled = true;
                        AnnounceToScreenReader("Cannot delete prompt");
                    }
                    break;

                case Key.Left:
                    // Prevent moving before prompt
                    if (TerminalTextBox.CaretIndex <= promptStartPosition)
                    {
                        e.Handled = true;
                    }
                    break;

                case Key.Home:
                    // Move to start of command, not start of line
                    e.Handled = true;
                    TerminalTextBox.CaretIndex = promptStartPosition;
                    break;

                case Key.Tab:
                    e.Handled = true;
                    AutoCompleteCommand();
                    break;

                case Key.C:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        if (TerminalTextBox.SelectedText.Length > 0)
                        {
                            try
                            {
                                Clipboard.SetText(TerminalTextBox.SelectedText);
                                AnnounceToScreenReader("Text copied to clipboard");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Copy error: {ex.Message}");
                            }
                            e.Handled = true;
                        }
                    }
                    break;

                case Key.V:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        try
                        {
                            // Allow paste only after prompt
                            if (TerminalTextBox.CaretIndex < promptStartPosition)
                            {
                                TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                            }
                            if (Clipboard.ContainsText())
                            {
                                string pasteText = Clipboard.GetText();
                                TerminalTextBox.SelectedText = pasteText;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Paste error: {ex.Message}");
                        }
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void HandleReadingModeKeys(KeyEventArgs e)
        {
            // In reading mode, allow full navigation and selection
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.C:
                        if (TerminalTextBox.SelectedText.Length > 0)
                        {
                            try
                            {
                                Clipboard.SetText(TerminalTextBox.SelectedText);
                                AnnounceToScreenReader($"Copied {TerminalTextBox.SelectedText.Length} characters");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Copy error: {ex.Message}");
                            }
                        }
                        e.Handled = true;
                        return;

                    case Key.A:
                        TerminalTextBox.SelectAll();
                        AnnounceToScreenReader("All text selected");
                        e.Handled = true;
                        return;

                    case Key.R:
                        // Let the main handler toggle reading mode
                        return;

                    case Key.Left:
                    case Key.Right:
                    case Key.Home:
                    case Key.End:
                        // Allow Ctrl+Arrow navigation
                        return;
                }
            }

            // Allow navigation keys
            switch (e.Key)
            {
                case Key.Left:
                case Key.Right:
                case Key.Up:
                case Key.Down:
                case Key.Home:
                case Key.End:
                case Key.PageUp:
                case Key.PageDown:
                    // Allow navigation and scroll to caret after key is processed
                    e.Handled = false;
                    Dispatcher.InvokeAsync(() => BringCaretIntoView(), System.Windows.Threading.DispatcherPriority.Background);
                    return;
                
                case Key.LeftShift:
                case Key.RightShift:
                case Key.LeftCtrl:
                case Key.RightCtrl:
                case Key.LeftAlt:
                case Key.RightAlt:
                case Key.Tab:
                    // Allow these keys for navigation
                    return;

                default:
                    // Block all other input in reading mode
                    e.Handled = true;
                    break;
            }
        }

        private bool IsNavigationKey(Key key)
        {
            return key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down ||
                   key == Key.Home || key == Key.End || key == Key.PageUp || key == Key.PageDown ||
                   key == Key.LeftShift || key == Key.RightShift || key == Key.LeftCtrl || key == Key.RightCtrl;
        }

        private async void ExecuteCommand()
        {
            try
            {
                if (promptStartPosition > TerminalTextBox.Text.Length)
                {
                    promptStartPosition = TerminalTextBox.Text.Length;
                }

                string command = TerminalTextBox.Text.Substring(promptStartPosition).Trim();
                
                if (string.IsNullOrWhiteSpace(command))
                {
                    TerminalTextBox.AppendText(Environment.NewLine);
                    TerminalTextBox.AppendText(currentPrompt);
                    promptStartPosition = TerminalTextBox.Text.Length;
                    TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                    return;
                }

                // Handle sudo su - escalate to administrator
                if (command.Trim().Equals("sudo su", StringComparison.OrdinalIgnoreCase) || 
                    command.Trim().Equals("sudo", StringComparison.OrdinalIgnoreCase))
                {
                    EscalateToAdministrator();
                    return;
                }

                // Add to history with limit
                lock (lockObject)
                {
                    if (commandHistory.Count == 0 || commandHistory[commandHistory.Count - 1] != command)
                    {
                        commandHistory.Add(command);
                        if (commandHistory.Count > MAX_HISTORY)
                        {
                            commandHistory.RemoveAt(0);
                        }
                    }
                    historyIndex = commandHistory.Count;
                }

                // Handle built-in commands
                if (HandleBuiltInCommand(command))
                {
                    return;
                }

                // Handle monitor commands
                if (HandleMonitorCommand(command))
                {
                    return;
                }

                // Move to new line before executing
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.ScrollToEnd();
                
                try
                {
                    string output = await commandExecutor!.ExecuteAsync(command, currentShell);
                    
                    // Debug output
                    if (string.IsNullOrEmpty(output))
                    {
                        output = "(Command produced no output)";
                    }
                    
                    AppendOutput(output, false);
                }
                catch (Exception ex)
                {
                    AppendOutput($"Error executing command: {ex.Message}", true);
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"Command error: {ex.Message}", true);
            }
        }

        private bool HandleBuiltInCommand(string command)
        {
            string cmd = command.ToLower().Trim();

            switch (cmd)
            {
                case "clear":
                case "cls":
                    ClearScreen();
                    return true;

                case "help":
                    ShowHelp();
                    return true;

                case "bash":
                    if (currentShell == "Bash")
                    {
                        TerminalTextBox.AppendText(Environment.NewLine);
                        TerminalTextBox.AppendText("Already in Bash mode.");
                        TerminalTextBox.AppendText(Environment.NewLine);
                        TerminalTextBox.AppendText(currentPrompt);
                        promptStartPosition = TerminalTextBox.Text.Length;
                        TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                        AnnounceToScreenReader("Already in Bash mode.");
                        return true;
                    }

                    // Check if Git Bash is available before switching
                    if (commandExecutor?.IsBashAvailable() != true)
                    {
                        TerminalTextBox.AppendText(Environment.NewLine);
                        TerminalTextBox.AppendText("ERROR: Bash is not available.");
                        TerminalTextBox.AppendText(Environment.NewLine);
                        TerminalTextBox.AppendText("Bash binaries not found in application directory.");
                        TerminalTextBox.AppendText(Environment.NewLine);
                        TerminalTextBox.AppendText(currentPrompt);
                        promptStartPosition = TerminalTextBox.Text.Length;
                        TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                        AnnounceToScreenReader("Bash not available. Cannot switch to Bash mode.");
                        return true;
                    }

                    currentShell = "Bash";
                    ShellStatusText.Text = "Shell: Bash (Git)";
                    TerminalTextBox.AppendText(Environment.NewLine);
                    TerminalTextBox.AppendText("Switched to Bash mode. Unix commands will run natively on Windows.");
                    TerminalTextBox.AppendText(Environment.NewLine);
                    TerminalTextBox.AppendText(currentPrompt);
                    promptStartPosition = TerminalTextBox.Text.Length;
                    TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                    AnnounceToScreenReader("Switched to Bash shell mode");
                    return true;

                case "powershell":
                case "pwsh":
                    if (currentShell == "PowerShell")
                    {
                        TerminalTextBox.AppendText(Environment.NewLine);
                        TerminalTextBox.AppendText("Already in PowerShell mode.");
                        TerminalTextBox.AppendText(Environment.NewLine);
                        TerminalTextBox.AppendText(currentPrompt);
                        promptStartPosition = TerminalTextBox.Text.Length;
                        TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                        AnnounceToScreenReader("Already in PowerShell mode.");
                        return true;
                    }
                    currentShell = "PowerShell";
                    ShellStatusText.Text = "Shell: PowerShell";
                    TerminalTextBox.AppendText(Environment.NewLine);
                    TerminalTextBox.AppendText("Switched to PowerShell mode.");
                    TerminalTextBox.AppendText(Environment.NewLine);
                    TerminalTextBox.AppendText(currentPrompt);
                    promptStartPosition = TerminalTextBox.Text.Length;
                    TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                    AnnounceToScreenReader("Switched to PowerShell mode");
                    return true;

                case "cmd":
                    if (currentShell == "CMD")
                    {
                        TerminalTextBox.AppendText(Environment.NewLine);
                        TerminalTextBox.AppendText("Already in CMD mode.");
                        TerminalTextBox.AppendText(Environment.NewLine);
                        TerminalTextBox.AppendText(currentPrompt);
                        promptStartPosition = TerminalTextBox.Text.Length;
                        TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                        AnnounceToScreenReader("Already in CMD mode.");
                        return true;
                    }
                    currentShell = "CMD";
                    ShellStatusText.Text = "Shell: CMD";
                    TerminalTextBox.AppendText(Environment.NewLine);
                    TerminalTextBox.AppendText("Switched to CMD mode.");
                    TerminalTextBox.AppendText(Environment.NewLine);
                    TerminalTextBox.AppendText(currentPrompt);
                    promptStartPosition = TerminalTextBox.Text.Length;
                    TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                    AnnounceToScreenReader("Switched to CMD mode");
                    return true;

                case "exit":
                case "quit":
                    Application.Current.Shutdown();
                    return true;

                default:
                    return false;
            }
        }

        private void ShowHelp()
        {
            StringBuilder helpText = new StringBuilder();
            
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine();
            
            TerminalTextBox.AppendText("═══════════════════════════════════════════════════════════════");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("═══════════════════════════════════════════════════════════════");
            
            TerminalTextBox.AppendText("                    ACCESSIBLE TERMINAL HELP");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("                    ACCESSIBLE TERMINAL HELP");
            
            TerminalTextBox.AppendText("═══════════════════════════════════════════════════════════════");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("═══════════════════════════════════════════════════════════════");
            
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine();
            
            TerminalTextBox.AppendText("BUILT-IN COMMANDS");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("BUILT-IN COMMANDS");
            
            TerminalTextBox.AppendText("─────────────────");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("─────────────────");
            
            TerminalTextBox.AppendText("  help          Show this help message");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  help          Show this help message");
            
            TerminalTextBox.AppendText("  clear, cls    Clear the terminal screen");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  clear, cls    Clear the terminal screen");
            
            TerminalTextBox.AppendText("  bash          Switch to Bash/Unix mode");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  bash          Switch to Bash/Unix mode");
            
            TerminalTextBox.AppendText("  powershell    Switch to PowerShell mode");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  powershell    Switch to PowerShell mode");
            
            TerminalTextBox.AppendText("  cmd           Switch to CMD mode");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  cmd           Switch to CMD mode");
            
            TerminalTextBox.AppendText("  sudo su       Restart as Administrator (UAC prompt)");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  sudo su       Restart as Administrator (UAC prompt)");
            
            TerminalTextBox.AppendText("  exit, quit    Exit the terminal");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  exit, quit    Exit the terminal");
            
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine();
            
            TerminalTextBox.AppendText("KEYBOARD SHORTCUTS");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("KEYBOARD SHORTCUTS");
            
            TerminalTextBox.AppendText("──────────────────");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("──────────────────");
            
            TerminalTextBox.AppendText("Basic Navigation:");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("Basic Navigation:");
            
            TerminalTextBox.AppendText("  Up/Down       Navigate command history");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Up/Down       Navigate command history");
            
            TerminalTextBox.AppendText("  Tab           Auto-complete paths and commands");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Tab           Auto-complete paths and commands");
            
            TerminalTextBox.AppendText("  Home          Jump to start of current command");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Home          Jump to start of current command");
            
            TerminalTextBox.AppendText("  Ctrl+C        Copy selected text");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+C        Copy selected text");
            
            TerminalTextBox.AppendText("  Ctrl+V        Paste text");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+V        Paste text");
            
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine();
            
            TerminalTextBox.AppendText("Display & Appearance:");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("Display & Appearance:");
            
            TerminalTextBox.AppendText("  Ctrl+R        Toggle Reading Mode (arrow key navigation)");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+R        Toggle Reading Mode (arrow key navigation)");
            
            TerminalTextBox.AppendText("  Ctrl+H        Toggle High Contrast mode");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+H        Toggle High Contrast mode");
            
            TerminalTextBox.AppendText("  Ctrl+L        Clear screen");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+L        Clear screen");
            
            TerminalTextBox.AppendText("  Ctrl+Plus     Increase font size");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+Plus     Increase font size");
            
            TerminalTextBox.AppendText("  Ctrl+Minus    Decrease font size");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+Minus    Decrease font size");
            
            TerminalTextBox.AppendText("  Ctrl+0        Reset font size to default");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+0        Reset font size to default");
            
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine();
            
            TerminalTextBox.AppendText("Output Navigation (Screen Reader Optimized):");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("Output Navigation (Screen Reader Optimized):");
            
            TerminalTextBox.AppendText("  Ctrl+N        Jump to Next output block");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+N        Jump to Next output block");
            
            TerminalTextBox.AppendText("  Ctrl+P        Jump to Previous output block");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+P        Jump to Previous output block");
            
            TerminalTextBox.AppendText("  Ctrl+E        Jump to Next Error");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+E        Jump to Next Error");
            
            TerminalTextBox.AppendText("  Ctrl+F        Toggle Fold/Expand current block");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+F        Toggle Fold/Expand current block");
            
            TerminalTextBox.AppendText("  Ctrl+D        Unfold all blocks");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+D        Unfold all blocks");
            
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine();
            
            TerminalTextBox.AppendText("Bookmarks & Monitoring:");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("Bookmarks & Monitoring:");
            
            TerminalTextBox.AppendText("  Ctrl+B        Add Bookmark at current position");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+B        Add Bookmark at current position");
            
            TerminalTextBox.AppendText("  Ctrl+J        Jump to Next bookmark");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+J        Jump to Next bookmark");
            
            TerminalTextBox.AppendText("  Ctrl+K        List all Bookmarks and Keywords");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+K        Jump to Previous bookmark");
            
            TerminalTextBox.AppendText("  Ctrl+M        Manage Monitored keywords");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+M        Manage Monitored keywords");
            
            TerminalTextBox.AppendText("  Ctrl+O        Show Output panels (errors/warnings/status)");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ctrl+O        Show Output panels (errors/warnings/status)");
            
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine();
            
            TerminalTextBox.AppendText("ACCESSIBILITY FEATURES");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("ACCESSIBILITY FEATURES");
            
            TerminalTextBox.AppendText("──────────────────────");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("──────────────────────");
            
            TerminalTextBox.AppendText("  • Full screen reader support with live announcements");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • Full screen reader support with live announcements");
            
            TerminalTextBox.AppendText("  • Structural navigation through output blocks");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • Structural navigation through output blocks");
            
            TerminalTextBox.AppendText("  • Smart output folding (auto-folds blocks >50 lines)");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • Smart output folding (auto-folds blocks >50 lines)");
            
            TerminalTextBox.AppendText("  • Output bookmarking for important points");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • Output bookmarking for important points");
            
            TerminalTextBox.AppendText("  • Keyword monitoring with auto-announcement");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • Keyword monitoring with auto-announcement");
            
            TerminalTextBox.AppendText("  • Categorized output panels (errors/warnings/status)");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • Categorized output panels (errors/warnings/status)");
            
            TerminalTextBox.AppendText("  • Reading mode for document-like navigation");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • Reading mode for document-like navigation");
            
            TerminalTextBox.AppendText("  • High contrast themes");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • High contrast themes");
            
            TerminalTextBox.AppendText("  • Adjustable font sizes");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • Adjustable font sizes");
            
            TerminalTextBox.AppendText("  • Complete keyboard-only operation");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • Complete keyboard-only operation");
            
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine();
            
            TerminalTextBox.AppendText("SHELL SUPPORT");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("SHELL SUPPORT");
            
            TerminalTextBox.AppendText("─────────────");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("─────────────");
            
            TerminalTextBox.AppendText("  • PowerShell - Windows PowerShell with full cmdlet support");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • PowerShell - Windows PowerShell with full cmdlet support");
            
            TerminalTextBox.AppendText("  • CMD - Windows Command Prompt");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • CMD - Windows Command Prompt");
            
            TerminalTextBox.AppendText("  • Bash - Unix/Linux commands with bundled binaries");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • Bash - Unix/Linux commands with bundled binaries");
            
            TerminalTextBox.AppendText("  • Tab autocomplete uses forward slashes in Bash mode");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • Tab autocomplete uses forward slashes in Bash mode");
            
            TerminalTextBox.AppendText("  • Windows executables accessible from all shell modes");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  • Windows executables accessible from all shell modes");
            
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine();
            
            TerminalTextBox.AppendText("DEVELOPER");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("DEVELOPER");
            
            TerminalTextBox.AppendText("─────────");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("─────────");
            
            TerminalTextBox.AppendText("  Ibrahim Badawy (@IBRASONIC)");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Ibrahim Badawy (@IBRASONIC)");
            
            TerminalTextBox.AppendText("  Email: ibrahim.m.badawy@gmail.com");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  Email: ibrahim.m.badawy@gmail.com");
            
            TerminalTextBox.AppendText("  GitHub: https://github.com/ibrasonic");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("  GitHub: https://github.com/ibrasonic");
            
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine();
            
            TerminalTextBox.AppendText("═══════════════════════════════════════════════════════════════");
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine("═══════════════════════════════════════════════════════════════");
            
            TerminalTextBox.AppendText(Environment.NewLine);
            helpText.AppendLine();
            
            TerminalTextBox.AppendText(currentPrompt);
            TerminalTextBox.AppendText(" ");
            promptStartPosition = TerminalTextBox.Text.Length;
            TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
            ScrollViewer.ScrollToEnd();
            
            // Announce the entire help text to screen reader
            AnnounceToScreenReader(helpText.ToString());
        }

        private void AppendOutput(string output, bool isError = false, bool addNewlineBefore = true)
        {
            try
            {
                lock (lockObject)
                {
                    // Prevent terminal from growing too large
                    if (TerminalTextBox.Text.Length > 500000)
                    {
                        int keepLength = 250000;
                        TerminalTextBox.Text = "... (output truncated for memory)\n" + TerminalTextBox.Text.Substring(TerminalTextBox.Text.Length - keepLength);
                        promptStartPosition = TerminalTextBox.Text.Length;
                        
                        // Clear ALL position-dependent tracking
                        outputBlocks.Clear();
                        bookmarks.Clear();
                        currentBlockIndex = -1;
                        currentBookmarkIndex = -1;
                        foldedBlocks.Clear();
                        
                        // Clear categorized outputs as positions are now invalid
                        errors.Clear();
                        warnings.Clear();
                        statusMessages.Clear();
                    }

                    if (!string.IsNullOrEmpty(output))
                    {
                        // Remove trailing and leading whitespace but keep content
                        output = output.Trim();
                        
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            int startPos = TerminalTextBox.Text.Length;
                            
                            // Detect block type and possibly fold
                            BlockType blockType = DetectBlockType(output);
                            int lineCount = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                            
                            // Create output block for navigation
                            OutputBlock block = new OutputBlock
                            {
                                StartPosition = startPos,
                                Command = GetLastCommand(),
                                Output = output,
                                IsError = isError,
                                Type = blockType
                            };

                            // Auto-fold large outputs
                            if (lineCount > MAX_OUTPUT_LINES_BEFORE_FOLD || blockType == BlockType.StackTrace || blockType == BlockType.LargeText)
                            {
                                string summary = GetOutputSummary(output, blockType);
                                TerminalTextBox.AppendText($"[Folded {lineCount} lines - {blockType}] {summary}\n");
                                TerminalTextBox.AppendText($"(Press Ctrl+F to expand, Ctrl+N/P to navigate blocks)\n");
                                block.IsFolded = true;
                                foldedBlocks[block.BlockId] = true;
                                
                                // Announce folded content
                                AnnounceToScreenReader($"Output folded: {lineCount} lines of {blockType}. {summary}");
                            }
                            else
                            {
                                TerminalTextBox.AppendText(output);
                                TerminalTextBox.AppendText(Environment.NewLine);
                                
                                // Monitor keywords and categorize output
                                MonitorKeywords(output);
                                CategorizeOutput(output, isError);
                                
                                // Announce entire output directly to screen reader
                                AnnounceToScreenReader(output);
                            }

                            block.EndPosition = TerminalTextBox.Text.Length;
                            outputBlocks.Add(block);
                        }
                    }

                    // Add prompt with space
                    TerminalTextBox.AppendText(currentPrompt);
                    TerminalTextBox.AppendText(" ");
                    promptStartPosition = TerminalTextBox.Text.Length;
                    TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                    TerminalTextBox.Focus();
                    ScrollViewer.ScrollToEnd();

                    if (isError)
                    {
                        AnnounceToScreenReader($"Command failed");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppendOutput error: {ex.Message}");
            }
        }

        private void NavigateHistory(int direction)
        {
            try
            {
                lock (lockObject)
                {
                    if (commandHistory.Count == 0) return;

                    historyIndex += direction;
                    historyIndex = Math.Max(0, Math.Min(historyIndex, commandHistory.Count));

                    // Clear current command
                    if (promptStartPosition <= TerminalTextBox.Text.Length)
                    {
                        TerminalTextBox.Text = TerminalTextBox.Text.Substring(0, promptStartPosition);
                    }

                    if (historyIndex < commandHistory.Count)
                    {
                        string historicalCommand = commandHistory[historyIndex];
                        TerminalTextBox.AppendText(historicalCommand);
                        AnnounceToScreenReader($"History: {historicalCommand}");
                    }
                    else
                    {
                        AnnounceToScreenReader("No more history");
                    }

                    TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"History navigation error: {ex.Message}");
            }
        }

        private void ClearScreen()
        {
            try
            {
                lock (lockObject)
                {
                    TerminalTextBox.Clear();
                    TerminalTextBox.Text = currentPrompt + " ";
                    promptStartPosition = TerminalTextBox.Text.Length;
                    TerminalTextBox.CaretIndex = promptStartPosition;
                    TerminalTextBox.Focus();
                    ScrollViewer.ScrollToTop();
                    outputBlocks.Clear(); // Clear navigation blocks
                    currentBlockIndex = -1;
                    foldedBlocks.Clear();
                    bookmarks.Clear(); // Clear bookmarks
                    currentBookmarkIndex = -1;
                    errors.Clear(); // Clear categorized outputs
                    warnings.Clear();
                    statusMessages.Clear();
                    AnnounceToScreenReader("Screen cleared");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Clear screen error: {ex.Message}");
            }
        }

        private void NavigateToNextBlock()
        {
            lock (lockObject)
            {
                if (outputBlocks.Count == 0)
                {
                    AnnounceToScreenReader("No output blocks to navigate");
                    return;
                }

                currentBlockIndex++;
                if (currentBlockIndex >= outputBlocks.Count)
                {
                    currentBlockIndex = 0;
                    AnnounceToScreenReader("Wrapped to first block");
                }

                JumpToBlock(currentBlockIndex);
            }
        }

        private void NavigateToPreviousBlock()
        {
            lock (lockObject)
            {
                if (outputBlocks.Count == 0)
                {
                    AnnounceToScreenReader("No output blocks to navigate");
                    return;
                }

                currentBlockIndex--;
                if (currentBlockIndex < 0)
                {
                    currentBlockIndex = outputBlocks.Count - 1;
                    AnnounceToScreenReader("Wrapped to last block");
                }

                JumpToBlock(currentBlockIndex);
            }
        }

        private void NavigateToNextError()
        {
            lock (lockObject)
            {
                if (outputBlocks.Count == 0)
                {
                    AnnounceToScreenReader("No output blocks");
                    return;
                }

                int startIndex = currentBlockIndex + 1;
                for (int i = 0; i < outputBlocks.Count; i++)
                {
                    int index = (startIndex + i) % outputBlocks.Count;
                    if (outputBlocks[index].IsError || outputBlocks[index].Type == BlockType.Error)
                    {
                        currentBlockIndex = index;
                        JumpToBlock(index);
                        return;
                    }
                }

                AnnounceToScreenReader("No error blocks found");
            }
        }

        private void JumpToBlock(int index)
        {
            if (index < 0 || index >= outputBlocks.Count) return;

            OutputBlock block = outputBlocks[index];
            
            // Validate positions are within bounds
            if (block.StartPosition >= TerminalTextBox.Text.Length)
            {
                AnnounceToScreenReader("Block position is invalid. Text has changed.");
                return;
            }
            
            TerminalTextBox.CaretIndex = Math.Min(block.StartPosition, TerminalTextBox.Text.Length - 1);
            TerminalTextBox.ScrollToLine(TerminalTextBox.GetLineIndexFromCharacterIndex(block.StartPosition));

            // Select the block for visibility
            int length = Math.Min(block.EndPosition - block.StartPosition, TerminalTextBox.Text.Length - block.StartPosition);
            if (length > 0 && block.StartPosition + length <= TerminalTextBox.Text.Length)
            {
                TerminalTextBox.Select(block.StartPosition, length);
            }

            // Announce block info
            string status = block.IsFolded ? "folded" : "expanded";
            string errorStatus = block.IsError ? "error" : "normal";
            AnnounceToScreenReader($"Block {index + 1} of {outputBlocks.Count}, {status}, {errorStatus}, type: {block.Type}");
        }

        private void ToggleFoldCurrentBlock()
        {
            lock (lockObject)
            {
                if (currentBlockIndex < 0 || currentBlockIndex >= outputBlocks.Count)
                {
                    AnnounceToScreenReader("No block selected. Use Ctrl+N to navigate to a block first.");
                    return;
                }

                OutputBlock block = outputBlocks[currentBlockIndex];
                block.IsFolded = !block.IsFolded;
                foldedBlocks[block.BlockId] = block.IsFolded;

                // Rebuild display
                RebuildTerminalDisplay();

                if (block.IsFolded)
                {
                    int lineCount = block.Output?.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length ?? 0;
                    AnnounceToScreenReader($"Block folded. {lineCount} lines of {block.Type} output collapsed. Press Ctrl+F to expand.");
                }
                else
                {
                    AnnounceToScreenReader($"Block expanded. {block.Type} output is now visible. Use arrow keys to read.");
                }
            }
        }

        private void UnfoldAllBlocks()
        {
            lock (lockObject)
            {
                int foldedCount = outputBlocks.Count(b => b.IsFolded);
                
                if (foldedCount == 0)
                {
                    AnnounceToScreenReader("No folded blocks. All output is already expanded.");
                    return;
                }
                
                foreach (var block in outputBlocks)
                {
                    block.IsFolded = false;
                }
                foldedBlocks.Clear();

                RebuildTerminalDisplay();
                AnnounceToScreenReader($"All blocks expanded. {foldedCount} folded blocks are now visible. Total {outputBlocks.Count} blocks.");
            }
        }

        private void RebuildTerminalDisplay()
        {
            try
            {
                lock (lockObject)
                {
                    // Save current user input
                    string currentInput = "";
                    if (promptStartPosition < TerminalTextBox.Text.Length)
                    {
                        currentInput = TerminalTextBox.Text.Substring(promptStartPosition);
                    }

                    // Rebuild the entire terminal display with current fold states
                    StringBuilder newText = new StringBuilder();
                    
                    foreach (var block in outputBlocks)
                    {
                        if (block == null) continue;
                        
                        // Update block start position
                        block.StartPosition = newText.Length;
                        
                        if (block.IsFolded)
                        {
                            int lineCount = block.Output?.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length ?? 0;
                            string summary = GetOutputSummary(block.Output ?? string.Empty, block.Type);
                            newText.AppendLine($"[Folded {lineCount} lines - {block.Type}] {summary}");
                            newText.AppendLine("(Press Ctrl+F to expand)");
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(block.Output))
                            {
                                newText.AppendLine(block.Output);
                            }
                        }
                        
                        // Update block end position
                        block.EndPosition = newText.Length;
                    }

                    // Add prompt and restore user input
                    newText.Append(currentPrompt);
                    newText.Append(" ");
                    promptStartPosition = newText.Length;
                    newText.Append(currentInput);
                    
                    TerminalTextBox.Text = newText.ToString();
                    TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                    TerminalTextBox.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RebuildTerminalDisplay error: {ex.Message}");
            }
        }

        private BlockType DetectBlockType(string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return BlockType.Normal;

            // Detect stack traces
            if (output.Contains("at ") && (output.Contains(".cs:line") || output.Contains("Exception")))
                return BlockType.StackTrace;

            // Detect JSON
            string trimmed = output.Trim();
            if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) || (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
            {
                try
                {
                    // Basic JSON validation
                    if (trimmed.Count(c => c == '{') == trimmed.Count(c => c == '}'))
                        return BlockType.Json;
                }
                catch { }
            }

            // Detect errors
            if (output.Contains("ERROR:") || output.Contains("Error:") || output.Contains("error:"))
                return BlockType.Error;

            // Detect headings (lines starting with # or === or ---)
            if (System.Text.RegularExpressions.Regex.IsMatch(output, @"^(#{1,6}\s|===|---).*$", System.Text.RegularExpressions.RegexOptions.Multiline))
                return BlockType.Heading;

            // Detect large text
            int lineCount = output.Split('\n').Length;
            if (lineCount > MAX_OUTPUT_LINES_BEFORE_FOLD)
                return BlockType.LargeText;

            return BlockType.Normal;
        }

        private string GetOutputSummary(string output, BlockType type)
        {
            if (string.IsNullOrWhiteSpace(output)) return "empty";

            string[] lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            switch (type)
            {
                case BlockType.StackTrace:
                    return $"Stack trace: {lines[0].Substring(0, Math.Min(50, lines[0].Length))}...";
                
                case BlockType.Json:
                    return $"JSON data: {lines.Length} lines";
                
                case BlockType.Error:
                    string firstError = lines.FirstOrDefault(l => l.Contains("ERROR") || l.Contains("Error")) ?? lines[0];
                    return firstError.Substring(0, Math.Min(80, firstError.Length));
                
                case BlockType.LargeText:
                    return $"First line: {lines[0].Substring(0, Math.Min(60, lines[0].Length))}...";
                
                default:
                    return lines[0].Substring(0, Math.Min(60, lines[0].Length));
            }
        }

        private string GetLastCommand()
        {
            lock (lockObject)
            {
                return commandHistory.Count > 0 ? commandHistory[commandHistory.Count - 1] : "";
            }
        }

        // Bookmark Management
        private void AddBookmark()
        {
            lock (lockObject)
            {
                int position = TerminalTextBox.CaretIndex;
                string context = GetContextAtPosition(position, 50);
                
                // Show dialog to get bookmark name
                var bookmarkDialog = new Window
                {
                    Title = "Add Bookmark",
                    Width = 450,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize
                };

                var grid = new Grid { Margin = new Thickness(20) };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = "Enter bookmark name:",
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                Grid.SetRow(label, 0);
                grid.Children.Add(label);

                var nameTextBox = new TextBox
                {
                    Text = $"Bookmark {bookmarks.Count + 1}",
                    FontSize = 14,
                    Padding = new Thickness(5),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                AutomationProperties.SetName(nameTextBox, "Bookmark name input");
                Grid.SetRow(nameTextBox, 1);
                grid.Children.Add(nameTextBox);

                var contextLabel = new TextBlock
                {
                    Text = $"Context: {context}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 15)
                };
                Grid.SetRow(contextLabel, 2);
                grid.Children.Add(contextLabel);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetRow(buttonPanel, 3);

                var okBtn = new Button
                {
                    Content = "Add Bookmark",
                    Width = 120,
                    Height = 35,
                    Margin = new Thickness(0, 0, 10, 0),
                    IsDefault = true
                };
                okBtn.Click += (s, e) =>
                {
                    bookmarkDialog.DialogResult = true;
                    bookmarkDialog.Close();
                };
                buttonPanel.Children.Add(okBtn);

                var cancelBtn = new Button
                {
                    Content = "Cancel",
                    Width = 100,
                    Height = 35,
                    IsCancel = true
                };
                cancelBtn.Click += (s, e) =>
                {
                    bookmarkDialog.DialogResult = false;
                    bookmarkDialog.Close();
                };
                buttonPanel.Children.Add(cancelBtn);

                grid.Children.Add(buttonPanel);
                bookmarkDialog.Content = grid;
                bookmarkDialog.Loaded += (s, e) =>
                {
                    nameTextBox.Focus();
                    nameTextBox.SelectAll();
                };

                bool? result = bookmarkDialog.ShowDialog();
                
                if (result == true && !string.IsNullOrWhiteSpace(nameTextBox.Text))
                {
                    Bookmark bookmark = new Bookmark
                    {
                        Position = position,
                        Label = nameTextBox.Text.Trim(),
                        Context = context
                    };
                    
                    bookmarks.Add(bookmark);
                    
                    // Limit bookmarks to 100
                    if (bookmarks.Count > 100)
                    {
                        bookmarks.RemoveAt(0);
                    }
                    
                    AnnounceToScreenReader($"Bookmark '{bookmark.Label}' added. Total bookmarks: {bookmarks.Count}");
                }
                else
                {
                    AnnounceToScreenReader("Bookmark cancelled");
                }
            }
        }

        private void JumpToNextBookmark()
        {
            lock (lockObject)
            {
                if (bookmarks.Count == 0)
                {
                    AnnounceToScreenReader("No bookmarks set. Use Ctrl+B to add a bookmark.");
                    return;
                }

                // Remove invalid bookmarks (positions beyond text length)
                bookmarks.RemoveAll(b => b.Position >= TerminalTextBox.Text.Length);
                
                if (bookmarks.Count == 0)
                {
                    AnnounceToScreenReader("All bookmarks are now invalid. Text has changed.");
                    currentBookmarkIndex = -1;
                    return;
                }

                currentBookmarkIndex++;
                if (currentBookmarkIndex >= bookmarks.Count)
                {
                    currentBookmarkIndex = 0;
                    AnnounceToScreenReader("Wrapped to first bookmark");
                }

                Bookmark bookmark = bookmarks[currentBookmarkIndex];
                
                // Validate position is still within bounds
                if (bookmark.Position < TerminalTextBox.Text.Length)
                {
                    TerminalTextBox.CaretIndex = bookmark.Position;
                    TerminalTextBox.ScrollToLine(TerminalTextBox.GetLineIndexFromCharacterIndex(bookmark.Position));
                    TerminalTextBox.Focus();
                    
                    // Announce bookmark with name and context
                    AnnounceToScreenReader($"Bookmark {currentBookmarkIndex + 1} of {bookmarks.Count}: {bookmark.Label}. {bookmark.Context}");
                }
                else
                {
                    AnnounceToScreenReader($"Bookmark '{bookmark.Label}' is invalid. Removing.");
                    bookmarks.RemoveAt(currentBookmarkIndex);
                    currentBookmarkIndex = -1;
                }
            }
        }
        
        private void JumpToPreviousBookmark()
        {
            lock (lockObject)
            {
                if (bookmarks.Count == 0)
                {
                    AnnounceToScreenReader("No bookmarks set. Use Ctrl+B to add a bookmark.");
                    return;
                }

                // Remove invalid bookmarks (positions beyond text length)
                bookmarks.RemoveAll(b => b.Position >= TerminalTextBox.Text.Length);
                
                if (bookmarks.Count == 0)
                {
                    AnnounceToScreenReader("All bookmarks are now invalid. Text has changed.");
                    currentBookmarkIndex = -1;
                    return;
                }

                currentBookmarkIndex--;
                if (currentBookmarkIndex < 0)
                {
                    currentBookmarkIndex = bookmarks.Count - 1;
                    AnnounceToScreenReader("Wrapped to last bookmark");
                }

                Bookmark bookmark = bookmarks[currentBookmarkIndex];
                
                // Validate position is still within bounds
                if (bookmark.Position < TerminalTextBox.Text.Length)
                {
                    TerminalTextBox.CaretIndex = bookmark.Position;
                    TerminalTextBox.ScrollToLine(TerminalTextBox.GetLineIndexFromCharacterIndex(bookmark.Position));
                    TerminalTextBox.Focus();
                    
                    // Announce bookmark with name and context
                    AnnounceToScreenReader($"Bookmark {currentBookmarkIndex + 1} of {bookmarks.Count}: {bookmark.Label}. {bookmark.Context}");
                }
                else
                {
                    AnnounceToScreenReader($"Bookmark '{bookmark.Label}' is invalid. Removing.");
                    bookmarks.RemoveAt(currentBookmarkIndex);
                    currentBookmarkIndex = -1;
                }
            }
        }

        private void ListBookmarks()
        {
            lock (lockObject)
            {
                if (bookmarks.Count == 0)
                {
                    AnnounceToScreenReader("No bookmarks set. Use Ctrl+B to add bookmarks.");
                    return;
                }

                // Create accessible dialog
                var bookmarksWindow = new Window
                {
                    Title = "Bookmarks",
                    Width = 650,
                    Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.CanResize
                };

                var grid = new Grid { Margin = new Thickness(20) };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var title = new TextBlock
                {
                    Text = $"Bookmarks ({bookmarks.Count})",
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                Grid.SetRow(title, 0);
                grid.Children.Add(title);

                var content = new StringBuilder();
                for (int i = 0; i < bookmarks.Count; i++)
                {
                    var b = bookmarks[i];
                    content.AppendLine($"[{i + 1}] {b.Label}");
                    content.AppendLine($"    Context: {b.Context}");
                    content.AppendLine();
                }

                var richTextBox = new RichTextBox
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(10),
                    Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Colors.LightGray),
                    FontSize = 13
                };
                richTextBox.Document.Blocks.Clear();
                richTextBox.Document.Blocks.Add(new Paragraph(new Run(content.ToString())));
                richTextBox.IsDocumentEnabled = true;
                richTextBox.PreviewTextInput += (s, e) => e.Handled = true;
                richTextBox.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Space ||
                        (e.Key >= Key.A && e.Key <= Key.Z) || (e.Key >= Key.D0 && e.Key <= Key.D9))
                    {
                        e.Handled = true;
                    }
                };
                AutomationProperties.SetName(richTextBox, "Bookmarks list. Use arrow keys to navigate.");
                Grid.SetRow(richTextBox, 1);
                grid.Children.Add(richTextBox);

                var closeBtn = new Button
                {
                    Content = "Close",
                    Width = 100,
                    Height = 35,
                    Margin = new Thickness(0, 15, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                closeBtn.Click += (s, args) => bookmarksWindow.Close();
                Grid.SetRow(closeBtn, 2);
                grid.Children.Add(closeBtn);

                bookmarksWindow.Content = grid;
                bookmarksWindow.Loaded += (s, args) =>
                {
                    richTextBox.Focus();
                    AnnounceToScreenReader($"{bookmarks.Count} bookmarks listed. Use arrow keys to navigate.");
                };
                bookmarksWindow.ShowDialog();
            }
        }

        private void ShowBookmarksAndKeywords()
        {
            lock (lockObject)
            {
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("═══════════════════════════════════════════════════════════");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText($"BOOKMARKS ({bookmarks.Count}):");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("═══════════════════════════════════════════════════════════");
                TerminalTextBox.AppendText(Environment.NewLine);

                if (bookmarks.Count == 0)
                {
                    TerminalTextBox.AppendText("  No bookmarks set. Use Ctrl+B to add bookmarks.");
                    TerminalTextBox.AppendText(Environment.NewLine);
                }
                else
                {
                    for (int i = 0; i < bookmarks.Count; i++)
                    {
                        var b = bookmarks[i];
                        TerminalTextBox.AppendText($"  [{i + 1}] {b.Label} - {b.Context}");
                        TerminalTextBox.AppendText(Environment.NewLine);
                    }
                }

                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("═══════════════════════════════════════════════════════════");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText($"MONITORED KEYWORDS ({monitoredKeywords.Count}):");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("═══════════════════════════════════════════════════════════");
                TerminalTextBox.AppendText(Environment.NewLine);
                
                foreach (var keyword in monitoredKeywords.OrderBy(k => k))
                {
                    TerminalTextBox.AppendText($"  • {keyword}");
                    TerminalTextBox.AppendText(Environment.NewLine);
                }

                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("Use Ctrl+M to add/remove keywords");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText(currentPrompt);
                TerminalTextBox.AppendText(" ");
                promptStartPosition = TerminalTextBox.Text.Length;
                TerminalTextBox.CaretIndex = promptStartPosition;

                AnnounceToScreenReader($"{bookmarks.Count} bookmarks and {monitoredKeywords.Count} monitored keywords");
            }
        }

        // Keyword Monitoring
        private void ShowKeywordManager()
        {
            lock (lockObject)
            {
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("═══════════════════════════════════════════════════════════");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("KEYWORD MONITORING MANAGER");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("═══════════════════════════════════════════════════════════");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("Current monitored keywords:");
                TerminalTextBox.AppendText(Environment.NewLine);
                
                foreach (var keyword in monitoredKeywords.OrderBy(k => k))
                {
                    TerminalTextBox.AppendText($"  • {keyword}");
                    TerminalTextBox.AppendText(Environment.NewLine);
                }

                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("To add a keyword, type: monitor add <keyword>");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("To remove a keyword, type: monitor remove <keyword>");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("To clear all keywords, type: monitor clear");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText(currentPrompt);
                TerminalTextBox.AppendText(" ");
                promptStartPosition = TerminalTextBox.Text.Length;
                TerminalTextBox.CaretIndex = promptStartPosition;

                AnnounceToScreenReader($"Keyword manager shown. {monitoredKeywords.Count} keywords monitored");
            }
        }

        private void MonitorKeywords(string output)
        {
            if (string.IsNullOrWhiteSpace(output) || monitoredKeywords.Count == 0)
                return;

            string lowerOutput = output.ToLower();
            List<string> detectedKeywords = new List<string>();
            
            foreach (var keyword in monitoredKeywords)
            {
                // Use word boundary matching for better accuracy
                string pattern = $"\\b{keyword.ToLower()}\\b";
                if (System.Text.RegularExpressions.Regex.IsMatch(lowerOutput, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    if (!detectedKeywords.Contains(keyword))
                    {
                        detectedKeywords.Add(keyword);
                    }
                }
            }

            if (detectedKeywords.Count > 0)
            {
                string keywordList = string.Join(", ", detectedKeywords);
                AnnounceToScreenReader($"Keywords detected: {keywordList}");
                
                if (pauseOnKeyword)
                {
                    AnnounceToScreenReader($"Output paused due to keywords. Press any key to continue.");
                }
            }
        }

        private bool HandleMonitorCommand(string command)
        {
            if (!command.StartsWith("monitor ", StringComparison.OrdinalIgnoreCase))
                return false;

            string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length < 2)
            {
                AppendOutput("Usage: monitor add|remove|clear [keyword]", false);
                return true;
            }

            string action = parts[1].ToLower();
            
            switch (action)
            {
                case "add":
                    if (parts.Length >= 3)
                    {
                        string keyword = parts[2];
                        monitoredKeywords.Add(keyword);
                        AppendOutput($"Now monitoring keyword: {keyword}", false);
                    }
                    else
                    {
                        AppendOutput("Usage: monitor add <keyword>", false);
                    }
                    break;

                case "remove":
                    if (parts.Length >= 3)
                    {
                        string keyword = parts[2];
                        if (monitoredKeywords.Remove(keyword))
                        {
                            AppendOutput($"Stopped monitoring keyword: {keyword}", false);
                        }
                        else
                        {
                            AppendOutput($"Keyword not found: {keyword}", false);
                        }
                    }
                    else
                    {
                        AppendOutput("Usage: monitor remove <keyword>", false);
                    }
                    break;

                case "clear":
                    int count = monitoredKeywords.Count;
                    monitoredKeywords.Clear();
                    AppendOutput($"Cleared {count} monitored keywords", false);
                    break;

                case "list":
                    StringBuilder listOutput = new StringBuilder();
                    listOutput.AppendLine($"Monitored keywords ({monitoredKeywords.Count}):");
                    foreach (var kw in monitoredKeywords.OrderBy(k => k))
                    {
                        listOutput.AppendLine($"  • {kw}");
                    }
                    AppendOutput(listOutput.ToString().TrimEnd(), false);
                    break;

                default:
                    AppendOutput($"Unknown action: {action}. Use add, remove, clear, or list", false);
                    break;
            }

            return true;
        }

        // Output Categorization
        private void CategorizeOutput(string output, bool isError)
        {
            if (string.IsNullOrWhiteSpace(output))
                return;

            CategorizedOutput item = new CategorizedOutput
            {
                Content = output.Length > 500 ? output.Substring(0, 500) + "..." : output,
                Position = TerminalTextBox.Text.Length,
                Command = GetLastCommand()
            };

            string lowerOutput = output.ToLower();

            // Categorize based on content
            if (isError || lowerOutput.Contains("error") || lowerOutput.Contains("exception") || lowerOutput.Contains("failed"))
            {
                errors.Add(item);
                if (errors.Count > MAX_CATEGORIZED_ITEMS)
                {
                    errors.RemoveRange(0, errors.Count - MAX_CATEGORIZED_ITEMS);
                }
            }
            else if (lowerOutput.Contains("warning") || lowerOutput.Contains("warn"))
            {
                warnings.Add(item);
                if (warnings.Count > MAX_CATEGORIZED_ITEMS)
                {
                    warnings.RemoveRange(0, warnings.Count - MAX_CATEGORIZED_ITEMS);
                }
            }
            else if (lowerOutput.Contains("success") || lowerOutput.Contains("completed") || 
                     lowerOutput.Contains("connected") || lowerOutput.Contains("ready") ||
                     lowerOutput.Contains("started") || lowerOutput.Contains("finished"))
            {
                statusMessages.Add(item);
                if (statusMessages.Count > MAX_CATEGORIZED_ITEMS)
                {
                    statusMessages.RemoveRange(0, statusMessages.Count - MAX_CATEGORIZED_ITEMS);
                }
            }
        }

        private void ShowOutputPanels()
        {
            lock (lockObject)
            {
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("═══════════════════════════════════════════════════════════");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("OUTPUT CATEGORIZATION PANELS");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText("═══════════════════════════════════════════════════════════");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText(Environment.NewLine);

                // Error Panel
                TerminalTextBox.AppendText($"┌─ ERROR PANEL ({errors.Count}) ─────────────────────────────────────┐");
                TerminalTextBox.AppendText(Environment.NewLine);
                if (errors.Count == 0)
                {
                    TerminalTextBox.AppendText("│ No errors detected                                          │");
                    TerminalTextBox.AppendText(Environment.NewLine);
                }
                else
                {
                    foreach (var error in errors.TakeLast(5))
                    {
                        string preview = error.Content.Length > 50 ? error.Content.Substring(0, 50) + "..." : error.Content;
                        TerminalTextBox.AppendText($"│ [{error.Timestamp:HH:mm:ss}] {preview}");
                        TerminalTextBox.AppendText(Environment.NewLine);
                    }
                    if (errors.Count > 5)
                    {
                        TerminalTextBox.AppendText($"│ ... and {errors.Count - 5} more errors");
                        TerminalTextBox.AppendText(Environment.NewLine);
                    }
                }
                TerminalTextBox.AppendText("└────────────────────────────────────────────────────────────┘");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText(Environment.NewLine);

                // Warning Panel
                TerminalTextBox.AppendText($"┌─ WARNING PANEL ({warnings.Count}) ───────────────────────────────┐");
                TerminalTextBox.AppendText(Environment.NewLine);
                if (warnings.Count == 0)
                {
                    TerminalTextBox.AppendText("│ No warnings detected                                        │");
                    TerminalTextBox.AppendText(Environment.NewLine);
                }
                else
                {
                    foreach (var warning in warnings.TakeLast(5))
                    {
                        string preview = warning.Content.Length > 50 ? warning.Content.Substring(0, 50) + "..." : warning.Content;
                        TerminalTextBox.AppendText($"│ [{warning.Timestamp:HH:mm:ss}] {preview}");
                        TerminalTextBox.AppendText(Environment.NewLine);
                    }
                    if (warnings.Count > 5)
                    {
                        TerminalTextBox.AppendText($"│ ... and {warnings.Count - 5} more warnings");
                        TerminalTextBox.AppendText(Environment.NewLine);
                    }
                }
                TerminalTextBox.AppendText("└────────────────────────────────────────────────────────────┘");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText(Environment.NewLine);

                // Status Panel
                TerminalTextBox.AppendText($"┌─ STATUS PANEL ({statusMessages.Count}) ──────────────────────────────┐");
                TerminalTextBox.AppendText(Environment.NewLine);
                if (statusMessages.Count == 0)
                {
                    TerminalTextBox.AppendText("│ No status messages                                          │");
                    TerminalTextBox.AppendText(Environment.NewLine);
                }
                else
                {
                    foreach (var status in statusMessages.TakeLast(5))
                    {
                        string preview = status.Content.Length > 50 ? status.Content.Substring(0, 50) + "..." : status.Content;
                        TerminalTextBox.AppendText($"│ [{status.Timestamp:HH:mm:ss}] {preview}");
                        TerminalTextBox.AppendText(Environment.NewLine);
                    }
                    if (statusMessages.Count > 5)
                    {
                        TerminalTextBox.AppendText($"│ ... and {statusMessages.Count - 5} more status messages");
                        TerminalTextBox.AppendText(Environment.NewLine);
                    }
                }
                TerminalTextBox.AppendText("└────────────────────────────────────────────────────────────┘");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText(Environment.NewLine);

                TerminalTextBox.AppendText(currentPrompt);
                TerminalTextBox.AppendText(" ");
                promptStartPosition = TerminalTextBox.Text.Length;
                TerminalTextBox.CaretIndex = promptStartPosition;

                AnnounceToScreenReader($"Output panels: {errors.Count} errors, {warnings.Count} warnings, {statusMessages.Count} status messages");
            }
        }

        private string GetContextAtPosition(int position, int maxLength)
        {
            try
            {
                int start = Math.Max(0, position - maxLength / 2);
                int length = Math.Min(maxLength, TerminalTextBox.Text.Length - start);
                
                if (length <= 0)
                    return "";

                string context = TerminalTextBox.Text.Substring(start, length);
                context = context.Replace("\r", "").Replace("\n", " ").Trim();
                
                return context.Length > maxLength ? context.Substring(0, maxLength) + "..." : context;
            }
            catch
            {
                return "";
            }
        }

        private void TerminalTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Don't restrict in reading mode
            if (isReadingMode)
            {
                return;
            }

            try
            {
                lock (lockObject)
                {
                    // Ensure prompt is never deleted
                    if (TerminalTextBox.Text.Length < promptStartPosition)
                    {
                        TerminalTextBox.Text = TerminalTextBox.Text.Substring(0, promptStartPosition) + currentPrompt + " ";
                        TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                        return;
                    }

                    // Prevent modification of text before prompt in command mode
                    if (TerminalTextBox.SelectionStart < promptStartPosition && TerminalTextBox.SelectionStart > 0)
                    {
                        int caretPos = TerminalTextBox.CaretIndex;
                        if (caretPos < promptStartPosition)
                        {
                            TerminalTextBox.CaretIndex = Math.Max(promptStartPosition, TerminalTextBox.Text.Length);
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors during clear operations
            }
        }

        private void TerminalTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            UpdateCursorPosition();
            
            // Auto-scroll to caret position in reading mode
            if (isReadingMode)
            {
                TerminalTextBox.Focus();
                BringCaretIntoView();
            }
        }

        private void UpdateCursorPosition()
        {
            try
            {
                int caretIndex = TerminalTextBox.CaretIndex;
                if (caretIndex < 0 || caretIndex > TerminalTextBox.Text.Length)
                    return;

                string text = TerminalTextBox.Text.Substring(0, Math.Min(caretIndex, TerminalTextBox.Text.Length));
                
                currentLine = text.Count(c => c == '\n') + 1;
                int lastNewLine = text.LastIndexOf('\n');
                currentColumn = caretIndex - lastNewLine;

                CursorPositionText.Text = $"Ln {currentLine}, Col {currentColumn}";
            }
            catch
            {
                // Ignore cursor position update errors
            }
        }

        private void BringCaretIntoView()
        {
            try
            {
                if (TerminalTextBox.CaretIndex >= 0 && TerminalTextBox.CaretIndex <= TerminalTextBox.Text.Length)
                {
                    var rect = TerminalTextBox.GetRectFromCharacterIndex(TerminalTextBox.CaretIndex);
                    ScrollViewer.ScrollToVerticalOffset(ScrollViewer.VerticalOffset + rect.Top - ScrollViewer.ViewportHeight / 2);
                }
            }
            catch
            {
                // Fallback: just scroll to make sure caret is visible
                try
                {
                    TerminalTextBox.CaretIndex = TerminalTextBox.CaretIndex; // Trigger caret visibility
                    TerminalTextBox.Focus();
                }
                catch { }
            }
        }

        private void AutoCompleteCommand()
        {
            try
            {
                if (TerminalTextBox.CaretIndex < promptStartPosition)
                    return;

                // Get the command text after the prompt
                string fullInput = "";
                if (promptStartPosition < TerminalTextBox.Text.Length)
                {
                    fullInput = TerminalTextBox.Text.Substring(promptStartPosition);
                }
                
                if (string.IsNullOrWhiteSpace(fullInput))
                {
                    AnnounceToScreenReader("No text to complete");
                    return;
                }

                // Split into command and arguments
                string[] parts = fullInput.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                string baseCommand = parts.Length > 0 ? parts[0] : "";
                string arguments = parts.Length > 1 ? parts[1] : "";
                
                // Built-in commands
                string[] builtInCommands = { "help", "clear", "cls", "bash", "powershell", "pwsh", "cmd", "sudo", "exit", "quit", "monitor" };
                
                List<string> matches = new List<string>();
                
                // Determine what we're completing
                string searchText = string.IsNullOrEmpty(arguments) ? baseCommand : arguments;
                bool completingCommand = string.IsNullOrEmpty(arguments);
                
                // If no arguments yet, complete the command itself
                if (completingCommand)
                {
                    // Match built-in commands
                    matches.AddRange(builtInCommands.Where(cmd => cmd.StartsWith(baseCommand, StringComparison.OrdinalIgnoreCase)));
                }
                
                // Try to match files/directories (always try, but especially when completing arguments)
                try
                {
                    string searchPattern = searchText.Trim() + "*";
                    string searchDir = commandExecutor?.GetCurrentDirectory() ?? Environment.CurrentDirectory;
                    string pathPrefix = ""; // Directory part to preserve when completing
                    
                    // If path contains directory separator, extract directory and filename parts
                    if (searchText.Contains(@"\") || searchText.Contains("/"))
                    {
                        int lastSep = Math.Max(searchText.LastIndexOf(@"\"), searchText.LastIndexOf('/'));
                        if (lastSep >= 0)
                        {
                            pathPrefix = searchText.Substring(0, lastSep + 1);
                            searchPattern = searchText.Substring(lastSep + 1) + "*";
                            
                            // Try to resolve the directory
                            if (Path.IsPathRooted(pathPrefix))
                            {
                                searchDir = pathPrefix;
                            }
                            else
                            {
                                searchDir = Path.Combine(searchDir, pathPrefix);
                            }
                        }
                    }
                    
                    if (Directory.Exists(searchDir))
                    {
                        // Use appropriate path separator based on shell mode
                        char pathSeparator = currentShell == "Bash" ? '/' : Path.DirectorySeparatorChar;
                        
                        var fileMatches = Directory.GetFileSystemEntries(searchDir, searchPattern)
                            .Select(f => {
                                string name = Path.GetFileName(f);
                                // Add trailing slash for directories
                                if (Directory.Exists(f))
                                    name += pathSeparator;
                                // Preserve the directory prefix
                                return pathPrefix + name;
                            })
                            .Where(n => !string.IsNullOrEmpty(n))
                            .Take(20); // Limit to 20 matches
                        
                        matches.AddRange(fileMatches);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"File matching error: {ex.Message}");
                }

                // Remove null entries
                matches = matches.Where(m => m != null).Distinct().ToList();

                if (matches.Count == 1)
                {
                    // Single match - complete it
                    string completion = matches[0];
                    
                    // Build the completed command
                    string completedText = "";
                    if (!string.IsNullOrEmpty(arguments))
                    {
                        // We were completing an argument
                        completedText = baseCommand + " " + completion;
                    }
                    else
                    {
                        // We were completing the command itself
                        completedText = completion;
                    }
                    
                    // Replace from prompt position
                    TerminalTextBox.Text = TerminalTextBox.Text.Substring(0, promptStartPosition) + completedText;
                    TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                    TerminalTextBox.Focus();
                    
                    AnnounceToScreenReader($"Completed: {completion}");
                }
                else if (matches.Count > 1)
                {
                    // Find common prefix
                    string commonPrefix = matches[0];
                    foreach (var match in matches.Skip(1))
                    {
                        int i = 0;
                        while (i < commonPrefix.Length && i < match.Length && 
                               char.ToLower(commonPrefix[i]) == char.ToLower(match[i]))
                        {
                            i++;
                        }
                        commonPrefix = commonPrefix.Substring(0, i);
                    }
                    
                    // If there's a common prefix longer than current input, complete to that
                    if (commonPrefix.Length > searchText.Length)
                    {
                        string completedText = "";
                        if (!string.IsNullOrEmpty(arguments))
                        {
                            completedText = baseCommand + " " + commonPrefix;
                        }
                        else
                        {
                            completedText = commonPrefix;
                        }
                        
                        TerminalTextBox.Text = TerminalTextBox.Text.Substring(0, promptStartPosition) + completedText;
                        TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                        AnnounceToScreenReader($"Completed to: {commonPrefix}. {matches.Count} matches available.");
                    }
                    else
                    {
                        // Show all matches
                        AnnounceToScreenReader($"{matches.Count} matches found");
                        TerminalTextBox.AppendText(Environment.NewLine);
                        TerminalTextBox.AppendText(string.Join("  ", matches.Take(20)));
                        if (matches.Count > 20)
                            TerminalTextBox.AppendText($"  ... and {matches.Count - 20} more");
                        TerminalTextBox.AppendText(Environment.NewLine);
                        TerminalTextBox.AppendText(currentPrompt);
                        TerminalTextBox.AppendText(" ");
                        int newPromptPos = TerminalTextBox.Text.Length;
                        TerminalTextBox.AppendText(fullInput);
                        promptStartPosition = newPromptPos;
                        TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                    }
                }
                else
                {
                    AnnounceToScreenReader("No matches found");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Auto-complete error: {ex.Message}");
                AnnounceToScreenReader($"Auto-complete error: {ex.Message}");
            }
        }

        private void AnnounceToScreenReader(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // Use UIA Notification event which works reliably with all screen readers
                    var peer = UIElementAutomationPeer.FromElement(TerminalTextBox);
                    if (peer == null)
                    {
                        peer = new UIElementAutomationPeer(TerminalTextBox);
                    }
                    
                    // RaiseNotificationEvent is the reliable way for screen reader announcements
                    if (peer is IInvokeProvider)
                    {
                        // Fallback for older frameworks
                        AutomationProperties.SetHelpText(TerminalTextBox, message);
                        peer.RaiseAutomationEvent(AutomationEvents.ToolTipOpened);
                    }
                    else
                    {
                        // Modern approach - works with NVDA, JAWS, Narrator
                        try
                        {
                            peer.GetType().GetMethod("RaiseNotificationEvent")?.Invoke(peer, new object[] 
                            { 
                                0, // AutomationNotificationKind.ItemAdded 
                                1, // AutomationNotificationProcessing.MostRecent
                                message,
                                Guid.NewGuid().ToString()
                            });
                        }
                        catch
                        {
                            // Final fallback - set help text and raise event
                            AutomationProperties.SetHelpText(TerminalTextBox, message);
                            peer.RaiseAutomationEvent(AutomationEvents.ToolTipOpened);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Screen reader announcement error: {ex.Message}");
            }
        }

        // Menu and Command Handlers
        private void ToggleReadingMode_Click(object sender, RoutedEventArgs e)
        {
            ToggleReadingMode();
        }

        private void ToggleReadingMode()
        {
            try
            {
                isReadingMode = !isReadingMode;
                
                if (isReadingMode)
                {
                    ModeStatusText.Text = "Reading Mode (Navigate with arrows, Ctrl+R to exit)";
                    ModeStatusText.Foreground = new SolidColorBrush(Colors.Cyan);
                    TerminalTextBox.IsReadOnly = false; // Keep false to allow selection
                    TerminalTextBox.IsReadOnlyCaretVisible = true;
                    AnnounceToScreenReader("Reading mode enabled. Use arrow keys to navigate. Shift+Arrow to select. Ctrl+C to copy. Press Ctrl+R to return to command mode.");
                }
                else
                {
                    ModeStatusText.Text = "Command Mode";
                    ModeStatusText.Foreground = (SolidColorBrush)FindResource("ForegroundBrush");
                    TerminalTextBox.IsReadOnly = false;
                    TerminalTextBox.Select(TerminalTextBox.Text.Length, 0);
                    TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                    TerminalTextBox.Focus();
                    AnnounceToScreenReader("Command mode enabled. Type commands and press Enter.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Toggle reading mode error: {ex.Message}");
            }
        }

        private void ToggleHighContrast_Click(object sender, RoutedEventArgs e)
        {
            ToggleHighContrast();
        }

        private void ToggleHighContrast()
        {
            isHighContrast = !isHighContrast;

            if (isHighContrast)
            {
                // High contrast theme
                this.Background = new SolidColorBrush(Colors.Black);
                TerminalTextBox.Background = new SolidColorBrush(Colors.Black);
                TerminalTextBox.Foreground = new SolidColorBrush(Colors.White);
                AnnounceToScreenReader("High contrast mode enabled");
            }
            else
            {
                // Normal theme
                this.Background = (SolidColorBrush)FindResource("BackgroundBrush");
                TerminalTextBox.Background = (SolidColorBrush)FindResource("BackgroundBrush");
                TerminalTextBox.Foreground = (SolidColorBrush)FindResource("ForegroundBrush");
                AnnounceToScreenReader("Normal contrast mode enabled");
            }
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            IncreaseFontSize();
        }

        private void IncreaseFontSize()
        {
            if (baseFontSize < MAX_FONT_SIZE)
            {
                baseFontSize += 2;
                baseFontSize = Math.Min(baseFontSize, MAX_FONT_SIZE);
                TerminalTextBox.FontSize = baseFontSize;
                AnnounceToScreenReader($"Font size increased to {baseFontSize}");
            }
            else
            {
                AnnounceToScreenReader($"Maximum font size reached");
            }
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            DecreaseFontSize();
        }

        private void DecreaseFontSize()
        {
            if (baseFontSize > MIN_FONT_SIZE)
            {
                baseFontSize -= 2;
                baseFontSize = Math.Max(baseFontSize, MIN_FONT_SIZE);
                TerminalTextBox.FontSize = baseFontSize;
                AnnounceToScreenReader($"Font size decreased to {baseFontSize}");
            }
            else
            {
                AnnounceToScreenReader($"Minimum font size reached");
            }
        }

        private void ResetZoom_Click(object sender, RoutedEventArgs e)
        {
            ResetFontSize();
        }

        private void ResetFontSize()
        {
            baseFontSize = 14;
            TerminalTextBox.FontSize = baseFontSize;
            AnnounceToScreenReader("Font size reset to default");
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ClearScreen();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TerminalTextBox.SelectedText.Length > 0)
                {
                    Clipboard.SetText(TerminalTextBox.SelectedText);
                    AnnounceToScreenReader($"Copied {TerminalTextBox.SelectedText.Length} characters");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Copy failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    if (TerminalTextBox.CaretIndex < promptStartPosition)
                    {
                        TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                    }
                    TerminalTextBox.SelectedText = Clipboard.GetText();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Paste failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            TerminalTextBox.SelectAll();
            AnnounceToScreenReader("All text selected");
        }

        private void NewTerminal_Click(object sender, RoutedEventArgs e)
        {
            var newWindow = new MainWindow();
            newWindow.Show();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void ShowShortcuts_Click(object sender, RoutedEventArgs e)
        {
            string shortcuts = @"KEYBOARD SHORTCUTS

Navigation & Editing:
  Ctrl+R       - Toggle Reading Mode
  Arrow Keys   - Navigate history (Up/Down) or text (in Reading Mode)
  Home         - Move to command start
  Tab          - Auto-complete

Display:
  Ctrl+H       - Toggle High Contrast
  Ctrl+L       - Clear Screen
  Ctrl+Plus    - Increase Font Size
  Ctrl+Minus   - Decrease Font Size
  Ctrl+0       - Reset Font Size

Clipboard:
  Ctrl+C       - Copy selected text
  Ctrl+V       - Paste text
  Ctrl+A       - Select all text

Window:
  Alt+F4       - Exit application";

            var shortcutsWindow = new Window
            {
                Title = "Keyboard Shortcuts",
                Width = 600,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.CanResize
            };

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var richTextBox = new RichTextBox
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10),
                Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14
            };
            richTextBox.Document.Blocks.Clear();
            richTextBox.Document.Blocks.Add(new Paragraph(new Run(shortcuts)));
            richTextBox.IsDocumentEnabled = true;
            
            // Make read-only by preventing text input while allowing navigation
            richTextBox.PreviewTextInput += (s, e) => e.Handled = true;
            richTextBox.PreviewKeyDown += (s, e) =>
            {
                // Block editing keys but allow navigation
                if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Space ||
                    (e.Key >= Key.A && e.Key <= Key.Z) || (e.Key >= Key.D0 && e.Key <= Key.D9))
                {
                    e.Handled = true;
                }
            };
            
            AutomationProperties.SetName(richTextBox, "Keyboard shortcuts list. Use arrow keys to navigate.");
            Grid.SetRow(richTextBox, 0);
            grid.Children.Add(richTextBox);

            var closeBtn = new Button
            {
                Content = "Close",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 15, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            closeBtn.Click += (s, args) => shortcutsWindow.Close();
            Grid.SetRow(closeBtn, 1);
            grid.Children.Add(closeBtn);

            shortcutsWindow.Content = grid;
            shortcutsWindow.Loaded += (s, args) => richTextBox.Focus();
            shortcutsWindow.ShowDialog();
        }

        private void Developer_Click(object sender, RoutedEventArgs e) { About_Click(sender, e); }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new Window
            {
                Title = "About Accessible Terminal",
                Width = 650,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.CanResize
            };

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Title
            var title = new TextBlock
            {
                Text = "Accessible Terminal v1.0",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            AutomationProperties.SetName(title, "Accessible Terminal version 1.0");
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            // Content in accessible TextBox
            var content = new StringBuilder();
            content.AppendLine("A professional command-line interface designed for accessibility.");
            content.AppendLine();
            content.AppendLine("FEATURES:");
            content.AppendLine("• Full screen reader support");
            content.AppendLine("• Reading mode with arrow navigation");
            content.AppendLine("• High contrast themes");
            content.AppendLine("• Unix/Bash commands with bundled binaries");
            content.AppendLine("• PowerShell and CMD support");
            content.AppendLine("• Adjustable font sizes");
            content.AppendLine("• Complete keyboard navigation");
            content.AppendLine("• Administrator elevation (sudo su)");
            content.AppendLine();
            content.AppendLine("DEVELOPER:");
            content.AppendLine("Ibrahim Badawy (@IBRASONIC)");
            content.AppendLine("Email: ibrahim.m.badawy@gmail.com");
            content.AppendLine("GitHub: https://github.com/ibrasonic");
            content.AppendLine();
            content.AppendLine("© 2025 Ibrahim Badawy");

            var richTextBox = new RichTextBox
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10),
                Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                Margin = new Thickness(0, 10, 0, 10),
                FontSize = 13
            };
            richTextBox.Document.Blocks.Clear();
            richTextBox.Document.Blocks.Add(new Paragraph(new Run(content.ToString())));
            richTextBox.IsDocumentEnabled = true;
            
            // Make read-only by preventing text input while allowing navigation
            richTextBox.PreviewTextInput += (s, e) => e.Handled = true;
            richTextBox.PreviewKeyDown += (s, e) =>
            {
                // Block editing keys but allow navigation
                if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Space ||
                    (e.Key >= Key.A && e.Key <= Key.Z) || (e.Key >= Key.D0 && e.Key <= Key.D9))
                {
                    e.Handled = true;
                }
            };
            
            AutomationProperties.SetName(richTextBox, "About information. Use arrow keys to navigate.");
            Grid.SetRow(richTextBox, 1);
            grid.Children.Add(richTextBox);

            // GitHub button
            var githubBtn = new Button
            {
                Content = "Visit GitHub Profile",
                Height = 35,
                Margin = new Thickness(0, 5, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 13,
                Padding = new Thickness(20, 5, 20, 5)
            };
            githubBtn.Click += (s, args) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://github.com/ibrasonic",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            Grid.SetRow(githubBtn, 2);
            grid.Children.Add(githubBtn);

            // Close button
            var closeBtn = new Button
            {
                Content = "Close",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            closeBtn.Click += (s, args) => aboutWindow.Close();
            Grid.SetRow(closeBtn, 3);
            grid.Children.Add(closeBtn);

            aboutWindow.Content = grid;
            aboutWindow.Loaded += (s, args) => richTextBox.Focus();
            aboutWindow.ShowDialog();
        }

        private void EscalateToAdministrator()
        {
            try
            {
                // Check if already running as administrator
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                bool isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

                if (isAdmin)
                {
                    TerminalTextBox.AppendText(Environment.NewLine);
                    TerminalTextBox.AppendText("Already running as Administrator.");
                    TerminalTextBox.AppendText(Environment.NewLine);
                    TerminalTextBox.AppendText(currentPrompt);
                    promptStartPosition = TerminalTextBox.Text.Length;
                    TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                    AnnounceToScreenReader("Already running as Administrator");
                    return;
                }

                // Prompt for UAC elevation
                var result = MessageBox.Show(
                    "This will restart the terminal with Administrator privileges.\\n\\nContinue?",
                    "Elevate to Administrator",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "AccessibleTerminal.exe",
                        UseShellExecute = true,
                        Verb = "runas" // Request administrator privileges
                    };

                    try
                    {
                        Process.Start(startInfo);
                        Application.Current.Shutdown();
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // User cancelled UAC prompt
                        TerminalTextBox.AppendText(Environment.NewLine);
                        TerminalTextBox.AppendText("Administrator elevation cancelled.");
                        TerminalTextBox.AppendText(Environment.NewLine);
                        TerminalTextBox.AppendText(currentPrompt);
                        promptStartPosition = TerminalTextBox.Text.Length;
                        TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                        AnnounceToScreenReader("Administrator elevation cancelled");
                    }
                }
                else
                {
                    TerminalTextBox.AppendText(Environment.NewLine);
                    TerminalTextBox.AppendText("Administrator elevation cancelled.");
                    TerminalTextBox.AppendText(Environment.NewLine);
                    TerminalTextBox.AppendText(currentPrompt);
                    promptStartPosition = TerminalTextBox.Text.Length;
                    TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                }
            }
            catch (Exception ex)
            {
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText($"Error: {ex.Message}");
                TerminalTextBox.AppendText(Environment.NewLine);
                TerminalTextBox.AppendText(currentPrompt);
                promptStartPosition = TerminalTextBox.Text.Length;
                TerminalTextBox.CaretIndex = TerminalTextBox.Text.Length;
                AnnounceToScreenReader($"Error elevating to administrator: {ex.Message}");
            }
        }
    }
}
