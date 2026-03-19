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

---

## 🔧 In Progress

- [ ] *(none right now)*

---

## 💡 Ideas / Future Work

### Automation & Deployment (next up)

- [ ] **Create Lab VMs button** — Execute LabVMPowerShell on the target Hyper-V server via SSH:
  - **Server prep (one-time per server):** Enable OpenSSH Server on each Windows Server 2026 Hyper-V host, configure the service account credentials in Key Vault (same pattern as network device creds)
  - **LabMod module update:** Add a `-Password` (or `-Credential`) parameter to `New-LabVM` / `Remove-LabVM` so the script can run non-interactively via SSH instead of prompting for passwords
  - **App code:** Look up target server from tenant config (e.g., `SEA-ER-04`), resolve its management IP, SSH in via existing `SshService`, run the LabVM PowerShell command, stream output to the modal
- [ ] **Provision Provider button** — Copy-to-clipboard for now (admin runs `New-LabECX` locally). Future: install `LabMod` module in Azure Automation Account and run as a Runbook (requires ECX API credentials in Automation Account)
- [ ] **BackoutConfig button** — Wire the BackoutConfig card's action button to execute the combined backout (Azure RG deletion + ECX deprovision + Lab VM removal)

### Post-Deploy Automation

- [ ] **Auto S-Key retrieval** — Button to auto-retrieve the ExpressRoute Service Key from Azure (via Resource ID or circuit name) and populate the config
- [ ] **Auto VPN Endpoints** — Button to retrieve VPN gateway public IPs from Azure and update the firewall VPN config with actual endpoint addresses
- [ ] **Post-deploy value injection** — Resolve placeholder values (e.g., `<REQUIRED-IP>`, S-TAGs) that are only known after Azure deployment:
  - Firewall: replace VPN gateway `address <REQUIRED-IP>` in `gw_Cust{Id}` IKE gateway config with actual customer peer IP
  - Router (both primary and secondary): replace S-TAG placeholders with actual outer VLAN tags from the ER circuit

### Config & Tenant Features

- [ ] **Firewall Bypass mode** — Add a toggle to tenant create/edit that generates a simplified config path bypassing the firewall:
  - New `FirewallBypass` boolean field on the Tenant table
  - Generate VRF config directly on the Nexus switch (skip SRX)
  - Firewall limited to RDP-to-VM and VM-to-internet policies only
  - Static route on switch (or firewall) for Azure VNet addresses via ER
- [ ] **View Released Tenants** — Browse released (deleted) tenants in read-only mode, including their generated configs. Option to clone a released tenant as a starting point for a new one.


### Infrastructure / DevOps

- [ ] **Storage access for post-install VM scripts** — Post-SFI: investigate whether the App Service Managed Identity can access the Scripts storage account directly, and pass credentials/SAS to VMs during provisioning

### Azure Automation Runbook — Plan of Record

- **Approach**: Submit generated PowerShell to an Azure Automation Account as a runbook job via REST API
- **Auth**: Automation Account uses a System-assigned Managed Identity with Contributor on the subscription (no credentials in the app)
- **Script prep**: Replace the `Get-AzContext` login check with `Connect-AzAccount -Identity` before submission; no other ConfigGenerator changes needed
- **Progress**: Poll job status via `GET .../jobs/{id}/output` and stream `Write-Host` lines back to the UI modal
- **Errors**: Automation API exposes separate Output/Error/Warning streams with timestamps — display errors directly in the modal
- **Re-runs**: Scripts are idempotent (Try/Get, Catch/New pattern) — user can tweak the textarea and resubmit without risk
- **Scope**: Covers CreateERPowerShell, CreateAzurePowerShell, and LabVMPowerShell — all three are Az PowerShell scripts
- **Setup needed**: One-time creation of an Azure Automation Account with Managed Identity + Contributor role assignment

---

## 📝 Notes

- Device SSH credentials are stored in Azure Key Vault (username in app config, password as KV secret)
- Platform detection uses device name conventions: `-MX`/`-SRX` = Juniper, `-NX` = NX-OS, `-ASR`/`-ISR` = IOS-XE
- Config comparison is order-agnostic (set-based), comments and blank lines are stripped before comparing
- Auth levels: TenantReadOnly (view), TenantAdmin (8+, write operations)
- `TODO.md` created to persist action items across conversation resets
