# GHCAdmin — GitHub Copilot Administrative Helper

A deployable toolkit that sets up a GitHub Copilot–powered personal task-management assistant in Visual Studio Code. The `GHCAdmin/` folder is copied to a OneDrive Commercial location, initialized as a Git repo, and used with GitHub Copilot to manage your TODO list with an opinionated, AI-assisted workflow.

## What Gets Deployed

| File | Purpose |
|------|---------|
| `.github/copilot-instructions.md` | Copilot personality and TODO management rules |
| `TODO.md` | Structured task list (Tasks, Projects, Recurring, Completed) |
| `README.md` | End-user getting-started guide |

## Prerequisites

- [Visual Studio Code](https://code.visualstudio.com/) with the GitHub Copilot extension
- [Git for Windows](https://git-scm.com/download/win) (or macOS/Linux equivalent)
- OneDrive for Work (Microsoft) installed and syncing

## Installation

Run the included installer from an elevated or standard PowerShell prompt:

```powershell
.\Install.ps1
```

The script will:

1. Create the target folder under your OneDrive Commercial directory
2. Copy the `GHCAdmin/` contents into it
3. Initialize a Git repository so Copilot can operate on the files

See `Install.ps1 -Help` for options.

## Usage

1. Open the deployed folder in VS Code
2. Start a Copilot Chat session — the `.github/copilot-instructions.md` file is picked up automatically
3. Ask Copilot to add, review, or reorganize tasks in `TODO.md`

## Project Structure

```
GHCAdmin/              # Source files copied to the user's machine
  .github/
    copilot-instructions.md
  README.md
  TODO.md
Media/                 # Screenshots and documentation assets
Install.ps1            # Deployment script
README.md              # This file (repo-level)
```

## License

This project is licensed under the [MIT License](LICENSE).
