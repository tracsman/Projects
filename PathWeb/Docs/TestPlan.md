q# PathWeb — Manual Test Plan

> **Purpose:** Comprehensive site-wide manual test plan for end-to-end validation.  
> **How to use:** Work through each section in order. Mark each test case ✅ Pass, ❌ Fail, or ⏭️ Skipped. Record notes in the right column. Auth levels are noted — log in as the appropriate user or have the DB updated to match.  
> **Environments:** Test against the deployed Azure App Service instance. For SSH/device tests, ensure lab connectivity.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Pass |
| ❌ | Fail (describe in Notes) |
| ⏭️ | Skipped / N/A |
| 🔒 | Requires specific auth level |

---

## 1. Health & Infrastructure Endpoints

These endpoints bypass authentication. Test from an unauthenticated browser / curl.

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 1.1 | `/health` returns OK | `curl https://<site>/health` | 200 JSON with `status: "healthy"` and `build` timestamp | ✅ | |
| 1.2 | `/warmup` succeeds | `curl https://<site>/warmup` | 200 JSON with `status: "warm"`, `durationMs`, pre-compiled query list | ✅ | |
| 1.3 | `/warmup` with broken SQL | Temporarily revoke DB access or wrong connection string | Returns structured error JSON, not a 500 stack trace | | |
| 1.4 | `/diag` JSON (authed) | Browse to `/diag` while logged in (auth level ≥ 14) | JSON diagnostics payload with automation, KV, logging, cache sections | ✅ | |
| 1.5 | `/diag?deep=true` live probes | Browse to `/diag?deep=true` | Additional live-probe results for Automation, Key Vault, Logic App | ✅ | |
| 1.6 | `/diag/view` HTML page | Browse to `/diag/view` | Formatted HTML diagnostics page with expandable sections | ✅ | |
| 1.7 | `/diag/view?deep=true` HTML page | Browse to `/diag/view?deep=true` | Formatted HTML diagnostics page with additional live-probe results for Automation, Key Vault, Logic App | ✅ | |
| 1.8 | `/diag` blocked for low auth | Log in as auth level < 14, browse to `/diag` | Permission denied or 403 | ❌ | Access successful with auth level 13 |

---

## 2. Authentication & Authorization

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 2.1 | Unauthenticated access blocked | Open site in incognito/private window, no login | Redirected to Entra ID login (or Easy Auth challenge) | | |
| 2.2 | Auth level 0 — no nav items | Log in as user with auth level 0 | Home page loads; no Tenants, Admin, or About links in nav | | |
| 2.3 | Auth level 1–5 — About menu only | Log in as auth level 1 | About dropdown visible; no Tenants or Admin links | | |
| 2.4 | Auth level 6–7 — Tenants visible | Log in as auth level 6 | Tenants nav link visible; Admin menu hidden | | |
| 2.5 | Auth level 8–10 — Admin (requests) | Log in as auth level 8 | Admin dropdown with "Tenant Requests"; no Devices/Addresses/Users/Settings | | |
| 2.6 | Auth level 11–13 — Admin + Devices | Log in as auth level 11 | Admin dropdown includes Devices and IP Addresses | | |
| 2.7 | Auth level 14+ — Full admin | Log in as auth level 14 | All Admin items visible: Users, Tooltips, Logs, Diagnostics, Settings | | |
| 2.8 | Direct URL bypass — Tenants | Auth level 0 user navigates to `/Tenants` directly | PermissionError view shown (not raw data) | | |
| 2.9 | Direct URL bypass — Admin pages | Auth level 6 user navigates to `/Settings` directly | PermissionError or redirect | | |
| 2.10 | Theme toggle | Click 🌙 button in nav bar | Page switches to dark/light mode; cookie persists across refresh | | |
| 2.11 | TempData messages render | Trigger an action that sets TempData (e.g., create a tenant) | Alert banner appears at top of page with correct color | | |

---

## 3. Home Page

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 3.1 | Home page loads | Navigate to `/` | Home/Index page renders without errors | | |
| 3.2 | Privacy page loads | Click "Privacy" link in footer | Privacy page renders | | |

---

## 4. Request a Lab (Public-Facing)

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 4.1 | Request index loads | Click "Request a Lab" in nav | Request list page loads | | |
| 4.2 | Create new request | Click Create → fill all required fields → Submit | Redirected to Details or Index; new request visible with "Pending" status | | |
| 4.3 | Create with missing required fields | Leave required fields blank → Submit | Validation errors shown inline; form not submitted | | |
| 4.4 | View request details | Click a request row | Details page shows all submitted fields | | |
| 4.5 | Request queue (admin) 🔒8 | Admin → Tenant Requests | Queue page lists pending requests with count matching nav badge | | |
| 4.6 | Review a request 🔒8 | Click Review on a pending request | Review page loads with approve/reject options | | |
| 4.7 | Approve a request 🔒8 | Approve a pending request | Status changes; tenant created; admin redirected to tenant Edit page; pending count decreases by 1 | | |
| 4.8 | Reject a request 🔒8 | Reject a pending request | Status changes to rejected; item no longer in pending queue | | |
| 4.9 | Approve creates ADO work item 🔒8 | Approve a request when Logic App is configured | ADO work item created; `WorkItemId` shown on tenant; TempData shows success with work item number | | |
| 4.10 | Approve — ADO failure is non-blocking 🔒8 | Approve a request when ADO Logic App is down/misconfigured | Tenant still created successfully; TempData shows warning (yellow) with ADO error note | | |
| 4.11 | Approve — already processed request 🔒8 | Use devtools to POST approve on an already-approved request | "Invalid or already processed request" error; no duplicate tenant | | |
| 4.12 | Approve — server preference parameter 🔒8 | Approve with a specific `serverPreference` value | TenantId assigned in the matching range (e.g., preference=2 → TenantId 2x) | | |
| 4.13 | Non-admin sees only own requests | Log in as non-admin; create a request; check Index | Only requests where you are the requestor or listed in Contacts are visible | | |
| 4.14 | Contacts-based visibility | Log in as a user listed in another request's Contacts field (not the requestor) | That request is visible in your list | | |
| 4.15 | Request pagination | Create >25 requests (or verify in DB) → check Index | Pagination controls appear; page 2 loads correctly | | |

