# Accessible Terminal

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey.svg)
![Accessibility](https://img.shields.io/badge/accessibility-WCAG%202.1-green.svg)

A professional, feature-rich Windows terminal application designed from the ground up with **full accessibility support** for screen readers and assistive technologies. Built for developers, system administrators, and power users who rely on keyboard navigation and screen readers like NVDA, JAWS, and Windows Narrator.

---

## 🌟 Key Highlights

- ✅ **Full Screen Reader Support** - Optimized for NVDA, JAWS, and Windows Narrator with live announcements
- ✅ **Multi-Shell Environment** - PowerShell, CMD, and Bash with seamless switching
- ✅ **46+ Unix Commands** - Native Unix command support on Windows without WSL
- ✅ **Advanced Navigation** - Structural output navigation, bookmarks, and error jumping
- ✅ **Reading Mode** - Navigate terminal output like a document with arrow keys
- ✅ **Smart Folding** - Auto-collapse large outputs with intelligent summaries
- ✅ **Zero Installation** - Portable, self-contained executable with all dependencies

---

## 📋 Table of Contents

- [Features](#-features)
- [Installation](#-installation)
- [Quick Start](#-quick-start)
- [Keyboard Shortcuts](#-keyboard-shortcuts-reference)
- [Built-in Commands](#-built-in-commands)
- [Shell Modes](#-shell-modes)
- [Accessibility Features](#-accessibility-features-in-detail)
- [Advanced Features](#-advanced-features)
- [Building from Source](#-building-from-source)
- [Technical Details](#-technical-details)
- [Contributing](#-contributing)
- [License](#-license)

---

## ✨ Features

### 🎯 Core Functionality

- **Multi-Shell Support**
  - PowerShell (default) with full cmdlet support
  - Windows Command Prompt (CMD)
  - Bash with 46+ bundled Unix binaries
  - Seamless switching between shells without restarting

- **Intelligent Auto-Completion**
  - Tab completion for commands and file paths
  - Context-aware suggestions
  - Bash mode uses forward slashes (`/`), PowerShell/CMD use backslashes (`\`)
  - Multi-match display when multiple options exist

- **Command History**
  - Up/Down arrow navigation through previous commands
  - Persistent history within session
  - Smart deduplication (doesn't repeat identical consecutive commands)
  - Maximum 1000 command history buffer

- **Protected Prompt**
  - Cannot accidentally delete the command prompt
  - Backspace/Delete only work on user input
  - Home key jumps to start of command (after prompt)

### ♿ Accessibility Features

- **Full Screen Reader Integration**
  - Real-time output announcements via UIA Notification events
  - Context-aware announcements (avoids information overload)
  - All UI elements properly labeled with AutomationProperties
  - Works with NVDA, JAWS, and Windows Narrator

- **Reading Mode (Ctrl+R)**
  - Toggle between input mode and reading mode
  - Navigate output with arrow keys like reading a document
  - Page Up/Down support for quick scrolling
  - Automatic focus management

- **High Contrast Theme (Ctrl+H)**
  - Toggle between standard and high contrast color schemes
  - Yellow text on black background (high contrast mode)
  - Black text on white background (standard mode)
  - Optimized for visual accessibility

- **Keyboard-Only Operation**
  - Complete functionality without mouse
  - All features accessible via keyboard shortcuts
  - Logical key combinations following Windows conventions

- **Smart Output Announcements**
  - Full output text announced automatically
  - Error messages announced immediately
  - Folded output announces line count and summary
  - Bookmark navigation announces name and context

### 🔧 Advanced Features

#### Output Management

- **Automatic Folding**
  - Auto-folds outputs exceeding 50 lines
  - Detects and folds stack traces automatically
  - Intelligent summaries for folded content
  - Ctrl+F to expand/collapse current block
  - Ctrl+D to unfold all blocks

- **Structural Navigation**
  - Ctrl+N: Jump to next output block
  - Ctrl+P: Jump to previous output block
  - Ctrl+E: Jump to next error in output
  - Clear visual separation between commands and outputs

- **Output Categorization**
  - Automatic categorization: Errors, Warnings, Status Messages
  - Ctrl+O to view categorized output panels
  - Timestamp tracking for all categorized items
  - Maximum 500 items per category (auto-cleanup)

#### Bookmarking System

- **Custom Bookmarks**
  - Ctrl+B: Add bookmark at current position with custom name
  - Ctrl+J: Jump to next bookmark
  - Ctrl+K: Jump to previous bookmark
  - Each bookmark stores: Position, Custom Label, Context (surrounding text)

- **Bookmark Navigation**
  - Wraps around (first → last, last → first)
  - Announces bookmark name and context when navigating
  - Automatic cleanup of invalid bookmarks

#### Keyword Monitoring

- **Real-time Keyword Tracking**
  - Monitor specific keywords in command output
  - Automatic screen reader announcement when keyword detected
  - Predefined keywords: error, exception, failed, timeout, warning, success, connected, completed

- **Keyword Management**
  - Ctrl+M: Open keyword manager
  - `monitor add <keyword>`: Add new keyword
  - `monitor remove <keyword>`: Remove keyword
  - `monitor clear`: Clear all keywords
  - `monitor list`: List all monitored keywords

### 🎨 Display & Customization

- **Adjustable Font Sizes**
  - Ctrl+Plus: Increase font size (range: 12-32pt)
  - Ctrl+Minus: Decrease font size
  - Ctrl+0: Reset to default (14pt)

- **Clipboard Integration**
  - Ctrl+C: Copy selected text
  - Ctrl+V: Paste from clipboard

- **Memory Management**
  - Auto-truncates terminal buffer at 500,000 characters
  - Prevents application slowdown with long-running processes

---

## 📦 Installation

### Prerequisites

- **Operating System**: Windows 10 (version 1809+) or Windows 11
- **Runtime**: .NET 8.0 Runtime (included in self-contained build)
- **Disk Space**: ~150 MB for application and dependencies

### Download & Run

1. **Download the latest release** from the Releases page
2. **Extract the archive** to any location (e.g., `C:\Tools\AccessibleTerminal`)
3. **Run `AccessibleTerminal.exe`** - No installation or admin rights required

### Portable Installation

The application is fully portable:
- Copy the entire folder to any location (USB drive, network share, etc.)
- All Unix binaries are included in `bin/unix/` folder
- No registry modifications or system dependencies

---

## 🚀 Quick Start

### First Launch

1. Launch `AccessibleTerminal.exe`
2. You'll start in **PowerShell mode** by default
3. Type `help` and press Enter to see all available commands
4. Press `Ctrl+R` to toggle Reading Mode and explore with arrow keys

### Basic Usage Examples

```powershell
# Display help information
help

# Clear the terminal screen
clear

# Switch to Bash mode (Unix commands)
bash

# List files in current directory (Bash)
ls -la

# Change directory
cd Documents

# Create a new directory
mkdir NewFolder

# Search for text in files
grep "search term" *.txt

# Switch back to PowerShell
powershell

# Use PowerShell cmdlets
Get-Process | Where-Object {$_.CPU -gt 100}

# Exit the application
exit
```

### Using Tab Completion

```bash
# Type partial path and press Tab
cd Doc[TAB] → cd Documents\

# Complete with multiple matches
cd D[TAB] → Shows: Desktop, Documents, Downloads

# Navigate paths
cd Documents\Pro[TAB] → cd Documents\Projects\
```

---

## ⌨️ Keyboard Shortcuts Reference

### Essential Shortcuts

| Shortcut | Action | Description |
|----------|--------|-------------|
| `Enter` | Execute command | Run the current command |
| `Up/Down` | Command history | Navigate through previous commands |
| `Tab` | Auto-complete | Complete commands and paths |
| `Ctrl+L` | Clear screen | Clear all terminal output |
| `Ctrl+C` | Copy | Copy selected text to clipboard |
| `Ctrl+V` | Paste | Paste text from clipboard |
| `Home` | Start of command | Jump to beginning of current command |

### Reading & Navigation

| Shortcut | Action | Description |
|----------|--------|-------------|
| `Ctrl+R` | Toggle Reading Mode | Enable/disable arrow key navigation of output |
| `Ctrl+N` | Next block | Jump to next output block |
| `Ctrl+P` | Previous block | Jump to previous output block |
| `Ctrl+E` | Next error | Jump to next error in output |
| `Arrow Keys` | Navigate (Reading Mode) | Move through output when Reading Mode is on |
| `Page Up/Down` | Scroll (Reading Mode) | Quick scrolling when Reading Mode is on |

### Output Management

| Shortcut | Action | Description |
|----------|--------|-------------|
| `Ctrl+F` | Toggle fold/expand | Fold or expand current output block |
| `Ctrl+D` | Unfold all | Expand all folded output blocks |

### Bookmarks

| Shortcut | Action | Description |
|----------|--------|-------------|
| `Ctrl+B` | Add bookmark | Create bookmark at current position with custom name |
| `Ctrl+J` | Next bookmark | Jump to next bookmark (wraps around) |
| `Ctrl+K` | Previous bookmark | Jump to previous bookmark (wraps around) |

### Display & Appearance

| Shortcut | Action | Description |
|----------|--------|-------------|
| `Ctrl+H` | Toggle High Contrast | Switch between standard and high contrast themes |
| `Ctrl+Plus` | Increase font size | Make text larger (max: 32pt) |
| `Ctrl+Minus` | Decrease font size | Make text smaller (min: 12pt) |
| `Ctrl+0` | Reset font size | Return to default size (14pt) |

### Advanced Features

| Shortcut | Action | Description |
|----------|--------|-------------|
| `Ctrl+M` | Keyword manager | Open keyword monitoring manager |
| `Ctrl+O` | Output panels | Show categorized output (errors/warnings/status) |

---

## 🔤 Built-in Commands

### Terminal Control Commands

| Command | Description |
|---------|-------------|
| `help` | Display comprehensive help with all shortcuts and features |
| `clear` or `cls` | Clear the terminal screen |
| `exit` or `quit` | Close the application |

### Shell Switching Commands

| Command | Description |
|---------|-------------|
| `bash` | Switch to Bash mode with Unix commands |
| `powershell` or `pwsh` | Switch to PowerShell mode |
| `cmd` | Switch to Windows Command Prompt mode |

### Privilege Escalation

| Command | Description |
|---------|-------------|
| `sudo su` or `sudo` | Restart application with administrator rights (UAC prompt) |

### Keyword Monitoring Commands

| Command | Description |
|---------|-------------|
| `monitor add <keyword>` | Add keyword to monitoring list |
| `monitor remove <keyword>` | Remove keyword from monitoring |
| `monitor list` | Display all monitored keywords |
| `monitor clear` | Remove all monitored keywords |

---

## 🐚 Shell Modes

### PowerShell Mode (Default)

**Features:**
- Full access to PowerShell cmdlets and scripts
- Object-based pipeline processing
- .NET Framework integration

**Examples:**
```powershell
Get-Process | Sort-Object CPU -Descending
Get-ChildItem -Recurse -Filter "*.txt"
```

**Path Completion:** Uses backslashes (`\`)

### CMD Mode

**Features:**
- Classic Windows command prompt
- Batch file support
- Legacy DOS commands

**Examples:**
```cmd
dir /s /b *.txt
ipconfig /all
```

**Path Completion:** Uses backslashes (`\`)

### Bash Mode

**Features:**
- 46+ bundled Unix commands (no WSL required)
- Unix-style pipes and redirection
- Shell scripting support

**Examples:**
```bash
ls -la | grep "\.txt"
find . -name "*.log"
cat file.txt | sed 's/old/new/g'
```

**Path Completion:** Uses forward slashes (`/`)

**Note:** Windows executables (like `code`, `npm`, `node`) are automatically detected and routed through CMD.

---

## ♿ Accessibility Features in Detail

### Screen Reader Support

**Supported Screen Readers:**
- NVDA (NonVisual Desktop Access) - Recommended
- JAWS (Job Access With Speech)
- Windows Narrator

**Announcement System:**
- Uses UIA (UI Automation) Notification events
- Immediate announcements for all command output
- Context-aware messages
- Full output text announced (not truncated)

**What Gets Announced:**
- Complete command output
- Error messages
- Shell mode changes
- Bookmark navigation with name and context
- Fold/unfold actions with line count
- All keyboard shortcut activations

### Reading Mode

**How it works:**
1. Press `Ctrl+R` to enable Reading Mode
2. Use arrow keys to navigate through output
3. Screen reader reads each line as you move
4. Press `Ctrl+R` again to return to command input

**Benefits:**
- Read output at your own pace
- Navigate back to previous information
- Skip through content quickly

---

## 🔧 Advanced Features

### Output Block Detection

The terminal automatically detects and categorizes output:

**Block Types:**
- Normal: Regular command output
- Error: Error messages and exceptions
- Stack Trace: Multi-line stack traces (auto-folded)
- JSON: JSON formatted data
- Large Text: Outputs exceeding 50 lines (auto-folded)

### Smart Folding

**Automatic Folding Triggers:**
- Output exceeds 50 lines
- Stack trace detected
- Large JSON responses

**Manual Control:**
- Ctrl+F: Toggle fold/expand current block
- Ctrl+D: Unfold all blocks at once

### Output Categorization

Access categorized output with `Ctrl+O`:

**Error Panel:** All error messages and exceptions with timestamps
**Warning Panel:** Warning messages and deprecation notices
**Status Panel:** Success messages and completion notifications

---

## 🏗️ Building from Source

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows 10/11 (x64)
- Git

### Clone and Build

```bash
git clone https://github.com/ibrasonic/AccessibleTerminal.git
cd AccessibleTerminal

# Build Release version
dotnet build -c Release

# Publish self-contained executable
dotnet publish -c Release -r win-x64 --self-contained -o MyApp
```

The published application will be in the `MyApp` folder.

---

## 🛠️ Technical Details

### Architecture

**Framework:** .NET 8.0 with Windows Presentation Foundation (WPF)
**Language:** C# 12

**Key Components:**
- **MainWindow.xaml.cs** (2,876 lines) - UI logic, screen reader integration, features
- **CommandExecutor.cs** (393 lines) - Multi-shell command execution
- **MainWindow.xaml** - WPF UI layout with AutomationProperties

### UI Automation Implementation

**Announcement Mechanism:**
```csharp
// Uses UIA Notification event for reliable screen reader announcements
peer.RaiseNotificationEvent(
    AutomationNotificationKind.ActionCompleted,
    AutomationNotificationProcessing.MostRecent,
    message,
    activityId
);
```

### Bundled Unix Commands

**Location:** `bin/unix/` (365 files, 0.20 MB)

**46 Core Commands:**
- File operations: `ls`, `cp`, `mv`, `rm`, `mkdir`, `touch`, `cat`, `find`
- Text processing: `grep`, `sed`, `awk`, `cut`, `sort`, `uniq`, `wc`, `head`, `tail`
- System utilities: `ps`, `kill`, `chmod`, `chown`, `du`, `df`
- Networking: `curl`, `wget`, `ssh`, `scp`
- Compression: `tar`, `gzip`, `gunzip`, `zip`, `unzip`
- Editors: `vim`, `nano`
- And more...

---

## 🤝 Contributing

Contributions are welcome! We especially appreciate improvements to accessibility features.

### How to Contribute

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/your-feature`
3. Make your changes and test thoroughly
4. Test with screen readers (NVDA, JAWS, Narrator)
5. Commit: `git commit -m "Add: Description"`
6. Push: `git push origin feature/your-feature`
7. Open a Pull Request

### Testing Checklist
- [ ] Tested with NVDA screen reader
- [ ] All keyboard shortcuts work
- [ ] Reading mode functions correctly
- [ ] High contrast mode displays properly
- [ ] Commands execute in all three shell modes

---

## 📄 License

MIT License - Copyright (c) 2025 Ibrahim Badawy (@IBRASONIC)

Permission is granted to use, copy, modify, and distribute this software for any purpose with or without fee.

See full license text in the source files.

---

## 👤 Author

**Ibrahim Badawy** ([@IBRASONIC](https://github.com/ibrasonic))

- 📧 **Email:** ibrahim.m.badawy@gmail.com
- 🐙 **GitHub:** [github.com/ibrasonic](https://github.com/ibrasonic)

---

## 🙏 Acknowledgments

- **Microsoft .NET Team** - For the excellent .NET 8.0 framework and WPF
- **Git for Windows** - For providing Unix binaries
- **NVDA Community** - For accessibility testing and feedback

---

## 📊 Project Statistics

| Metric | Value |
|--------|-------|
| **Total Code Lines** | 3,269 lines (C#) |
| **Framework** | .NET 8.0 WPF |
| **Published Size** | ~150 MB (self-contained) |
| **Unix Commands** | 46 commands (365 files) |
| **Supported Shells** | 3 (PowerShell, CMD, Bash) |
| **Keyboard Shortcuts** | 25+ combinations |
| **Screen Readers** | 3 (NVDA, JAWS, Narrator) |

---

## 📞 Support & Links

- **Issues:** [GitHub Issues](https://github.com/ibrasonic/AccessibleTerminal/issues)
- **Discussions:** [GitHub Discussions](https://github.com/ibrasonic/AccessibleTerminal/discussions)
- **Email:** ibrahim.m.badawy@gmail.com

---

<div align="center">

**Made with ❤️ and ⌨️ for accessibility and productivity**

If you find this project helpful, please consider giving it a ⭐ on GitHub!

</div>
