function Remove-LabECX{
    <#
    .SYNOPSIS
        Deprovisions Equinix Fabric connections for ExpressRoute circuits in the Pathfinder lab.

    .DESCRIPTION
        Authenticates to the Equinix Fabric v4 API, retrieves all ExpressRoute circuits for the
        specified tenant, and deprovisions (deletes) the primary and secondary connections using
        the UUID tags saved by New-LabECX. If UUIDs are missing from the circuit tags, the user
        is prompted to supply them interactively.

    .PARAMETER TenantID
        The two-digit tenant ID in the lab. Valid values are 10 to 99.

    .PARAMETER PeeringLocation
        The Equinix peering location for the connections. Valid values are "Ashburn" and "Seattle".

    .PARAMETER Subscription
        The Azure subscription containing the tenant resource group. Defaults to "ExpressRoute-Lab".

    .EXAMPLE
        Remove-LabECX -TenantID 16 -PeeringLocation Ashburn

        Deprovisions ECX connections for all provisioned circuits in tenant 16 at Ashburn.

    .EXAMPLE
        Remove-LabECX -TenantID 22 -PeeringLocation Seattle -Subscription "Hybrid-PM-Test-1"

        Deprovisions ECX connections for tenant 22 at Seattle using a non-default subscription.

    .LINK
        New-LabECX
    #>

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
    $SubID = $script:SubscriptionMap[$Subscription]

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
    Write-Log "Checking login and permissions" -TimeStamp
    Try {Get-AzResourceGroup -Name "LabInfrastructure" -ErrorAction Stop | Out-Null}
    Catch {# Login and set subscription for ARM
            Write-Log "  Logging in to ARM"
            Try {$Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop -WarningAction SilentlyContinue).Subscription}
            Catch {Connect-AzAccount | Out-Null
                    $Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop -WarningAction SilentlyContinue).Subscription}
            Write-Log "  Current Sub: $($Sub.Name) ($($Sub.Id))"
            Try {Get-AzResourceGroup -Name "LabInfrastructure" -ErrorAction Stop | Out-Null}
            Catch {Write-Warning "Permission check failed, ensure tenant id is set correctly!"
                    Return}
    }

    # 3. Get ECX OAuth Token
    # Get ECX API Keys from Key Vault
    Write-Log "Getting ECX OAuth Token" -TimeStamp
    Write-Log "  Grabbing ECX secrets"
    $ECXClientID = Get-AzKeyVaultSecret -VaultName $kvName -Name $kvECXClientID -AsPlainText
    $ECXSecret = Get-AzKeyVaultSecret -VaultName $kvName -Name $kvECXSecret -AsPlainText
    Write-Log "  ECX secrets retrieved"

    # Get REST OAuth Token
    Write-Log "  Getting OAuth Token"
    $TokenURI = "https://api.equinix.com/oauth2/v1/token"
    $TokenBody = @{
        grant_type = "client_credentials"
        client_id = $ECXClientID
        client_secret = $ECXSecret
    } | ConvertTo-Json
    Try {$token = Invoke-RestMethod -Method Post -Uri $TokenURI -Body $TokenBody -ContentType application/json}
    Catch {
        Write-Warning "An error occurred getting an OAuth certificate from Equinix."
        Write-Log $error[0].ErrorDetails.Message
        Return }
    $ConnHeader =  @{"Authorization" = "Bearer $($token.access_token)"}
    Write-Log "  OAuth Token retrieved"

    # 4. Load all ER circuits into an array
    Write-Log "Pulling ER Circuit(s)" -TimeStamp
    $Circuits = Get-AzExpressRouteCircuit -ResourceGroupName $RGName | Where-Object Name -Like "$TenantStub*"

    # 5. Loop through Circuits array deleting connections
    $ConnURI = "https://api.equinix.com/fabric/v4/connections"
    ForEach ($Circuit in $Circuits) {
        # If provisioned, issue the delete command on the two Connections
        If ($Circuit.CircuitProvisioningState -eq "Enabled" -and $Circuit.ServiceProviderProvisioningState -eq "Provisioned") {
            Write-Log "Deprovisioning $($Circuit.Name)" -TimeStamp

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
                Write-Log "$($Circuit.Name) - UUID1 is missing or invalid"
                $UUID1 = Read-Host "Enter UUID1 (Primary connection UUID) or press Enter to skip"
                $tempGuid = [guid]::Empty
                If ([string]::IsNullOrWhiteSpace($UUID1) -or -not [guid]::TryParse($UUID1, [ref]$tempGuid)) {
                    Write-Warning "Invalid or empty UUID1 provided, skipping UUID-based deletion"
                    $UUID1 = $null
                }
            }
            
            If ($null -eq $UUID2) {
                Write-Log "$($Circuit.Name) - UUID2 is missing or invalid"
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
                        Write-Log "$($connection.name) has been submitted for deprovisioning" -TimeStamp
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
                                Write-Log "  Error: $($_.Exception.Message)"
                            }
                        } else {
                            Write-Warning "$($Circuit.Name) deprovisioning failed for UUID $ConnUUID"
                            Write-Log "  Error: $($_.Exception.Message)"
                        }
                    }
                }
            } Else {
                # Skip this circuit if UUIDs are not available
                Write-Warning "$($Circuit.Name) skipping deprovisioning due to missing or invalid UUIDs"
            }
        }
        ElseIf ($Circuit.ServiceProviderProvisioningState -eq "NotProvisioned") {
            Write-Log "$($Circuit.Name) is already deprovisioned" -TimeStamp
        }
        Else {
            Write-Log "$($Circuit.Name) is in an unknown/bad state" -TimeStamp
        }
    } 

    # 7. End nicely
    $EndTime = Get-Date
    $TimeDiff = New-TimeSpan $StartTime $EndTime
    $RunTime = $TimeDiff.ToString('hh\:mm\:ss')
    Write-Log "$($DeprovisionCount/2) circuits deprovisioned ($DeprovisionCount ECX Connections)" -TimeStamp
    Write-Log "Time to deprovision: $RunTime"
    Write-Host
}
