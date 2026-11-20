# Offline regression checks: every Azure, Equinix, prompt, and file-log operation is mocked.
$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $PSScriptRoot '..\LabMod'
$script:assertions = 0

function Assert-ProviderTest {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
    $script:assertions++
}

function Invoke-ProviderTest {
    param(
        [string]$Action = 'New-LabECX',
        [string]$Scenario = 'Success',
        [string]$Location = 'Seattle'
    )

    $module = New-Module -ArgumentList $modulePath, $Scenario -ScriptBlock {
        param($ModulePath, $Scenario)
        $script:scenario = $Scenario
        $script:IsInteractive = $false
        $script:SubscriptionMap = @{ 'ExpressRoute-Lab' = 'test-subscription' }
        $script:requests = [Collections.Generic.List[object]]::new()
        $script:logs = [Collections.Generic.List[object]]::new()
        $script:deleteCount = 0

        function Write-LabLogEntry {
            param($Message, $Level)
            $script:logs.Add([pscustomobject]@{ Message = $Message; Level = $Level })
        }
        function Get-Module {
            param($Name, [switch]$ListAvailable)
            if ($script:scenario -ne 'MissingModule') { [pscustomobject]@{ Name = 'Az.Network' } }
        }
        function Get-AzContext { [pscustomobject]@{ Subscription = @{ Id = 'test-subscription'; Name = 'Test' } } }
        function Get-AzResourceGroup {
            [CmdletBinding()]
            param($Name)
            [pscustomobject]@{ Name = $Name }
        }
        function Get-AzKeyVaultSecret {
            param($VaultName, $Name, [switch]$AsPlainText)
            'FAKE-SECRET-DO-NOT-PRINT'
        }
        function Get-AzExpressRouteCircuit {
            [CmdletBinding()]
            param($ResourceGroupName)
            if ($script:scenario -eq 'CircuitLookupFailure') { Write-Error 'Mock circuit lookup failure'; return }
            if ($script:scenario -eq 'NoCircuits') { return }
            $isRemove = $script:action -eq 'Remove-LabECX'
            [pscustomobject]@{
                Name = "$ResourceGroupName-ER"
                Id = '/test/circuit'
                CircuitProvisioningState = if ($script:scenario -eq 'NotReady') { 'Disabled' } else { 'Enabled' }
                ServiceProviderProvisioningState = if ($script:scenario -eq 'AlreadyDone') {
                    if ($isRemove) { 'NotProvisioned' } else { 'Provisioned' }
                } elseif ($isRemove) { 'Provisioned' } else { 'NotProvisioned' }
                ServiceKey = 'FAKE-SERVICE-KEY-DO-NOT-PRINT'
                ServiceProviderProperties = @{ BandwidthInMbps = 50 }
                Tag = if ($script:scenario -eq 'MissingUUIDs') { @{} } else {
                    @{ UUID1 = '00000000-0000-0000-0000-000000000001'; UUID2 = '00000000-0000-0000-0000-000000000002' }
                }
            }
        }
        function Invoke-RestMethod {
            [CmdletBinding()]
            param($Method, $Uri, $Body, $Headers, $ContentType)
            $script:requests.Add([pscustomobject]@{ Method = $Method; Uri = $Uri; Body = $Body })
            if ($Uri -like '*/oauth2/*') {
                if ($script:scenario -eq 'OAuthFailure') { throw 'Mock authentication failure' }
                return [pscustomobject]@{ access_token = 'FAKE-TOKEN-DO-NOT-PRINT' }
            }
            if ($Method -eq 'Delete') {
                $script:deleteCount++
                if ($script:deleteCount -eq 1) {
                    if ($script:scenario -eq 'DeleteFailure') { throw 'Mock delete failure' }
                    if ($script:scenario -eq 'AlreadyDeleted') {
                        $errorRecord = [Management.Automation.ErrorRecord]::new(
                            [Exception]::new('Mock missing connection'), 'Missing', 'ObjectNotFound', $null)
                        $errorRecord.ErrorDetails = [Management.Automation.ErrorDetails]::new('{"errorCode":"EQ-300008"}')
                        throw $errorRecord
                    }
                }
                return [pscustomobject]@{ name = 'Test connection' }
            }
            $request = $Body | ConvertFrom-Json
            $secondary = $request.name.EndsWith('-sec')
            if (-not $secondary -and $script:scenario -eq 'PrimaryFailure') { throw 'Mock primary failure' }
            if ($secondary -and $script:scenario -eq 'SecondaryFailure') { throw 'Mock secondary failure' }
            [pscustomobject]@{
                uuid = if ($secondary) { '00000000-0000-0000-0000-000000000002' } else { '00000000-0000-0000-0000-000000000001' }
                operation = @{ equinixStatus = if ($secondary -and $script:scenario -eq 'SecondaryRejected') { 'FAILED' } else { 'PROVISIONING' } }
                redundancy = @{ group = 'test-group' }
            }
        }
        function Update-AzTag {
            [CmdletBinding()]
            param($ResourceId, $Tag, $Operation)
            if ($script:scenario -eq 'TagFailure') { throw 'Mock tag failure' }
            $script:requests.Add([pscustomobject]@{ Method = 'Tag'; Uri = $ResourceId; Body = $Tag })
        }
        function Read-Host {
            param($Prompt)
            if ($script:scenario -ne 'MissingUUIDs') { throw 'Unexpected prompt' }
            ''
        }
        . (Join-Path $ModulePath 'Private\Write-LabProviderStatus.ps1')
        . (Join-Path $ModulePath 'Private\Write-LabProviderSummary.ps1')
        . (Join-Path $ModulePath 'Public\New-LabECX.ps1')
        . (Join-Path $ModulePath 'Public\Remove-LabECX.ps1')
    }

    & $module {
        param($Action, $Location)
        $script:action = $Action
        $records = [Collections.Generic.List[object]]::new()
        $exception = $null
        # A local physical-server name must not prevent this isolated mock test.
        $oldComputerName = $env:COMPUTERNAME
        try {
            $env:COMPUTERNAME = 'ProviderOutputTest'
            & $Action -TenantID 10 -PeeringLocation $Location 3>&1 4>&1 6>&1 |
                ForEach-Object { $records.Add($_) }
        }
        catch { $exception = $_ }
        finally { $env:COMPUTERNAME = $oldComputerName }
        [pscustomobject]@{
            Text = ($records | Where-Object { $_ -is [string] }) -join "`n"
            Records = $records.ToArray()
            Requests = $script:requests.ToArray()
            Logs = $script:logs.ToArray()
            Exception = $exception
        }
    } $Action $Location
}

