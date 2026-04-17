# PathWeb — To Do List

> Shared action-item tracker between the developer and Copilot.
> Update this file as items are completed or new work is identified.
> Copilot: read this file at the start of each new conversation for context.

---

## ✅ Completed

- [x] **Compare to Device** — Set-based config comparison via SSH (controller + JS + modal UI)
- [x] **Verify Off Device** — SSH grep for tenant references on network devices
- [x] **Fix deploy warmup timestamp mismatch** — Server now reads `build.json` instead of DLL `LastWriteTime`
- [x] **Fix Compare modal readability** — Bright text colors on `#1a1a2e` background for light/dark mode
- [x] **Clean up ghost folders in .csproj** — Removed stale `<Folder Include>` and nested `PathWeb\` reference
- [x] **Clean up obsolete markdown files** — Deleted `FIXED_README.md`, `START_HERE.md`, `CREDENTIALS_SETUP.md`, `SETUP.md`, `area_structure.txt`
- [x] **Update README.md** — Rewritten to reflect current .NET 10 / Azure / SSH / Logic App stack
- [x] **Apply to Device** — Two-phase apply: preview via Compare, confirm in modal, push delta via ShellStream with commit check/show compare (Juniper) or wr (Cisco)
- [x] **Show Logs** — Admin log viewer: custom DbLoggerProvider writes to AppLog SQL table, LogsController with level filter/search/paging, color-coded view at Admin → Logs
- [x] **Streamline auth logging** — Downgraded routine per-request auth logs to Debug, cached AuthLevel lookups for 5 minutes
- [x] **Patch Device** — Full sync: preview both add/remove buckets via Compare, confirm in modal, push additions + delete/no-prefix removals via ShellStream
- [x] **Per-device backout configs** — ConfigGenerator now saves individual `{ConfigType}-out` backout configs to SQL alongside the combined BackoutConfig
- [x] **Remove from Device** — Two-phase remove: preview backout config from SQL `-out` records, confirm in modal, push via ShellStream
- [x] **Code refactoring pass** — BaseController for shared helpers, PlatformDetector static class, DeviceActionsController extraction, Config.cshtml JS refactor (824→553 lines), About page updated
- [x] **Deploy to Azure button** — Azure Automation runbook submission and polling tested end-to-end for `CreateERPowerShell` / `CreateAzurePowerShell`; status tracking, reopen flow, and recent-run history are working
- [x] **Clean up transient Automation runbooks** — Completed/failed Azure Automation runbooks are now deleted after terminal status is persisted, keeping the Automation Account/resource group tidy while preserving history in SQL
- [x] **Automation run tracking** — Persist Azure Automation runs in SQL, show per-config status badges/re-open actions, restore long-running runbook state/output when revisiting the Config page, and show a small recent-run history list for Azure PowerShell configs
- [x] **Harden health/warmup endpoints** — Bypass auth-level DB lookup for `/health` and `/warmup`, return structured warmup errors, and avoid Windows-only identity calls in the DB logger on non-Windows hosts
- [x] **Expand `/diag` diagnostics** — Added automation, Logic App, Key Vault, device, logging, cache, table-presence, and configuration-warning sections, plus optional `deep=true` live probes for Azure Automation, Key Vault, and Logic App host reachability
- [x] **Add `/diag/view` HTML diagnostics page** — Kept `/diag` as the JSON source, added a browser-friendly diagnostics viewer at `/diag/view`, and linked it from the admin menu
- [x] **Show device apply status badges** — Persisted `Apply to Device` success/failure attempts in SQL and surfaced per-device status badges/timestamps on the Config page; the device badge is clickable to reopen the last apply result, and PowerShell recent-run sections now default collapsed
- [x] **Notification eMail** — Dedicated-mailbox Logic App deployment/test setup is working, the `Notification eMail` config card now supports `Send Email` with persisted status badging, and the email logo is served from the public `email-assets` path excluded from Easy Auth
- [x] **Hide per-device `-out` configs from Config page** — Kept the per-device backout records in SQL for `Remove from Device`, but stopped rendering those individual `-out` config cards in the UI
- [x] **Provision Provider button** — The `ServiceProviderInstructions` config card now submits the existing `New-LabECX` script through Azure Automation, tracks status/history in SQL, reuses the automation modal/polling flow, skips non-ECX tenants, and creates runbooks in the PowerShell 7.x runtime required by `LabMod`
- [x] **Admin Settings page** — Added an Admin → Settings page with an `Auto Delete Runbooks` toggle plus an `Automation Runbook Type` field backed by the SQL `Settings` table (`AutoDeleteRunbook` and `AutomationRunbookType` in `SettingName`, values stored in `ProdVersion`), and Azure Automation runbook cleanup now honors the auto-delete value
- [x] **SQL-backed logging controls** — Added `Logging:Default` plus dynamic `Logging:<category>` overrides on the Admin → Settings page, with hierarchical subcategory matching in the DB logger provider so production verbosity can be tuned without redeploying
- [x] **Expand quick-deploy/warmup coverage** — `/warmup` now pre-compiles newer EF Core query shapes for requests queue, settings, tenant config run history, logs paging/filtering, and address ordering; `Warmup.ps1` and `quick-deploy.ps1` now also touch `Logs`, `Requests/Queue`, `Settings`, and `diag/view`
- [x] **Target configured Azure Automation runbook type** — Runbook creation now reads `Settings.SettingName = 'AutomationRunbookType'` and submits that API runbook type dynamically (currently `PowerShell72` for PowerShell 7.2)
- [x] **View released tenants on index** — The Tenants index page now supports switching between active and released tenants using `DeletedDate == null` vs `DeletedDate != null`, and includes server-side pagination so the larger released-tenant list remains usable
- [x] **Clone released tenant from Details** — Inactive tenant Details pages now hide `Edit` / `Release` / `Create Config`, add a clone confirmation step, and create a new active tenant record with a fresh GUID/TenantID and reset post-deploy fields before redirecting to the new tenant's Details page
- [x] **Inactive tenant Config page is read-only** — Released tenant Config pages now hide `Create/Regenerate Config`, suppress per-config action buttons, keep copy-to-clipboard available, and prevent UI-triggered config changes for inactive tenants
- [x] **Create Lab VMs** — Full production integration of on-prem VM creation via direct SSH to Hyper-V hosts:
  - **LabMod module work:** Added private helpers (`Assert-LabAdminContext`, `Write-LabLogEntry`, `Write-LabRunStatus`), public `Start-LabVmRequest` with caller-supplied `RunId`, structured JSON output via `---LABVM-JSON---` marker, and fixed `Assert-LabAdminContext | Out-Null` to prevent `$true` leaking into results (`LabMod` 1.4.3.9)
  - **SSH execution architecture:** Credentials exist only as in-memory PSCredential objects passed via `-EncodedCommand` (never written to disk); SSH keepalive prevents connection drops during long-running commands; fire-and-forget background tasks with in-memory `LabVmRunTracker` (`ConcurrentDictionary`) avoid the Azure App Service 230-second gateway timeout
  - **`LabVmController`** with four endpoints: `Servers` (available Hyper-V hosts for a lab), `Requests` (parsed VM rows from `LabVMPowerShell` config), `Submit` (accepts per-VM server overrides, launches background SSH), `Status` (instant in-memory lookup with per-request progress)
  - **Dynamic server resolution:** Target Hyper-V server derived from first digit of TenantId (e.g., tenant 18 → `SEA-ER-01`), with per-VM server override dropdowns in the UI
  - **Per-request live progress:** Each VM runs as its own SSH command so the tracker updates after each VM completes (not all-at-once at the end); VMs on different servers run in parallel via `Task.WhenAll`
  - **Config page modal UI:** Table with `#`, `OS`, `Target Server` (dropdown), and `Status` columns; per-row ✅/⏳/❌ indicators with VM names; resume-on-reopen (close modal during creation, reopen to see current state)
  - **SQL persistence:** `LabVmRun` table stores completed runs with success/failure, VM names, timestamps, and per-request output; status badge + timestamp displayed on the `On-prem VM PowerShell` card header (matches existing firewall/email badge pattern); badge updates live when a run finishes without page refresh
  - **Validated end-to-end on `SEA-ER-08` and `SEA-ER-09`** with Seattle Tenant 18: multiple VMs created, no timeouts, no files on disk; fixed SSH credential bug (was using network device credentials instead of server admin credentials from `LabSecrets` vault)
  - **Spike endpoints** (`/diag/test-labvm-ssh` and `/diag/test-labvm-ssh/status`) still present in `Program.cs` for reference; to be removed in cleanup pass
