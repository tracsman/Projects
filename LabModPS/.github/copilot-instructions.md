# Project Guidelines

## Overview

LabMod is a PowerShell 7+ module for Pathfinder Lab on-prem Hyper-V VM management. It follows a Public/Private function split pattern with dot-sourced `.ps1` files.

## Code Style

- All functions use `Verb-LabNoun` naming with the `Lab` prefix (e.g. `New-LabVM`, `Remove-LabECX`)
- Use only PowerShell [approved verbs](https://learn.microsoft.com/en-us/powershell/scripting/developer/cmdlet/approved-verbs-for-windows-powershell-commands)
- All public functions must use `[CmdletBinding()]` and include comment-based help (`.SYNOPSIS`, `.DESCRIPTION`, `.PARAMETER`, `.EXAMPLE`)
- Use validation attributes on parameters: `[ValidateRange()]`, `[ValidateSet()]`, `[Parameter(Mandatory)]`
- Use `$script:` scope for module-level state variables

## Architecture

- `LabMod.psm1` — module root, dot-sources Private and Public folders, defines `Write-Log` helper
- `Private/` — internal helpers not exported (e.g. `Assert-LabAdminContext`, `Write-LabLogEntry`)
- `Public/` — exported functions, one per file, file name matches function name
- `LabMod.psd1` — module manifest; update `FunctionsToExport` when adding public functions

## Conventions

- Two-tier logging: `Write-Log` for user-facing output, `Write-LabLogEntry` for structured JSON logging to `*.log.jsonl`
- Interactive vs automation awareness: use `$script:IsInteractive` to branch between `Write-Host` and `Write-Verbose`
- Use numbered section comments (`# 1. Initialize`, `# 2. Validate`) and `# region:`/`# endregion` blocks for logical flow
- Suppress unwanted output with `| Out-Null`
- Track operation duration with `$StartTime`/`$EndTime` and `New-TimeSpan`
- Use `throw` for terminating errors in assertion/validation functions

## Project Guidelines
- The workspace root is Q:\Bin\git\Projects (the Git repo), but the LabModPS project is at Q:\Bin\git\Projects\LabModPS. When creating files, use paths relative to the project folder.
- At the start of a new conversation, read TODO.md in the LabModPS project folder (Q:\Bin\git\Projects\LabModPS\TODO.md) for the current action-item list, completed work, and project notes. Update it as items are completed or new work is identified.
