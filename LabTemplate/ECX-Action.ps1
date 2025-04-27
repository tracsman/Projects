[CmdletBinding()]
param (
    [Parameter(ValueFromPipeline=$true, HelpMessage='Enter Resource Group Name')]
    [string]$RGName,
    [switch]$Deprovision=$false)

# Equinix Provisioning Script for PathLab Customers
#
# To ensure you are in the right subscrition run
# Get-AzContext
#
# 1. Get ECX Token
# 2. Load ER circuit
# 3. Deprovision Circuit(s) if needed
# 3.1 Set json body to search for provisioned, lab-related connections
# 3.2 Pull ECX Connections into Connections array
# 3.3 Loop through connections and deprovision
# 4. Provision Circuit(s) if needed
# 4.1 Load all Circuits in RG into an array and loop through
# 4.1.1 If enabled and not provisioned, provision the circuit

# Pri: 192.168.x.16/30
# Sec: 192.168.x.20/30
# VLAN: xx0

# Function to build Equinix Connection JSON Body
function Build-ConnBody {
    param (
        [string]$ConnName,        # e.g. SEA-Cust10-ER-Pri
        [string]$ConnVlanTag,     # e.g. 100
        [string]$LabLocation,     # ASH or SEA
        [string]$CircuitLocation, # Equinix location code e.g. SE or DC, etc
        [string]$SKey,
        [int]$Mbps,
        [string]$GroupID=0,
        [string]$ContactEmail="jonor@microsoft.com"
    )
    # Set port UUIDs based on location and type, ASH Pri/Sec or SEA Pri/Sec
    If ($LabLocation -eq "ASH" -and $GroupID -eq 0) {
        $ConnPortUUID = "66284add-88dd-8dd0-b4e0-30ac094f8af1"
    }
    elseif ($LabLocation -eq "ASH" -and $GroupID -ne 0) {
        $ConnPortUUID = "66284add-88df-8df0-b4e0-30ac094f8af1"
    }
    elseif ($LabLocation -eq "SEA" -and $GroupID -eq 0) {
        $ConnPortUUID = "66284add-7ab1-ab10-b4e0-30ac094f8af1"
    }
    elseif ($LabLocation -eq "SEA" -and $GroupID -ne 0) {
        $ConnPortUUID = "66284add-6ba7-ba70-b4e0-30ac094f8af1"
    } 
    else {
        # Error, stop script
        Write-Warning "Invalid LabLocation or ConnType"
        Return $null
    }
    $jsonObject = [ordered]@{
        type = "EVPL_VC"
        name = $ConnName
        bandwidth = $Mbps
        aSide = [ordered]@{
            accessPoint = [ordered]@{
                type = "COLO"
                port = [ordered]@{
                    uuid = $ConnPortUUID
                }
                linkProtocol = [ordered]@{
                    type = "QINQ"
                    vlanCTag = $ConnVlanTag
                    vlanSTag = $ConnVlanTag
                }
            }
        }
        zSide = [ordered]@{
            accessPoint = [ordered]@{
                type = "SP"
                profile = [ordered]@{
                    uuid = "a1390b22-bbe0-4e93-ad37-85beef9d254d" # Profile GUID for ExpressRoute
                }
                location = [ordered]@{
                    metroCode = $CircuitLocation # SE = Seattle, DC = Ashburn
                }
                linkProtocol = [ordered]@{
                    type = "QINQ"
                    vlanCTag = $ConnVlanTag
                }
                authenticationKey = $SKey
            }
        }
        notifications = @(
            [ordered]@{
                type = "ALL"
                emails = @($ContactEmail)
            }
        )
    }
    if ($GroupID -ne 0) {
        $jsonObject.redundancy = [ordered]@{
            group = $GroupID
            priority = "SECONDARY"
        }
    }

    # Convert the hashtable to a JSON string
    $jsonString = $jsonObject | ConvertTo-Json -Depth 5
    
    # Output the JSON string
    return $jsonString
}

# Non-configurable Variable Initialization (ie don't modify these)
$SubID = '4bffbb15-d414-4874-a2e4-c548c6d45e2a'    # ExpressRoute-lab Subscription ID
$tenantID = '72f988bf-86f1-41af-91ab-2d7cd011db47' # Microsoft Tenant ID
$EastRegion = "eastus"
$WestRegion = "westus2"

