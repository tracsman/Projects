# Building a PathLab Base VM VHDX

This README instructs in the creation of a new base VHDX for the following OS:

* [Windows Server 2019](#windows-server-2019)
* [CentOS](#centos-base-build-centos-8)
* [Ubuntu](#ubuntu-server-non-gui-base-build-ubuntu-2004)

The instructions on this page are for normal lab tenant environments, e.g. SEA-Cust14. It is NOT for workshop base images, for workshop images use the specific workshop instructions in the DevOps TCSP repo.

## Windows Server 2019

 **Must be built on a Seattle physical server!!!**

* RDP to any physical server in the Seattle Path Lab
* Open an Admin PS console, run ````Build-LabBaseVHDX```` to create the VM Object
* Once complete, in Hyper-V Manager, open a connection to VM
* Start the VM
* When prompted, press any key to boot from DVD
* Accept language defaults, click next
* Click "Install Now"
* Select Datacenter (Desktop Experience) from the list and click Next
* Accept license terms, click next
* Select "Custom" option
* Select Drive 0 for install and click Next
* While waiting for windows to install, go to the ExpressRoute-lab key vault and get the Sever-Admin password for Administrator password use
* After the OS install completes, the OS must be configured, connect to the VM set admin password
* Using the admin password, log into the VM
* On the Base VM, open an admin ISE PS session
  * Switch to the physical server PowerShell run ````Build-LabBaseVM````, this will load the first part of the base VM script into the clipboard and pause.
  * Switch to the base VM, in the VM connection ensure "Enhanced Session" is **OFF**. To do this, in the Virtual Machine Connection client, click "View", and ensure the "Enhanced Session" is not enabled.
  * In the Virtual Machine Connection client, click "Clipboard", "Type Clipboard Text" to paste the first half of the script into the PowerShell ISE script window.
  * Return to the lab server, press Enter to copy the second part of the script to the clipboard.
  * Paste in the script in the Base VM PowerShell ISE and then run the script (no save required).
  * The installation of the Edge browser may prompt a patching cycle, allow this, and reboot if needed. If you have to reboot, log back in after reboot and open Admin PowerShell ISE, the script should still be there, run it again. Repeat until Edge installs.
  * At the conclusion of the script, the VM will reboot.
* Once rebooted, from the Azure Mgmt jump box RDP to server 10.1.7.120, use the Admin password to log back into the Base VM.
* Patch OS
  * Close the PowerShell ISE window if it auto-opens, don't save script if prompted.
  * Patch, reboot if needed
  * Repeat until no more updates
* Shutdown
  * Right-click start and select "Shutdown or sign-out", then "Shut down"
  * If prompted, select "Other (Planned)" for shut down reason
* Once VM is off, close Connect and/or RDP sessions with VM
* In Hyper-V Manager, navigate to the VM, wait for the VM State to become "Off".
* Right-click the VM and Delete it
* In File Explorer, copy the VHDX file (Base2019.vhdx) from the C:\Hyper-V\Virtual Hard Disks to the C:\Hyper-V\ISO\BaseVHDX folder
* Copy the VHDX to \\10.17.7.7\Binaries\VMImages\BaseVHDX folder to easier disemination to other servers (replace existing file if prompted)
* Delete the original VHDX file in C:\Hyper-V\Virtual Hard Disks
* Log on to each physical server to be used as a lab VM Host, and in an elevated PS prompt run Update-LabLibrary to pull the VHDX library (including the new VHDX) down to each server.

## CentOS Base Build (CentOS 9)

 **Must be built on a Seattle physical server!!!**

* RDP to any physical server in the Seattle Path Lab
* Open an Admin PS console, run ````Build-LabBaseVHDX -OS CentOS````
* Once complete, in Hyper-V Manager, open a connection to VM
* Start the VM
* OS Install Prompt: Install CentOS 9
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
  * Begin Installation
  * Finish configuration
  * **system installs, takes about 5 minutes**
  * On completion, click the reboot button
* Open Admin PowerShell:

  ````PowerShell
    Enable-VMIntegrationService -VMName BaseCentOS -Name "Guest Service Interface"
    Copy-VMFile -Name BaseCentOS -SourcePath "C:\Hyper-V\ISO\BaseVHDX\tenant-shell.sh" -DestinationPath '/var/tmp/LabMod/' -FileSource Host -CreateFullPath -Force
    Copy-VMFile -Name BaseCentOS -SourcePath "C:\Hyper-V\ISO\BaseVHDX\base-update.sh" -DestinationPath '/var/tmp/LabMod/' -FileSource Host -CreateFullPath -Force
  ````

* Open Putty to *local user*@10.1.7.45
* At login prompt, use your local credentials

    ````bash
    sudo -u root sh /var/tmp/LabMod/base-update.sh
    sudo shutdown now
    ````

* Back in PowerShell

    ````PowerShell
    Copy-Item "C:\Hyper-V\Virtual Hard Disks\BaseCentOS.vhdx" "C:\Hyper-V\ISO\BaseVHDX\BaseCentOS.vhdx" -Force
    ````

CentOS base image is now complete and copied to the ISO dir, proceed with tenant VM creation

### CentOS Tenant VM Build

in admin ps:

````PowerShell
New-LabVM 90 -OS CentOS
````

## Ubuntu Server (non-GUI) Base Build Ubuntu 22.04

 **Must be built on a Seattle physical server!!!**

* RDP to any physical server in the Seattle Path Lab
* Open an Admin PS console, run ````Build-LabBaseVHDX -OS Ubuntu````
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
    * Tab to Address: 10.1.7.46
    * Tab to Gateway: 10.1.7.1
    * Tab to Name servers: 1.1.1.1,1.0.0.1
    * Tab to save, Enter to continue
    * arrow down to Done, Enter to continue
  * Configure proxy: leave blank, Enter to continue
  * Configure Ubuntu mirror: accept default, Enter to continue
  * Installer Update: if this screen presents, select "Update to the new installer"
  * Filesystem setup: "Use An Entire Disk" (default), Enter to continue
  * Filesystem setup: Accept default local disk, Enter to continue
  * Filesystem setup: Accept default FILE SYSTEM SUMMARY, Enter to bring up pop-up warning
  * Confirm Destructive action: arrow down to Continue, Enter to continue
  * Profile Setup:
    * Your name: Enter your first and last name separated by a space
    * Your server's name: baseubuntu (all lower case)
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
* Open Putty session to 10.1.7.46
  * At login prompt, use your local credentials
  * Run:
  
   ````bash
   sudo apt-get update && sudo apt-get upgrade -y && sudo apt-get full-upgrade -y && sudo apt-get autoremove -y
   sudo apt-get install linux-virtual linux-cloud-tools-virtual linux-tools-virtual -y
   ````

  * Wait for install to complete
* Back on the physical server, open Admin PowerShell:

    ````PowerShell
  Copy-VMFile -Name BaseUbuntu -SourcePath "C:\Hyper-V\ISO\BaseVHDX\tenant-shell.sh" -DestinationPath '/var/tmp/LabMod/' -FileSource Host -CreateFullPath -Force
  Copy-VMFile -Name BaseUbuntu -SourcePath "C:\Hyper-V\ISO\BaseVHDX\base-update.sh" -DestinationPath '/var/tmp/LabMod/' -FileSource Host -CreateFullPath -Force
  ````

* Back in Putty:

    ````bash
    sudo -u root sh /var/tmp/LabMod/base-update.sh
    sudo shutdown now
    ````

* Back in PowerShell

    ````PowerShell
    Copy-Item "C:\Hyper-V\Virtual Hard Disks\BaseUbuntu.vhdx" "C:\Hyper-V\ISO\BaseVHDX\BaseUbuntu.vhdx" -Force
    ````

Ubuntu Base image is now complete and copied to the ISO dir, proceed with tenant VM creation

### Ubuntu Tenant VM Build

in admin ps:

````PowerShell
New-LabVM 90 -OS Ubuntu
````
