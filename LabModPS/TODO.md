# TODO

## Active

### Runbook Output Hygiene

1. [ ] **Add final structured `Write-Output` result object**
   Emit a concise end-of-run object with multiple properties summarizing the job outcome for remaining runbook-callable commands. Provider commands now have a formatted summary; a machine-readable object contract for all cmdlets remains separate work.

### Log Tooling

2. [ ] **Create `Get-LabLog` cmdlet to parse JSONL log files**
   Add a public function that reads `*.log.jsonl` files from the logs folder, deserializes each line, and returns structured objects for filtering/reporting.



## Completed

4. [x] **Fix Windows post-build credential binding (LabMod 1.5.1.2)**
   v1.5.0.0 wrapped `$Users` as `(,$Users)` in a three-argument `-ArgumentList`, so inside the VM `$Users[1]` was null and `ConvertTo-SecureString` failed after IP setup, skipping PathLabUser creation and rename. Ubuntu and `-PwdUpdateOnly` were unaffected. Offline check: `.\Tests\LabVmPostBuild.Tests.ps1`.

3. [x] **Tenant-wide VM numbering (LabMod 1.5.1.1)**
   Add optional `VMNumber` to `Start-LabVmRequest` and `New-LabVM`; PathWeb passes the tenant row index so names and guest IP suffixes stay consistent across hosts and OS types. Explicit creation rejects local VM/disk collisions; omitted numbering preserves legacy behavior. Offline checks: `.\Tests\LabVmNaming.Tests.ps1`. Install on every target Hyper-V host before PathWeb deployment; reconcile existing duplicates and re-test 6D.4/6D.6/6D.29.

2. [x] **Readable ECX provider output (LabMod 1.5.1.0)**
   `New-LabECX` and `Remove-LabECX` use private status/summary helpers with banners, timestamped steps, indented details, and OK/SKIP/FAIL markers. Interactive progress is colored; Automation receives one plain-text copy on the success stream without requiring `-Verbose`. Summaries report accepted connection requests, skipped circuits, failures, and duration, including partial failures and terminating errors. Shared `Write-Log` and unrelated commands are unchanged. Offline checks: `.\Tests\ProviderOutput.Tests.ps1`. Package prepared locally; publishing and real-device/provider tests remain pending.

1. [x] **Replace routine `Write-Host` with `Write-Verbose`**
   Runbook-callable functions now use `Write-Log` (which routes to `Write-Verbose` in automation). Excluded: `Build-LabBaseVM.ps1` (clipboard meta-script), `Invoke-Command` blocks in `New-LabVM.ps1` (guest VM context), and `Get-LabECX.ps1` (interactive-only formatting).
