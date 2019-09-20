function Remove-LabECX{
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
    # 5. Load all ECX connections into an array
    # 6. Loop through Circuits array searching connection array
    # 7. End Nicely
    #

    # 1. Initialize
    # Script Variables
    $StartTime = Get-Date
    $DeprovisionCount = 0
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
    Write-Host "Success" -ForegroundColor Green

    # 4. Load all ER circuits into an array
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Pulling ER Circuit(s)" -ForegroundColor Cyan
    $Circuits = Get-AzExpressRouteCircuit -ResourceGroupName $RGName | Where-Object Name -Like "$TenantStub*"

    # 5. Load all ECX connections into an array
    $ConnURI = "https://api.equinix.com/ecx/v3/l2/buyer/connections?pageSize=300&status=PROVISIONED"
    Try {$connections = Invoke-RestMethod -Method Get -Uri $ConnURI -Headers $ConnHeader -ErrorAction Stop
        Write-Host "  Pulled (up to) top 300 provisioned ECX Connections"}
    Catch {Write-Warning "An error occured pulling ECX Connections from Equinix."
        Write-Host
        Write-Host $error[0].ErrorDetails.Message
        Return }
    
    # 6. Loop through Circuits array searching connection array
    $ConnURI = "https://api.equinix.com/ecx/v3/l2/connections"
    ForEach ($Circuit in $Circuits) {
        # If provisioned, search for ECX Connection in ECX array
        If ($Circuit.CircuitProvisioningState -eq "Enabled" -and $Circuit.ServiceProviderProvisioningState -eq "Provisioned") { 
            $UUIDs = $connections.content | Where-Object authorizationKey -eq $Circuit.ServiceKey | Sort-Object Name | Select-Object uuid, name
    
            # Loop through connections and deprovision matching s-key
            ForEach ($UUID in $UUIDs) {
                $ConnUUID = $UUID.uuid
                $connection = Invoke-RestMethod -Method Delete -Uri $ConnURI"/$ConnUUID" -Headers $ConnHeader
                If ($connection.message -eq "deleted connection successfully") {
                    Write-Host (Get-Date)' - ' -NoNewline
                    Write-Host $UUID.Name -NoNewline -ForegroundColor Yellow
                    Write-Host " has been deprovisioned"
                    $DeprovisionCount++}
                Else {Write-Warning $Circuit.Name "deprovisioing has failed on "$UUID.Name}
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
            Write-Host " is in an unknown state"
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
