# PathWeb — Manual Test Plan

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
| 1.1 | `/health` returns OK | `curl https://<site>/health` | 200 JSON with `status: "healthy"` and `build` timestamp | | |
| 1.2 | `/warmup` succeeds | `curl https://<site>/warmup` | 200 JSON with `status: "warm"`, `durationMs`, pre-compiled query list | | |
| 1.3 | `/warmup` with broken SQL | Temporarily revoke DB access or wrong connection string | Returns structured error JSON, not a 500 stack trace | | |
| 1.4 | `/diag` JSON (authed) | Browse to `/diag` while logged in (auth level ≥ 14) | JSON diagnostics payload with automation, KV, logging, cache sections | | |
| 1.5 | `/diag?deep=true` live probes | Browse to `/diag?deep=true` | Additional live-probe results for Automation, Key Vault, Logic App | | |
| 1.6 | `/diag/view` HTML page | Browse to `/diag/view` | Formatted HTML diagnostics page with expandable sections | | |
| 1.7 | `/diag` blocked for low auth | Log in as auth level < 14, browse to `/diag` | Permission denied or 403 | | |

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
| 4.7 | Approve a request 🔒8 | Approve a pending request | Status changes; pending count in nav decreases by 1 | | |
| 4.8 | Reject a request 🔒8 | Reject a pending request | Status changes to rejected; item no longer in pending queue | | |

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
| 5.17 | Released tenant — no Edit button | Open a released tenant's Details | Edit, Release, Create Config buttons are hidden | | |

---

## 6. Tenant Config Page 🔒6

### 6A. Config Generation & Display

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 6A.1 | Config page loads | Tenant Details → View Config | Config page renders with config cards | | |
| 6A.2 | Create/Regenerate Config 🔒8 | Click Create Config (or Regenerate) | Configs generated; cards appear for all expected ConfigTypes | | |
| 6A.3 | Copy to clipboard | Click copy icon on any config card | Config text copied to clipboard; toast/feedback shown | | |
| 6A.4 | Per-device `-out` cards hidden | After config generation | Individual `{DeviceName}-out` cards do NOT appear; only combined BackoutConfig (if any) | | |
| 6A.5 | Inactive tenant — read-only | Open Config page for a released tenant | No Create/Regenerate button; no action buttons; copy still works | | |
| 6A.6 | Server Preference (ConfigVersion 0) | Create tenant with ConfigVersion=0 | UI label shows "Server Preference" (not "0" or "ConfigVersion 0") | | |

### 6B. Network Device Actions (Apply / Compare / Patch / Remove)

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 6B.1 | Compare to Device | Select "Compare to Device" → Run on a device card | Modal opens showing set-based diff (additions/removals); order-agnostic | | |
| 6B.2 | Compare — device unreachable | Run Compare against an offline device | Error message in modal (timeout or connection refused) | | |
| 6B.3 | Apply to Device 🔒8 | Select "Apply to Device" → Run → review diff → Confirm | Delta pushed via SSH; success message; status badge updates on card | | |
| 6B.4 | Apply — nothing to apply | Run Apply when device already matches config | "No changes needed" or empty diff message | | |
| 6B.5 | Patch Device 🔒8 | Select "Patch Device" → Run → review add/remove buckets → Confirm | Both additions and removals pushed; success badge | | |
| 6B.6 | Remove from Device 🔒8 | Select "Remove from Device" → Run → review backout config → Confirm | Backout config pushed via SSH; success badge | | |
| 6B.7 | Remove — no backout config | Run Remove when no `-out` record exists in SQL | Appropriate error message ("no backout config found") | | |
| 6B.8 | Verify Off Device 🔒8 | Select "Verify Off Device" → Run | SSH grep for tenant references; results shown in modal | | |
| 6B.9 | Device status badge — clickable | Click a device status badge (✅/❌) | Reopens the last apply result | | |
| 6B.10 | Apply to Juniper device | Apply config to a `-MX` or `-SRX` device | Uses `commit check` / `show compare` flow | | |
| 6B.11 | Apply to Cisco device | Apply config to a `-NX` or `-ASR`/`-ISR` device | Uses `wr` (write) flow | | |

