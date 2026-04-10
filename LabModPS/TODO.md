# TODO

## Active

### Runbook Output Hygiene

1. [ ] **Add final structured `Write-Output` result object**
   Emit a concise end-of-run object with multiple properties summarizing the job outcome; exact property schema to be decided later.



## Completed

1. [x] **Replace routine `Write-Host` with `Write-Verbose`**
   Runbook-callable functions now use `Write-Log` (which routes to `Write-Verbose` in automation). Excluded: `Build-LabBaseVM.ps1` (clipboard meta-script), `Invoke-Command` blocks in `New-LabVM.ps1` (guest VM context), and `Get-LabECX.ps1` (interactive-only formatting).