foreach ($action in 'New-LabECX', 'Remove-LabECX') {
    foreach ($location in 'Seattle', 'Ashburn') {
        $run = Invoke-ProviderTest -Action $action -Location $location
        Assert-ProviderTest ($null -eq $run.Exception) "$action $location unexpectedly threw: $($run.Exception)"
        Assert-ProviderTest ($run.Text -match 'LabMod:.*Provider') "$action banner missing"
        Assert-ProviderTest ($run.Text -match '\d{2}:\d{2}:\d{2} ==> Checking login') "$action timestamped step missing"
        Assert-ProviderTest ($run.Text -match '(?m)^    Current Sub:|^    Retrieving ECX credentials') "$action detail indentation missing"
        Assert-ProviderTest ($run.Text -match '\[ OK \].*submitted for (de)?provisioning') "$action acceptance status missing"
        Assert-ProviderTest ($run.Text -match 'Result\s+:\s+Submitted') "$action result must say Submitted, not provisioned"
        Assert-ProviderTest ($run.Text -match 'ConnectionsSubmitted\s+:\s+2') "$action submitted count is incorrect"
        Assert-ProviderTest ($run.Text -match 'CircuitsExamined\s+:\s+1') "$action circuit count is incorrect"
        Assert-ProviderTest ($run.Text -match 'Failures\s+:\s+0') "$action unexpected failure count"
        Assert-ProviderTest ($run.Text -notmatch 'FAKE-SECRET|FAKE-TOKEN|FAKE-SERVICE-KEY') "$action leaked a secret"
        Assert-ProviderTest (@($run.Records | Where-Object { $_ -is [Management.Automation.VerboseRecord] }).Count -eq 0) "$action duplicated progress to Verbose"
        Assert-ProviderTest (@($run.Records | Where-Object { $_ -is [Management.Automation.InformationRecord] }).Count -eq 0) "$action duplicated progress to Host"
        Assert-ProviderTest ($run.Logs.Count -gt 0) "$action lost structured logging"
        $remoteCalls = @($run.Requests | Where-Object { $_.Uri -like '*/fabric/*' })
        Assert-ProviderTest ($remoteCalls.Count -eq 2) "$action must submit exactly two connection operations"
        if ($action -eq 'New-LabECX') {
            $primary = $remoteCalls[0].Body | ConvertFrom-Json
            $secondary = $remoteCalls[1].Body | ConvertFrom-Json
            Assert-ProviderTest ($primary.aSide.accessPoint.linkProtocol.vlanSTag -eq '10') 'VLAN changed'
            Assert-ProviderTest ($primary.zSide.accessPoint.authenticationKey -eq 'FAKE-SERVICE-KEY-DO-NOT-PRINT') 'Service key changed'
            Assert-ProviderTest ($secondary.redundancy.group -eq 'test-group' -and $secondary.redundancy.priority -eq 'SECONDARY') 'Redundancy changed'
            Assert-ProviderTest (@($run.Requests | Where-Object Method -eq 'Tag').Count -eq 1) 'UUID tags not saved'
        }
        else {
            Assert-ProviderTest (@($remoteCalls | Where-Object Method -ne 'Delete').Count -eq 0) 'Removal changed remote operation'
        }
    }
    foreach ($scenario in 'AlreadyDone', 'NoCircuits') {
        $run = Invoke-ProviderTest -Action $action -Scenario $scenario
        Assert-ProviderTest ($null -eq $run.Exception) "$action $scenario unexpectedly threw"
        Assert-ProviderTest ($run.Text -match '\[SKIP\]') "$action $scenario skip message missing"
        Assert-ProviderTest ($run.Text -match 'Result\s+:\s+NoChanges') "$action $scenario result is incorrect"
        Assert-ProviderTest (@($run.Requests | Where-Object Uri -like '*/fabric/*').Count -eq 0) "$action $scenario must not modify connections"
    }
    foreach ($scenario in 'MissingModule', 'OAuthFailure') {
        $run = Invoke-ProviderTest -Action $action -Scenario $scenario
        Assert-ProviderTest ($run.Text -match '\[FAIL\]') "$action $scenario failure message missing"
        Assert-ProviderTest ($run.Text -match 'Result\s+:\s+Failed') "$action $scenario must not report success"
        Assert-ProviderTest ($run.Text -match 'Failures\s+:\s+1') "$action $scenario failure count incorrect"
        Assert-ProviderTest (@($run.Requests | Where-Object Uri -like '*/fabric/*').Count -eq 0) "$action $scenario must not modify connections"
    }
    $run = Invoke-ProviderTest -Action $action -Scenario CircuitLookupFailure
    Assert-ProviderTest ($null -ne $run.Exception) "$action circuit lookup errors must remain visible"
    Assert-ProviderTest ($run.Text -match 'Result\s+:\s+Failed') "$action failed circuit lookup must not report NoChanges"
}