### 6C. PowerShell / Azure Automation Actions

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 6C.1 | Deploy ER to Azure 🔒8 | `CreateERPowerShell` card → "Deploy to Azure" → Run | Automation modal opens; runbook created and job submitted; status polls to completion | | |
| 6C.2 | Deploy Azure PS to Azure 🔒8 | `CreateAzurePowerShell` card → "Deploy to Azure" → Run | Same automation flow; runbook type matches Settings.AutomationRunbookType | | |
| 6C.3 | Remove from Azure 🔒8 | `CreateAzurePowerShell` card → "Remove from Azure" → Run | Fetches `-out` backout script from SQL; opens in automation modal; `Write-Status` dual output works | | |
| 6C.4 | Remove from Azure — no backout | Run Remove on a tenant that was never deployed | Error message ("no backout script found") | | |
| 6C.5 | ER Remove redirects to Azure card | `CreateERPowerShell` card → "Remove from Azure" | Info message / redirect to `CreateAzurePowerShell` card | | |
| 6C.6 | Provision Provider 🔒8 | `ServiceProviderInstructions` card → "Provision at Provider" → Run | `New-LabECX` submitted via Automation; status tracked | | |
| 6C.7 | Provision — non-ECX tenant | Run Provision on a tenant that isn't ECX | Skipped with appropriate message | | |
| 6C.8 | Deprovision Provider 🔒8 | `ServiceProviderInstructions` card → "Deprovision at Provider" → Run | `Remove-LabECX` submitted via Automation | | |
| 6C.9 | Runbook auto-delete | Complete a runbook run with AutoDeleteRunbook=true in Settings | Runbook deleted from Azure Automation after terminal status | | |
| 6C.10 | Runbook auto-delete disabled | Set AutoDeleteRunbook=false; run a runbook | Runbook remains in Azure Automation after completion | | |
| 6C.11 | Automation modal — reopen | Close modal during a running job → reopen same card action | Modal resumes with current status/output | | |
| 6C.12 | Recent run history | After completing a run, check the config card | Recent-run history section visible (collapsed by default for PS cards) | | |
| 6C.13 | Status badge persistence | Complete a Deploy → refresh page | Status badge + timestamp visible on card header | | |

### 6D. Lab VM Actions (Create / Remove)

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 6D.1 | Create Lab VMs 🔒8 | `LabVMPowerShell` card → "Create Lab VMs" → Run | Modal shows VM table with `#`, `OS`, `Target Server`, `Status` columns | | |
| 6D.2 | Server dropdown defaults | Check Target Server column | Default server derived from first digit of TenantId (e.g., tenant 18 → SEA-ER-01) | | |
| 6D.3 | Override target server | Change a VM's target server dropdown → Submit | VM created on the overridden server | | |
| 6D.4 | Per-VM progress indicators | Watch modal during creation | ⏳ while running, ✅ on success, ❌ on failure; VM name appears | | |
| 6D.5 | Parallel server execution | Submit VMs targeting different servers | VMs on different servers run in parallel (check timestamps) | | |
| 6D.6 | Resume on reopen | Close modal during creation → reopen | Current state restored; completed VMs show status | | |
| 6D.7 | VM run persists to SQL | After completion, refresh page | Status badge + timestamp on `LabVMPowerShell` card header | | |
| 6D.8 | Badge updates without refresh | Watch card header while run completes in background | Badge appears/updates live | | |
| 6D.9 | Remove Lab VMs — Scan 🔒8 | `LabVMPowerShell` card → "Remove Lab VMs" → Run | Scan phase: SSHs to candidate `-ER-` servers; returns per-server VM list with names and states | | |
| 6D.10 | Remove — no VMs found | Scan on a tenant with no existing VMs | "No VMs found" message; no confirm/remove option | | |
| 6D.11 | Remove — confirm and execute | After scan shows VMs → Confirm removal | Confirm dialog appears; after confirm, `Remove-LabVM` runs on each server; per-server progress shown | | |
| 6D.12 | Remove — mode labels | Check modal labels during remove flow | Mode-appropriate labels ("Remove" not "Create") throughout | | |
| 6D.13 | Create on server unreachable | Target a Hyper-V server that is down | ❌ with error message for that server; other servers unaffected | | |

### 6E. Email Actions

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 6E.1 | Send Email 🔒8 | `NotificationEmail` card → "Send Email" → Run | Email sent via Logic App webhook; success status badge | | |
| 6E.2 | Send Email — Logic App down | Disconnect Logic App or use wrong URL | Error message; failure badge persisted | | |
| 6E.3 | Email status badge | After send, refresh page | Badge + timestamp on card header | | |

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

---

## 8. IP Addresses 🔒11

| # | Test Case | Steps | Expected Result | Pass/Fail | Notes |
|---|-----------|-------|-----------------|-----------|-------|
| 8.1 | Address index loads | Admin → IP Addresses | Address list with all columns | | |
| 8.2 | Address Details | Click a row | Details page | | |
| 8.3 | Edit address | Details → Edit → change a field → Save | Updated value persists | | |
| 8.4 | Release address | Release action on an address | Address released; row updated | | |

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
| 10.2 | Tooltips render on forms | Open Tenant Create/Edit form | Field help icons/tooltips visible and functional | | |

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

**Total Test Cases: 119**
