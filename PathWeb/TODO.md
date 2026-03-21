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

---

## 🔧 In Progress

- [ ] **Create Lab VMs prerequisites in progress**
  - `1.1` tested on `SEA-ER-08` — OpenSSH access is working via management IP `10.1.7.81`
  - `1.2` tested on `SEA-ER-08` — non-interactive remote PowerShell works when SSH explicitly launches `pwsh -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass`
  - `1.2` module check passed on `SEA-ER-08` — `LabMod` is visible to PowerShell 7 (`LabMod` `1.4.1.7` under `C:\Program Files\PowerShell\7\Modules\LabMod`)
  - current working assumption: PathWeb should connect by management IP from the `Devices` table and explicitly invoke `pwsh` rather than relying on the SSH default shell
  - temporary admin-only diagnostic endpoint added: `/diag/test-labvm-ssh` resolves hardcoded `SEA-ER-08` from `Devices`, SSHes by management IP, explicitly launches `pwsh`, and returns raw output for the spike/proof-of-concept

---

## 💡 Ideas / Future Work

### Automation & Deployment (next up)

- [ ] **Create Lab VMs button** — Execute LabVMPowerShell on the target Hyper-V server via SSH:
  - **Server prep (one-time per server):** Enable OpenSSH Server on each Windows Server 2026 Hyper-V host, configure the service account credentials in Key Vault (same pattern as network device creds)
  - **LabMod module update:** Add a `-Password` (or `-Credential`) parameter to `New-LabVM` / `Remove-LabVM` so the script can run non-interactively via SSH instead of prompting for passwords
  - **App code:** Look up target server from tenant config (e.g., `SEA-ER-04`), resolve its management IP, SSH in via existing `SshService`, run the LabVM PowerShell command, stream output to the modal

#### Create Lab VMs — Plan of Attack

1. **Prerequisites and host readiness**
   1.1. Enable and verify OpenSSH Server on each Windows Server 2026 Hyper-V host that PathWeb may target.
   1.2. Confirm the service account can sign in over SSH and launch PowerShell non-interactively on each target host.
   1.3. Store Hyper-V host credentials in Azure Key Vault using a clear per-host or per-lab naming convention.
   1.4. Confirm PathWeb can resolve and read those credentials using the existing managed identity + Key Vault path.
   1.5. Define the deterministic mapping from tenant/lab/server preference to target Hyper-V host name (for example `SEA` → `SEA-ER-04`).

2. **LabMod and remote command contract**
   2.1. Update `New-LabVM` / `Remove-LabVM` to accept `-Password` or `-Credential` so they never prompt interactively.
   2.2. Manually test the non-interactive PowerShell command on a host before wiring it into PathWeb.
   2.3. Decide the exact command contract PathWeb will send to the host, including tenant inputs, VM selections, credentials, and expected output format.
   2.4. Define a clean end-of-run output pattern so remote execution can return a useful success/failure summary.

3. **Backend proof-of-concept execution path**
   3.1. Build a small backend proof-of-concept that resolves tenant → host → management IP → credentials without touching the UI yet.
   3.2. Reuse `SshService` to connect to a target host and execute the intended PowerShell command remotely.
   3.3. Capture stdout/stderr and confirm the output is stable enough to surface in the app.
   3.4. Fail early and clearly when host mapping, management IP, credentials, or SSH connectivity are missing.

4. **App abstractions and safety checks**
   4.1. Add a dedicated helper/service for tenant-to-host resolution and host credential lookup so controllers stay thin.
   4.2. Validate that the tenant is active and has sufficient information to determine a target host before running any VM action.
   4.3. Add guardrails against duplicate/concurrent Lab VM create requests for the same tenant.
   4.4. Decide whether remove/backout will share the same abstraction once create is working.

5. **UI and operator workflow**
   5.1. Enable the `Create Lab VMs` action from the `LabVMPowerShell` config card only after backend execution is proven.
   5.2. Reuse the existing modal pattern if possible so output streaming and status behavior stay consistent with other actions.
   5.3. Show target host, running state, output, and success/failure clearly in the modal.
   5.4. Add a confirmation step before the actual create action if the workflow feels risky in practice.

6. **Run history and persistence**
   6.1. Decide whether Lab VM actions should be persisted similarly to Azure Automation and device actions.
   6.2. If yes, store tenant, action, target host, submitted by/date, success/failure, and output summary in SQL.
   6.3. Surface the latest attempt and recent history in the Config page if that proves useful.

7. **Incremental rollout and validation**
   7.1. Test with a single lab and a single Hyper-V host first.
   7.2. Validate one simple create scenario end-to-end before generalizing.
   7.3. After create works, add and test the corresponding remove/backout path.
   7.4. Only then expand the feature to additional labs/hosts and broader tenant combinations.

### Post-Deploy Automation

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
