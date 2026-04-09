function Update-LabLibrary {
    <#
    .SYNOPSIS
        Mirrors the lab ISO and base image library from the file share to the local server.

    .DESCRIPTION
        Uses Robocopy /MIR to synchronize the contents of \\10.17.7.7\Binaries\VMImages
        to C:\Hyper-V\ISO on the local Hyper-V host. Requires an elevated session.

    .EXAMPLE
        Update-LabLibrary

        Synchronizes the ISO library from the central file share.
    #>
    # Admin Session Check
    if (-not (Assert-LabAdminContext -WarnOnly)) {
        Return
    }

    # Library Update
    Robocopy.exe \\10.17.7.7\Binaries\VMImages "C:\Hyper-V\ISO" /MIR
}