- [x] **Device Validate action** — Per-row "Validate" button on the Devices index page (and on the individual Device details page) to spot-check SSH connectivity and readiness:
  - **Routers/switches**: SSH with device credentials, runs `show version brief` (Juniper) or `show version` (Cisco) to confirm connectivity and auth
  - **Servers**: SSH with server admin credentials from `LabSecrets` vault, runs a multi-check script via `pwsh` — PowerShell 7 installed, Windows Server 2025, sshd running, LabMod module ≥ 1.5.0, log directory exists — displayed as a pass/fail table
  - Inline result on Details page, modal on Index page; `SshService.RunCommandWithCredentialsAsync` and `RunPowerShellCommandWithCredentialsAsync` added for explicit credential support
- [x] **Config card action dropdowns + removal actions** — Replaced standalone buttons with unified `<select>` + `▶ Run` dropdowns on all non-device config cards, and wired up all removal/teardown actions:
  - `CreateERPowerShell`: Deploy to Azure | Remove from Azure (info-only redirect to CreateAzurePowerShell card)
  - `CreateAzurePowerShell`: Deploy to Azure | Remove from Azure (fetches `-out` backout script from SQL, opens in automation modal with `Write-Status` dual-output for runbook/console)
  - `ServiceProviderInstructions`: Provision at Provider | Deprovision at Provider (swaps `New-LabECX` → `Remove-LabECX` in automation modal)
  - `LabVMPowerShell`: Create Lab VMs | Remove Lab VMs (two-phase scan + remove via SSH)
  - Network device cards already had dropdowns — included in test pass