---

## 5. Tenants — Index & CRUD 🔒6

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 5.1 | Tenant index loads | Navigate to Tenants | Paginated table of active tenants | | |
| 5.2 | Sort by Lab | Click "Lab" column header | Rows sort ascending; click again for descending | | |
| 5.3 | Sort by Tenant | Click "Tenant" column header | Rows sort by TenantId | | |
| 5.4 | Sort by Ninja | Click "Ninja" column header | Rows sort by NinjaOwner | | |
| 5.5 | Sort by Date | Click "Date" column header | Rows sort by date | | |
| 5.6 | Sort by Usage | Click "Usage" column header | Rows sort by Usage | | |
| 5.7 | Search filter | Enter a lab name in search box → search | Only matching tenants shown | | |
| 5.8 | Search — no results | Enter gibberish search string | Empty table with appropriate message | | |
| 5.9 | Show released tenants | Toggle "Show Released" | Table shows tenants with non-null DeletedDate | | |
| 5.10 | Pagination (released) | If >50 released tenants, click page 2 | Second page of results loads | | |
| 5.11 | Create tenant 🔒8 | Click Create New → fill form → Save | Redirected to Details with new tenant; TenantId auto-assigned | | |
| 5.12 | Create — validation errors | Leave required fields blank → Save | Inline validation errors; no save | | |
| 5.13 | View tenant Details | Click a tenant row | Details page shows all fields | | |
| 5.14 | Edit tenant 🔒8 | Details → Edit → change a field → Save | Updated value persists; returned to Details | | |
| 5.15 | Release tenant 🔒8 | Details → Release → Confirm | Tenant gets DeletedDate; disappears from active list; appears in released list | | |
| 5.16 | Clone released tenant 🔒8 | Switch to released → open a released tenant → Clone | Confirmation step shown; new active tenant created with fresh GUID/TenantID; post-deploy fields reset; redirected to new tenant Details | | |
| 5.17 | Clone — fields reset correctly 🔒8 | After cloning, check new tenant Details | `Msftp2p`, `Msftadv`, `Msfttags`, `Skey`, `VpnendPoint` are blank; `WorkItemId`=0; `ConfigVersion`=0; `DeletedDate`=null; design options (Ersku, VMs, etc.) carried over from source | | |
| 5.18 | Released tenant — no Edit button | Open a released tenant's Details | Edit, Release, Create Config buttons are hidden | | |

---

## 6. Tenant Config Page 🔒6

### 6A. Config Generation & Display

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 6A.1 | Config page loads | Tenant Details → View Config | Config page renders with config cards | | |
| 6A.2 | Create/Regenerate Config 🔒8 | Click Create Config (or Regenerate) | Configs generated; cards appear for all expected ConfigTypes | | |
| 6A.3 | Regenerate overwrites previous | Generate config → edit tenant field → Regenerate | New config reflects updated tenant data; old config replaced | | |
| 6A.4 | Copy to clipboard | Click copy icon on any config card | Config text copied to clipboard; toast/feedback shown | | |
| 6A.5 | Copy large config | Copy a multi-KB config (e.g., CreateAzurePowerShell) | Full content copied without truncation | | |
| 6A.6 | Per-device `-out` cards hidden | After config generation | Individual `{DeviceName}-out` cards do NOT appear; only combined BackoutConfig (if any) | | |
| 6A.7 | Inactive tenant — read-only | Open Config page for a released tenant | No Create/Regenerate button; no action dropdowns; copy still works | | |
| 6A.8 | Inactive tenant — direct POST blocked | Use browser devtools to POST a config action on a released tenant | Server rejects the action (not just UI hiding) | | |
| 6A.9 | Server Preference (ConfigVersion 0) | Create tenant with ConfigVersion=0 | UI label shows "Server Preference" (not "0" or "ConfigVersion 0") | | |
| 6A.10 | Config page with no configs yet | Open Config page before ever generating | No config cards; "Create Config" button visible (for admin); no JS errors | | |
| 6A.11 | Config card ordering | Check card layout after generation | Cards appear in a consistent, logical order across tenants | | |

