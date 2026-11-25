# Offline checks: verifies credentials bind correctly inside PowerShell Direct script blocks.
# Hyper-V, guest commands, and logging are mocked; no VM is started or changed.
$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $PSScriptRoot '..\LabMod'

$module = New-Module -ArgumentList $modulePath -ScriptBlock {
	param($ModulePath)
	$script:LabLogDirectory = 'C:\Hyper-V\Logs'
	$script:LabCommonLogPath = 'C:\Hyper-V\Logs\LabMod.log.jsonl'
	$script:bindings = [Collections.Generic.List[object]]::new()

	function Assert-LabAdminContext { param([switch]$WarnOnly) $true }
	function Write-Log { param($Message, [switch]$TimeStamp) }
	function Write-Host { param([Parameter(ValueFromRemainingArguments)]$Object, [switch]$NoNewline, $ForegroundColor) }
	function Test-Path { [CmdletBinding()] param($Path, $LiteralPath) $true }
	function Get-VM { [CmdletBinding()] param($Name) }
	function Start-VM { param($Name) }
	function Wait-VM { param($Name, $For) }
	function Invoke-Command {
		[CmdletBinding()]
		param($VMName, $Credential, [Parameter(Position = 0)][scriptblock]$ScriptBlock, [object[]]$ArgumentList)
		$paramBlock = $ScriptBlock.Ast.ParamBlock
		if ($null -eq $paramBlock) { return 'Administrator' }
		# Bind ArgumentList positionally to the real param block, as remoting does, without running the guest body.
		$probe = [scriptblock]::Create($paramBlock.Extent.Text + '; $PSBoundParameters')
		$script:bindings.Add((& $probe @ArgumentList))
	}

	. (Join-Path $ModulePath 'Public\New-LabVM.ps1')
}

$result = & $module {
	$password = 'FAKE-TEST-PASSWORD'
	$credential = [pscredential]::new('PathLabUser', (ConvertTo-SecureString $password -AsPlainText -Force))
	$oldComputerName = $env:COMPUTERNAME
	$failures = [Collections.Generic.List[string]]::new()
	try {
		$env:COMPUTERNAME = 'SEA-ER-01'
		foreach ($mode in 'PostBuildOnly', 'PwdUpdateOnly') {
			$script:bindings.Clear()
			$parameters = @{ TenantID = 12; VMNumber = 1; OS = 'Server2025'; AdminCred = $credential; UserCred = $credential; $mode = $true }
			New-LabVM @parameters
			if ($script:bindings.Count -ne 1) { $failures.Add("$mode did not invoke the guest script exactly once"); continue }
			$users = $script:bindings[0]['Users']
			if (@($users).Count -ne 2) { $failures.Add("$mode bound Users with $(@($users).Count) element(s)") }
			if ($users[0] -ne 'PathLabUser') { $failures.Add("$mode bound the wrong user name") }
			if ($users[1] -ne $password) { $failures.Add("$mode bound a null or wrong password") }
			if ($mode -eq 'PostBuildOnly') {
				if ($script:bindings[0]['VMName'] -ne 'SEA-ER-12-VM01') { $failures.Add('PostBuildOnly bound the wrong VM name') }
				if ($script:bindings[0]['TenantID'] -ne 12) { $failures.Add('PostBuildOnly bound the wrong tenant') }
			}
		}
	}
	finally { $env:COMPUTERNAME = $oldComputerName }
	, $failures.ToArray()
}

if ($result.Count -gt 0) { throw ("Lab VM post-build checks failed:`n" + ($result -join "`n")) }
Write-Output 'Lab VM post-build credential binding checks passed.'
