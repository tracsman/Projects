[CmdletBinding()]
param (
    [Parameter(Mandatory=$true, ValueFromPipeline=$true, HelpMessage='Enter Resource Group Name (e.g. SEA-Cust10)')]
    [string]$RGName)

# 1. Initialize
# 2. Create Resource group
# 3. Create Key Vault, Secret, and set permissions
# 4. Push Bicep deployment to Azure
# 5. Wait for circuits to be created
# 6. Provision Circutis at Equinix
# 6.1 Proceed only if all circuits are provisioned
# 7. Create ER Connections
# 8. End nicely

# 1. Initialize
$StartTime = Get-Date
$ShortRegion = "westus2"

# 2. Create Resource Group
Write-Host (Get-Date)' - ' -NoNewline
Write-Host "Creating $RGName" -ForegroundColor Cyan
Try {Get-AzResourceGroup -Name $RGName -ErrorAction Stop | Out-Null
        Write-Host "  resource group exists, skipping"}
Catch {New-AzResourceGroup -Name $RGName -Location $ShortRegion | Out-Null}

# 3. Create Key Vault, Secret, and set permissions
Write-Host (Get-Date)' - ' -NoNewline
Write-Host "Creating Key Vault" -ForegroundColor Cyan
$kvName = $RGName + '-kv'
$kv = Get-AzKeyVault -VaultName $kvName -ResourceGroupName $RGName
If ($null -eq $kv) {$kv = New-AzKeyVault -VaultName $kvName -ResourceGroupName $RGName -Location $ShortRegion
                    Start-Sleep -Seconds 10}
Else {Write-Host "  Key Vault exists, skipping"}

# Set Key Vault permissions
Write-Host (Get-Date)' - ' -NoNewline
Write-Host "Assigning Key Vault Role for Jonor" -ForegroundColor Cyan
$roleAssignment = Get-AzRoleAssignment -ObjectId "910c2e97-2d42-47b2-8c1d-29d2f9eb5ef8" -RoleDefinitionName "Key Vault Secrets Officer" -ResourceGroupName $RGName
If ($null -eq $roleAssignment) {
    New-AzRoleAssignment -ObjectId "910c2e97-2d42-47b2-8c1d-29d2f9eb5ef8" -RoleDefinitionName "Key Vault Secrets Officer" -ResourceGroupName $RGName | Out-Null
} Else {
    Write-Host "  Role assignment exists, skipping"
}

# Add Secret
$UserName = "PathLabUser"
$RegEx='^(?=\P{Ll}*\p{Ll})(?=\P{Lu}*\p{Lu})(?=\P{N}*\p{N})(?=[\p{L}\p{N}]*[^\p{L}\p{N}])[\s\S]{12,}$'
Do {$UserPass = ([char[]](Get-Random -Input $(40..43 + 46..59 + 63..91 + 95..122) -Count 20)) -join ""}
While ($UserPass -cnotmatch $RegEx)
$UserSecPass = ConvertTo-SecureString $UserPass -AsPlainText -Force

$kvs = Get-AzKeyVaultSecret -VaultName $kvName -Name $UserName -ErrorAction Stop 
If ($null -eq $kvs) {
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Adding $UserName to $kvName" -ForegroundColor Cyan
    $kvs = Set-AzKeyVaultSecret -VaultName $kvName -Name $UserName -SecretValue $UserSecPass -ErrorAction Stop
}
Else {Write-Host "  $UserName exists, skipping"}

# 4. Push Bicep deployment to Azure
Write-Host (Get-Date)' - ' -NoNewline
Write-Host "Deploying Bicep" -ForegroundColor Cyan
$DepList = Get-AzResourceGroupDeployment -ResourceGroupName $RGName -ErrorAction SilentlyContinue
$templateFile = "C:\Bin\Git\Projects\LabTemplate\Deploy-Lab.bicep"
$deploymentRoot = "PathLabBase"
$deploymentName = $deploymentRoot + "-" + ($DepList.Count + 1).ToString("00")
New-AzResourceGroupDeployment -ResourceGroupName $RGName -TemplateFile $templateFile -Name $deploymentName -adminPwd $UserSecPass -asJob -ErrorAction Stop | Out-Null

