LogicApp__WorkflowResourceId# PathWeb

Internal lab management application for the Pathfinders team. Manages tenant environments, network device configuration, IP address allocation, and user administration across the Seattle and Ashburn labs.

## Tech Stack

- **.NET 10** / ASP.NET Core MVC with Razor views
- **Entity Framework Core 10** with Azure SQL Database (Entra ID auth)
- **Azure App Service** with Easy Auth (Microsoft Entra ID)
- **SSH.NET** for network device connectivity (Juniper, Cisco NX-OS, IOS-XE)
- **Azure Key Vault** for device credentials
- **Azure Logic Apps** for ADO work item integration
- **Bootstrap 5** with dark mode support

## Features

- **Tenant Management** — Create, edit, release lab tenant environments with auto-assigned Tenant IDs
- **Config Generation** — Auto-generate device configs (routers, switches, firewalls) and PowerShell scripts per tenant
- **Device Operations** — SSH to network devices to verify, compare, apply, and remove tenant configs
- **IP Address Management** — Allocate and release public IP ranges across labs
- **User & Auth** — Database-driven auth levels with Easy Auth in production, DevUser identity for local dev
- **Field Help Tooltips** — Database-driven contextual help via custom Tag Helper
- **ADO Integration** — Sync tenant changes to Azure DevOps work items via Logic Apps

## Running Locally

```bash
dotnet run
```

Local dev uses a fake identity from the `DevUser` setting in `appsettings.Development.json` — no Entra ID setup required.

## Deploying to Azure

```powershell
.\quick-deploy.ps1
```

Builds, packages, and pushes to the existing Azure App Service. The full `deploy-to-azure.ps1` script handles resource provisioning if needed.

## Logic App connection diagnostics

When an email or Azure DevOps Logic App request fails, PathWeb can read the current status of that workflow's API connections and suggest reauthorization when Azure reports an authentication error. The email result appears in the existing send modal and saved status; Azure DevOps errors appear in the existing approval or tenant-edit warning. This checks Logic Apps API connections, not every external service the application uses. A connection's current status is diagnostic evidence, not proof it caused the request failure.

Configure the following App Service settings to the **resource IDs of the exact workflows behind the corresponding trigger URLs** (copy each Resource ID from the workflow's Azure Portal Properties page):

- `LogicApp__EmailWorkflowResourceId` for `LogicApp__EmailTriggerUrl`.
- `LogicApp__WorkflowResourceId` for `LogicApp__TriggerUrl` (Azure DevOps).

Grant the App Service managed identity read access to each configured `Microsoft.Logic/workflows` resource and each API connection it uses (`Microsoft.Web/connections`). A scoped Reader role on those resources is one option. Do not grant write or reauthorization permissions for this feature. If configuration, permissions, or the check itself is unavailable, the original error remains visible with instructions to inspect run history; the app does not guess that authentication failed. Changing workflow connections or trigger URLs requires updating these settings and access as needed.

The check does not send emails, retry requests, reset credentials, or change resources. A failed or interrupted request can have partially completed: check the inbox and sent mailbox, or the Azure DevOps work item, before retrying. Workflow run history remains the source for the specific failure. Existing Logic App definitions need no changes.
