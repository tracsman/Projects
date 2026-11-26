# Offline test: the actual LabMod removal command runs with mocked Hyper-V and filesystem operations.
$ErrorActionPreference = 'Stop'
$controller = Get-Content (Join-Path $PSScriptRoot '..\Controllers\LabVmController.cs') -Raw
$method = [regex]::Match($controller, '(?s)private async Task ExecuteRemoveOnServerAsync\(.*?\n    private async Task ExecuteServerBatchAsync\(').Value
if (-not $method) { throw 'Removal method not found.' }
$builder = [regex]::Match($method, '(?s)var sb = new StringBuilder\(\);(.*?)\n\s*try\s*\{\s*var \(success, output\)').Groups[1].Value
if (-not $builder) { throw 'Removal script builder not found.' }
$lines = foreach ($match in [regex]::Matches($builder, 'sb\.AppendLine\(\$?"((?:\\.|[^"\\])*)"\);')) {
	$match.Groups[1].Value.Replace('\\', '\').Replace('{{', '{').Replace('}}', '}').Replace('{tenantId}', '12').Replace('{JsonMarker}', '---LABVM-JSON---')
}
$scriptText = $lines -join "`n"
if (-not $scriptText.Contains('LabMod\Remove-LabVM') -or -not $scriptText.Contains('---LABVM-JSON---')) {
	throw 'Could not extract the full removal script.'
}

$modulePath = Join-Path $PSScriptRoot '..\..\LabModPS\LabMod\Public\Remove-LabVM.ps1'
$module = New-Module -Name LabMod -ArgumentList $modulePath -ScriptBlock {
	param($Path)
	$script:actions = [Collections.Generic.List[string]]::new()
	function Assert-LabAdminContext {
		param([switch]$WarnOnly)
		if ($script:denyAdmin) { if ($WarnOnly) { return $false }; throw 'Not elevated' }
		$true
	}
	function Get-VM { param($Name) $script:actions.Add("Get:$Name"); [pscustomobject]@{ Name = 'SEA-ER-12-VM01' } }
	function Stop-VM { param($Name) $script:actions.Add("Stop:$Name"); if ($script:stopFails) { throw 'Mock stop failure' } }
	function Remove-VM { param($Name, [switch]$Force) $script:actions.Add("Remove:$Name") }
	function Remove-Item { param($Path) $script:actions.Add("Disk:$Path") }
	function Write-Log { param($Message, [switch]$TimeStamp) }
	. $Path
	Export-ModuleMember -Function Remove-LabVM
}
Import-Module $module
$oldComputerName = $env:COMPUTERNAME
try {
	$env:COMPUTERNAME = 'SEA-ER-01'
	foreach ($case in @('Success', 'Not elevated', 'Mock stop failure')) {
		& $module { param($case) $script:actions.Clear(); $script:denyAdmin = $case -eq 'Not elevated'; $script:stopFails = $case -eq 'Mock stop failure' } $case
		$output = @(& ([scriptblock]::Create($scriptText)))
		$marker = [array]::LastIndexOf($output, '---LABVM-JSON---')
		if ($marker -lt 0 -or $marker -ne $output.Count - 2) { throw "Missing structured result for $case" }
		$result = $output[-1] | ConvertFrom-Json
		$actions = @(& $module { $script:actions.ToArray() })
		if ($case -eq 'Success') {
			if ($result.Status -ne 'Completed' -or -not $result.Success -or $actions.Count -ne 4 -or $actions[0] -ne 'Get:SEA-ER-12-VM*' -or $actions[1] -ne 'Stop:SEA-ER-12-VM*' -or $actions[2] -ne 'Remove:SEA-ER-12-VM*' -or $actions[3] -ne 'Disk:C:\Hyper-V\Virtual Hard Disks\SEA-ER-12-VM*.vhdx') { throw "Successful removal had wrong result or actions: $($actions -join ', ')" }
		} else {
			if ($result.Status -ne 'Failed' -or $result.Success -or $result.Message -ne $case) { throw "Failure not surfaced for $case" }
			if ($case -eq 'Not elevated' -and $actions.Count -ne 0) { throw 'Non-admin performed removal actions.' }
			if ($case -eq 'Mock stop failure' -and $actions.Count -ne 2) { throw 'Removal continued after stop failure.' }
		}
	}
	Write-Output 'Lab VM removal script checks passed: success, non-admin, and stop failure.'
} finally {
	$env:COMPUTERNAME = $oldComputerName
	Remove-Module LabMod -ErrorAction SilentlyContinue
}
