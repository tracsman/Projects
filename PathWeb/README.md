# PathWeb

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