# Login and permissions check
Write-Host (Get-Date)' - ' -NoNewline
Write-Host "Checking login and permissions" -ForegroundColor Cyan
Try {Get-AzResourceGroup -Name "LabInfrastructure" -ErrorAction Stop | Out-Null
     $Sub = (Set-AzContext -Subscription $subID -Tenant $tenantID -ErrorAction Stop).Subscription
     Write-Host "  Current Sub:",$Sub.Name,"(",$Sub.Id,")"}
Catch {# Login and set subscription for ARM
        Write-Host "  Logging in to ARM"
        Try {$Sub = (Set-AzContext -Subscription $subID -Tenant $tenantID -ErrorAction Stop).Subscription}
        Catch {Connect-AzAccount | Out-Null
               $Sub = (Set-AzContext -Subscription $subID -Tenant $tenantID -ErrorAction Stop).Subscription}
        Write-Host "    Current Sub:",$Sub.Name,"(",$Sub.Id,")"
        Try {Get-AzResourceGroup -Name "LabInfrastructure" -ErrorAction Stop | Out-Null}
        Catch {Write-Warning "  Permission check failed, ensure customer id is set correctly!"
               Return}
}

# 1. Get ECX Token
#If ($null -eq $global:UserName) {$global:UserName = Read-host "Enter your ECX Username"}
#If ($null -eq $global:Password -or $global:Password -eq "") {$global:Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($(Read-host "Enter your ECX password:" -AsSecureString)))} 
#If ($global:Password -eq "") {Write-Warning "An ECX Password is required to continue";Return}

# Get Keys from Key Vault
$kvName = "LabSecrets"
$kvClientID = Get-AzKeyVaultSecret -VaultName $kvName -Name "ECXClientID" -AsPlainText
$kvClientSecret = Get-AzKeyVaultSecret -VaultName $kvName -Name "ECXSecret" -AsPlainText

# Get REST OAuth Token
$TokenURI = "https://api.equinix.com/oauth2/v1/token"
$TokenBody = "{" + 
             "  ""grant_type"": ""client_credentials""," +
             "  ""client_id"": ""$kvClientID""," +
             "  ""client_secret"": ""$kvClientSecret""" +
             "}"
Try {$token = Invoke-RestMethod -Method Post -Uri $TokenURI -Body $TokenBody -ContentType application/json}
Catch {Write-Warning "An error occured getting an OAuth certificate from Equinix."
       Write-Host
       Write-Host $error[0].ErrorDetails.Message
       Return}
$ConnHeader =  @{"Authorization" = "Bearer $($token.access_token)"}

# 2. Load ER circuit
$Circuits = Get-AzExpressRouteCircuit -ResourceGroupName $RGName #-Name "$RGName-ER"