### 6B. Network Device Actions (Apply / Compare / Patch / Remove / Verify)

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 6B.1 | Compare to Device | Select "Compare to Device" → Run on a device card | Modal opens showing set-based diff (additions/removals); order-agnostic; comments/blanks stripped | | |
| 6B.2 | Compare — device unreachable | Run Compare against an offline device | Error message in modal (timeout or connection refused); no hung spinner | | |
| 6B.3 | Compare — device in sync | Run Compare when device already matches config | "No differences" or empty diff message; no false positives | | |
| 6B.4 | Compare — ignores line ordering | Reorder lines on device (manually or via different commit) → Compare | Still shows "no differences" if same set of lines | | |
| 6B.5 | Apply to Device 🔒8 | Select "Apply to Device" → Run → review diff → Confirm | Delta pushed via SSH; success message; status badge updates on card | | |
| 6B.6 | Apply — nothing to apply | Run Apply when device already matches config | "No changes needed" message; no SSH write commands sent | | |
| 6B.7 | Apply — Juniper commit check flow | Apply config to a `-MX` or `-SRX` device | Preview shows `commit check` / `show compare` output; confirm pushes `commit` | | |
| 6B.8 | Apply — Juniper commit failure | Apply invalid config to Juniper device | `commit check` fails; error shown in modal; no partial commit left | | |
| 6B.9 | Apply — Cisco write flow | Apply config to a `-NX` or `-ASR`/`-ISR` device | Uses `wr` (write memory) after config push | | |
| 6B.10 | Apply — SSH session drops mid-push | Kill SSH connectivity during apply (e.g., brief network blip) | Error reported; no partial config orphaned on device (Juniper rollback) | | |
| 6B.11 | Patch Device 🔒8 | Select "Patch Device" → Run → review add/remove buckets → Confirm | Both additions (new lines) and removals (delete/no-prefix) pushed; success badge | | |
| 6B.12 | Patch — additions only | Patch when device has missing lines but nothing extra | Only additions shown; no removal section | | |
| 6B.13 | Patch — removals only | Patch when device has extra lines but nothing missing | Only removals shown; no addition section | | |
| 6B.14 | Remove from Device 🔒8 | Select "Remove from Device" → Run → review backout config → Confirm | Per-device `-out` backout config pushed via SSH; success badge | | |
| 6B.15 | Remove — no backout config | Run Remove when no `-out` record exists in SQL | Appropriate error message ("no backout config found"); no crash | | |
| 6B.16 | Verify Off Device 🔒8 | Select "Verify Off Device" → Run | SSH grep for `Cust{TenantId}` references; results shown in modal | | |
| 6B.17 | Verify — clean device | Run Verify on device with no tenant references | "No references found" or equivalent clean result | | |
| 6B.18 | Device status badge — clickable | Click a device status badge (✅/❌) | Reopens the last apply result in modal | | |
| 6B.19 | Device status badge — shows timestamp | Check badge after apply | Timestamp of last apply attempt visible | | |
| 6B.20 | Multiple devices — independent badges | Apply to device A (success), Apply to device B (fail) | Device A shows ✅, Device B shows ❌; each is independent | | |
| 6B.21 | Dropdown — switch action without running | Select "Compare" → switch to "Apply" without clicking Run | Dropdown changes; no request sent; no stale state | | |
| 6B.22 | Dropdown — rapid double-click Run | Double-click Run quickly on a device action | Only one request sent; no duplicate SSH sessions | | |

### 6C. PowerShell / Azure Automation Actions

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| **Submit & Execute** | | | | | |
| 6C.1 | Deploy ER to Azure 🔒8 | `CreateERPowerShell` card → "Deploy to Azure" → Run | Automation modal opens; runbook created via REST API; job submitted; status polls to completion | | |
| 6C.2 | Deploy Azure PS to Azure 🔒8 | `CreateAzurePowerShell` card → "Deploy to Azure" → Run | Same automation flow; runbook type matches `Settings.AutomationRunbookType` (`PowerShell72`) | | |
| 6C.3 | Script preparation — Managed Identity | Open automation modal → check "Prepared Script" tab/section | Interactive `Connect-AzAccount` stripped; replaced with Managed Identity auth | | |
| 6C.4 | Script preparation — no double-encoding | Check prepared script for encoding artifacts | No double-escaped characters or corrupted strings | | |
| 6C.5 | Runbook type from Settings | Change `AutomationRunbookType` in Settings to a different value → Deploy | Runbook created with the new type (verify via Azure portal or `/diag`) | | |
| **Remove from Azure** | | | | | |
| 6C.6 | Remove from Azure 🔒8 | `CreateAzurePowerShell` card → "Remove from Azure" → Run | Fetches `-out` backout script from SQL; opens in automation modal | | |
| 6C.7 | Remove — `Write-Status` dual output | Check automation modal output during backout run | Both colored console output AND captured runbook output visible | | |
| 6C.8 | Remove — no backout script | Run Remove on a tenant that was never deployed (no `-out` config) | Error message "No backout config found for 'CreateAzurePowerShell'" | | |
| 6C.9 | ER Remove redirects to Azure card | `CreateERPowerShell` card → "Remove from Azure" | Info message directing user to `CreateAzurePowerShell` card | | |
| **Provider Provisioning** | | | | | |
| 6C.10 | Provision Provider 🔒8 | `ServiceProviderInstructions` card → "Provision at Provider" → Run | `New-LabECX` submitted via Automation; status tracked in SQL | | |
| 6C.11 | Provision — non-ECX tenant (Direct) | Run Provision on tenant with `EruplinkPort` ≠ "ECX" | "This tenant uses ExpressRoute Direct" message; no runbook created | | |
| 6C.12 | Provision — no ER circuit | Run Provision on tenant with `Ersku` = "None" | "No ExpressRoute circuit is configured" message | | |
| 6C.13 | Deprovision Provider 🔒8 | `ServiceProviderInstructions` card → "Deprovision at Provider" → Run | `Remove-LabECX` submitted (not `New-LabECX`); verify cmdlet name in prepared script | | |
| **Runbook Lifecycle & Cleanup** | | | | | |
| 6C.14 | Runbook auto-delete on success | Complete a runbook run with `AutoDeleteRunbook=true` | Runbook deleted from Azure Automation after terminal status persisted | | |
| 6C.15 | Runbook auto-delete on failure | Runbook run fails with `AutoDeleteRunbook=true` | Runbook still deleted (cleanup applies to all terminal states) | | |
| 6C.16 | Runbook auto-delete disabled | Set `AutoDeleteRunbook=false`; complete a run | Runbook remains in Azure Automation account | | |
| 6C.17 | Job status — Completed | Run a simple script to completion | Status shows "Completed" ✅; output captured | | |
| 6C.18 | Job status — Failed | Run a script that throws an error | Status shows "Failed" ❌; exception text captured and displayed | | |
| 6C.19 | Job status — long-running | Submit a script that runs > 60 seconds | Polling continues; modal stays responsive; no timeout on PathWeb side | | |
| 6C.20 | Job status — Suspended/Stopped | Manually suspend or stop a running job in Azure portal | PathWeb picks up terminal status on next poll; badge updates | | |
| **Modal & History** | | | | | |
| 6C.21 | Automation modal — reopen running | Close modal during a running job → reopen same card action | Modal resumes with current status/output from last poll | | |
| 6C.22 | Automation modal — reopen completed | Reopen modal after job completed (page not refreshed) | Shows completed status with full output | | |
| 6C.23 | Page refresh during running job | Refresh the Config page while a job is running | `Latest` endpoint refreshes status; job continues; badge reflects current state | | |
| 6C.24 | Recent run history | After completing 2+ runs for same config type | Recent-run history section shows multiple entries, most recent first | | |
| 6C.25 | Recent run history — collapsed | Check PS card recent-run section on page load | Section defaults to collapsed; expandable on click | | |
| 6C.26 | Status badge persistence | Complete a Deploy → hard refresh page (Ctrl+F5) | Status badge + timestamp visible on card header (from SQL, not memory) | | |
| 6C.27 | Multiple config types simultaneously | Start Deploy on `CreateERPowerShell` AND `CreateAzurePowerShell` at same time | Both jobs tracked independently; both modals work; both badges update | | |
| **Negative / Edge Cases** | | | | | |
| 6C.28 | Submit with empty config | Delete config from SQL → try Deploy | "No stored config found" error message | | |
| 6C.29 | Submit with invalid config type | Use devtools to POST `configType=FakeType` | "Config type 'FakeType' is not enabled for Azure Automation yet" | | |
| 6C.30 | Submit with invalid TenantGuid | Use devtools to POST `tenantGuid=00000000-...` | "Missing tenant or config type" error | | |
| 6C.31 | Azure Automation service unavailable | Revoke Managed Identity permissions or block network | Graceful error in modal ("Automation submit failed"); no unhandled exception | | |
| 6C.32 | Stale job ID in URL/memory | Poll status for a deleted job ID | Automation API returns error; PathWeb handles gracefully | | |

