# Building a PathLab Base VM VHDX

This README instructs in the creation of a new base VHDX for the following OS:

* [Windows Server 2019](#windows-server-2019)
* [CentOS](#centos-base-build-centos-8)
* [Ubuntu](#ubuntu-server-non-gui-base-build-ubuntu-1804)

## Windows Server 2019

Instruction set coming soon (ish)

## CentOS Base Build (CentOS 8)

 **Must be built on a Seattle physical server!!!**

* RDP to any physical server in the Seattle Path Lab
* Open an Admin PS console, run Build-LabBaseCentOS.ps1 (<https://raw.githubusercontent.com/tracsman/Projects/master/LabModPS/Build-LabBaseCentOS.ps1>)
* Once complete, in Hyper-V Manager, open a connection to VM
* Start the VM
* OS Install Prompt: Install CentOS 8
* GUI Setup:
  * English/English (United States), Continue
  * Date & Time: Los Angeles, Set local time, Done
  * Software Selection:
    * Base Environment: "Minimal Install"
    * Add-Ons: "Guest Agents"
    * Done
  * Installation Destination: Select "Msft Virtual Disk", Done
    * Network & HostName:
      * Host name: CentOSBase, Apply
      * Ethernet (eth0): Configure...
        * General Tab:
          * Check "automatically connect to this network..."
        * IPv4 Settings Tab:
          * Method: Manual
          * Address: Add
          * Address: 10.1.7.45
          * Netmask: 25
          * Gateway: 10.1.7.1
        * DNS Servers: 1.1.1.1,1.0.0.1
      * Save
    * Done
  * Begin Installation
  * Root Password:
    * Root Password: Get from key vault, you need to type, no paste :(
    * Confirm: repeat above
    * Done
  * Create User:
    * Full Name: Your name
    * User Name: Your local account user name
    * Make administrator: Check
    * Require Password: Check
    * Password: Your local account password
    * Confirm: repeat password
    * Done
  * Finish configuration
  * **system installs, takes about 5 minutes**
  * On completion, click the reboot button
* Open Admin PowerShell:

  ````PowerShell
    Enable-VMIntegrationService -VMName CentOSBase -Name "Guest Service Interface"
    Copy-VMFile -Name CentOSBase -SourcePath "C:\Hyper-V\ISO\BaseVHDX\tenant-shell.sh" -DestinationPath '/var/tmp/LabMod/' -FileSource Host -CreateFullPath -Force
    Copy-VMFile -Name CentOSBase -SourcePath "C:\Hyper-V\ISO\BaseVHDX\base-update.sh" -DestinationPath '/var/tmp/LabMod/' -FileSource Host -CreateFullPath -Force
  ````

* Open Putty to *local user*@10.1.7.45
* At login prompt, use your local credentials

    ````bash
    sudo -u root sh /var/tmp/LabMod/base-update.sh
    sudo shutdown now
    ````

* Back in PowerShell

    ````PowerShell
    Copy-Item "C:\Hyper-V\Virtual Hard Disks\CentOSBase.vhdx" "C:\Hyper-V\ISO\BaseVHDX\BaseCentOS.vhdx" -Force
    ````

CentOS base image is now complete and copied to the ISO dir, proceed with tenant VM creation

### CentOS Tenant VM Build

in admin ps:

````PowerShell
New-LabVM 90 -OS CentOS
````

## Ubuntu Server (non-GUI) Base Build Ubuntu 18.04

 **Must be built on a Seattle physical server!!!**

* RDP to any physical server in the Seattle Path Lab
* Open an Admin PS console, run Build-LabBaseUbuntu.ps1 (<https://raw.githubusercontent.com/tracsman/Projects/master/LabModPS/Build-LabBaseUbuntu.ps1>)
* Start the VM
* OS Install Prompt: Install Ubuntu Server (default option)
* CGA Setup:
  * Preferred Language: English, Enter to Continue
  * Keyboard Config: English (US) / English (US), Enter to continue
  * Network Connections:
    * arrow up to eth0, Enter to bring up menu
    * arrow down to select IPv4, enter to bring up menu
    * IPv4 Method: press Enter and select Manual
    * Tab to Subnet: 10.1.7.0/25
    * Tab to Address: 10.1.7.45
    * Tab to Gateway: 10.1.7.1
    * Tab to Name servers: 1.1.1.1,1.0.0.1
    * Tab to save, Enter to continue
    * arrow down to Done, Enter to continue
  * Configure proxy: leave blank, Enter to continue
  * Configure Ubuntu mirror: accept default, Enter to continue
  * Filesystem setup: "Use An Entire Disk" (default), Enter to continue
  * Filesystem setup: Accept default local disk, Enter to continue
  * Filesystem setup: Accept default FILE SYSTEM SUMMARY, Enter to bring up pop-up warning
  * Confirm Destructive action: arrow down to Continue, Enter to continue
  * Profile Setup:
    * Your name: Enter your first and last name separated by a space
    * Your server's name: ubuntubase (all lower case)
    * Pick a username: Your local account user name
    * Choose a password: Your local account password
    * Confirm password: repeat password
    * tab to "Done", Enter to continue
  * SSH Setup: Enter to check "Install OpenSSH server", tab to "Done", Enter to continue
  * Featured Server Snaps: arrow to "Done" (leaving all unchecked), Enter to continue
  * **system installs, takes about 5 - 10 minutes**
  * **system installs security updates, takes about 5 minutes**
  * When updates are done, on-screen "button" will flip to "Reboot", select and hit Enter
* After reboot, you'll get a "remove installation media", ignore it and hit Enter
* Close Virtual Machine Connection window
* Open Putty session to 10.1.7.45
  * At login prompt, use your local credentials
  * Run:
  
   ````bash
   sudo apt-get install linux-virtual linux-cloud-tools-virtual linux-tools-virtual -y
   ````

  * Wait for install to complete
* Back on the physical server, open Admin PowerShell:

    ````PowerShell
  Copy-VMFile -Name ubuntubase -SourcePath "C:\Hyper-V\ISO\BaseVHDX\tenant-shell.sh" -DestinationPath '/var/tmp/LabMod/' -FileSource Host -CreateFullPath -Force
  Copy-VMFile -Name ubuntubase -SourcePath "C:\Hyper-V\ISO\BaseVHDX\base-update.sh" -DestinationPath '/var/tmp/LabMod/' -FileSource Host -CreateFullPath -Force
  ````

* Back in Putty:

    ````bash
    sudo -u root sh /var/tmp/LabMod/base-update.sh
    sudo shutdown now
    ````

* Back in PowerShell

    ````PowerShell
    Copy-Item "C:\Hyper-V\Virtual Hard Disks\UbuntuBase.vhdx" "C:\Hyper-V\ISO\BaseVHDX\BaseUbuntu.vhdx" -Force
    ````

Ubuntu Base image is now complete and copied to the ISO dir, proceed with tenant VM creation

### Ubuntu Tenant VM Build

in admin ps:

````PowerShell
New-LabVM 90 -OS Ubuntu
````
