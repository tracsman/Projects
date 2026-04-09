# Copilot Instructions

## Project Guidelines
- User's Azure SQL Database: Server: labconfig.database.windows.net, Database: LabConfig. Tables needed: Config, PublicIP, Regions, Settings, Tenant, Users. User connects with Entra ID auth using jonor@microsoft.com account. Always use modern Entra ID-based authentication (Managed Identity, delegated tokens, or DefaultAzureCredential) for Azure DevOps authentication. Never suggest or use PATs (Personal Access Tokens).
- The workspace root is Q:\Bin\git\Projects (the Git repo), but the PathWeb project is at Q:\Bin\git\Projects\PathWeb. When creating files, use paths relative to the project folder like "Areas/About/Views/Home/Lab.cshtml" instead of "PathWeb/Areas/About/Views/Home/Lab.cshtml". The create_file tool uses the project folder as the base, so file paths should not include the "PathWeb/" prefix. Including this prefix will create a nested PathWeb/PathWeb directory. The replace_string_in_file and get_file tools can find existing files with either path format.
- At the start of a new conversation, read TODO.md in the PathWeb project folder (Q:\Bin\git\Projects\PathWeb\TODO.md) for the current action-item list, completed work, and project notes. Update it as items are completed or new work is identified.
- The Tenant metadata/display label for the temporary pre-save value represented by ConfigVersion=0 should remain 'Server Preference'; ConfigVersion is persisted terminology, but the create/edit UI concept here is still Server Preference and it is not saved to SQL in that form.
- The project should use SQL-backed logging settings with a global key plus per-category overrides using the `Logging:<category>` key structure.
- For PathWeb lab VM work, reuse the same username/password Key Vault secret pattern as network devices for on-prem servers, and identify Hyper-V servers by the `<lab>-ER-xx` naming convention (look for `-ER-`).
- For the Lab VM spike, do not add persistence/history; run the full `New-LabVM` command (not a reduced safe-mode variant), target only `SEA-ER-08`, and use the `LabVMPowerShell` config from Seattle Tenant 18 as the test input source from SQL. The admin Key Vault name is `LabSecrets` (not `LabInfrastructure`) for the `Server-Admin` secret.
- For Lab VM work, add `Start-LabVmRequest` in `LabMod` with default log/status paths, no `VMInstance`, no explicit `Import-Module`, and move common admin validation and logging into private module helpers; replace routine `Write-Host` with structured/loggable output across module cmdlets over time.
- For Lab VM work, writing passwords to disk is a non-starter; avoid any design that persists credentials in local wrapper scripts or files. Additionally, for Server 08, passwords must be passed in by PathWeb at runtime since it cannot reach Key Vault.

## Naming Conventions
- Use Azure connector/resource naming to follow the pattern `PathWeb-<purpose>-conn`, e.g., `PathWeb-ADO-conn`, instead of generic connector names.

## Workflow Automation Preferences
- Prefer Azure Logic Apps over Power Automate for ADO integration workflow automation.

## File Manipulation Guidelines
- When using replace_string_in_file, always include enough surrounding context (the full HTML tag/line at minimum) to avoid matching just a substring inside an attribute value and corrupting the markup.

## Azure Automation Guidelines
- The custom Azure Automation module `LabMod` is only available in the PowerShell 7.2 runtime. Do not hardcode the Azure Automation runbook type; read it from the SQL `Settings` table row where `SettingName = 'AutomationRunbookType'` (currently `PowerShell72`). Any Automation runbooks that need `LabMod` must target that configured runbook type. Azure Automation runbook execution should be driven by the SQL `Settings` row with `SettingName='AutomationRunbookType'` (for example `PowerShell72`).
- Azure Automation auto-loads the `LabMod` module in the runbook session, so explicit `Import-Module LabMod` preamble code should not be added.
