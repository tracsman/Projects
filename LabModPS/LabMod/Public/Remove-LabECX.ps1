function Remove-LabECX{
    [CmdletBinding()]
    param (
        [Parameter(Mandatory=$true, ValueFromPipeline=$true, HelpMessage='Enter Tenant ID')]
        [ValidateRange(10,99)]
        [int]$TenantID,
        [Parameter(Mandatory=$true, HelpMessage='Enter ER Location')]
        [ValidateSet('Ashburn','Seattle')]
        [string]$PeeringLocation,
        [ValidateSet('ExpressRoute-Lab','ExpressRoute-lab-bvt','Hybrid-PM-Demo-1','Hybrid-PM-Test-1','Hybrid-PM-Test-2','Hybrid-PM-Repro-1')]
        [string]$Subscription='ExpressRoute-Lab')

    # Equinix Provisioning Script for PathFinder Lab
    #
    # 1. Initialize
    # 2. Validate
    # 3. Get ECX OAuth Token
    # 4. Load all ER circuits into an array
    # 5. Loop through Circuits array deleting connections
    # 6. End Nicely
    #

    # 1. Initialize
    # Script Variables
    $StartTime = Get-Date
    $DeprovisionCount = 0
    Switch ($Subscription) {
        'ExpressRoute-Lab'     {$SubID = '4bffbb15-d414-4874-a2e4-c548c6d45e2a'}
        'ExpressRoute-lab-bvt' {$SubID = '79573dd5-f6ea-4fdc-a3aa-d05586980843'}
        'Hybrid-PM-Demo-1'     {$SubID = '28bf59a7-de1b-4c94-92ec-a5aab87885f7'}
        'Hybrid-PM-Test-1'     {$SubID = 'f2a54638-fcdc-443b-a6fe-5ea64d2c9e0e'}
        'Hybrid-PM-Test-2'     {$SubID = '43467485-b19a-4b68-ac94-c9a8e980ca7f'}
        'Hybrid-PM-Repro-1'    {$SubID = '79573dd5-f6ea-4fdc-a3aa-d05586980843'}
    }

    # Azure Variables
    If ($PeeringLocation -eq "Seattle") {$RGName = "SEA-Cust$TenantID"}
    Else {$RGName = "ASH-Cust$TenantID"}
    $TenantStub = "$RGName-ER"
    $kvName = "LabSecrets"
    $kvECXClientID = "ECXClientID"
    $kvECXSecret = "ECXSecret"

    # 2. Validate
    # Az Module Test
    $ModCheck = Get-Module Az.Network -ListAvailable
    If ($Null -eq $ModCheck) {
        Write-Warning "The Az PowerShell module was not found. This script uses the Az modules for PowerShell"
        Write-Warning "See the blob post for more information at: https://azure.microsoft.com/blog/how-to-migrate-from-azurerm-to-az-in-azure-powershell/"
        Return
        }

    # Login and permissions check
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Checking login and permissions" -ForegroundColor Cyan
    Try {Get-AzResourceGroup -Name "LabInfrastructure" -ErrorAction Stop | Out-Null}
    Catch {# Login and set subscription for ARM
            Write-Host "  Logging in to ARM"
            Try {$Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop -WarningAction SilentlyContinue).Subscription}
            Catch {Connect-AzAccount | Out-Null
                    $Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop -WarningAction SilentlyContinue).Subscription}
            Write-Host "  Current Sub:",$Sub.Name,"(",$Sub.Id,")"
            Try {Get-AzResourceGroup -Name "LabInfrastructure" -ErrorAction Stop | Out-Null}
            Catch {Write-Warning "Permission check failed, ensure tenant id is set correctly!"
                    Return}
    }

    # 3. Get ECX OAuth Token
    # Get ECX API Keys from Key Vault
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Getting ECX OAuth Token" -ForegroundColor Cyan
    Write-Host "  Grabbing ECX secrets..." -NoNewline
    $ECXClientID = Get-AzKeyVaultSecret -VaultName $kvName -Name $kvECXClientID -AsPlainText
    $ECXSecret = Get-AzKeyVaultSecret -VaultName $kvName -Name $kvECXSecret -AsPlainText
    Write-Host "Success" -ForegroundColor Green

    # Get REST OAuth Token
    Write-Host "  Getting OAuth Token...." -NoNewline
    $TokenURI = "https://api.equinix.com/oauth2/v1/token"
    $TokenBody = @{
        grant_type = "client_credentials"
        client_id = $ECXClientID
        client_secret = $ECXSecret
    } | ConvertTo-Json
    Try {$token = Invoke-RestMethod -Method Post -Uri $TokenURI -Body $TokenBody -ContentType application/json}
    Catch {Write-Host
        Write-Warning "An error occurred getting an OAuth certificate from Equinix."
        Write-Host
        Write-Host $error[0].ErrorDetails.Message
        Return }
    $ConnHeader =  @{"Authorization" = "Bearer $($token.access_token)"}
    Write-Host "Success" -ForegroundColor Green

    # 4. Load all ER circuits into an array
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Pulling ER Circuit(s)" -ForegroundColor Cyan
    $Circuits = Get-AzExpressRouteCircuit -ResourceGroupName $RGName | Where-Object Name -Like "$TenantStub*"

    # 5. Loop through Circuits array deleting connections
    $ConnURI = "https://api.equinix.com/fabric/v4/connections"
    ForEach ($Circuit in $Circuits) {
        # If provisioned, issue the delete command on the two Connections
        If ($Circuit.CircuitProvisioningState -eq "Enabled" -and $Circuit.ServiceProviderProvisioningState -eq "Provisioned") {
            Write-Host (Get-Date)' - ' -NoNewline
            Write-Host "Deprovisioning $($Circuit.Name)" -ForegroundColor Cyan

            # Check if circuit has UUID tags
            $UUID1 = $null
            $UUID2 = $null
            $UseTaggedUUIDs = $false
            $DoInitialValidation = $true
            
            If ($null -eq $Circuit.Tag){
                $DoInitialValidation = $false
            }

            If ($DoInitialValidation -and $Circuit.Tag.ContainsKey("UUID1")) {
                $tempGuid = [guid]::Empty
                If ([guid]::TryParse($Circuit.Tag["UUID1"], [ref]$tempGuid)) {
                    $UUID1 = $Circuit.Tag["UUID1"]
                } Else {
                    Write-Warning "$($Circuit.Name) - UUID1 tag is not a valid GUID"
                }
            }

            If ($DoInitialValidation -and $Circuit.Tag.ContainsKey("UUID2")) {
                # Validate UUID2 is a GUID
                $tempGuid = [guid]::Empty
                If ([guid]::TryParse($Circuit.Tag["UUID2"], [ref]$tempGuid)) {
                    $UUID2 = $Circuit.Tag["UUID2"]
                } Else {
                    Write-Warning "$($Circuit.Name) - UUID2 tag is not a valid GUID"
                }
            }

            # Prompt for missing or invalid UUIDs
            If ($null -eq $UUID1) {
                Write-Host "$($Circuit.Name) - UUID1 is missing or invalid" -ForegroundColor Yellow
                $UUID1 = Read-Host "Enter UUID1 (Primary connection UUID) or press Enter to skip"
                $tempGuid = [guid]::Empty
                If ([string]::IsNullOrWhiteSpace($UUID1) -or -not [guid]::TryParse($UUID1, [ref]$tempGuid)) {
                    Write-Warning "Invalid or empty UUID1 provided, skipping UUID-based deletion"
                    $UUID1 = $null
                }
            }
            
            If ($null -eq $UUID2) {
                Write-Host "$($Circuit.Name) - UUID2 is missing or invalid" -ForegroundColor Yellow
                $UUID2 = Read-Host "Enter UUID2 (Secondary connection UUID) or press Enter to skip"
                $tempGuid = [guid]::Empty
                If ([string]::IsNullOrWhiteSpace($UUID2) -or -not [guid]::TryParse($UUID2, [ref]$tempGuid)) {
                    Write-Warning "Invalid or empty UUID2 provided, skipping UUID-based deletion"
                    $UUID2 = $null
                }
            }
            # Use tagged UUIDs if both are valid
            If ($UUID1 -and $UUID2) {
                $UseTaggedUUIDs = $true
            }
            
            If ($UseTaggedUUIDs) {
                # Delete using tagged UUIDs
                ForEach ($ConnUUID in @($UUID1, $UUID2)) {
                    Try {
                        $connection = Invoke-RestMethod -Method Delete -Uri "$ConnURI/$ConnUUID" -Headers $ConnHeader -ErrorAction Stop
                        Write-Host (Get-Date)' - ' -NoNewline
                        Write-Host $connection.name -NoNewline -ForegroundColor Yellow
                        Write-Host " has been submitted for deprovisioning"
                        $DeprovisionCount++
                    }
                    Catch {
                        if ($_.ErrorDetails.Message) {
                            try {
                                $errorResponse = $_.ErrorDetails.Message | ConvertFrom-Json
                                if ($errorResponse.errorCode -eq "EQ-300008" ) {
                                    Write-Warning "$($Circuit.Name) - Connection with UUID $ConnUUID not found"
                                    continue
                                } else {
                                    $errorMessage = $errorResponse[0].errorMessage
                                    Write-Warning "$($Circuit.Name) - $errorMessage (UUID: $ConnUUID)"
                                }
                            } catch {
                                Write-Warning "$($Circuit.Name) deprovisioning failed for UUID $ConnUUID"
                                Write-Host "  Error: $($_.Exception.Message)"
                            }
                        } else {
                            Write-Warning "$($Circuit.Name) deprovisioning failed for UUID $ConnUUID"
                            Write-Host "  Error: $($_.Exception.Message)"
                        }
                    }
                }
            } Else {
                # Skip this circuit if UUIDs are not available
                Write-Warning "$($Circuit.Name) skipping deprovisioning due to missing or invalid UUIDs"
            }
        }
        ElseIf ($Circuit.ServiceProviderProvisioningState -eq "NotProvisioned") {
            Write-Host (Get-Date)' - ' -NoNewline
            Write-Host $Circuit.Name -NoNewline -ForegroundColor Yellow
            Write-Host " is already deprovisioned"
        }
        Else {
            Write-Host (Get-Date)' - ' -NoNewline
            Write-Host $Circuit.Name -NoNewline -ForegroundColor Red
            Write-Host " is in an unknown/bad state"
        }
    } 

    # 7. End nicely
    $EndTime = Get-Date
    $TimeDiff = New-TimeSpan $StartTime $EndTime
    $Mins = $TimeDiff.Minutes
    $Secs = $TimeDiff.Seconds
    $RunTime = '{0:00}:{1:00} (M:S)' -f $Mins,$Secs
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "$($DeprovisionCount/2) circuits deprovisioned ($DeprovisionCount ECX Connections)" -ForegroundColor Green
    Write-Host "Time to deprovision: $RunTime"
    Write-Host
}