- [x] **Remove Lab VMs** — Two-phase remove for on-prem VMs via SSH:
  - **Scan phase:** `LabVmController.Scan` SSHs to all candidate Hyper-V servers (`-ER-` devices) in parallel, runs `Get-VM -Name "{Lab}-ER-{TenantId}-VM*"` to discover existing VMs, returns per-server results with VM names and states
  - **Remove phase:** `LabVmController.RemoveSubmit` accepts the list of servers with VMs, SSHs to each in parallel running `Remove-LabVM -TenantID {id}`, uses structured JSON output via `---LABVM-JSON---` marker, fire-and-forget with in-memory tracker and polling
  - **UI:** Reuses `labVmModal` with mode awareness (`_labVmRemoveMode`); scan results show VM names/states per server; confirm dialog before removal; per-server progress indicators; mode-appropriate labels/messages throughout
  - **Backout script improvement:** `CreateAzurePowerShell-out` now includes a `Write-Status` helper function that detects runbook context via `$PSPrivateMetadata.JobId` and emits `Write-Output` for Automation capture alongside `Write-Host` for console color
- [x] **Architecture diagrams** — Created `Docs/Architecture.md` with 8 Mermaid diagrams (system overview, controller/service deps, data model, config card actions, SSH ops, Lab VM pattern, auth flow, Automation lifecycle)
- [x] **Manual test plan** — Created `Docs/TestPlan.md` with 235 test cases across 15 sections; iteratively expanded with config integration depth, tenant option→config variation matrix, and gap analysis pass

---

## 🔧 In Progress

- [ ] **Comprehensive testing phase** — End-to-end validation of all config card actions, removal flows, and modal behaviors before marking the feature set as production-ready
- [ ] **Restore auth cache TTL to 5 minutes** — `AuthLevelService.cs` line 53 was reduced from 5→1 minute to speed up manual auth-level testing; restore to `TimeSpan.FromMinutes(5)` when testing is complete
- [x] **Remove hardcoded Logic App trigger URL from `deploy-to-azure.ps1`** — Corporate security flagged exposed SAS `sig=` in the hardcoded trigger URL; refactored to an optional `-LogicAppTriggerUrl` parameter (no default); existing App Service setting is preserved unless explicitly overridden; **key rotation still required**

---

## 💡 Ideas / Future Work

### Automation & Deployment

