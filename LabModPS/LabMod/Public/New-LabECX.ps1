function New-LabECX{
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
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Checking login and permissions" -ForegroundColor Cyan
    $CurrentContext = Get-AzContext
    If ($CurrentContext.Subscription.Id -ne $SubID) {
        # Login and set subscription for ARM
        Write-Host "  Logging in to ARM"
        Try {$Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop).Subscription}
        Catch {Connect-AzAccount | Out-Null
                $Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop).Subscription}
        Write-Host "  Current Sub:",$Sub.Name,"(",$Sub.Id,")"
    }
    Else {
        Write-Host "  Current Sub:",$CurrentContext.Subscription.Name,"(",$CurrentContext.Subscription.Id,")"
    }
    
    # 3. Get ECX Token
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Getting ECX OAuth Token" -ForegroundColor Cyan
    Write-Host "  Grabbing ECX secrets..." -NoNewline
    $kvName = "LabSecrets"
    $ECXClientID = Get-AzKeyVaultSecret -VaultName $kvName -Name "ECXClientID" -AsPlainText
    $ECXSecret = Get-AzKeyVaultSecret -VaultName $kvName -Name "ECXSecret" -AsPlainText
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
            $connectionPri = Invoke-RestMethod -Method Post -Uri $ConnURI -Headers $ConnHeader -Body $ConnBodyPri -ContentType application/json
            If ($connectionPri.operation.equinixStatus -eq "PROVISIONING") {
                Write-Host (Get-Date)' - ' -NoNewline
                Write-Host $Circuit.Name'Primary' -NoNewline -ForegroundColor Yellow
                Write-Host " has been submitted for provisioning"
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
                    Write-Host (Get-Date)' - ' -NoNewline
                    Write-Host $Circuit.Name'Secondary' -NoNewline -ForegroundColor Yellow
                    Write-Host " has been submitted for provisioning"
                    $SecUUID = $connectionSec.uuid
                    
                    # Update circuit tags with connection UUIDs
                    $Tags = @{
                        UUID1 = $PriUUID
                        UUID2 = $SecUUID
                    }
                    Try {
                        #$Circuit | Set-AzExpressRouteCircuit -ErrorAction Stop | Out-Null
                        Update-AzTag -ResourceId $Circuit.Id -Tag $Tags -Operation Merge -ErrorAction Stop | Out-Null
                        Write-Host "  Tagged circuit with connection UUIDs"
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
    Write-Host "$ProvisionCount circuits submitted for provisioning ($($ProvisionCount*2) ECX connections)" -ForegroundColor Green
    Write-Host "Time to create: $RunTime"
    Write-Host
}