### 6D. Lab VM Actions (Create / Remove)

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| **Create Lab VMs** | | | | | |
| 6D.1 | Create Lab VMs 🔒8 | `LabVMPowerShell` card → "Create Lab VMs" → Run | Modal shows VM table with `#`, `OS`, `Target Server` (dropdown), `Status` columns | | |
| 6D.2 | Server dropdown defaults | Check Target Server column | Default server derived from first digit of TenantId (e.g., tenant 18 → `SEA-ER-01`) | | |
| 6D.3 | Server resolution — various TenantIds | Check defaults: TenantId 10→Server01, 22→Server02, 81→Server08 | Each maps to correct server based on first digit | | |
| 6D.4 | Override target server | Change a VM's target server dropdown → Submit | VM created on the overridden server (not the default) | | |
| 6D.5 | Per-VM progress indicators | Watch modal during creation | ⏳ while running, ✅ on success with VM name, ❌ on failure with error | | |
| 6D.6 | Parallel server execution | Submit VMs targeting 2+ different servers | VMs on different servers run in parallel (`Task.WhenAll`); check timestamps are close | | |
| 6D.7 | Sequential on same server | Submit 2+ VMs targeting the same server | VMs on same server run sequentially; tracker updates after each VM completes | | |
| 6D.8 | Structured JSON output | Check that `---LABVM-JSON---` marker is parsed correctly | VM names, statuses extracted from JSON; no raw marker text shown in UI | | |
| 6D.9 | Resume on reopen | Close modal during creation → reopen | Current state restored from in-memory tracker; completed VMs show status | | |
| 6D.10 | VM run persists to SQL | After completion, refresh page | Status badge + timestamp on `LabVMPowerShell` card header; `LabVmRun` row in DB | | |
| 6D.11 | Badge updates without refresh | Watch card header while run completes in background | Badge appears/updates live via JS polling | | |
| 6D.12 | No credentials on disk | Check Hyper-V server filesystem after VM creation | No password files, wrapper scripts, or credential artifacts on disk | | |
| 6D.13 | SSH keepalive prevents timeout | Create a VM that takes > 2 minutes | SSH connection stays alive; no dropped connection errors | | |
| **Remove Lab VMs** | | | | | |
| 6D.14 | Remove — Scan phase 🔒8 | `LabVMPowerShell` card → "Remove Lab VMs" → Run | Scan SSHs to all candidate `-ER-` servers in parallel; returns per-server VM list | | |
| 6D.15 | Scan — shows VM names and states | Check scan results in modal | Each server section shows VM names (e.g., `SEA-ER-18-VM01`) and states (Running/Off) | | |
| 6D.16 | Scan — no VMs found | Scan on a tenant with no existing VMs on any server | "No VMs found" message; confirm/remove button hidden | | |
| 6D.17 | Scan — partial results | Some servers reachable, some not | Reachable servers show results; unreachable show error; results not blocked by failures | | |
| 6D.18 | Remove — confirm dialog | After scan shows VMs → click Remove | Confirm dialog appears ("Are you sure?"); must explicitly confirm before removal starts | | |
| 6D.19 | Remove — confirm and execute | Confirm removal | `Remove-LabVM -TenantID {id}` runs on each server with VMs; per-server progress shown | | |
| 6D.20 | Remove — mode labels | Check modal title, buttons, messages during remove flow | Mode-appropriate labels ("Remove Lab VMs" not "Create Lab VMs") throughout | | |
| 6D.21 | Remove — polling and completion | Watch modal during removal | Status updates per-server; final badge shows success/failure | | |
| **Negative / Edge Cases** | | | | | |
| 6D.22 | Create on server unreachable | Target a Hyper-V server that is down or not responding | ❌ with connection error for that server; other servers unaffected | | |
| 6D.23 | Create with no `LabVMPowerShell` config | Try Create before generating configs | Error: no config found (or button disabled) | | |
| 6D.24 | No `-ER-` servers in Devices table | Try Create/Remove for a lab with no Hyper-V servers defined | "No servers found" or empty server dropdown | | |
| 6D.25 | Server admin creds missing from KV | Remove `Server-Admin` secret from `LabSecrets` vault → try Create | Key Vault error; graceful message; no unhandled exception | | |
| 6D.26 | Create → immediate Remove | Create VMs → immediately run Remove scan | Scan finds the just-created VMs; removal works | | |
| 6D.27 | Double-submit prevention | Click Submit twice rapidly in Create modal | Only one background task launched; no duplicate VMs | | |
| 6D.28 | App Service restart mid-run | Restart App Service while a Lab VM run is in progress | In-memory tracker lost; polling returns "not found"; no data corruption; SQL reflects last known state | | |

