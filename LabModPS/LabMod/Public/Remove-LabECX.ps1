function Remove-LabECX{
    [CmdletBinding()]
    param (
        [Parameter(Mandatory=$true, ValueFromPipeline=$true, HelpMessage='Enter Tenant ID')]
        [ValidateRange(10,99)]
        [int]$TenantID,
        [Parameter(Mandatory=$true, HelpMessage='Enter ER Location')]
        [ValidateSet('Ashburn','Seattle')]
        [string]$Location,
        [ValidateSet('ExpressRoute-Lab','Pathfinder')]
        [string]$Subscription='ExpressRoute-Lab')

    # Equinix Provisioning Script for the AFD Lab
    #
    # To ensure you are in the right subscrition run
    # Get-AzContext
    #
    # 1. Get ECX Token
    # 2. Load all ER circuits into an array
    # 3. Loop through array
    # 3.1 If enabled and not provisioned, provision the circuit
    #
    # Re-run this script as often as needed during the
    # class to get the ExpressRoute circuits provisioned.
    #

    # Az Module Test
    $ModCheck = Get-Module Az.Network -ListAvailable
    If ($Null -eq $ModCheck) {
        Write-Warning "The Az PowerShell module was not found. This script uses the Az modules for PowerShell"
        Write-Warning "See the blob post for more information at: https://azure.microsoft.com/blog/how-to-migrate-from-azurerm-to-az-in-azure-powershell/"
        Return
        }

    # Non-configurable Variable Initialization (ie don't modify these)
    Switch ($Subscription) {
        'ExpressRoute-Lab' {$SubID = '4bffbb15-d414-4874-a2e4-c548c6d45e2a'}
        'Pathfinder' {$SubID = '79573dd5-f6ea-4fdc-a3aa-d05586980843'}
    }
    If ($Location -eq "Seattle") {$TenantStub = "SEA-Cust$TenantID-ER"}
    Else {$TenantStub = "ASH-Cust$TenantID-ER"}

    # Login and permissions check
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Checking login and permissions" -ForegroundColor Cyan
    Try {Get-AzResourceGroup -Name "Utilities" -ErrorAction Stop | Out-Null}
    Catch {# Login and set subscription for ARM
            Write-Host "Logging in to ARM"
            Try {$Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop).Subscription}
            Catch {Connect-AzAccount | Out-Null
                    $Sub = (Set-AzContext -Subscription $subID -ErrorAction Stop).Subscription}
            Write-Host "Current Sub:",$Sub.Name,"(",$Sub.Id,")"
            Try {Get-AzResourceGroup -Name "LabInfrastructure" -ErrorAction Stop | Out-Null}
            Catch {Write-Warning "Permission check failed, ensure tenant id is set correctly!"
                    Return}
    }

    # 1. Get ECX Token
    $kvName = "LabSecrets"
    $kvECXClientID = "ECXClientID"
    $kvECXSecret = "ECXSecret"
    Write-Host "  Grabbing ECX secrets"
    $ECXClientID = Get-AzKeyVaultSecret -VaultName $kvName -Name $kvECXClientID
    $ECXSecret = Get-AzKeyVaultSecret -VaultName $kvName -Name $kvECXSecret -ErrorAction Stop

    # Get REST OAuth Token
    $TokenURI = "https://api.equinix.com/oauth2/v1/token"
    $TokenBody = "{" + 
                "  ""grant_type"": ""client_credentials""," +
                "  ""client_id"": ""$ECXClientID""," +
                "  ""client_secret"": ""$ECXSecret""" +
                "}"
    Try {$token = Invoke-RestMethod -Method Post -Uri $TokenURI -Body $TokenBody -ContentType application/json}
    Catch {Write-Warning "An error occured getting an OAuth certificate from Equinix."
        Write-Host
        Write-Host $error[0].ErrorDetails.Message
        Return }
    $ConnHeader =  @{"Authorization" = "Bearer $($token.access_token)"}

    # 2. Load all ER circuits into an array
    $Circuits = Get-AzExpressRouteCircuit | Where-Object Name -Like "$TenantStub*"

    # 3. Loop through array
    # Deprovision Circuit(s)
    # Load all ECX connections into an array
    $ConnURI = "https://api.equinix.com/ecx/v3/l2/buyer/connections?pageSize=300&status=PROVISIONED"
    Try {$connections = Invoke-RestMethod -Method Get -Uri $ConnURI -Headers $ConnHeader -ErrorAction Stop
        Write-Host "Pulled (up to) top 300 provisioned ECX Connections"}
    Catch {Write-Warning "An error occured pulling ECX Connections from Equinix."
        Write-Host
        Write-Host $error[0].ErrorDetails.Message
        Return }
    
    # Loop Circuits and delete
    $ConnURI = "https://api.equinix.com/ecx/v3/l2/connections"
    ForEach ($Circuit in $Circuits | Sort-Object ResourceGroupName ) {
        If ($Circuit.CircuitProvisioningState -eq "Enabled" -and $Circuit.ServiceProviderProvisioningState -eq "Provisioned") {
            # 4.1 If provisioned, search for ECX Connection in ECX array
            $UUIDs = $connections.content | Where-Object authorizationKey -eq $Circuit.ServiceKey | Sort-Object Name | Select-Object uuid, name
    
            # 4.2 Loop through any connections and deprovision
            ForEach ($UUID in $UUIDs) {
                $ConnUUID = $UUID.uuid
                $connection = Invoke-RestMethod -Method Delete -Uri $ConnURI"/$ConnUUID" -Headers $ConnHeader
                If ($connection.message -eq "deleted connection successfully") {
                    Write-Host (Get-Date)' - ' -NoNewline
                    Write-Host $UUID.Name -NoNewline -ForegroundColor Yellow
                    Write-Host " has been deprovisioned"}
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
}
