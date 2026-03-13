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

---

## 🔧 In Progress

_(nothing currently in progress)_

---

## 💡 Ideas / Future Work

- [ ] **Deploy to Azure button** — Execute CreateERPowerShell / CreateAzurePowerShell via Azure Automation Runbook
- [ ] **Create Lab VMs button** — Execute LabVMPowerShell via Azure Automation Runbook
- [ ] **Deploy to Provider button** — Wire up ServiceProviderInstructions delivery
- [ ] **BackoutConfig TBD button** — Define what backout action should do
- [ ] **Notification eMail** — Consider sending directly from the app vs download-only

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