### 6E. Email Actions

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 6E.1 | Send Email 🔒8 | `NotificationEmail` card → "Send Email" → Run | Email sent via Logic App webhook; success status badge | | |
| 6E.2 | Send Email — verify recipient | Check received email | Correct recipient, subject ("Your PathLab Environment is Ready!"), HTML body with logo | | |
| 6E.3 | Send Email — logo renders | Check email body in recipient's inbox | Logo image loads from public `email-assets` path (not broken image) | | |
| 6E.4 | Send Email — no eMailHTML config | Try Send before generating configs (or delete config) | Error: "No stored notification email found" | | |
| 6E.5 | Send Email — wrong configType | Use devtools to POST with `configType=FakeEmail` | Error: "Config type 'FakeEmail' is not enabled for email sending" | | |
| 6E.6 | Send Email — Logic App down | Disconnect Logic App or use wrong webhook URL | Error message in UI; failure badge persisted in SQL | | |
| 6E.7 | Email status badge | After send, hard refresh page | Badge + timestamp on card header (persisted from `EmailSendRun`) | | |
| 6E.8 | Send Email twice | Send → wait for badge → Send again | Second send succeeds; badge updates to new timestamp | | |
| 6E.9 | Send Email — tenant has no Contacts | Clear Contacts field on tenant → try Send Email | Error: "Tenant has no contact email addresses configured" | | |

### 6F. Full Config Lifecycle (End-to-End Sequencing)

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 6F.1 | Full deploy sequence | Generate Config → Deploy ER → Deploy Azure PS → Provision Provider → Create VMs → Send Email | Each step completes; all badges show ✅; no step blocks the next | | |
| 6F.2 | Full teardown sequence | Remove VMs → Deprovision Provider → Remove from Azure → Remove from all Devices | Each removal step completes; badges update; tenant left clean | | |
| 6F.3 | Deploy → Edit tenant → Regenerate | Deploy to Azure → Edit tenant field → Regenerate Config | New config generated; old automation run history preserved; re-deploy uses new config | | |
| 6F.4 | Actions across ConfigVersions | Create tenant (v0) → generate → create config (v1) → regenerate | Configs target correct ConfigVersion; old v0 configs not leaked into v1 actions | | |

### 6G. Tenant Option → Config Variation Matrix 🔒8

Each tenant option on the Create/Edit page drives conditional branches in config generation. These tests verify that changing a single option produces the correct config output and that downstream actions (Apply, Deploy, etc.) work with that variant.

> **How to test:** For each row, create (or edit) a tenant with the specified option, generate config, and verify the expected config cards contain the described content. Where noted, also test the downstream action.

#### Lab & Region

| # | Test Case | Tenant Options | Expected Config Impact | Pass/Fail | Notes |
|---|-----------|---------------|----------------------|-----------|-------|
| 6G.1 | SEA lab tenant | Lab=SEA | Devices target `SEA-SRX42-01`, `SEA-MX480-01/02`, `SEA-NX93-01/02`; ER location=Seattle; IPv4 octets use SEA region values | | |
| 6G.2 | ASH lab tenant | Lab=ASH | Devices target `ASH-*` equivalents; ER location=Washington DC; region=East US | | |
| 6G.3 | Lab auto-selects Azure Region | Change Lab dropdown from SEA to ASH (or vice versa) | AzureRegion auto-populates (SEA→West US 2, ASH→East US) if empty | | |

#### ExpressRoute SKU & Uplink

| # | Test Case | Tenant Options | Expected Config Impact | Pass/Fail | Notes |
|---|-----------|---------------|----------------------|-----------|-------|
| 6G.4 | ER SKU = None | Ersku=None | `CreateERPowerShell` says "no ER required"; `ServiceProviderInstructions` says "no ER requested"; no ER gateway in Azure PS; firewall/router sections that depend on ER are absent | | |
| 6G.5 | ER SKU = Standard, ECX uplink | Ersku=Standard, EruplinkPort=ECX | `CreateERPowerShell` uses `ServiceProviderName=Equinix`; bandwidth in Mbps; `ServiceProviderInstructions` shows `New-LabECX` command; Provision at Provider button is active | | |
| 6G.6 | ER SKU = Premium, ECX uplink | Ersku=Premium, EruplinkPort=ECX | Same as Standard but `SkuTier=Premium` in ER creation script | | |
| 6G.7 | ER Direct — 100G Cisco MSEE (SEA) | EruplinkPort="100G Direct Cisco MSEE" | `CreateERPowerShell` loads `SEA-100Gb-Port-01`; bandwidth in Gbps; `ServiceProviderInstructions` says "ExpressRoute Direct, no SP actions"; Provision button shows "not required" | | |
| 6G.8 | ER Direct — 100G Juniper MSEE (SEA) | EruplinkPort="100G Direct Juniper MSEE" | Loads `SEA-100Gb-Port-02` | | |
| 6G.9 | ER Direct — 10G Juniper MSEE (ASH) | Lab=ASH, EruplinkPort="10G Direct Juniper MSEE" | Loads `ASH-10Gb-PortPair-01` | | |

