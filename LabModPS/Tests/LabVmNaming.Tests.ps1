# Offline checks: Hyper-V, disk copying, guest configuration, and logging are mocked.
$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $PSScriptRoot '..\LabMod'

$module = New-Module -ArgumentList $modulePath -ScriptBlock {
	param($ModulePath)
	$script:assertions = 0
	function Assert-LabVmTest {
		param([bool]$Condition, [string]$Message)
		if (-not $Condition) { throw $Message }
		$script:assertions++
	}
	$script:LabLogDirectory = 'C:\Hyper-V\Logs'
	$script:LabCommonLogPath = 'C:\Hyper-V\Logs\LabMod.log.jsonl'
	$script:vms = @{}
	$script:disks = @{}
	$script:copies = [Collections.Generic.List[string]]::new()
	$script:statuses = [Collections.Generic.List[object]]::new()
	$script:forwarded = [Collections.Generic.List[object]]::new()

	function Assert-LabAdminContext { param([switch]$WarnOnly) $true }
	function Write-Log { param($Message, [switch]$TimeStamp) }
	function Write-LabLogEntry { param($RunId, $TenantId, $Action, $Level, $Message, $Data) }
	function Write-LabRunStatus {
		param($RunId, $TenantId, $Action, $Status, $Message, $StartedAt, $CompletedAt, $ExitCode, $StatusPath)
		$script:statuses.Add([pscustomobject]@{ RunId = $RunId; Status = $Status; Message = $Message })
	}
	function Get-VM {
		[CmdletBinding()]
		param($Name = '*')
		if ($script:inventoryFailure) { throw 'Mock Hyper-V inventory failure' }
		$script:vms[$env:COMPUTERNAME] | Where-Object Name -like $Name
	}
	function Test-Path {
		[CmdletBinding()]
		param($Path, $LiteralPath)
		if ($LiteralPath) { return $script:disks.ContainsKey("$env:COMPUTERNAME|$LiteralPath") }
		$true
	}
	function New-LabVMDrive {
		param($From, $To)
		$script:copies.Add($To)
		$script:disks["$env:COMPUTERNAME|$To"] = $true
	}
	function New-VM {
		param($Name, $VHDPath, $Path, $MemoryStartupBytes, $Generation)
		if ($Name -eq $script:failVmName) { throw 'Mock VM creation failure' }
		$vm = [pscustomobject]@{ Name = $Name; Disk = $VHDPath; HostName = $env:COMPUTERNAME }
		$script:vms[$env:COMPUTERNAME] = @($script:vms[$env:COMPUTERNAME]) + $vm
		$vm
	}
	function Set-VM { param($VM, $SnapshotFileLocation, $SmartPagingFilePath) }
	function Add-VMScsiController { param($VMName) }
	function Set-VMProcessor { param($VM, $Count) }
	function Set-VMMemory { param($VM, $DynamicMemoryEnabled, $MaximumBytes, $MinimumBytes, $StartupBytes) }
	function Remove-VMNetworkAdapter { param($VM) }
	function Add-VMNetworkAdapter { param($VM, $Name, $SwitchName) }
	function Get-VMNetworkAdapter { param($VM) [pscustomobject]@{ Name = 'PublicNIC' } }
	function Set-VMNetworkAdapterVlan { [CmdletBinding()] param($VMNetworkAdapter, [switch]$Access, $VlanId) }
	function Enable-VMIntegrationService { param($VMName, $Name) }

	. (Join-Path $ModulePath 'Public\New-LabVM.ps1')
	$script:createVm = ${function:New-LabVM}
	function New-LabVM {
		param($TenantID, $OS, $AdminCred, $UserCred, $VMNumber)
		$parameters = @{ TenantID = $TenantID; OS = $OS; AdminCred = $AdminCred; UserCred = $UserCred; VMCreateOnly = $true }
		if ($PSBoundParameters.ContainsKey('VMNumber')) { $parameters.VMNumber = $VMNumber }
		$script:forwarded.Add([pscustomobject]@{ HostName = $env:COMPUTERNAME; Number = $VMNumber; OS = $OS })
		& $script:createVm @parameters
	}
	. (Join-Path $ModulePath 'Public\Start-LabVmRequest.ps1')
}

