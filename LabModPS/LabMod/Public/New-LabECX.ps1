function New-LabECX{
    <#
    .SYNOPSIS
        Provisions Equinix Fabric connections for ExpressRoute circuits in the Pathfinder lab.

    .DESCRIPTION
        Authenticates to the Equinix Fabric v4 API, retrieves all ExpressRoute circuits for the
        specified tenant, and provisions primary and secondary EVPL connections for each circuit
        that is enabled but not yet provisioned. Connection UUIDs are tagged back onto the Azure
        circuit resource for later deprovisioning with Remove-LabECX.

    .PARAMETER TenantID
        The two-digit tenant ID in the lab. Valid values are 10 to 99.

    .PARAMETER PeeringLocation
        The Equinix peering location for the connections. Valid values are "Ashburn" and "Seattle".

    .PARAMETER Subscription
        The Azure subscription containing the tenant resource group. Defaults to "ExpressRoute-Lab".

    .EXAMPLE
        New-LabECX -TenantID 16 -PeeringLocation Ashburn

        Provisions ECX connections for all enabled circuits in tenant 16 at Ashburn.

    .EXAMPLE
        New-LabECX -TenantID 22 -PeeringLocation Seattle -Subscription "Hybrid-PM-Test-1"

        Provisions ECX connections for tenant 22 at Seattle using a non-default subscription.

    .LINK
        Remove-LabECX
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
    $SubID = $script:SubscriptionMap[$Subscription]

    # Azure Variables
    If ($PeeringLocation -eq "Seattle") {$RGName = "SEA-Cust$TenantID"}
    Else {$RGName = "ASH-Cust$TenantID"}
    $TenantStub = "$RGName-ER"
    
    # Equinix Variables
    $ConnProfileUUID = "a1390b22-bbe0-4e93-ad37-85beef9d254d"

    # 2. Validate
    # Ensure this isn't on a physical machine
    If ($env:COMPUTERNAME -match '^(SEA|ASH)-ER-\d+$'){
        Write-Warning "This script should not be run on a PathLab physical machine. It must be run from a host that can login to and access Azure."
        Return}

    # Az Module Test
    $ModCheck = Get-Module Az.Network -ListAvailable
    If ($Null -eq $ModCheck) {
        Write-Warning "The Az PowerShell module was not found. This commandlet uses the Az modules for PowerShell"
        Write-Warning "See the blob post for more information at: https://azure.microsoft.com/blog/how-to-migrate-from-azurerm-to-az-in-azure-powershell/"
        Return
        }

    # Login and permissions check
    Write-Log "Checking login and permissions" -TimeStamp
    $CurrentContext = Get-AzContext
    If ($CurrentContext.Subscription.Id -ne $SubID) {
        # Login and set subscription for ARM
        Write-Log "  Logging in to ARM"
        Try {$Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop).Subscription}
        Catch {Connect-AzAccount | Out-Null
                $Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop).Subscription}
        Write-Log "  Current Sub: $($Sub.Name) ($($Sub.Id))"
    }
    Else {
        Write-Log "  Current Sub: $($CurrentContext.Subscription.Name) ($($CurrentContext.Subscription.Id))"
    }
    
    # 3. Get ECX Token
    Write-Log "Getting ECX OAuth Token" -TimeStamp
    Write-Log "  Grabbing ECX secrets"
    $kvName = "LabSecrets"
    $ECXClientID = Get-AzKeyVaultSecret -VaultName $kvName -Name "ECXClientID" -AsPlainText
    $ECXSecret = Get-AzKeyVaultSecret -VaultName $kvName -Name "ECXSecret" -AsPlainText
    Write-Log "  ECX secrets retrieved"

    # Get REST OAuth Token
    Write-Log "  Getting OAuth Token"
    $TokenURI = "https://api.equinix.com/oauth2/v1/token"
    $TokenBody = "{" + 
                "  ""grant_type"": ""client_credentials""," +
                "  ""client_id"": ""$ECXClientID""," +
                "  ""client_secret"": ""$ECXSecret""" +
                "}"
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

    # 5. Loop through Circuit array
    ForEach ($Circuit in $Circuits) {
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

            # Build the Call for Primary Connection (using Fabric v4 API)
            $ConnURI = "https://api.equinix.com/fabric/v4/connections"
            $ConnBodyPri = @{
                type = "EVPL_VC"
                name = $ConnNamePri
                bandwidth = $Mbps
                aSide = @{
                    accessPoint = @{
                        type = "COLO"
                        port = @{
                            uuid = $ConnPriPortUUID
                        }
                        linkProtocol = @{
                            type = "QINQ"
                            vlanSTag = $ConnSTag
                        }
                    }
                }
                zSide = @{
                    accessPoint = @{
                        type = "SP"
                        profile = @{
                            uuid = $ConnProfileUUID
                        }
                        location = @{
                            metroCode = $ConnMetro
                        }
                        linkProtocol = @{
                            type = "QINQ"
                            vlanSTag = $ConnSTag
                        }
                        authenticationKey = $SKey
                    }
                }
                notifications = @(
                    @{
                        type = "ALL"
                        emails = @("a@b.com")
                    }
                )
            } | ConvertTo-Json -Depth 5

            # Provision Primary Connection
            Try {
                $connectionPri = Invoke-RestMethod -Method Post -Uri $ConnURI -Headers $ConnHeader -Body $ConnBodyPri -ContentType application/json
            }
            Catch {
                Write-Warning "$($Circuit.Name) primary connection provisioning failed: $($_.Exception.Message)"
                continue
            }
            If ($connectionPri.operation.equinixStatus -eq "PROVISIONING") {
                Write-Log "$($Circuit.Name) Primary has been submitted for provisioning" -TimeStamp
                $PriUUID = $connectionPri.uuid
                
                # Build and provision Secondary Connection with redundancy group
                $GroupID = $connectionPri.redundancy.group
                $ConnBodySec = @{
                    type = "EVPL_VC"
                    name = $ConnNameSec
                    bandwidth = $Mbps
                    aSide = @{
                        accessPoint = @{
                            type = "COLO"
                            port = @{
                                uuid = $ConnSecPortUUID
                            }
                            linkProtocol = @{
                                type = "QINQ"
                                vlanSTag = $ConnSTag
                            }
                        }
                    }
                    zSide = @{
                        accessPoint = @{
                            type = "SP"
                            profile = @{
                                uuid = $ConnProfileUUID
                            }
                            location = @{
                                metroCode = $ConnMetro
                            }
                            linkProtocol = @{
                                type = "QINQ"
                                vlanSTag = $ConnSTag
                            }
                            authenticationKey = $SKey
                        }
                    }
                    notifications = @(
                        @{
                            type = "ALL"
                            emails = @("a@b.com")
                        }
                    )
                    redundancy = @{
                        group = $GroupID
                        priority = "SECONDARY"
                    }
                } | ConvertTo-Json -Depth 5

                $connectionSec = Invoke-RestMethod -Method Post -Uri $ConnURI -Headers $ConnHeader -Body $ConnBodySec -ContentType application/json
                If ($connectionSec.operation.equinixStatus -eq "PROVISIONING") {
                    Write-Log "$($Circuit.Name) Secondary has been submitted for provisioning" -TimeStamp
                    $SecUUID = $connectionSec.uuid
                    
                    # Update circuit tags with connection UUIDs
                    $Tags = @{
                        UUID1 = $PriUUID
                        UUID2 = $SecUUID
                    }
                    Try {
                        #$Circuit | Set-AzExpressRouteCircuit -ErrorAction Stop | Out-Null
                        Update-AzTag -ResourceId $Circuit.Id -Tag $Tags -Operation Merge -ErrorAction Stop | Out-Null
                        Write-Log "  Tagged circuit with connection UUIDs"
                    }
                    Catch {
                        Write-Warning "  Failed to tag circuit with UUIDs: $($_.Exception.Message)"
                    }
                    
                    $ProvisionCount++
                }
                Else {Write-Warning $Circuit.Name "secondary connection provisioning has failed"}
            }
            Else {Write-Warning $Circuit.Name "primary connection provisioning has failed"} 
        }
        ElseIf ($Circuit.ServiceProviderProvisioningState -eq "Provisioned") {
            Write-Log "$($Circuit.Name) is already provisioned" -TimeStamp
        }
        ElseIf ($Circuit.CircuitProvisioningState -ne "Enabled") {
            Write-Log "$($Circuit.Name) is not yet ready for provisioning" -TimeStamp
        }
        Else {
            Write-Log "$($Circuit.Name) is in an unknown state" -TimeStamp
        }
    }

    # 6. End nicely
    $EndTime = Get-Date
    $TimeDiff = New-TimeSpan $StartTime $EndTime
    $RunTime = $TimeDiff.ToString('hh\:mm\:ss')
    Write-Log "$ProvisionCount circuits submitted for provisioning ($($ProvisionCount*2) ECX connections)" -TimeStamp
    Write-Log "Time to create: $RunTime"
    Write-Host
}
