Function Remove-LabVM {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true, ValueFromPipeline = $true, HelpMessage = 'Enter Company ID')]
        [int]$TenantID)
}
