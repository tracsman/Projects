function Update-LabLibrary {
    # Admin Session Check
    If (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Warning "This script must be run elevated as Administrator!"
        Return
    }

    # Library Update
    Robocopy.exe \\10.17.7.4\Binaries\VMImages "C:\Hyper-V\ISO" /MIR
}