#### Peering Options

| # | Test Case | Tenant Options | Expected Config Impact | Pass/Fail | Notes |
|---|-----------|---------------|----------------------|-----------|-------|
| 6G.10 | Private peering ON | PvtPeering=true | Azure PS: VNet created, private peering P2P addresses set, VLAN={TenantId}0; Firewall: BGP neighbor config for private peering; Router: private peering interface config | | |
| 6G.11 | Private peering OFF | PvtPeering=false | No VNet (unless VMs or VPN need it); no private peering in Azure PS; no private peering in firewall/router config | | |
| 6G.12 | Microsoft peering ON | Msftpeering=true, Msfttags populated | Azure PS: MSFT peering with route filter, BGP communities from Msfttags, NAT IP; Firewall: Microsoft peering policy/NAT rules | | |
| 6G.13 | Microsoft peering OFF | Msftpeering=false | No Microsoft peering section in any config | | |
| 6G.14 | Both peerings ON | PvtPeering=true, Msftpeering=true | Both peering blocks appear in Azure PS, firewall, and router configs | | |

#### ER Gateway

| # | Test Case | Tenant Options | Expected Config Impact | Pass/Fail | Notes |
|---|-----------|---------------|----------------------|-----------|-------|
| 6G.15 | ER Gateway = None | ErgatewaySize=None | No ER gateway block in Azure PS; no ER connection section | | |
| 6G.16 | ER Gateway = Standard/HighPerf/UltraPerf | ErgatewaySize=ErGw1AZ (or similar) | ER gateway creation block present with correct SKU; GatewaySubnet created; PIP allocated | | |
| 6G.17 | ER FastPath ON | ErfastPath=true | ER connection includes `-EnableFastPath` parameter | | |
| 6G.18 | ER FastPath OFF | ErfastPath=false | No FastPath parameter on ER connection | | |

#### VPN Options

| # | Test Case | Tenant Options | Expected Config Impact | Pass/Fail | Notes |
|---|-----------|---------------|----------------------|-----------|-------|
| 6G.19 | VPN Gateway = None | Vpngateway=None | No VPN gateway, local gateway, or connection blocks in Azure PS; no VPN sections in firewall config | | |
| 6G.20 | VPN Gateway — single active | Vpngateway=VpnGw2, Vpnconfig=Single | Single PIP, single ipconfig; one VPN connection; firewall: single IKE gateway | | |
| 6G.21 | VPN Gateway — Active-Active | Vpngateway=VpnGw2, Vpnconfig=Active-Active | Two PIPs (`pip1`/`pip2`), two ipconfigs, `-EnableActiveActiveFeature`; firewall: dual IKE gateways | | |
| 6G.22 | VPN + ER Gateway together | Vpngateway≠None, ErgatewaySize≠None | Both gateways share GatewaySubnet; BGP peer IPs differ based on combined presence (`.142/.143` vs `.132/.133` vs `.254`) | | |
| 6G.23 | VPN BGP ON | Vpnbgp=true | BGP parameters on VPN connection; firewall BGP neighbor for VPN | | |

#### Address Family (IPv4 / IPv6 / Dual Stack)

| # | Test Case | Tenant Options | Expected Config Impact | Pass/Fail | Notes |
|---|-----------|---------------|----------------------|-----------|-------|
| 6G.24 | IPv4 only | AddressFamily=IPv4 | VNet has only IPv4 address space; NICs have single ipconfig; no IPv6 PIPs; no `fd:` prefixes in any config | | |
| 6G.25 | Dual stack (IPv4+IPv6) | AddressFamily=Dual | VNet has both IPv4 and IPv6 address spaces; NICs get `ipconfig1` (v4) + `ipconfig2` (v6); dual PIPs per VM; private peering has IPv6 P2P addresses; firewall/router have IPv6 interface + BGP config | | |
| 6G.26 | IPv6 only | AddressFamily=IPv6 | Same IPv6 additions as Dual (verify no crash if IPv4 sections still generated) | | |

#### Azure VMs

| # | Test Case | Tenant Options | Expected Config Impact | Pass/Fail | Notes |
|---|-----------|---------------|----------------------|-----------|-------|
| 6G.27 | No Azure VMs | AzVm1-4=None | No VM loop, NSG, PIP, NIC, or extension blocks in Azure PS | | |
| 6G.28 | 1 Windows VM | AzVm1=Windows, rest=None | VM loop count=1; NSG opens RDP (3389); post-deploy extension=ICMPv4; `Set-AzVMSourceImage` uses WindowsServer | | |
| 6G.29 | 1 Ubuntu VM | AzVm1=Ubuntu | NSG opens SSH (22); `Set-AzVMSourceImage` uses Canonical/ubuntu-24_04-lts; no post-deploy Windows extension | | |
| 6G.30 | Mixed VMs (Windows+Ubuntu) | AzVm1=Windows, AzVm2=Ubuntu | VM loop count=2; each VM gets OS-specific NSG and image; extension only runs for Windows VMs | | |
| 6G.31 | 4 Azure VMs | AzVm1-4 all set | VM loop count=4; all 4 get PIPs, NICs, NSGs | | |
| 6G.32 | Azure VMs + Dual Stack | AzVm1=Windows, AddressFamily=Dual | Each VM gets both IPv4 and IPv6 PIPs; NIC has two ipconfigs | | |

#### Lab (On-Prem) VMs