# 5. Wait for circuits to be created
# Given circuit creation takes some time, sleep for 4 minutes then wait for them to be enabled
$circuits = Get-AzExpressRouteCircuit -ResourceGroupName $RGName -ErrorAction Stop
if ($circuits.Count -eq 0) {
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Waiting for circuits to be created, this may take some time to complete (up to 3 minutes for creation and 10 minutes for completion)." -ForegroundColor Cyan
    start-sleep -Seconds 180
}
$LoopCount = 0
do {
    $OkToProceed = $true
    $circuits = Get-AzExpressRouteCircuit -ResourceGroupName $RGName -ErrorAction Stop
    foreach ($circuit in $circuits) {
        if ($circuit.ProvisioningState -ne 'Succeeded') {
            $OkToProceed = $false
        }
    }
    if (-not $OkToProceed) {
        Write-Host "  Still waiting (another 30 seconds) for circuit creation to complete."
        Start-Sleep -Seconds 30
    }
    $LoopCount++
} until ($OkToProceed -or $LoopCount -ge 25)
if (-not $OkToProceed) {
    Write-Host (Get-Date)
    Write-Warning "Circuits are still not ready, job timing out, please check the Azure portal and try again."
    Return
}

# 6. Provision Circutis at Equinix
.\ECX-Action.ps1 -RGName $RGName

# 6.1 Proceed only if all circuits are provisioned
$LoopCount = 0
do {
    $OkToProceed = $true
    $circuits = Get-AzExpressRouteCircuit -ResourceGroupName $RGName -ErrorAction Stop
    foreach ($circuit in $circuits) {
        if ($circuit.ServiceProviderProvisioningState -ne 'Provisioned') {
            Write-Host $circuit.Name' is still not provisioned.'
            $OkToProceed = $false
        }
    }
    if (-not $OkToProceed) {
        Write-Host "Waiting for Equinix to provision circuits, this may take some time to complete."
        Start-Sleep -Seconds 15
    }
    $LoopCount++
} until ($OkToProceed -or $LoopCount -ge 40)
if ($LoopCount -ge 40) {
    Write-Warning "Equinix circuits are still not provisioned, job timing out, please check the Equinix portal and try again."
    Return
}

# 7. Create ER Connection
# Check if connection already exist, if not, create it
Write-Host (Get-Date)' - ' -NoNewline
Write-Host "Creating ExpressRoute Connections" -ForegroundColor Cyan
try {Get-AzVirtualNetworkGatewayConnection -ResourceGroupName $RGName -Name $RGName'-VNet01-gw-er-conn' -ErrorAction Stop | Out-Null
        Write-Host "  Connection exists, skipping"}
catch {$vnet = Get-AzVirtualNetworkGateway -ResourceGroupName $RGName -Name $RGName'-VNet01-gw-er' -ErrorAction Stop
       $ckt = Get-AzExpressRouteCircuit -ResourceGroupName $RGName -Name $RGName'-ER' -ErrorAction Stop
       New-AzVirtualNetworkGatewayConnection -ResourceGroupName $RGName -Name $RGName'-VNet01-gw-er-conn' -Location $ShortRegion  -VirtualNetworkGateway1 $vnet -PeerId $ckt.Id -ConnectionType ExpressRoute -ErrorAction Stop | Out-Null
       Write-Host "  Connection created"} 

# 8. End nicely
$EndTime = Get-Date
$TimeDiff = New-TimeSpan $StartTime $EndTime
$Mins = $TimeDiff.Minutes
$Secs = $TimeDiff.Seconds
$RunTime = '{0:00}:{1:00} (M:S)' -f $Mins,$Secs
Write-Host (Get-Date)' - ' -NoNewline
Write-Host "Environment $RGName completed successfully" -ForegroundColor Green
Write-Host "Time to create: $RunTime"
Write-Host