- [ ] **Expand Lab VM to additional labs**
- [ ] **LabMod log viewer** — Admin tool to view the `LabMod.log.jsonl` file on a given Hyper-V server via SSH:
  - SSH to the selected server's management IP using existing `SshService` and read `C:\Hyper-V\Logs\LabMod.log.jsonl`
  - Parse the JSONL lines and display in a searchable/filterable table (timestamp, level, RunId, message)
  - Server picker dropdown reusing the `LabVmController.Servers` pattern (or a standalone Admin page with a server selector)
  - Optional: tail/refresh to watch recent activity; filter by RunId to correlate with a specific Lab VM run
- [ ] **Auto S-Key retrieval**
- [ ] **Auto VPN Endpoints** — Button to retrieve VPN gateway public IPs from Azure and update the firewall VPN config with actual endpoint addresses
- [ ] **Post-deploy value injection** — Resolve placeholder values (e.g., `<REQUIRED-IP>`, S-TAGs) that are only known after Azure deployment:
  - Firewall: replace VPN gateway `address <REQUIRED-IP>` in `gw_Cust{Id}` IKE gateway config with actual customer peer IP
  - Router (both primary and secondary): replace S-TAG placeholders with actual outer VLAN tags from the ER circuit

### Config & Tenant Features

- [ ] **Microsoft Peering BGP Communities helper** — Keep `Msfttags` as the source-of-truth text field, but add an assisted picker/suggestion flow so users do not need to know raw community strings up front:
  - **Preserve freeform entry:** Continue allowing manual comma/semicolon/newline-separated BGP community input for advanced users and edge cases
  - **Normalize and validate input:** Parse entered values, trim/dedupe them, allow comma/semicolon/newline separators, and validate the expected `number:number` format before save/config generation
  - **Add a lightweight picker UI:** Add a searchable modal or side panel that lets users search/select known community entries instead of building a large tree selector
  - **Support region-based suggestions:** Use the selected Azure Region to suggest likely Microsoft peering communities, but do not silently auto-fill them; let the user explicitly add suggested entries
  - **Add a small curated catalog:** Back the picker with a maintained catalog of known communities and metadata such as display name, service family, Azure region, geography, and whether an entry is a common default for a region
  - **Show friendly labels for entered values:** Under the text field, resolve known community values to human-readable labels and flag unknown values as custom/unrecognized so users can review what they entered
  - **Add preset shortcuts:** Consider lightweight presets such as `Selected Azure region only`, `Selected region + paired region`, `Same geography`, and `Custom`
  - **Keep storage simple:** Continue storing the final selected/manual values in the existing `Msfttags` text field rather than inventing a more complex persisted structure unless later experience proves it is necessary
  - **Avoid live doc scraping at runtime:** Populate the catalog from app-managed static data or a DB table rather than scraping Learn pages live in the UI path
  - **Future follow-up:** If this proves useful in practice, consider adding a small maintenance workflow for refreshing the curated community catalog from Azure documentation updates
- [ ] **Firewall Bypass mode** — Add a toggle to tenant create/edit that generates a simplified config path bypassing the firewall:
  - New `FirewallBypass` boolean field on the Tenant table
  - Generate VRF config directly on the Nexus switch (skip SRX)
  - Firewall limited to RDP-to-VM and VM-to-internet policies only
  - Static route on switch (or firewall) for Azure VNet addresses via ER

### Infrastructure / DevOps

- [ ] **Storage access for post-install VM scripts** — Post-SFI: investigate whether the App Service Managed Identity can access the Scripts storage account directly, and pass credentials/SAS to VMs during provisioning

### Runbook Output Hygiene

- [ ] **Parse structured runbook result objects in PathWeb**
   Once runbooks emit a final structured `Write-Output` object, detect and present its properties cleanly in the modal/history UI instead of showing only raw text.

---

## 📝 Notes

- Device SSH credentials are stored in Azure Key Vault (username in app config, password as KV secret)
- Platform detection uses device name conventions: `-MX`/`-SRX` = Juniper, `-NX` = NX-OS, `-ASR`/`-ISR` = IOS-XE
- Config comparison is order-agnostic (set-based), comments and blank lines are stripped before comparing
- Auth levels: TenantReadOnly (view), TenantAdmin (8+, write operations)
- `TODO.md` created to persist action items across conversation resets