foreach ($scenario in 'PrimaryFailure', 'SecondaryFailure', 'SecondaryRejected', 'TagFailure') {
    $run = Invoke-ProviderTest -Scenario $scenario
    Assert-ProviderTest ($run.Text -match '\[FAIL\]') "$scenario failure message missing"
    $expected = if ($scenario -eq 'PrimaryFailure') { 'Failed' } else { 'PartialFailure' }
    Assert-ProviderTest ($run.Text -match "Result\s+:\s+$expected") "$scenario summary is misleading"
    $submitted = if ($scenario -eq 'PrimaryFailure') { 0 } elseif ($scenario -eq 'TagFailure') { 2 } else { 1 }
    Assert-ProviderTest ($run.Text -match "ConnectionsSubmitted\s+:\s+$submitted") "$scenario accepted connection count incorrect"
    Assert-ProviderTest (($null -ne $run.Exception) -eq ($scenario -eq 'SecondaryFailure')) "$scenario changed terminating-error behavior"
}

$run = Invoke-ProviderTest -Action Remove-LabECX -Scenario DeleteFailure
Assert-ProviderTest ($run.Text -match 'Result\s+:\s+PartialFailure') 'Partial deletion must not report complete success'
Assert-ProviderTest ($run.Text -match 'ConnectionsSubmitted\s+:\s+1') 'Partial deletion count must not invent half a circuit'
$run = Invoke-ProviderTest -Action Remove-LabECX -Scenario AlreadyDeleted
Assert-ProviderTest ($run.Text -match '\[SKIP\].*not found') 'Missing connection should be explicitly skipped'
Assert-ProviderTest ($run.Text -match 'Failures\s+:\s+0') 'Already-deleted connection should not count as a new failure'
$run = Invoke-ProviderTest -Action Remove-LabECX -Scenario MissingUUIDs
Assert-ProviderTest ($run.Text -match 'Result\s+:\s+Failed') 'Missing UUIDs must not report success'
Assert-ProviderTest (@($run.Requests | Where-Object Method -eq Delete).Count -eq 0) 'Missing UUIDs must not delete connections'

