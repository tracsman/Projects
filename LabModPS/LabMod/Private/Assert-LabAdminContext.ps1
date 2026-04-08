function Assert-LabAdminContext {
    [CmdletBinding()]
    param(
        [switch]$WarnOnly
    )

    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)

    if ($isAdmin) {
        return $true
    }

    $message = "This command must be run elevated as Administrator!"

    if ($WarnOnly) {
        Write-Warning $message
        return $false
    }

    throw $message
}
