# TODO

## Active

### Runbook Output Hygiene

1. [ ] **Add final structured `Write-Output` result object**
   Emit a concise end-of-run object with multiple properties summarizing the job outcome for remaining runbook-callable commands. Provider commands now have a formatted summary; a machine-readable object contract for all cmdlets remains separate work.

### Log Tooling

2. [ ] **Create `Get-LabLog` cmdlet to parse JSONL log files**
   Add a public function that reads `*.log.jsonl` files from the logs folder, deserializes each line, and returns structured objects for filtering/reporting.



## Completed

2. [x] **Readable ECX provider output (LabMod 1.5.1.0)**
   `New-LabECX` and `Remove-LabECX` use private status/summary helpers with banners, timestamped steps, indented details, and OK/SKIP/FAIL markers. Interactive progress is colored; Automation receives one plain-text copy on the success stream without requiring `-Verbose`. Summaries report accepted connection requests, skipped circuits, failures, and duration, including partial failures and terminating errors. Shared `Write-Log` and unrelated commands are unchanged. Offline checks: `.\Tests\ProviderOutput.Tests.ps1`. Package prepared locally; publishing and real-device/provider tests remain pending.

1. [x] **Replace routine `Write-Host` with `Write-Verbose`**
   Runbook-callable functions now use `Write-Log` (which routes to `Write-Verbose` in automation). Excluded: `Build-LabBaseVM.ps1` (clipboard meta-script), `Invoke-Command` blocks in `New-LabVM.ps1` (guest VM context), and `Get-LabECX.ps1` (interactive-only formatting).