| # | Test Case | Tenant Options | Expected Config Impact | Pass/Fail | Notes |
|---|-----------|---------------|----------------------|-----------|-------|
| 6G.33 | No Lab VMs | LabVm1-4=None | `LabVMPowerShell` config says "no on-prem VMs"; firewall has no Lab VM policies; Create Lab VMs action shows no VM rows | | |
| 6G.34 | 1 Lab VM | LabVm1=Windows | `LabVMPowerShell` has 1 VM row; firewall includes RDP/internet policies for lab VM subnet; Create Lab VMs modal shows 1 row | | |
| 6G.35 | 4 Lab VMs | LabVm1-4 all set | `LabVMPowerShell` has 4 rows; firewall policies cover all; Create Lab VMs modal shows 4 rows with server dropdowns | | |
| 6G.36 | Lab VMs drive firewall config | LabVm1=Windows, Vpngateway=None, Msftpeering=false | Firewall config IS generated (because Lab VMs need RDP/internet policies even without VPN or MSFT peering) | | |

#### Combinatorial / Edge Cases

| # | Test Case | Tenant Options | Expected Config Impact | Pass/Fail | Notes |
|---|-----------|---------------|----------------------|-----------|-------|
| 6G.37 | Minimal tenant (no ER, no VPN, no VMs) | Ersku=None, Vpngateway=None, AzVm1-4=None, LabVm1-4=None | Config cards generated but all say "not requested/required"; no firewall config (or minimal); Deploy actions show "no script required" or equivalent | | |
| 6G.38 | Maximal tenant (everything ON) | Ersku=Premium, ECX, PvtPeering=true, Msftpeering=true, ErGw=UltraPerf, FastPath=true, VPN=AA, Dual stack, 4 Azure VMs, 4 Lab VMs | All config sections populated; largest possible Azure PS script; full firewall/router configs; all action buttons active | | |
| 6G.39 | Edit option → Regenerate | Change Vpngateway from None to VpnGw2 → Regenerate | VPN sections now appear in Azure PS and firewall configs that weren't there before | | |
| 6G.40 | Config reflects ER speed correctly | ECX at 50 Mbps vs Direct at 10 Gbps | ECX: `BandwidthInMbps 50`; Direct: `BandwidthinGbps 10` (note different parameter and unit) | | |
| 6G.41 | Server Preference = "No on-prem needed" | serverPreference=100 | TenantId assigned in 100+ range; no Hyper-V server mapping (first digit > available servers); Lab VM actions may show no available servers | | |

---

## 7. Devices 🔒11

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 7.1 | Device index loads | Admin → Devices | Device list with all columns | | |
| 7.2 | Device Details | Click a device row | Details page with all device fields | | |
| 7.3 | Edit device 🔒11 | Details → Edit → change IP → Save | Updated value persists | | |
| 7.4 | Validate — router/switch | Click Validate on a Juniper/Cisco device | SSH `show version` (brief); connectivity confirmed in result | | |
| 7.5 | Validate — Hyper-V server | Click Validate on a `-ER-` server | Multi-check table: PS7 installed, Win Server 2025, sshd running, LabMod ≥ 1.5.0, log dir exists | | |
| 7.6 | Validate — unreachable device | Validate against an offline device | Error message (timeout / connection refused) | | |
| 7.7 | Validate — Index modal | Click Validate on Index page row (not Details) | Result appears in a modal | | |
| 7.8 | Validate — Details inline | Click Validate on Details page | Result appears inline on the page | | |
| 7.9 | Platform detection — Juniper | Device named `*-MX*` or `*-SRX*` | Detected as Juniper | | |
| 7.10 | Platform detection — NX-OS | Device named `*-NX*` | Detected as NX-OS | | |
| 7.11 | Platform detection — IOS-XE | Device named `*-ASR*` or `*-ISR*` | Detected as IOS-XE | | |
| 7.12 | Run Command page | Devices → Details → Run Command (if present) | Command entry and output display | | |
| 7.13 | Sort by Lab | Click "Lab" column header | Rows sort ascending; click again for descending | | |
| 7.14 | Sort by Name | Click "Name" column header | Rows sort by device name | | |
| 7.15 | Sort by Type | Click "Type" column header | Rows sort by device type | | |
| 7.16 | Sort by OS | Click "OS" column header | Rows sort by OS | | |
| 7.17 | Sort by InService | Click "InService" column header | Rows sort by in-service status | | |
| 7.18 | Auth gating — level < 11 blocked | Log in as auth level 6–10 → navigate to `/Devices` directly | PermissionError view shown | | |

---

## 8. IP Addresses 🔒11

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 8.1 | Address index loads | Admin → IP Addresses | Address list with all columns | | |
| 8.2 | Address Details | Click a row | Details page | | |
| 8.3 | Edit address | Details → Edit → change a field → Save | Updated value persists | | |
| 8.4 | Release address | Release action on an address | Address released; row updated | | |
| 8.5 | Auth gating — level < 11 blocked | Log in as auth level 6–10 → navigate to `/Addresses` directly | PermissionError view shown | | |
| 8.6 | Details — invalid/missing ID | Navigate to `/Addresses/Details` with no ID or a bogus GUID | TempData error message and redirect (not unhandled exception) | | |

---

## 9. Users 🔒14

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 9.1 | User index loads | Admin → Users | User list with email and auth level | | |
| 9.2 | Create user | Create New → fill email + auth level → Save | User created; appears in list | | |
| 9.3 | Create — duplicate email | Enter an existing email → Save | Validation error (or DB constraint error handled gracefully) | | |
| 9.4 | Edit user auth level | Edit a user → change auth level → Save | Updated level persists; user sees different nav items on next request | | |
| 9.5 | Delete user | Delete a user → Confirm | User removed from list | | |
| 9.6 | Delete — self deletion | Try to delete your own user record | Should warn or prevent self-lockout | | |

---

