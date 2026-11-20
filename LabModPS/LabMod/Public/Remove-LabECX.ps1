function Remove-LabECX{
    <#
    .SYNOPSIS
        Deprovisions Equinix Fabric connections for ExpressRoute circuits in the Pathfinder lab.

    .DESCRIPTION
        Authenticates to the Equinix Fabric v4 API, retrieves all ExpressRoute circuits for the
        specified tenant, and deprovisions (deletes) the primary and secondary connections using
        the UUID tags saved by New-LabECX. If UUIDs are missing from the circuit tags, the user
        is prompted to supply them interactively.
        Displays a banner, timestamped progress, and a formatted outcome summary. Counts
        accepted deletion requests individually so partial failures remain visible.

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
    $SubID = $script:SubscriptionMap[$Subscription]

    # Azure Variables
    If ($PeeringLocation -eq "Seattle") {$RGName = "SEA-Cust$TenantID"}
    Else {$RGName = "ASH-Cust$TenantID"}
    $TenantStub = "$RGName-ER"
    $kvName = "LabSecrets"
    $kvECXClientID = "ECXClientID"
    $kvECXSecret = "ECXSecret"
    $providerRun = @{
        Script = 'Remove-LabECX'
        Tenant = $RGName
        Started = $StartTime
        CircuitsExamined = 0
        ConnectionsSubmitted = 0
        CircuitsSkipped = 0
        Failures = 0
    }

    try {
        Write-LabProviderStatus -Kind Banner -Message ('=' * 70)
        Write-LabProviderStatus -Kind Banner -Message ' LabMod: Deprovision Provider'
        Write-LabProviderStatus -Kind Banner -Message " Tenant : $RGName"
        Write-LabProviderStatus -Kind Banner -Message " Started: $($StartTime.ToString('u'))"
        Write-LabProviderStatus -Kind Banner -Message ('=' * 70)

        # 2. Validate
        # Az Module Test
        $ModCheck = Get-Module Az.Network -ListAvailable
        If ($Null -eq $ModCheck) {
            $providerRun.Failures++
            Write-LabProviderStatus -Kind Fail -Message 'The required Az.Network module is not installed.'
            Write-Warning "The Az PowerShell module was not found. This script uses the Az modules for PowerShell"
            Write-Warning "See the blob post for more information at: https://azure.microsoft.com/blog/how-to-migrate-from-azurerm-to-az-in-azure-powershell/"
            Return
            }

        # Login and permissions check
        Write-LabProviderStatus -Kind Step -Message "Checking login and permissions"
        Try {Get-AzResourceGroup -Name "LabInfrastructure" -ErrorAction Stop | Out-Null}
        Catch {# Login and set subscription for ARM
                Write-LabProviderStatus -Message "Logging in to ARM"
                Try {$Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop -WarningAction SilentlyContinue).Subscription}
                Catch {Connect-AzAccount | Out-Null
                        $Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop -WarningAction SilentlyContinue).Subscription}
                Write-LabProviderStatus -Message "Current Sub: $($Sub.Name) ($($Sub.Id))"
                Try {Get-AzResourceGroup -Name "LabInfrastructure" -ErrorAction Stop | Out-Null}
                Catch {
                        $providerRun.Failures++
                        Write-LabProviderStatus -Kind Fail -Message 'Azure permission check failed.'
                        Write-Warning "Permission check failed, ensure tenant id is set correctly!"
                        Return}
        }

        # 3. Get ECX OAuth Token
        # Get ECX API Keys from Key Vault
        Write-LabProviderStatus -Kind Step -Message "Getting ECX OAuth Token"
        Write-LabProviderStatus -Message "Retrieving ECX credentials from Key Vault"
        $ECXClientID = Get-AzKeyVaultSecret -VaultName $kvName -Name $kvECXClientID -AsPlainText
        $ECXSecret = Get-AzKeyVaultSecret -VaultName $kvName -Name $kvECXSecret -AsPlainText
        Write-LabProviderStatus -Kind OK -Message "ECX credentials retrieved"

        # Get REST OAuth Token
        Write-LabProviderStatus -Message "Requesting OAuth token"
        $TokenURI = "https://api.equinix.com/oauth2/v1/token"
        $TokenBody = @{
            grant_type = "client_credentials"
            client_id = $ECXClientID
            client_secret = $ECXSecret
        } | ConvertTo-Json
        Try {$token = Invoke-RestMethod -Method Post -Uri $TokenURI -Body $TokenBody -ContentType application/json}
        Catch {
            $providerRun.Failures++
            Write-LabProviderStatus -Kind Fail -Message 'Unable to authenticate to Equinix.'
            Write-Warning "An error occurred getting an OAuth certificate from Equinix."
            Return }
        $ConnHeader =  @{"Authorization" = "Bearer $($token.access_token)"}
        Write-LabProviderStatus -Kind OK -Message "Authenticated to Equinix"

        # 4. Load all ER circuits into an array
        Write-LabProviderStatus -Kind Step -Message "Retrieving ExpressRoute circuits"
        $Circuits = Get-AzExpressRouteCircuit -ResourceGroupName $RGName -ErrorAction Stop | Where-Object Name -Like "$TenantStub*"
        if (@($Circuits).Count -eq 0) {
            Write-LabProviderStatus -Kind Skip -Message "No matching ExpressRoute circuits found in $RGName."
        }

        # 5. Loop through Circuits array deleting connections
        $ConnURI = "https://api.equinix.com/fabric/v4/connections"
        ForEach ($Circuit in $Circuits) {
            $providerRun.CircuitsExamined++
            # If provisioned, issue the delete command on the two Connections
            If ($Circuit.CircuitProvisioningState -eq "Enabled" -and $Circuit.ServiceProviderProvisioningState -eq "Provisioned") {
                Write-LabProviderStatus -Kind Step -Message "Deprovisioning $($Circuit.Name)"

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
                    Write-LabProviderStatus -Message "$($Circuit.Name) - UUID1 is missing or invalid"
                    $UUID1 = Read-Host "Enter UUID1 (Primary connection UUID) or press Enter to skip"
                    $tempGuid = [guid]::Empty
                    If ([string]::IsNullOrWhiteSpace($UUID1) -or -not [guid]::TryParse($UUID1, [ref]$tempGuid)) {
                        Write-Warning "Invalid or empty UUID1 provided, skipping UUID-based deletion"
                        $UUID1 = $null
                    }
                }

                If ($null -eq $UUID2) {
                    Write-LabProviderStatus -Message "$($Circuit.Name) - UUID2 is missing or invalid"
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
                            Write-LabProviderStatus -Kind OK -Message "$($Circuit.Name): connection $ConnUUID submitted for deprovisioning"
                            $providerRun.ConnectionsSubmitted++
                        }
                        Catch {
                            if ($_.ErrorDetails.Message) {
                                try {
                                    $errorResponse = $_.ErrorDetails.Message | ConvertFrom-Json
                                    if ($errorResponse.errorCode -eq "EQ-300008" ) {
                                        Write-LabProviderStatus -Kind Skip -Message "$($Circuit.Name): connection $ConnUUID was not found"
                                        Write-Warning "$($Circuit.Name) - Connection with UUID $ConnUUID not found"
                                        continue
                                    } else {
                                        $providerRun.Failures++
                                        Write-LabProviderStatus -Kind Fail -Message "$($Circuit.Name): connection $ConnUUID deprovisioning failed."
                                        $errorMessage = $errorResponse[0].errorMessage
                                        Write-Warning "$($Circuit.Name) - $errorMessage (UUID: $ConnUUID)"
                                    }
                                } catch {
                                    $providerRun.Failures++
                                    Write-LabProviderStatus -Kind Fail -Message "$($Circuit.Name): connection $ConnUUID deprovisioning failed."
                                    Write-Warning "$($Circuit.Name) deprovisioning failed for UUID $ConnUUID"
                                    Write-LabProviderStatus -Message "Error: $($_.Exception.Message)"
                                }
                            } else {
                                $providerRun.Failures++
                                Write-LabProviderStatus -Kind Fail -Message "$($Circuit.Name): connection $ConnUUID deprovisioning failed."
                                Write-Warning "$($Circuit.Name) deprovisioning failed for UUID $ConnUUID"
                                Write-LabProviderStatus -Message "Error: $($_.Exception.Message)"
                            }
                        }
                    }
                } Else {
                    $providerRun.Failures++
                    $providerRun.CircuitsSkipped++
                    Write-LabProviderStatus -Kind Fail -Message "$($Circuit.Name): missing or invalid UUIDs; deprovisioning skipped."
                    # Skip this circuit if UUIDs are not available
                    Write-Warning "$($Circuit.Name) skipping deprovisioning due to missing or invalid UUIDs"
                }
            }
            ElseIf ($Circuit.ServiceProviderProvisioningState -eq "NotProvisioned") {
                $providerRun.CircuitsSkipped++
                Write-LabProviderStatus -Kind Skip -Message "$($Circuit.Name) is already deprovisioned"
            }
            Else {
                $providerRun.CircuitsSkipped++
                Write-LabProviderStatus -Kind Skip -Message "$($Circuit.Name) is in an unknown/bad state"
            }
        }

    }
    catch {
        $providerRun.Failures++
        Write-LabProviderStatus -Kind Fail -Message "Provider deprovisioning stopped: $($_.Exception.Message)"
        throw
    }
    finally {
        Write-LabProviderSummary -Run $providerRun
    }
}
