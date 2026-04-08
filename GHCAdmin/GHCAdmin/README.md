# ToDo — GitHub Copilot Task Manager

A simple to-do solution to help you manage your ever-growing and complex ToDo items. This can be your main ToDo list or a "table of contents" with pointers to your lists, OneNotes, ADO queries, etc. Using GitHub Copilot in Visual Studio Code, you can leverage the LLM of your choice to have a defined personality with foundational, "universal" rules and a separate markdown file that is managed by GitHub Copilot (or you if you feel like jumping in).

## What the Install Script Did

Running the installer (`irm https://aka.ms/GHCAdmin | iex`) made the following changes on your machine:

### Prerequisites Installed (if missing)

- **Visual Studio Code** — installed to `%LOCALAPPDATA%\Programs\Microsoft VS Code` and added to PATH

### Files Created in OneDrive

A folder was created inside your OneDrive Commercial directory (default: `Documents\Copilot\ToDo`). It contains:

| File | Purpose |
|------|---------|
| `TODO.md` | Your structured task list with Tasks, Projects, Recurring, and Completed sections |
| `README.md` | This file — your getting-started guide |
| `.github/copilot-instructions.md` | Personality and rules that GitHub Copilot follows when managing your tasks |

### Desktop Shortcut

A shortcut named **GHCAdmin ToDo** was placed on your Desktop. Double-clicking it opens VS Code directly to your ToDo folder.

## First Steps

### 1. Open the Folder in VS Code

Use any of these methods:

- Double-click the **GHCAdmin ToDo** shortcut on your Desktop
- Run in PowerShell: `code "$env:OneDriveCommercial\Documents\Copilot\ToDo"` (adjust the path if you chose a custom location)

### 2. Start a Copilot Chat Session

Open the Copilot Chat panel in VS Code (`Ctrl+Alt+I`). The `.github/copilot-instructions.md` file is picked up automatically — Copilot already knows how to manage your tasks.

### 3. Add Your First Task

Ask Copilot something like:

- *"Add a task: Finish quarterly report, due 2026-04-15, high priority"*
- *"Add a project: Plan team offsite — sub-tasks: book venue, send invites, arrange catering"*
- *"Add a recurring item: Submit timesheet, weekly, next due Monday"*

Copilot will place the item in the correct section of `TODO.md` and ask for any missing details (due date, priority, etc.).

### 4. Review Your Tasks

Ask Copilot to review your list:

- *"Review my tasks"* — get a summary by section and priority, with overdue items highlighted
- *"What's overdue?"* — see only past-due items
- *"Sort my tasks"* — reorder by priority and due date

### 5. Complete and Archive Tasks

When you finish something, tell Copilot:

- *"Mark 'Finish quarterly report' as done"* — Copilot checks the box and moves it to the Completed section
- Recurring items are never archived — Copilot unchecks them and advances the next-due date automatically

## TODO.md Structure

Your `TODO.md` is organized into four sections:

- **Tasks** — one-off items with a due date and priority (High / Medium / Low)
- **Projects** — multi-step efforts with indented sub-task checkboxes
- **Recurring** — repeating items (Weekly, Monthly, etc.) that reset automatically
- **Completed** — archive of finished tasks and projects

## License

This project is licensed under the [MIT License](LICENSE).
