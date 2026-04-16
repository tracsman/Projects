# PathWeb — Architecture Diagrams

> Generated from the codebase structure. Render these Mermaid diagrams on GitHub, in VS Code (Mermaid extension), or paste into [mermaid.live](https://mermaid.live) to export as SVG/PNG.

---

## 1. High-Level System Architecture

```mermaid
graph TB
    subgraph "Client"
        Browser["Browser<br/>(Bootstrap 5 + vanilla JS)"]
    end

    subgraph "Azure App Service"
        AppService["PathWeb<br/>.NET 10 Razor Pages + MVC"]
        EasyAuth["Easy Auth<br/>(Entra ID)"]
    end

    subgraph "Azure Data & Identity"
        SQL["Azure SQL Database<br/>(LabConfig)"]
        KV["Azure Key Vault<br/>(LabSecrets + per-tenant)"]
        EntraID["Microsoft Entra ID"]
    end

    subgraph "Azure Automation"
        Automation["Azure Automation Account<br/>(PowerShell 7.2 runbooks)"]
        LabMod["LabMod Module<br/>(New-LabVM, New-LabECX, etc.)"]
    end

    subgraph "On-Prem Infrastructure"
        Routers["Juniper MX / SRX<br/>Cisco NX-OS / IOS-XE"]
        HyperV["Hyper-V Servers<br/>(SEA-ER-01..08+)"]
    end

    subgraph "External Services"
        LogicApp["Azure Logic App<br/>(Email via dedicated mailbox)"]
    end

    Browser -->|HTTPS| EasyAuth --> AppService
    AppService -->|EF Core + Entra ID auth| SQL
    AppService -->|DefaultAzureCredential| KV
    AppService -->|REST API| Automation
    Automation --- LabMod
    AppService -->|SSH<br/>Renci.SshNet| Routers
    AppService -->|SSH<br/>Renci.SshNet| HyperV
    AppService -->|HTTP webhook| LogicApp
    EasyAuth -.->|token validation| EntraID
```

---

## 2. Controller & Service Dependency Graph

```mermaid
graph LR
    subgraph "Controllers"
        TC[TenantsController]
        AC[AutomationController]
        DAC[DeviceActionsController]
        DC[DevicesController]
        LVC[LabVmController]
        EC[EmailController]
        LC[LogsController]
        SC[SettingsController]
        RC[RequestsController]
        AdC[AddressesController]
        HC[HomeController]
        AbC[AboutController]
        DiagC[DiagnosticsController]
        TTC[ToolTipsController]
    end

    subgraph "Services"
        CG[ConfigGenerator]
        AS[AutomationService]
        SSH[SshService]
        LA[LogicAppService]
        Auth[AuthLevelService]
        SS[SettingsService]
        Tracker[LabVmRunTracker]
        PD[PlatformDetector]
    end

    subgraph "Infrastructure"
        DB[(LabConfigContext<br/>EF Core)]
        KV[SecretClient<br/>Key Vault]
        Logger[DbLoggerProvider]
    end

    TC --> CG & Auth & DB
    AC --> AS & DB & SS
    DAC --> SSH & DB & Auth
    DC --> DB & SSH & KV
    LVC --> SSH & KV & DB & Tracker
    EC --> LA & DB
    LC --> DB
    SC --> DB & SS
    RC --> DB
    AdC --> DB
    DiagC --> DB & SS & KV

    CG --> DB
    AS --> KV & SS
    SSH --> KV
    LA --> DB
    Auth --> DB
    SS --> DB
    Logger --> DB
```

---

## 3. Data Model (Key SQL Tables)

```mermaid
erDiagram
    Tenant ||--o{ Config : "has configs"
    Tenant ||--o{ TenantRequest : "has requests"
    Tenant ||--o{ AutomationRun : "has runs"
    Tenant ||--o{ DeviceActionRun : "device applies"
    Tenant ||--o{ EmailSendRun : "email sends"
    Tenant ||--o{ LabVmRun : "VM runs"

    Tenant {
        int TenantId PK
        string TenantName
        int ConfigVersion
        datetime DeletedDate
    }

    Config {
        int Id PK
        int TenantId FK
        string ConfigType
        string ConfigData
    }

    AutomationRun {
        int Id PK
        int TenantId FK
        string RunbookName
        string Status
        datetime StartTime
    }

    DeviceActionRun {
        int Id PK
        int TenantId FK
        string DeviceName
        string Action
        string Status
    }

    LabVmRun {
        int Id PK
        int TenantId FK
        string Status
        string VmNames
        datetime CreatedDate
    }

    EmailSendRun {
        int Id PK
        int TenantId FK
        string Status
        datetime SentDate
    }

    Device {
        int Id PK
        string DeviceName
        string ManagementIp
    }

    Setting {
        int Id PK
        string SettingName
        string ProdVersion
    }

    AppLog {
        int Id PK
        string Level
        string Category
        string Message
        datetime Timestamp
    }

    User {
        int Id PK
        string Email
        int AuthLevel
    }
```

---

## 4. Config Card Actions Flow

```mermaid
flowchart TB
    subgraph "Config Page (per tenant)"
        Cards["Config Cards<br/>one per ConfigType"]
    end

    Cards -->|CreateERPowerShell| ER["Deploy to Azure<br/>(Automation runbook)"]
    Cards -->|CreateAzurePowerShell| AZ["Deploy to Azure<br/>(Automation runbook)"]
    Cards -->|ServiceProviderInstructions| SP["Provision at Provider<br/>(New-LabECX runbook)"]
    Cards -->|LabVMPowerShell| VM["Create Lab VMs<br/>(SSH to Hyper-V)"]
    Cards -->|Network Device configs| DEV["Apply / Patch / Remove<br/>(SSH to device)"]
    Cards -->|NotificationEmail| EM["Send Email<br/>(Logic App webhook)"]

    ER -->|Remove from Azure| AZ_OUT["Backout script<br/>via Automation"]
    AZ -->|Remove from Azure| AZ_OUT
    SP -->|Deprovision| SP_OUT["Remove-LabECX<br/>via Automation"]
    VM -->|Remove Lab VMs| VM_OUT["Scan + Remove-LabVM<br/>via SSH"]
    DEV -->|Remove from Device| DEV_OUT["Push backout config<br/>via SSH"]
```

---

## 5. SSH Operations Architecture

```mermaid
sequenceDiagram
    participant UI as Browser
    participant Ctrl as Controller
    participant SSH as SshService
    participant KV as Key Vault
    participant Dev as Network Device / Hyper-V

    UI->>Ctrl: Action request (Apply/Compare/Create VM)
    Ctrl->>KV: Get credentials (device or server-admin)
    KV-->>Ctrl: Username + Password
    Ctrl->>SSH: RunCommandAsync / RunPowerShellCommandAsync

    alt Network Device (Juniper/Cisco)
        SSH->>Dev: SSH connect (device creds)
        SSH->>Dev: show/configure commands via ShellStream
        Dev-->>SSH: Command output
    else Hyper-V Server
        SSH->>Dev: SSH connect (server-admin creds)
        SSH->>Dev: pwsh -EncodedCommand (Start-LabVmRequest / Remove-LabVM)
        Dev-->>SSH: Structured JSON via ---LABVM-JSON--- marker
    end

    SSH-->>Ctrl: Parsed results
    Ctrl-->>UI: JSON response (or fire-and-forget + poll)
```

---

## 6. Lab VM Fire-and-Forget Pattern

```mermaid
sequenceDiagram
    participant UI as Browser
    participant Ctrl as LabVmController
    participant Tracker as LabVmRunTracker<br/>(ConcurrentDictionary)
    participant BG as Background Task
    participant HV as Hyper-V Server (SSH)

    UI->>Ctrl: POST /Submit (VM list + server overrides)
    Ctrl->>Tracker: Create run entry (status: Running)
    Ctrl-->>UI: 200 OK (runId)
    Ctrl->>BG: Fire-and-forget Task.WhenAll (per-server)

    loop Per Hyper-V Server
        BG->>HV: SSH: Start-LabVmRequest / Remove-LabVM
        HV-->>BG: Per-VM JSON results
        BG->>Tracker: Update progress (per VM)
    end

    loop Polling (every 2s)
        UI->>Ctrl: GET /Status?runId=xxx
        Ctrl->>Tracker: Read current state
        Ctrl-->>UI: Per-VM status + progress
    end

    BG->>Tracker: Mark complete
    BG->>Ctrl: Persist to LabVmRun SQL table
```

---

## 7. Authentication & Authorization Flow

```mermaid
flowchart LR
    subgraph "Azure App Service"
        EasyAuth["Easy Auth<br/>(platform layer)"]
        Handler["EasyAuthAuthenticationHandler<br/>(reads X-MS-* headers)"]
        AuthSvc["AuthLevelService<br/>(DB lookup, 5-min cache)"]
    end

    User["User<br/>(Entra ID)"] -->|login| EasyAuth
    EasyAuth -->|X-MS-CLIENT-PRINCIPAL-NAME| Handler
    Handler -->|ClaimsPrincipal| AuthSvc
    AuthSvc -->|SELECT AuthLevel FROM Users| DB[(SQL Users table)]

    AuthSvc -->|TenantReadOnly| ReadOps["View configs/tenants"]
    AuthSvc -->|TenantAdmin ≥8| WriteOps["Apply, Deploy, Create VMs"]
```

---

## 8. Azure Automation Runbook Lifecycle

```mermaid
stateDiagram-v2
    [*] --> Creating: User clicks Deploy/Provision
    Creating --> Queued: PUT runbook + PUT job (REST API)
    Queued --> Running: Azure Automation scheduler
    Running --> Completed: Script finishes
    Running --> Failed: Script error
    Completed --> Cleanup: AutoDeleteRunbook=true?
    Failed --> Cleanup: AutoDeleteRunbook=true?
    Cleanup --> [*]: DELETE runbook

    state "Tracked in SQL" as sql {
        Running --> AutomationRun: Persist start
        Completed --> AutomationRun: Persist result
        Failed --> AutomationRun: Persist error
    }
```

---

## How to Export

| Method | Steps |
|--------|-------|
| **GitHub** | Push this file — GitHub renders Mermaid natively in markdown |
| **mermaid.live** | Paste any `mermaid` block at [mermaid.live](https://mermaid.live) → export PNG/SVG |
| **VS Code** | Install "Markdown Preview Mermaid Support" extension → preview this file |
| **draw.io** | Use mermaid.live to export SVG → import into draw.io for further editing |
| **PowerPoint** | Export SVG from mermaid.live → Insert as image in PowerPoint |
