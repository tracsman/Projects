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
  - **Validated end-to-end on `SEA-ER-08`** with Seattle Tenant 18: multiple Ubuntu VMs created in ~4.5 minutes per pair, no timeouts, no files on disk
  - **Spike endpoints** (`/diag/test-labvm-ssh` and `/diag/test-labvm-ssh/status`) still present in `Program.cs` for reference; to be removed in cleanup pass

---

## 🔧 In Progress

- [ ] **Create Lab VMs — cleanup and hardening**
  - [x] Remove spike endpoints from `Program.cs`
  - [x] Duplicate/concurrent request guardrails — UI-level: active run resumes on reopen, confirm dialog after previous completion; server-side not needed for internal tool
  - [x] Add warmup query for `LabVmRun` table to `/warmup` endpoint
  - [ ] Validate on additional Hyper-V servers beyond `SEA-ER-08`

---

## 💡 Ideas / Future Work

### Automation & Deployment

- [ ] **Remove Lab VMs** — Corresponding remove/backout path for on-prem VMs via SSH, reusing the same `LabVmController` / `SshService` / `LabMod` architecture
- [ ] **Expand Lab VM to additional labs** — Enable OpenSSH on Ashburn Hyper-V hosts, validate the `<Lab>-ER-xx` naming convention works for ASH

- [ ] **Auto S-Key retrieval** — Button to auto-retrieve the ExpressRoute Service Key from Azure (via Resource ID or circuit name) and populate the config
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

### PowerShell Runbook Output Hygiene

- [ ] **Replace routine `Write-Host` with `Write-Verbose`** — Update generated/submitted PowerShell scripts so step-by-step chatter goes to the verbose stream instead of the main output stream
- [ ] **Add final structured `Write-Output` result object** — Emit a concise end-of-run object with multiple properties summarizing the job outcome; exact property schema to be decided later
- [ ] **Parse structured runbook result objects in PathWeb** — Once runbooks emit a final structured `Write-Output` object, detect and present its properties cleanly in the modal/history UI instead of showing only raw text

---

## 📝 Notes

- Device SSH credentials are stored in Azure Key Vault (username in app config, password as KV secret)
- Platform detection uses device name conventions: `-MX`/`-SRX` = Juniper, `-NX` = NX-OS, `-ASR`/`-ISR` = IOS-XE
- Config comparison is order-agnostic (set-based), comments and blank lines are stripped before comparing
- Auth levels: TenantReadOnly (view), TenantAdmin (8+, write operations)
- `TODO.md` created to persist action items across conversation resets
