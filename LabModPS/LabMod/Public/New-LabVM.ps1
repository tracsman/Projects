function New-LabVM {
    <#
    .SYNOPSIS
        This command creates a new VM object, of the requested operatining system, and configures it for use in the physical
        Pathfinder lab, including network configuration and appropriate username and password for this tenant.

    .DESCRIPTION
        This command creates a new VM object, of the requested operatining system, and configures it for use in the physical
        Pathfinder lab, including network configuration and appropriate username and password for this tenant.

    .PARAMETER TenantID
        This parameter is required and is the two digit tenant ID in the lab. Valid values are from 10 to 99 inclusive.

    .PARAMETER Subscription
        This optional parameter signifies the Azure subscription in which the tenant resource group has already been created.
        Valid values are "ExpressRoute-Lab" and "Pathfinder", the default value is "ExpressRoute-Lab".

    .PARAMETER OS
        This optional parameter signifies the requested operating system for the VM. Valid values are "Server2019", "CentOS",
        and "Ubuntu", the default value is "Server2019".

    .PARAMETER CopyOnly
        This optional parameter instructs this command to only create the new VM VHDX, and doesn't configure it for use or
        start the VM.

    .PARAMETER VMCreateOnly
        This optional parameter instructs this command to only create the VM object in hypervisor. The VHDX must already 
        exist. This option will not and doesn't configure it for use or start the VM.

    .PARAMETER PostBuildOnly
        This optional parameter instructs this command to only apply the post build config (updating network settings, VM
        Name and User accounts). The VM Object must already exist and be running.

    .PARAMETER PwdUpdateOnly
        This optional parameter instructs this command to only update the PathLabUser password from the Azure Key Vaule.
        The VM must already exist and be running.

    .EXAMPLE
        New-LabVM 16

        This command creates a VM for tenant 16. The subscription and OS would default to "ExpressRoute-Lab" and 
        "Server2019" respectively

    .EXAMPLE
        New-LabVM -TenantID 16 -Subscription "Pathfinder" -OS "Centos"

        This command creates a VM for tenant 16. The subscription and OS are specifically called out to override the
        default values.

    .LINK
        https://github.com/tracsman/ERPath/tree/master/Team/Jon/LabPSModule

    .NOTES
        If this is the first VM tenant on this server the VM name will be suffixed with "01", if this command is run
        mulitple times for the same tenant the suffix is automatically increamentedted by 1, ie 01, 02, 03, etc. The
        paramenters of each machine can be different as needed. For instance the 01 VM may be Server2019 and the 02
        VM be Centos and VM03 Ubuntu if required for the tenant.

    #>

    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true, ValueFromPipeline = $true, HelpMessage = 'Enter Company ID')]
        [ValidateRange(10, 99)]
        [int]$TenantID,
        [ValidateSet('ExpressRoute-Lab','Pathfinder')]
        [string]$Subscription='ExpressRoute-Lab',
        [ValidateSet("Server2019", "CentOS", "Ubuntu")]
        [string]$OS="Server2019",
        [switch]$CopyOnly = $false,
        [switch]$VMCreateOnly = $false,
        [switch]$PostBuildOnly = $false,
        [switch]$PwdUpdateOnly = $false)

    #
    # Create On Prem VM Execution Path
    # 1. Initialize
    # 2. Validate
    # 3. Copy VHDX
    # 4. Create VM Object
    # 5a. Do post-deploy build
    # or
    # 5b. Do Password Update
    # 6. End nicely

    # 1. Initialize
    $StartTime = Get-Date

    # Lab Environment Variables
    $Lab = ($env:COMPUTERNAME.Split("-"))[0]
    [int]$VMInstance = 0
    Do {
        $VMInstance++
        $VMName = $Lab + "-ER-" + $TenantID + "-VM" + $VMInstance.ToString("00")
    } Until ((Get-VM -Name "$VMName*").Count -eq 0)
    Switch ($OS) {
        'Server2019' {$BaseVHDName = "Base2019.vhdx"}
        'Centos' {$BaseVHDName = "BaseCentOS.vhdx"}
        'Ubuntu' {$BaseVHDName = "BaseUbuntu.vhdx"}
    }
    $VHDSource = "C:\Hyper-V\ISO\BaseVHDX\" + $BaseVHDName
    $VMConfig = "C:\Hyper-V\Config"
    $VHDDest = "C:\Hyper-V\Virtual Hard Disks\"
    $VMDisk = $VHDDest + $VMName + ".vhdx"  

    # Azure Variables
    Switch ($Subscription) {
        'ExpressRoute-Lab' {$SubID = '4bffbb15-d414-4874-a2e4-c548c6d45e2a'}
        'Pathfinder' {$SubID = '79573dd5-f6ea-4fdc-a3aa-d05586980843'}
    }
    $kvNameAdmin = "LabSecrets"
    $kvSecretNameAdmin = "Server-Admin"
    $kvNameUser = "PathLabUser"

    # Script Variables
    If (-not $CopyOnly -and -not $VMCreateOnly -and -not $PostBuildOnly -and -not $PwdUpdateOnly) {
        $CopyOnly = $true
        $VMCreateOnly = $true
        $PostBuildOnly = $true
        $Action = "Building VM"
    }
    ElseIf ($CopyOnly) {$Action = "Coping VM drive"}
    ElseIf ($VMCreateOnly) {$Action = "Creating VM (no post build)"}
    ElseIf ($PostBuildOnly ) {$Action = "Post build script deployment"}
   
    # 2. Validate
    # Admin Session Check
    If (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Warning "This script must be run elevated as Administrator!"
        Return
    }

    # Login and permissions check
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Checking login and permissions" -ForegroundColor Cyan
    # Login and set subscription for ARM
    Try {$Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop).Subscription}
    Catch {
        Write-Host "  Logging in to ARM"
        Connect-AzAccount -UseDeviceAuthentication -Subscription $Subscription | Out-Null
        $Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop).Subscription
    }
    If ($SubID -ne $Sub.Id) {
        Write-Warning "  Logging in or setting context on subscription failed, please troubleshoot and retry."
        Return
    }
    Else { Write-Host "  Current Sub:", $Sub.Name, "(", $Sub.Id, ")" }

    If (-not (Test-Path $VHDSource)) { Write-Host; Write-Warning "Base .VHDX file ($BaseVHDName) not found"; Write-Host; Return }

    # Get admin creds
    Write-Host "  Grabbing admin secrets"
    $kvsAdmin = Get-AzKeyVaultSecret -VaultName $kvNameAdmin -Name $kvSecretNameAdmin -ErrorAction Stop
    $AdminCred = New-Object System.Management.Automation.PSCredential ("Administrator", $kvsAdmin.SecretValue)

    # Send the starting info
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "$Action..." -ForegroundColor Cyan
    Write-Host "          Lab: " -NoNewline
    Write-Host "$Lab" -ForegroundColor Yellow
    Write-Host "    Tenant ID: " -NoNewline
    Write-Host "$TenantID" -ForegroundColor Yellow
    Write-Host "       VMName: " -NoNewline
    Write-Host "$VMName" -ForegroundColor Yellow
    Write-Host "           OS: " -NoNewline
    Write-Host "$OS" -ForegroundColor Yellow
    Write-Verbose "      CopyOnly= $CopyOnly"
    Write-Verbose "  VMCreateOnly= $VMCreateOnly"
    Write-Verbose " PostBuildOnly= $PostBuildOnly"
    Write-Verbose " PwdUpdateOnly= $PwdUpdateOnly"

    # 3. Copy VHDX
    If ($CopyOnly) {
        Write-Host (Get-Date)' - ' -NoNewline
        Write-Host "Copying VHD for Tenant $TenantID" -ForegroundColor Cyan
        New-LabVMDrive -from $VHDSource -to $VMDisk
    }

    # 4. Create VM Object
    If ($VMCreateOnly) {
        Write-Host (Get-Date)' - ' -NoNewline
        Write-Host "Creating VM" -ForegroundColor Cyan
        $VLAN = $TenantID
        $VM = New-VM -Name $VMName -VHDPath $VMDisk -Path $VMConfig -MemoryStartupBytes 4GB -Generation 2
        Set-VM -VM $VM -SnapshotFileLocation $VMConfig -SmartPagingFilePath $VMConfig
        Add-VMScsiController -VMName $VMName
        Set-VMProcessor -VM $VM -Count 4
        Set-VMMemory -VM $VM -DynamicMemoryEnabled $True -MaximumBytes 8GB -MinimumBytes 4GB -StartupBytes 4GB
        Remove-VMNetworkAdapter -VM $VM
        Add-VMNetworkAdapter -VM $VM -Name "PublicNIC" -SwitchName "vs-NIC3" 
        $VMNic = Get-VMNetworkAdapter -VM $VM
        Set-VMNetworkAdapterVlan -VMNetworkAdapter $VMNic -Access -VlanId $VLAN -ErrorAction Stop
        Enable-VMIntegrationService -VMName $VMName -Name "Guest Service Interface"
    }

    # 5a. Do post-deploy build
    If ($PostBuildOnly) {
        Switch ($OS) {
            "Server2019" {
                Write-Host (Get-Date)' - ' -NoNewline
                Write-Host "Starting VM" -ForegroundColor Cyan
                Start-VM -Name $VMName
                Wait-VM -Name $VMName -For Heartbeat
                Write-Host (Get-Date)' - ' -NoNewline
                Write-Host "Pushing post deploy config" -ForegroundColor Cyan
                Write-Host "  Obtaining $kvNameUser secrets from Key Vault"
                $kvName = $Lab + '-Cust' + $TenantID + '-kv'
                $VM_UserName = $kvNameUser
                $kvs = Get-AzKeyVaultSecret -VaultName $kvName -Name $VM_UserName -ErrorAction Stop
                $ssPtr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($kvs.SecretValue)
                try {
                    $VM_UserPwd = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($ssPtr)
                } finally {
                    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ssPtr)
                }

                $Users = @($VM_UserName, $VM_UserPwd)

                If ((Invoke-Command -VMName $VMName -Credential $AdminCred { "Administrator" } -ErrorAction SilentlyContinue) -ne "Administrator") {
                    Write-Host "  Waiting for VM to come online: " -NoNewline
                }
                While ((Invoke-Command -VMName $VMName -Credential $AdminCred { "Administrator" } -ErrorAction SilentlyContinue) -ne "Administrator") {
                    Write-Host "*" -NoNewline
                    Start-Sleep -Seconds 1
                }
                Write-Host "  VM online, pushing post deploy config"
                Invoke-Command -VMName $VMName -Credential $AdminCred -ScriptBlock {
                    param ($VMName, $TenantID, $Users)

                    If (($VMName.Split("-"))[0] -eq "SEA") { $SecondOctet = 1 } Else { $SecondOctet = 2 }
                    If (($VMName.Split("-"))[0] -eq "SEA") { $IPv6Prefix = "2001:5a0:4406" } Else { $IPv6Prefix = "2001:5a0:3c06"}
                    [int]$VMHostIP = 9 + $VMName.Substring($VMName.Length - 2)
                    $VMIPv4 = "10." + $SecondOctet + "." + $TenantID + "." + $VMHostIP
                    $VMGWv4 = "10." + $SecondOctet + "." + $TenantID + ".1"
                    $VMIPv6 = $IPv6Prefix + ":" + $TenantID + "::" + $VMHostIP
                    $VMGWv6 = $IPv6Prefix + ":" + $TenantID + "::1"

                    $nicv4 = Get-NetIPAddress -AddressFamily IPv4 -AddressState Preferred -InterfaceAlias "Ethernet*"
                    If ($nicv4.IPv4Address -eq $VMIPv4) {"IPv4 address already set, skipping"}
                    Else {New-NetIPAddress -InterfaceIndex $nicv4.InterfaceIndex -IPAddress $VMIPv4 -PrefixLength 25 -DefaultGateway $VMGWv4 | Out-Null
                          Set-DnsClientServerAddress -InterfaceIndex $nicv4.InterfaceIndex -ServerAddresses "1.1.1.1", "1.0.0.1" | Out-Null}

                    $nicv6 = Get-NetIPAddress -AddressFamily IPv6 -AddressState Preferred -InterfaceAlias "Ethernet*"
                    If ($nicv6.IPv6Address -eq $VMIPv6) {"IPv6 address already set, skipping"}
                    Else {New-NetIPAddress -InterfaceIndex $nicv6.InterfaceIndex -IPAddress $VMIPv6 -PrefixLength 64 -DefaultGateway $VMGWv6 | Out-Null
                          Set-DnsClientServerAddress -InterfaceIndex $nicv6.InterfaceIndex -ServerAddresses "2606:4700:4700::1111", "2606:4700:4700::1001" | Out-Null}

                    # Turn on remote access and open firewall for it
                    Set-ItemProperty -Path "HKLM:\System\CurrentControlSet\Control\Terminal Server" -Name "fDenyTSConnections" -Value 0
                    Enable-NetFirewallRule -DisplayGroup "Remote Desktop"

                    # Turn On ICMPv4
                    Write-Host "  Opening ICMPv4 Port"
                    Try {Get-NetFirewallRule -Name Allow_ICMPv4_in -ErrorAction Stop | Out-Null
                        Write-Host "    Port already open"}
                    Catch {New-NetFirewallRule -DisplayName "Allow ICMPv4" -Name Allow_ICMPv4_in -Action Allow -Enabled True -Profile Any -Protocol ICMPv4 | Out-Null
                        Write-Host "    Port opened"}

                    # Turn On ICMPv6
                    Write-Host "  Opening ICMPv4 Port"
                    Try {Get-NetFirewallRule -Name Allow_ICMPv6_in -ErrorAction Stop | Out-Null
                        Write-Host "    Port already open"}
                    Catch {New-NetFirewallRule -DisplayName "Allow ICMPv6" -Name Allow_ICMPv6_in -Action Allow -Enabled True -Profile Any -Protocol ICMPv6 | Out-Null
                        Write-Host "    Port opened"}

                    # Get usernames and passwords
                    $VM_UserName = $Users[0]
                    $VM_UserPass = ConvertTo-SecureString $Users[1] -AsPlainText -Force 

                    # Create Local Accounts and add to Admin group
                    New-LocalUser -Name $VM_UserName -Password $VM_UserPass -FullName $VM_UserName -AccountNeverExpires -PasswordNeverExpires | Out-Null
                    Add-LocalGroupMember -Group "Administrators" -Member $VM_UserName

                    # Rename and restart
                    Rename-Computer -NewName "$VMName" -Restart

                } -ArgumentList $VMName, $TenantID, $Users
            }
            "CentOS" {
                If (($VMName.Split("-"))[0] -eq "SEA") { $SecondOctet = 1 } Else { $SecondOctet = 2 }
                If (($VMName.Split("-"))[0] -eq "SEA") { $IPv6Prefix = "2001:5a0:4406" } Else { $IPv6Prefix = "2001:5a0:3c06"}
                [int]$VMHostIP = 9 + $VMName.Substring($VMName.Length - 2)
                $VMIPv4 = "10." + $SecondOctet + "." + $TenantID + "." + $VMHostIP
                $VMGWv4 = "10." + $SecondOctet + "." + $TenantID + ".1"
                $VMIPv6 = $IPv6Prefix + ":" + $TenantID + "::" + $VMHostIP
                $VMGWv6 = $IPv6Prefix + ":" + $TenantID + "::1"

                Write-Host (Get-Date)' - ' -NoNewline
                Write-Host "Pushing post deploy config" -ForegroundColor Cyan
                
                Write-Host "  Obtaining $kvNameUser secrets from Key Vault"
                $kvName = $Lab + '-Cust' + $TenantID + '-kv'
                $VM_UserName = $kvNameUser
                $kvs = Get-AzKeyVaultSecret -VaultName $kvName -Name $VM_UserName -ErrorAction Stop
                $ssPtr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($kvs.SecretValue)
                try {
                    $VM_UserPwd = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($ssPtr)
                } finally {
                    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ssPtr)
                }
                
                Write-Host "  Updating SecureBootTemplate"
                Set-VMFirmware -VMName $VMName -SecureBootTemplate "MicrosoftUEFICertificateAuthority"
                                
                Write-Host "  Starting VM"
                Start-VM -Name $VMName
                Wait-VM -Name $VMName -For IPAddress
                Start-Sleep 10
                
                Write-Host "  Creating startup script"
                mkdir "$env:TEMP\LabMod\" -Force | Out-Null
                Out-File "$env:TEMP\LabMod\TenantScriptNeeded.txt" -Force -NoNewline -Encoding ascii
                $VM_UserName+":"+$VM_UserPwd | Out-File "$env:TEMP\LabMod\temp.txt" -Force -NoNewline -Encoding ascii
                $script  = "cp /etc/sysconfig/network-scripts/ifcfg-eth0 /etc/sysconfig/network-scripts/ifcfg-eth0.bak`n"
                $script += "sed -i 's/IPADDR=10.1.7.45/IPADDR=$VMIPv4/g' /etc/sysconfig/network-scripts/ifcfg-eth0`n"
                $script += "sed -i 's/GATEWAY=10.1.7.1/GATEWAY=$VMGWv4/g' /etc/sysconfig/network-scripts/ifcfg-eth0`n"
                $script += "echo 'IPV6ADDR=$VMIPv6/64' >> /etc/sysconfig/network-scripts/ifcfg-eth0`n"
                $script += "echo 'IPV6_DEFAULTGW=$VMGWv6' >> /etc/sysconfig/network-scripts/ifcfg-eth0`n"
                $script += "hostnamectl set-hostname $VMName`n"
                $script += "/usr/sbin/useradd -m $VM_UserName`n"
                $script += "cat /var/tmp/LabMod/temp.txt | passwd`n"
                $script += "usermod -aG wheel $VM_UserName`n"
                $script += "rm /var/tmp/LabMod/temp.txt -f`n"
                $script | Out-File "$env:TEMP\LabMod\tenant-update.sh" -Force -NoNewline -Encoding ascii

                Write-Host "  Copying startup scripts"
                Start-Sleep 10
                Copy-VMFile -Name $VMName -SourcePath "$env:TEMP\LabMod\temp.txt" -DestinationPath '/var/tmp/LabMod' -FileSource Host -Force
                Copy-VMFile -Name $VMName -SourcePath "$env:TEMP\LabMod\tenant-update.sh" -DestinationPath '/var/tmp/LabMod/' -FileSource Host -Force
                Copy-VMFile -Name $VMName -SourcePath "$env:TEMP\LabMod\TenantScriptNeeded.txt" -DestinationPath '/var/tmp/LabMod/' -FileSource Host -Force

                Write-Host "  Rebooting to kick off scripts"
                Write-Host "  Waiting on VM..."
                Stop-VM -Name $VMName
                Start-VM -Name $VMName
                Wait-VM -Name $VMName -For IPAddress
                Start-Sleep 15
                Stop-VM -Name $VMName
                Start-Sleep 5
                Start-VM -Name $VMName
                Wait-VM -Name $VMName -For IPAddress
                Start-Sleep 10
            }
            "Ubuntu" {
                If (($VMName.Split("-"))[0] -eq "SEA") { $SecondOctet = 1 } Else { $SecondOctet = 2 }
                If (($VMName.Split("-"))[0] -eq "SEA") { $IPv6Prefix = "2001:5a0:4406" } Else { $IPv6Prefix = "2001:5a0:3c06"}
                [int]$VMHostIP = 9 + $VMName.Substring($VMName.Length - 2)
                $VMIPv4 = "10." + $SecondOctet + "." + $TenantID + "." + $VMHostIP
                $VMGWv4 = "10." + $SecondOctet + "." + $TenantID + ".1"
                $VMIPv6 = $IPv6Prefix + ":" + $TenantID + "::" + $VMHostIP
                $VMGWv6 = $IPv6Prefix + ":" + $TenantID + "::1"

                Write-Host (Get-Date)' - ' -NoNewline
                Write-Host "Pushing post deploy config" -ForegroundColor Cyan
                
                Write-Host "  Obtaining $kvNameUser secrets from Key Vault"
                $kvName = $Lab + '-Cust' + $TenantID + '-kv'
                $VM_UserName = $kvNameUser
                $kvs = Get-AzKeyVaultSecret -VaultName $kvName -Name $VM_UserName -ErrorAction Stop
                $ssPtr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($kvs.SecretValue)
                try {
                    $VM_UserPwd = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($ssPtr)
                } finally {
                    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ssPtr)
                }
                
                Write-Host "  Updating SecureBootTemplate"
                Set-VMFirmware -VMName $VMName -SecureBootTemplate "MicrosoftUEFICertificateAuthority"

                Write-Host "  Starting VM"
                Start-VM -Name $VMName
                Wait-VM -Name $VMName -For IPAddress
                Start-Sleep 10

                Write-Host "  Creating startup script"
                mkdir "$env:TEMP\LabMod\" -Force | Out-Null
                Out-File "$env:TEMP\LabMod\TenantScriptNeeded.txt" -Force -NoNewline -Encoding ascii
                $VM_UserName+":"+$VM_UserPwd | Out-File "$env:TEMP\LabMod\temp.txt" -Force -NoNewline -Encoding ascii
                $script  = "cp /etc/netplan/00-installer-config.yaml /etc/netplan/00-installer-config.yaml.bak`n"
                $script += "sed -i 's/10.1.7.45/$VMIPv4/g' /etc/netplan/00-installer-config.yaml`n"
                $script += "sed -i 's/10.1.7.1/$VMGWv4/g' /etc/netplan/00-installer-config.yaml`n"
                $script += "sed -i 's/\/25/\/25\n      - $VMIPv6\/64/g' /etc/netplan/00-installer-config.yaml`n"
                $script += "sed -i 's/gateway4: $VMGWv4/gateway4: $VMGWv4\n      gateway6: $VMGWv6/g' /etc/netplan/00-installer-config.yaml`n"
                $script += "sed -i 's/1.0.0.1/1.0.0.1\n        - 2606:4700:4700::1111\n        - 2606:4700:4700::1001/g' /etc/netplan/00-installer-config.yaml`n"
                $script += "netplan --debug generate`n"
				$script += "netplan apply`n"
                $script += "hostnamectl set-hostname $VMName`n"
                $script += "/usr/sbin/useradd -m $VM_UserName`n"
                $script += "cat /var/tmp/LabMod/temp.txt | /usr/sbin/chpasswd`n"
                $script += "/usr/sbin/usermod -aG sudo $VM_UserName`n"
                $script += "rm /var/tmp/LabMod/temp.txt -f`n"
                $script | Out-File "$env:TEMP\LabMod\tenant-update.sh" -Force -NoNewline -Encoding ascii

                Write-Host "  Copying startup scripts"
                Start-Sleep 10
                Copy-VMFile -Name $VMName -SourcePath "$env:TEMP\LabMod\temp.txt" -DestinationPath '/var/tmp/LabMod' -FileSource Host -Force
                Copy-VMFile -Name $VMName -SourcePath "$env:TEMP\LabMod\tenant-update.sh" -DestinationPath '/var/tmp/LabMod/' -FileSource Host -Force
                Copy-VMFile -Name $VMName -SourcePath "$env:TEMP\LabMod\TenantScriptNeeded.txt" -DestinationPath '/var/tmp/LabMod/' -FileSource Host -Force

                Write-Host "  Rebooting to kick off scripts"
                Write-Host "  Waiting on VM..."
                Stop-VM -Name $VMName
                Start-VM -Name $VMName
                Wait-VM -Name $VMName -For IPAddress
                Start-Sleep 10
                Write-Host "  Rebooting to instantiate new settings"
                Write-Host "  Waiting on VM..."
                Stop-VM -Name $VMName
                Start-VM -Name $VMName
                Wait-VM -Name $VMName -For IPAddress
                Start-Sleep 10
            }
        }
    }

    # 5b. Do Password Update
    If ($PwdUpdateOnly) {
        Write-Host (Get-Date)' - ' -NoNewline
        Write-Host "Updating Lab VM Passwords" -ForegroundColor Cyan
        Write-Host "  obtaining secrets from Key Vault"
        $kvName = $Lab + '-Cust' + $TenantID + '-kv'

        $VM_UserName = "User01"
        $kvs = Get-AzKeyVaultSecret -VaultName $kvName -Name $VM_UserName -ErrorAction Stop
        $ssPtr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($kvs.SecretValue)
        try {
            $VM_UserPwd = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($ssPtr)
        } finally {
            [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ssPtr)
        }

        $Users = @($VM_UserName, $VM_UserPWD)

        If ((Invoke-Command -VMName $VMName -Credential $AdminCred { "Administrator" } -ErrorAction SilentlyContinue) -ne "Administrator") {
            Write-Host "  Waiting for VM to come online: " -NoNewline
        }
        While ((Invoke-Command -VMName $VMName -Credential $AdminCred { "Administrator" } -ErrorAction SilentlyContinue) -ne "Administrator") {
            Write-Host "*" -NoNewline
            Start-Sleep -Seconds 1
        }
        Write-Host " VM online, pushing password updates"
        Invoke-Command -VMName $VMName -Credential $AdminCred -ScriptBlock {
            param ($Users)

            # Get usernames and passwords
            $VM_UserName = $Users[0]
            $VM_UserPass = ConvertTo-SecureString $Users[1] -AsPlainText -Force 

            # Update Local Accounts and add to Admin group
            Set-LocalUser -Name $VM_UserName -Password $VM_UserPass | Out-Null

        } -ArgumentList (,$Users)
    }

    # 6. End nicely
    $EndTime = Get-Date
    $TimeDiff = New-TimeSpan $StartTime $EndTime
    $Mins = $TimeDiff.Minutes
    $Secs = $TimeDiff.Seconds
    $RunTime = '{0:00}:{1:00} (M:S)' -f $Mins,$Secs
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "$Action completed successfully" -ForegroundColor Green
    Write-Host "Time to create: $RunTime"
    Write-Host
}
