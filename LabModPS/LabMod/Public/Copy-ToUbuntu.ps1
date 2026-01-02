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
    $localFile = "base-update.sh"

    Write-Host "Creating directory on Ubuntu VM..."
    echo $pw | ssh $user@$ip "sudo -S mkdir -p /var/tmp/LabMod"

    Write-Host "Fixing ownership..."
    echo $pw | ssh $user@$ip "sudo -S chown $user:$user /var/tmp/LabMod"

    Write-Host "Copying file..."
    scp $localFile "$user@$ip:/var/tmp/LabMod/"

    Write-Host "Done."
}