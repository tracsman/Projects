# ============================================================================
# Multi-VNet Infrastructure Deployment Script
# Azure China Cloud Shell (China North 3)
# ============================================================================

param(
    [Parameter(Mandatory = $false)]
    [string]$ResourceGroupName = "MCCert-RG",

    [Parameter(Mandatory = $false)]
    [string]$Location = "chinanorth3",

    [Parameter(Mandatory = $false)]
    [string]$TemplateFile = "./main.bicep",

    [Parameter(Mandatory = $false)]
    [string]$ParametersFile = "./main.bicepparam"
)

# ============================================================================
# Functions
# ============================================================================

function Write-Banner {
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " Multi-VNet Infrastructure Deployment" -ForegroundColor Cyan
    Write-Host " Azure China - China North 3" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host ""
}

function Test-AzureConnection {
    Write-Host "Checking Azure connection..." -ForegroundColor Yellow
    try {
        $context = Get-AzContext
        if (-not $context) {
            Write-Host "Not connected to Azure. Please run Connect-AzAccount first." -ForegroundColor Red
            exit 1
        }
        Write-Host "Connected to subscription: $($context.Subscription.Name)" -ForegroundColor Green
        Write-Host "Environment: $($context.Environment.Name)" -ForegroundColor Green
        return $context
    }
    catch {
        Write-Host "Error checking Azure connection: $_" -ForegroundColor Red
        exit 1
    }
}

# ============================================================================
# Main Script
# ============================================================================

Write-Banner

# Check Azure connection
$context = Test-AzureConnection

# Verify we're in Azure China
if ($context.Environment.Name -notlike "*China*") {
    Write-Host ""
    Write-Host "WARNING: You are not connected to Azure China cloud!" -ForegroundColor Yellow
    Write-Host "Current environment: $($context.Environment.Name)" -ForegroundColor Yellow
    Write-Host ""
    $continue = Read-Host "Do you want to continue anyway? (y/n)"
    if ($continue -ne 'y') {
        Write-Host "Exiting. To connect to Azure China, run:" -ForegroundColor Cyan
        Write-Host "  Connect-AzAccount -Environment AzureChinaCloud" -ForegroundColor White
        exit 0
    }
}

Write-Host ""
Write-Host "Deployment Configuration:" -ForegroundColor Cyan
Write-Host "  Resource Group: $ResourceGroupName" -ForegroundColor White
Write-Host "  Location: $Location" -ForegroundColor White
Write-Host "  Template: $TemplateFile" -ForegroundColor White
Write-Host ""

# Prompt for required parameters
Write-Host "Enter deployment parameters:" -ForegroundColor Yellow
Write-Host ""

# Admin Password
$adminPassword = Read-Host -Prompt "Enter admin password for VMs" -AsSecureString

# Entra ID Tenant ID
$aadTenantId = Read-Host -Prompt "Enter Entra ID Tenant ID (GUID)"
while (-not ($aadTenantId -match '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$')) {
    Write-Host "Invalid GUID format. Please enter a valid Tenant ID." -ForegroundColor Red
    $aadTenantId = Read-Host -Prompt "Enter Entra ID Tenant ID (GUID)"
}

# VPN Shared Key
$vpnSharedKey = Read-Host -Prompt "Enter S2S VPN Pre-Shared Key" -AsSecureString

# Confirm deployment
Write-Host ""
Write-Host "Ready to deploy with the following settings:" -ForegroundColor Cyan
Write-Host "  Resource Group: $ResourceGroupName" -ForegroundColor White
Write-Host "  Location: $Location" -ForegroundColor White
Write-Host "  Tenant ID: $aadTenantId" -ForegroundColor White
Write-Host ""

$confirm = Read-Host "Proceed with deployment? (y/n)"
if ($confirm -ne 'y') {
    Write-Host "Deployment cancelled." -ForegroundColor Yellow
    exit 0
}

# ============================================================================
# Create Resource Group
# ============================================================================

Write-Host ""
Write-Host "Creating resource group '$ResourceGroupName'..." -ForegroundColor Yellow

try {
    $rg = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction SilentlyContinue
    if (-not $rg) {
        $rg = New-AzResourceGroup -Name $ResourceGroupName -Location $Location
        Write-Host "Resource group created successfully." -ForegroundColor Green
    }
    else {
        Write-Host "Resource group already exists." -ForegroundColor Green
    }
}
catch {
    Write-Host "Error creating resource group: $_" -ForegroundColor Red
    exit 1
}

# ============================================================================
# Deploy Bicep Template
# ============================================================================

Write-Host ""
Write-Host "Starting Bicep deployment..." -ForegroundColor Yellow
Write-Host "This may take 30-45 minutes due to VPN Gateway provisioning." -ForegroundColor Yellow
Write-Host ""

$deploymentName = "MCCert-Deployment-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

try {
    # Convert secure strings to plain text for deployment (required by New-AzResourceGroupDeployment)
    $adminPasswordPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($adminPassword)
    )
    $vpnSharedKeyPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($vpnSharedKey)
    )

    $deployment = New-AzResourceGroupDeployment `
        -Name $deploymentName `
        -ResourceGroupName $ResourceGroupName `
        -TemplateFile $TemplateFile `
        -adminPassword $adminPasswordPlain `
        -aadTenantId $aadTenantId `
        -vpnSharedKey $vpnSharedKeyPlain `
        -Verbose

    # Clear sensitive variables
    $adminPasswordPlain = $null
    $vpnSharedKeyPlain = $null

    if ($deployment.ProvisioningState -eq "Succeeded") {
        Write-Host ""
        Write-Host "============================================================" -ForegroundColor Green
        Write-Host " Deployment Successful!" -ForegroundColor Green
        Write-Host "============================================================" -ForegroundColor Green
        Write-Host ""
        Write-Host "Outputs:" -ForegroundColor Cyan
        Write-Host "  VPN Gateway Public IP: $($deployment.Outputs.vnet01VpnGatewayPublicIp.Value)" -ForegroundColor White
        Write-Host "  Cisco CSR Public IP: $($deployment.Outputs.vnet03CiscoPublicIp.Value)" -ForegroundColor White
        Write-Host "  Bastion Host: $($deployment.Outputs.vnet02BastionName.Value)" -ForegroundColor White
        Write-Host "  P2S Client Pool: $($deployment.Outputs.p2sClientPool.Value)" -ForegroundColor White
        Write-Host "  P2S NAT Pool: $($deployment.Outputs.p2sNatPoolOutput.Value)" -ForegroundColor White
        Write-Host ""
        Write-Host "Next Steps:" -ForegroundColor Yellow
        Write-Host "  1. Download VPN client from Azure Portal (VNet01-gw-vpn)" -ForegroundColor White
        Write-Host "  2. Configure Cisco CSR with S2S VPN settings (use the PSK you provided)" -ForegroundColor White
        Write-Host "  3. Access Windows VM via Bastion (VNet02-bastion)" -ForegroundColor White
        Write-Host ""
    }
    else {
        Write-Host ""
        Write-Host "Deployment completed with state: $($deployment.ProvisioningState)" -ForegroundColor Yellow
    }
}
catch {
    Write-Host ""
    Write-Host "Deployment failed: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "To view detailed error information, run:" -ForegroundColor Yellow
    Write-Host "  Get-AzResourceGroupDeploymentOperation -ResourceGroupName $ResourceGroupName -DeploymentName $deploymentName" -ForegroundColor White
    exit 1
}
