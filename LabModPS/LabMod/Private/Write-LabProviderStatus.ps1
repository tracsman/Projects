function Write-LabProviderStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Message,

        [ValidateSet('Banner', 'Step', 'Detail', 'OK', 'Skip', 'Fail')]
        [string]$Kind = 'Detail'
    )

    $text = switch ($Kind) {
        'Banner' { $Message }
        'Step'   { '{0} ==> {1}' -f (Get-Date -Format 'HH:mm:ss'), $Message }
        'Detail' { '    {0}' -f $Message.TrimStart() }
        'OK'     { '[ OK ] {0}' -f $Message }
        'Skip'   { '[SKIP] {0}' -f $Message }
        'Fail'   { '[FAIL] {0}' -f $Message }
    }

    $level = if ($Kind -eq 'Fail') { 'Error' } else { 'Information' }
    Write-LabLogEntry -Message $text -Level $level

    # Emit exactly one user-facing copy; do not mirror routine progress to Verbose.
    $isRunbook = $null -ne $PSPrivateMetadata -and $null -ne $PSPrivateMetadata.JobId
    if ($isRunbook -or -not $script:IsInteractive) {
        Write-Output $text
    }
    else {
        $color = switch ($Kind) {
            'Banner' { 'DarkCyan' }
            'Step'   { 'Cyan' }
            'Detail' { 'Gray' }
            'OK'     { 'Green' }
            'Skip'   { 'DarkGray' }
            'Fail'   { 'Red' }
        }
        Write-Host $text -ForegroundColor $color
    }
}