## 10. Tooltips 🔒14

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 10.1 | Tooltip index loads | Admin → Tooltips | Tooltip list/editor loads | | |
| 10.2 | Edit and save tooltips 🔒14 | Change a tooltip text → Save | Updated text persists; success message shown | | |
| 10.3 | Tooltip cache invalidation | Edit a tooltip → Save → open Tenant Create form (no app restart) | New tooltip text appears immediately (cache refreshed on save) | | |
| 10.4 | Tooltips render on forms | Open Tenant Create/Edit form | Field help icons/tooltips visible and functional | | |
| 10.5 | Auth gating — level < 14 blocked | Log in as auth level 11 → navigate to `/ToolTips` directly | PermissionError view shown | | |

---

## 11. Logs 🔒14

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 11.1 | Logs page loads | Admin → Logs | Log table with level, category, message, timestamp | | |
| 11.2 | Filter by level | Select "Warning" from level dropdown | Only Warning+ entries shown | | |
| 11.3 | Search by text | Enter a search term | Filtered results matching message text | | |
| 11.4 | Pagination | Navigate pages if >1 page of results | Correct page loads; page indicators accurate | | |
| 11.5 | Color-coded levels | Check log rows | Different colors for Debug, Info, Warning, Error, Critical | | |
| 11.6 | Logs are being written | Perform some actions → check Logs | Recent entries appear with correct category and user | | |

---

## 12. Settings 🔒14

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 12.1 | Settings page loads | Admin → Settings | Page shows Auto Delete Runbooks toggle and Automation Runbook Type field | | |
| 12.2 | Toggle Auto Delete Runbooks | Flip the toggle → Save | Value persists in SQL `Settings` table (`AutoDeleteRunbook`) | | |
| 12.3 | Change Automation Runbook Type | Change value → Save | Value persists (`AutomationRunbookType`); next runbook creation uses new type | | |
| 12.4 | Logging:Default level | Change default log level → Save | DB logger adjusts; verify by checking what appears in Logs page | | |
| 12.5 | Add logging category override | Add `Logging:Microsoft.EntityFrameworkCore` = `Warning` | EF Core debug/info logs suppressed; category-specific override active | | |
| 12.6 | Hierarchical subcategory matching | Set `Logging:PathWeb` = `Debug` | All `PathWeb.*` subcategories inherit Debug level | | |
| 12.7 | Remove logging override | Delete a category override → Save | Falls back to `Logging:Default` for that category | | |
| 12.8 | Auth gating — level < 14 blocked | Log in as auth level 11 → navigate to `/Settings` directly | PermissionError view shown | | |

---

## 13. About Pages

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 13.1 | About this Site | About → ...this Site | Page renders with app description | | |
| 13.2 | About the Physical Lab | About → ...the Physical Lab | Lab info page renders | | |
| 13.3 | About the Logical Tenant | About → ...the Logical Tenant | Tenant concept page renders | | |
| 13.4 | Site Dev Progress | About → ...Site Dev Progress | Progress/changelog page renders | | |

---

## 14. Cross-Cutting Concerns

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 14.1 | Dark mode across all pages | Enable dark mode → visit every major page | Consistent dark theme; no unreadable text or invisible elements | | |
| 14.2 | Light mode across all pages | Enable light mode → visit every major page | Consistent light theme | | |
| 14.3 | Modal readability — dark mode | Open Compare/Apply/Automation/LabVM modals in dark mode | Bright text on `#1a1a2e` background; all text legible | | |
| 14.4 | Modal readability — light mode | Open same modals in light mode | Proper contrast; no dark-on-dark text | | |
| 14.5 | Browser cache busting | Deploy a new build → browse the site | No stale CSS/JS served (Cache-Control headers set) | | |
| 14.6 | Build timestamp in footer | Check footer on any page | Shows current build date/time | | |
| 14.7 | Error page | Navigate to `/nonexistent` or trigger a 500 | Custom error page renders (not raw stack trace) | | |
| 14.8 | Responsive layout — mobile | Resize browser to mobile width | Navbar collapses to hamburger menu; tables scroll horizontally | | |
| 14.9 | Responsive layout — tablet | Resize to tablet width | Layout adapts; no horizontal overflow | | |
| 14.10 | Concurrent users | Two users perform actions on same tenant simultaneously | No data corruption; last write wins or appropriate conflict handling | | |
| 14.11 | SQL injection — search fields | Enter `'; DROP TABLE Tenants; --` in tenant search | No SQL error; search returns no results; database intact | | |
| 14.12 | XSS — text fields | Enter `<script>alert('xss')</script>` in a tenant name | Script NOT executed; rendered as escaped text | | |
| 14.13 | CSRF protection | POST to a form endpoint without antiforgery token | Request rejected (400 or redirect) | | |
| 14.14 | Long-running SSH timeout | Initiate SSH to a very slow device | Timeout handled gracefully; not a hung page | | |
| 14.15 | Key Vault unavailable | Temporarily block KV access → try a device action | Graceful error message; no unhandled exception | | |

---

## 15. Deployment & Warmup

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 15.1 | Quick deploy script | Run `quick-deploy.ps1` | Deploys successfully; touches `/health`, `/warmup`, Logs, Requests/Queue, Settings, `diag/view` | | |
| 15.2 | Warmup script | Run `Warmup.ps1` post-deploy | All warmup URLs return 200; EF Core queries pre-compiled | | |
| 15.3 | Build timestamp after deploy | Check `/health` and footer | Both show same/consistent build timestamp | | |
| 15.4 | First request after cold start | Restart App Service → first page load | Page loads (may be slow but no errors); warmup queries already cached | | |

---

## Sign-Off

| Role | Name | Date | Result |
|------|------|------|--------|
| Tester | | | |
| Developer | | | |
| Reviewer | | | |

**Total Test Cases: 235**