$helperModule = New-Module -ArgumentList $modulePath -ScriptBlock {
    param($ModulePath)
    $script:IsInteractive = $true
    $script:colors = [Collections.Generic.List[object]]::new()
    function Write-LabLogEntry { param($Message, $Level) }
    function Write-Host {
        param($Object, $ForegroundColor)
        $script:colors.Add([pscustomobject]@{ Text = $Object; Color = $ForegroundColor })
    }
    . (Join-Path $ModulePath 'Private\Write-LabProviderStatus.ps1')
    . (Join-Path $ModulePath 'Private\Write-LabProviderSummary.ps1')
}
$helperResults = & $helperModule {
    $output = @(foreach ($kind in 'Banner','Step','Detail','OK','Skip','Fail') {
        Write-LabProviderStatus -Kind $kind -Message 'Test message'
    })
    $PSPrivateMetadata = @{ JobId = 'fake-job' }
    $runbookOutput = @(Write-LabProviderStatus -Kind OK -Message 'Runbook message')
    $PSPrivateMetadata = $null
    $script:IsInteractive = $false
    $longRun = @{
        Script = 'New-LabECX'; Tenant = 'SEA-Cust10'; Started = (Get-Date).AddMinutes(-65)
        Failures = 0; ConnectionsSubmitted = 0; CircuitsExamined = 0; CircuitsSkipped = 0
    }
    [pscustomobject]@{
        InteractiveOutput = $output
        Colors = $script:colors.ToArray()
        RunbookOutput = $runbookOutput
        Summary = (Write-LabProviderSummary -Run $longRun) -join "`n"
    }
}
Assert-ProviderTest ($helperResults.InteractiveOutput.Count -eq 0) 'Interactive progress must not pollute the success pipeline'
Assert-ProviderTest ($helperResults.Colors.Count -eq 6) 'Runbooks must not also emit host copies'
Assert-ProviderTest ($helperResults.Colors[1].Color -eq 'Cyan' -and $helperResults.Colors[3].Color -eq 'Green' -and
    $helperResults.Colors[4].Color -eq 'DarkGray' -and $helperResults.Colors[5].Color -eq 'Red') 'Console status colors are incorrect'
Assert-ProviderTest ($helperResults.RunbookOutput.Count -eq 1 -and $helperResults.RunbookOutput[0] -eq '[ OK ] Runbook message') 'Runbook detection must override interactive host detection'
Assert-ProviderTest ($helperResults.Summary -match 'Duration\s+:\s+65:') 'Duration must not wrap after an hour'

Write-Output "Passed $script:assertions provider-output assertions (no live operations)."
