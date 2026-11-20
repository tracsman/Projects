function Write-LabProviderSummary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [hashtable]$Run
    )

    $endTime = Get-Date
    $duration = $endTime - $Run.Started
    $durationText = '{0:00}:{1:00}' -f [math]::Floor($duration.TotalMinutes), $duration.Seconds
    $result = if ($Run.Failures -gt 0) {
        if ($Run.ConnectionsSubmitted -gt 0) { 'PartialFailure' } else { 'Failed' }
    }
    elseif ($Run.ConnectionsSubmitted -gt 0) { 'Submitted' }
    else { 'NoChanges' }

    $summary = [pscustomobject][ordered]@{
        Script = $Run.Script
        Tenant = $Run.Tenant
        Started = $Run.Started.ToString('u')
        Finished = $endTime.ToString('u')
        Duration = $durationText
        Result = $result
        CircuitsExamined = $Run.CircuitsExamined
        ConnectionsSubmitted = $Run.ConnectionsSubmitted
        CircuitsSkipped = $Run.CircuitsSkipped
        Failures = $Run.Failures
    }

    Write-LabProviderStatus -Kind Banner -Message ('=' * 70)
    Write-LabProviderStatus -Kind Banner -Message " Finished in $durationText (M:S) - $result"
    Write-LabProviderStatus -Kind Banner -Message ('=' * 70)
    # Match PathWeb's formatted completion record rather than a host-dependent table.
    $summary | Format-List | Out-String | Write-Output
}
