function Copy-ToUbuntu {
    # Prompt for username
    $user = Read-Host "Enter the Ubuntu username"

    # Prompt for password (secure)
    $sec = Read-Host "Enter the password" -AsSecureString
    $pw  = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
        )

    # Static IP
    $ip = "10.1.7.46"

    # Local file to copy
    $localFile1 = "\\10.17.7.7\Binaries\VMImages\BaseVHDX\base-update.sh"
    $localFile2 = "\\10.17.7.7\Binaries\VMImages\BaseVHDX\tenant-shell.sh"

    Write-Host "Creating directory on Ubuntu VM..."
    Write-Output $pw | ssh $user@$ip "sudo -S mkdir -p /var/tmp/LabMod"

    Write-Host "Fixing ownership..."
    Write-Output $pw | ssh $user@$ip "sudo -S chown $user`:$user /var/tmp/LabMod"

    Write-Host "Copying files..."
    scp "$localFile1" "$user@$ip`:/var/tmp/LabMod/"
    scp "$localFile2" "$user@$ip`:/var/tmp/LabMod/"

    Write-Host "Done."
}