$assertions = & $module {
	$oldComputerName = $env:COMPUTERNAME
	$credential = [pscredential]::new('TestUser', (ConvertTo-SecureString 'FAKE-TEST-CREDENTIAL' -AsPlainText -Force))
	try {
		# Run server 2 first to prove numbering is independent of scheduling and host-local inventory.
		$results = @{}
		foreach ($row in @(
			@{ Number = 3; HostName = 'SEA-ER-02'; OS = 'Ubuntu' },
			@{ Number = 1; HostName = 'SEA-ER-01'; OS = 'Server2025' },
			@{ Number = 2; HostName = 'SEA-ER-01'; OS = 'Ubuntu' }
		)) {
			$env:COMPUTERNAME = $row.HostName
			$results[$row.Number] = Start-LabVmRequest -TenantID 12 -VMNumber $row.Number -OS $row.OS -RunId "test-$($row.Number)" -AdminCred $credential -UserCred $credential -Confirm:$false
			$expected = 'SEA-ER-12-VM{0:00}' -f $row.Number
			Assert-LabVmTest ($results[$row.Number].Status -eq 'Completed') "Row $($row.Number) did not complete"
			Assert-LabVmTest ($results[$row.Number].CreatedVmNames.Count -eq 1 -and $results[$row.Number].CreatedVmNames[0] -eq $expected) "Row $($row.Number) returned the wrong VM name"
			$vm = @(Get-VM -Name $expected)
			Assert-LabVmTest ($vm.Count -eq 1 -and $vm[0].HostName -eq $row.HostName) "Row $($row.Number) deployed to the wrong host"
			Assert-LabVmTest ($vm[0].Disk -eq "C:\Hyper-V\Virtual Hard Disks\$expected.vhdx") "Row $($row.Number) has the wrong disk name"
		}
		Assert-LabVmTest (($script:forwarded.Number -join ',') -eq '3,1,2') 'Request wrapper changed VM numbers'
		Assert-LabVmTest (($script:forwarded.OS -join ',') -eq 'Ubuntu,Server2025,Ubuntu') 'Request wrapper changed operating systems'

		$env:COMPUTERNAME = 'SEA-ER-01'
		$before = @(Get-VM).Count
		$failure = $null
		try { Start-LabVmRequest -TenantID 12 -VMNumber 1 -RunId 'duplicate' -AdminCred $credential -UserCred $credential -Confirm:$false | Out-Null }
		catch { $failure = $_ }
		Assert-LabVmTest ($failure.Exception.Message -like "*SEA-ER-12-VM01*already exists*") 'Duplicate VM must fail explicitly'
		Assert-LabVmTest (@(Get-VM).Count -eq $before) 'Duplicate request created an extra VM'
		Assert-LabVmTest ($script:statuses[-1].RunId -eq 'duplicate' -and $script:statuses[-1].Status -eq 'Failed') 'Duplicate request did not record failure'

		$script:failVmName = 'SEA-ER-13-VM01'
		$failure = $null
		try { Start-LabVmRequest -TenantID 13 -VMNumber 1 -RunId 'failed-row' -AdminCred $credential -UserCred $credential -Confirm:$false | Out-Null }
		catch { $failure = $_ }
		finally { $script:failVmName = $null }
		Assert-LabVmTest ($failure.Exception.Message -eq 'Mock VM creation failure') 'Creation failure was swallowed'
		$nextRow = Start-LabVmRequest -TenantID 13 -VMNumber 2 -RunId 'next-row' -AdminCred $credential -UserCred $credential -Confirm:$false
		Assert-LabVmTest ($nextRow.CreatedVmNames.Count -eq 1 -and $nextRow.CreatedVmNames[0] -eq 'SEA-ER-13-VM02') 'A prior failure shifted the next tenant row number'

		$env:COMPUTERNAME = 'SEA-ER-02'
		$disk = 'C:\Hyper-V\Virtual Hard Disks\SEA-ER-12-VM04.vhdx'
		$script:disks["$env:COMPUTERNAME|$disk"] = $true
		$failure = $null
		try { & $script:createVm -TenantID 12 -VMNumber 4 -CopyOnly -AdminCred $credential -UserCred $credential }
		catch { $failure = $_ }
		Assert-LabVmTest ($failure.Exception.Message -like '*Refusing to overwrite*') 'Existing disk must fail explicitly'
		Assert-LabVmTest ($script:copies.Count -eq 0) 'Existing disk was overwritten'

		& $script:createVm -TenantID 12 -VMNumber 5 -CopyOnly -AdminCred $credential -UserCred $credential
		Assert-LabVmTest ($script:copies.Count -eq 1 -and $script:copies[0] -eq 'C:\Hyper-V\Virtual Hard Disks\SEA-ER-12-VM05.vhdx') 'Explicit copy uses the wrong tenant disk'

		$script:inventoryFailure = $true
		$failure = $null
		try { & $script:createVm -TenantID 12 -VMNumber 6 -VMCreateOnly -AdminCred $credential -UserCred $credential }
		catch { $failure = $_ }
		finally { $script:inventoryFailure = $false }
		Assert-LabVmTest ($failure.Exception.Message -eq 'Mock Hyper-V inventory failure') 'Inventory failures must not be mistaken for a free name'

		# Omitted numbering retains the existing interactive next-free behavior.
		$legacy = Start-LabVmRequest -TenantID 12 -RunId 'legacy' -AdminCred $credential -UserCred $credential -Confirm:$false
		Assert-LabVmTest ($legacy.CreatedVmNames.Count -eq 1 -and $legacy.CreatedVmNames[0] -eq 'SEA-ER-12-VM01') 'Legacy automatic numbering changed'

		$before = @(Get-VM).Count
		$preview = Start-LabVmRequest -TenantID 12 -VMNumber 9 -WhatIf
		Assert-LabVmTest ($preview.Status -eq 'Preview' -and @(Get-VM).Count -eq $before) 'WhatIf created a VM'
		foreach ($number in 0, 100) {
			$failure = $null
			try { Start-LabVmRequest -TenantID 12 -VMNumber $number -Confirm:$false | Out-Null }
			catch { $failure = $_ }
			Assert-LabVmTest ($null -ne $failure) "Invalid VM number $number was accepted"
		}
	}
	finally { $env:COMPUTERNAME = $oldComputerName }
	$script:assertions
}

$controller = Get-Content (Join-Path $PSScriptRoot '..\..\PathWeb\Controllers\LabVmController.cs') -Raw
if (-not $controller.Contains('-VMNumber {req.Index}')) {
	throw 'PathWeb must forward the original tenant row index, not a per-host index'
}
Write-Output "Lab VM naming checks passed ($($assertions + 1) assertions)."