If ($Deprovision) {
    # 3. Deprovision Circuit(s)
    # 3.1 Set json body to search for provisioned, lab-related connections
    $searchBody = @'
    {"filter": {
    "and": [
      {"property": "/name",
       "operator": "LIKE",
       "values": ["SEA-Cust", "ASH-Cust"]
      },
      {"property": "/operation/equinixStatus",
       "operator": "=",
       "values": ["PROVISIONED"]
      },
      {"property": "/direction",
       "operator": "=",
       "values": ["OUTGOING"]
      }]},
   "pagination": {
     "limit": 100,
     "offset": 0,
     "total": 0
      },
   "sort": [
     {"property": "/name",
      "direction": "DESC"
     }]
}
'@
    # 3.2 Pull ECX Connections into Connections array
    $ConnURI = "https://api.equinix.com/fabric/v4/connections/search"
    Try {$connections = Invoke-RestMethod -Method Post -Uri $ConnURI -Headers $ConnHeader -Body $searchBody -ContentType application/json -ErrorAction Stop
        Write-Host "Pulled (up to) top 300 provisioned ECX Connections"}
    Catch {Write-Warning "An error occured pulling ECX Connections from Equinix."
        Write-Host
        Write-Host $error[0].ErrorDetails.Message
        Return }
    
    # 3.3 Loop through connections and deprovision
    $ConnURI = "https://api.equinix.com/fabric/v4/connections"
    ForEach ($Circuit in $Circuits | Sort-Object Name ) {
        If ($Circuit.CircuitProvisioningState -eq "Enabled" -and $Circuit.ServiceProviderProvisioningState -eq "Provisioned") {
            $UUIDs = $connections.data | Where-Object { $_.zSide.accessPoint.authenticationKey -eq $Circuit.ServiceKey } | Select-Object uuid, name

            ForEach ($UUID in $UUIDs) {
                $ConnUUID = $UUID.uuid
                $connection = Invoke-RestMethod -Method Delete -Uri $ConnURI"/$ConnUUID" -Headers $ConnHeader
                If ($connection.operation.equinixStatus -eq "DEPROVISIONING") {
                    Write-Host (Get-Date)' - ' -NoNewline
                    Write-Host $UUID.Name -NoNewline -ForegroundColor Yellow
                    Write-Host " has been deprovisioned"}
                Else {Write-Warning "$($Circuit.Name) deprovisioing has failed on $($UUID.Name)"}
                }
        }
        elseif ($Circuit.ServiceProviderProvisioningState -eq "NotProvisioned") {
            Write-Host (Get-Date)' - ' -NoNewline
            Write-Host $Circuit.Name -NoNewline -ForegroundColor Yellow
            Write-Host " is already deprovisioned"
        }
        else {
            Write-Host (Get-Date)' - ' -NoNewline
            Write-Host $Circuit.Name -NoNewline -ForegroundColor Red
            Write-Host " is in an unknown state"
        }
    }
}
Else {
    # 4. Provision Circuit(s) if needed
    # 4.1 Load all Circuits in RG into an array
    ForEach ($Circuit in $Circuits | Sort-Object Name ) {
        If ($Circuit.CircuitProvisioningState -eq "Enabled" -and $Circuit.ServiceProviderProvisioningState -eq "NotProvisioned") {
            # 4.1.1 If enabled and not provisioned, provision the circuit
            If ($Circuit.Location -eq $EastRegion) { # East Circuit connected to Ashburn
                $Lab = "ASH"
                $ConnMetro = "DC"
            }
            ElseIf ($Circuit.Location -eq $WestRegion) { # West Circuit connected to Seattle
                $Lab = "SEA"
                $ConnMetro = "SE"
            }
            else {
                # Spit an error message and exit loop
                Write-Warning "Invalid Circuit Location"
                Continue
            }
            $ConnSTag = $RGName.Substring($RGName.Length - 2,2) + "0"
            $SKey = $Circuit.ServiceKey
            $Mbps = $Circuit.ServiceProviderProperties.BandwidthInMbps
            $ConnNamePri = $Circuit.Name + "-pri"
            $ConnNameSec = $Circuit.Name + "-sec"

            # Build the Call For the Primary Circuit
            $StatusCodePri = $null
            $StatusCodeSec = $null
            $ConnURI = "https://api.equinix.com/fabric/v4/connections"
            $ConnBody = Build-ConnBody -ConnName $ConnNamePri -ConnVlanTag $ConnSTag `
                                       -LabLocation $Lab -CircuitLocation $ConnMetro `
                                       -SKey $SKey -Mbps $Mbps
            #[System.Windows.MessageBox]::Show($ConnBody)
            $connection = Invoke-RestMethod -Method Post -Uri $ConnURI -Headers $ConnHeader -Body $ConnBody -ContentType application/json -StatusCodeVariable $StatusCodePri
            If ($connection.operation.equinixStatus -eq "PROVISIONING") {
                $StatusCodePri = 202
                Write-Host (Get-Date)' - ' -NoNewline
                Write-Host $Circuit.Name'Primary' -NoNewline -ForegroundColor Yellow
                Write-Host " has been submitted for provisioning"
                $GroupID = $connection.redundancy.group
                $ConnBody = Build-ConnBody -ConnName $ConnNameSec -ConnVlanTag $ConnSTag `
                                       -LabLocation $Lab -CircuitLocation $ConnMetro `
                                       -SKey $SKey -Mbps $Mbps -GroupID $GroupID
                $connection = Invoke-RestMethod -Method Post -Uri $ConnURI -Headers $ConnHeader -Body $ConnBody -ContentType application/json -StatusCodeVariable $StatusCodeSec
                If ($connection.operation.equinixStatus -eq "PROVISIONING") {
                    $StatusCodeSec = 202
                    Write-Host (Get-Date)' - ' -NoNewline
                    Write-Host $Circuit.Name'Secondary' -NoNewline -ForegroundColor Yellow
                    Write-Host " has been submitted for provisioning"
                }
            }
            Else {
                Write-Warning $Circuit.Name "provisioning has failed. Pri Leg Code: $StatusCodePri, Sec Leg Code: $StatusCodeSec"}
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
}