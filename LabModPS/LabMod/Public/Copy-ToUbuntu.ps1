function Copy-ToUbuntu {
    <#
    .SYNOPSIS
        Copies base-image build scripts to the Ubuntu base VM via SCP.

    .DESCRIPTION
        Prompts for SSH credentials, creates the /var/tmp/LabMod directory on the Ubuntu
        base VM at 10.1.7.46, and copies the base-update.sh and tenant-shell.sh scripts
        from the lab file share. This is used during the initial Ubuntu base VHDX build.

    .EXAMPLE
        Copy-ToUbuntu

        Prompts for credentials and copies the build scripts to the Ubuntu base VM.
    #>
    # Prompt for username
    $user = Read-Host "Enter the Ubuntu username"

    # Prompt for password (secure)
    $sec = Read-Host "Enter the password" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    try {
        $pw = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }

    # Static IP
    $ip = "10.1.7.46"

    # Local file to copy
    $localFile1 = "\\10.17.7.7\Binaries\VMImages\BaseVHDX\base-update.sh"
    $localFile2 = "\\10.17.7.7\Binaries\VMImages\BaseVHDX\tenant-shell.sh"

    Write-Log "Creating directory on Ubuntu VM..." -TimeStamp
    Write-Output $pw | ssh $user@$ip "sudo -S mkdir -p /var/tmp/LabMod"

    Write-Log "Fixing ownership..."
    Write-Output $pw | ssh $user@$ip "sudo -S chown $user`:$user /var/tmp/LabMod"

    Write-Log "Copying files..."
    scp "$localFile1" "$user@$ip`:/var/tmp/LabMod/"
    scp "$localFile2" "$user@$ip`:/var/tmp/LabMod/"

    Write-Log "Done." -TimeStamp
}