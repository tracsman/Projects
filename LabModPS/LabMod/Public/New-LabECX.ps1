function New-LabECX{
    [CmdletBinding()]
    param (
        [Parameter(Mandatory=$true, ValueFromPipeline=$true, HelpMessage='Enter Tenant ID')]
        [ValidateRange(10,99)]
        [int]$TenantID,
        [Parameter(Mandatory=$true, HelpMessage='Enter ER Location')]
        [ValidateSet('Ashburn','Seattle')]
        [string]$PeeringLocation,
        [ValidateSet('ExpressRoute-Lab','Pathfinder')]
        [string]$Subscription='ExpressRoute-Lab')

    # Equinix Provisioning Script for PathFinder Lab
    #
    # 1. Initialize
    # 2. Validate
    # 3. Get ECX Token
    # 4. Load all ER circuits into an array
    # 5. Loop through array
    # 5.1 If enabled and not provisioned, provision the circuit
    # 6. End Nicely
    #

    # 1. Initialize
    # Script Variables
    $StartTime = Get-Date
    $ProvisionCount = 0
    $CircuitCount = 0
    Switch ($Subscription) {
        'ExpressRoute-Lab' {$SubID = '4bffbb15-d414-4874-a2e4-c548c6d45e2a'}
        'Pathfinder' {$SubID = '79573dd5-f6ea-4fdc-a3aa-d05586980843'}
    }

    # Azure Variables
    If ($PeeringLocation -eq "Seattle") {$RGName = "SEA-Cust$TenantID"}
    Else {$RGName = "ASH-Cust$TenantID"}
    $TenantStub = "$RGName-ER"
    $kvName = "LabSecrets"
    $kvECXClientID = "ECXClientID"
    $kvECXSecret = "ECXSecret"
    
    # Equinix Variables
    $ConnProfileUUID = "a1390b22-bbe0-4e93-ad37-85beef9d254d"

    # 2. Validate
    # Az Module Test
    $ModCheck = Get-Module Az.Network -ListAvailable
    If ($Null -eq $ModCheck) {
        Write-Warning "The Az PowerShell module was not found. This commandlet uses the Az modules for PowerShell"
        Write-Warning "See the blob post for more information at: https://azure.microsoft.com/blog/how-to-migrate-from-azurerm-to-az-in-azure-powershell/"
        Return
        }

    # Login and permissions check
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Checking login and permissions" -ForegroundColor Cyan
    Try {Get-AzResourceGroup -Name "Utilities" -ErrorAction Stop | Out-Null}
    Catch {# Login and set subscription for ARM
            Write-Host "  Logging in to ARM"
            Try {$Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop).Subscription}
            Catch {Connect-AzAccount | Out-Null
                    $Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop).Subscription}
            Write-Host "  Current Sub:",$Sub.Name,"(",$Sub.Id,")"
            Try {Get-AzResourceGroup -Name "LabInfrastructure" -ErrorAction Stop | Out-Null}
            Catch {Write-Warning "Permission check failed, ensure tenant id is set correctly!"
                    Return}
    }

    # 3. Get ECX Token
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Getting ECX OAuth Token" -ForegroundColor Cyan
    Write-Host "  Grabbing ECX secrets..." -NoNewline
    $ECXClientID = (Get-AzKeyVaultSecret -VaultName $kvName -Name $kvECXClientID).SecretValueText
    $ECXSecret = (Get-AzKeyVaultSecret -VaultName $kvName -Name $kvECXSecret).SecretValueText
    Write-Host "Success" -ForegroundColor Green

    # Get REST OAuth Token
    Write-Host "  Getting OAuth Token...." -NoNewline
    $TokenURI = "https://api.equinix.com/oauth2/v1/token"
    $TokenBody = "{" + 
                "  ""grant_type"": ""client_credentials""," +
                "  ""client_id"": ""$ECXClientID""," +
                "  ""client_secret"": ""$ECXSecret""" +
                "}"
    Try {$token = Invoke-RestMethod -Method Post -Uri $TokenURI -Body $TokenBody -ContentType application/json}
    Catch {Write-Host
        Write-Warning "An error occured getting an OAuth certificate from Equinix."
        Write-Host
        Write-Host $error[0].ErrorDetails.Message
        Return }
    $ConnHeader =  @{"Authorization" = "Bearer $($token.access_token)"}
    Write-Host "Sucess" -ForegroundColor Green

    # 4. Load all ER circuits into an array
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Pulling ER Circuit(s)" -ForegroundColor Cyan
    $Circuits = Get-AzExpressRouteCircuit -ResourceGroupName $RGName | Where-Object Name -Like "$TenantStub*"

    # 5. Loop through Circuit array
    ForEach ($Circuit in $Circuits | Sort-Object ResourceGroupName ) {
        $CircuitCount++
        If ($Circuit.CircuitProvisioningState -eq "Enabled" -and $Circuit.ServiceProviderProvisioningState -eq "NotProvisioned") {
            # 5.1 If enabled and not provisioned, provision the circuit
            If ($PeeringLocation -eq "Ashburn") {
                $ConnPriPortUUID = "66284add-88dd-8dd0-b4e0-30ac094f8af1"
                $ConnSecPortUUID = "66284add-88df-8df0-b4e0-30ac094f8af1"
                $ConnMetro = "DC"
            }
            Else {$ConnPriPortUUID = "66284add-7ab1-ab10-b4e0-30ac094f8af1"
                $ConnSecPortUUID = "66284add-6ba7-ba70-b4e0-30ac094f8af1"
                $ConnMetro = "SE"
            }
            $SKey = $Circuit.ServiceKey
            $Mbps = $Circuit.ServiceProviderProperties.BandwidthInMbps
            $ConnName = $Circuit.Name
            $ConnNamePri = $ConnName + "-pri"
            $ConnNameSec = $ConnName + "-sec"
            If ($CircuitCount -eq 1) {$ConnSTag = "$TenantID"}
            Else{$ConnSTag = "$TenantID" + "$CircuitCount"}

            # Build the Call
            $ConnURI = "https://api.equinix.com/ecx/v3/l2/connections"
            $ConnBody = "{`r" +
                        "  ""primaryName"": ""$ConnNamePri"",`r" +
                        "  ""primaryPortUUID"": ""$ConnPriPortUUID"",`r" +
                        "  ""primaryVlanSTag"": $ConnSTag,`r" +
                        "  ""profileUUID"": ""$ConnProfileUUID"",`r" +
                        "  ""authorizationKey"": ""$SKey"",`r" +
                        "  ""speed"": $Mbps,`r" +
                        "  ""speedUnit"": ""MB"",`r" +
                        "  ""notifications"": [""a@b.com""],`r" +
                        "  ""sellerMetroCode"": ""$ConnMetro"",`r" +
                        "  ""secondaryName"": ""$ConnNameSec"",`r" +
                        "  ""secondaryPortUUID"": ""$ConnSecPortUUID"",`r" +
                        "  ""secondaryVlanSTag"": $ConnSTag`r" +
                        "}"
            #[System.Windows.MessageBox]::Show($ConnBody)
            $connection = Invoke-RestMethod -Method Post -Uri $ConnURI -Headers $ConnHeader -Body $ConnBody -ContentType application/json
            If ($connection.message -eq "Connection Saved Successfully") {
                Write-Host (Get-Date)' - ' -NoNewline
                Write-Host $Circuit.Name -NoNewline -ForegroundColor Yellow
                Write-Host " has been provisioned"
                $ProvisionCount++}
            Else {Write-Warning $Circuit.Name "provisioing has failed"} 
        }
        ElseIf ($Circuit.ServiceProviderProvisioningState -eq "Provisioned") {
            Write-Host (Get-Date)' - ' -NoNewline
            Write-Host $Circuit.Name -NoNewline -ForegroundColor Yellow
            Write-Host " is already provisioned"
        }
        ElseIf ($Circuit.CircuitProvisioningState -ne "Enabled") {
            Write-Host (Get-Date)' - ' -NoNewline
            Write-Host $Circuit.Name -NoNewline -ForegroundColor Yellow
            Write-Host " is not yet ready for provisioning"
        }
        Else {
            Write-Host (Get-Date)' - ' -NoNewline
            Write-Host $Circuit.Name -NoNewline -ForegroundColor Red
            Write-Host " is in an unknown state"
        }
    }

    # 6. End nicely
    $EndTime = Get-Date
    $TimeDiff = New-TimeSpan $StartTime $EndTime
    $Mins = $TimeDiff.Minutes
    $Secs = $TimeDiff.Seconds
    $RunTime = '{0:00}:{1:00} (M:S)' -f $Mins,$Secs
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "$ProvisionCount circuits provisioned ($($ProvisionCount*2) ECX connections)" -ForegroundColor Green
    Write-Host "Time to create: $RunTime"
    Write-Host
}