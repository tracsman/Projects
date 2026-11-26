# Offline checks for persisted selected-action metadata. No Azure operations are invoked.
$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '..\Services\AutomationRunAction.cs') -Raw
Add-Type -TypeDefinition ("#nullable enable`nusing System;`n" + $source)
$type = [PathWeb.Services.AutomationRunAction]
foreach ($test in @(
	@{ Config = 'CreateAzurePowerShell'; Action = 'deploy'; Expected = $true },
	@{ Config = 'CreateAzurePowerShell'; Action = 'remove'; Expected = $true },
	@{ Config = 'CreateERPowerShell'; Action = 'deploy'; Expected = $true },
	@{ Config = 'ServiceProviderInstructions'; Action = 'provision'; Expected = $true },
	@{ Config = 'ServiceProviderInstructions'; Action = 'deprovision'; Expected = $true },
	@{ Config = 'ServiceProviderInstructions'; Action = 'remove'; Expected = $false },
	@{ Config = 'CreateAzurePowerShell'; Action = 'provision'; Expected = $false },
	@{ Config = 'CreateAzurePowerShell'; Action = $null; Expected = $true }
)) {
	if ($type::IsValid($test.Config, $test.Action) -ne $test.Expected) { throw "Invalid validation result: $($test.Config) / $($test.Action)" }
}
foreach ($action in 'deploy', 'remove', 'provision', 'deprovision') {
	$saved = $type::AddMarker('Write-Output test', $action)
	if ($type::GetAction($saved) -ne $action) { throw "Action was not preserved: $action" }
}
if ($type::GetAction('New-LabECX 12 SEA') -ne 'unknown') { throw 'Legacy script was guessed.' }
if ($type::GetAction($type::AddMarker('script', $null)) -ne 'unknown') { throw 'Missing action was guessed.' }
$old = $type::AddMarker('script', 'deploy')
if ($type::GetAction($type::AddMarker($old, 'remove')) -ne 'remove') { throw 'Latest marker was not authoritative.' }
Write-Output 'Automation action checks passed: valid combinations, persistence, reruns, and unknown legacy runs.'
