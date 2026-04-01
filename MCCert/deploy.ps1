# ============================================================================
# Multi-VNet Infrastructure Deployment Script
# Azure China Cloud Shell (China North 3)
# ============================================================================

param(
    [Parameter(Mandatory = $false)]
    [string]$ResourceGroupName = "VPN-jonor",

    [Parameter(Mandatory = $false)]
    [string]$Location = "chinanorth3",

    [Parameter(Mandatory = $false)]
    [string]$TemplateFile = "./main.bicep",

    [Parameter(Mandatory = $false)]
    [string]$ParametersFile = "./main.bicepparam",

    [Parameter(Mandatory = $false)]
    [object]$DeployVMs = $null,  # Auto-detect if not specified

    [Parameter(Mandatory = $false)]
    [object]$DeployGateway = $null  # Auto-detect if not specified
)

# ============================================================================
# Functions
# ============================================================================

function Write-Banner {
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " Multi-VNet Infrastructure Deployment" -ForegroundColor Cyan
    Write-Host " Azure China - China North 3" -ForegroundColor Cyan
    Write-Host ""
    Write-Host " Usage: ./deploy.ps1 [optional: -DeployVMs `$true|`$false]" -ForegroundColor White
    Write-Host " Auto-detects phase: Infrastructure → VMs → Updates" -ForegroundColor White  
    Write-Host " Phase 1: Infrastructure (VNet, VPN Gateway, Key Vault)" -ForegroundColor White
    Write-Host " Phase 2: Add VMs and complete deployment" -ForegroundColor White
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

# Validate DeployVMs parameter if provided
if ($null -ne $DeployVMs -and $DeployVMs -notin @($true, $false, 0, 1, "true", "false")) {
    Write-Host "ERROR: DeployVMs parameter must be `$true, `$false, 0, 1, 'true', or 'false'" -ForegroundColor Red
    exit 1
}
if ($DeployVMs -in @(0, "false")) { $DeployVMs = $false }
if ($DeployVMs -in @(1, "true")) { $DeployVMs = $true }

# Auto-detect deployment phase if not explicitly specified
if ($null -eq $DeployVMs) {
    Write-Host "Auto-detecting deployment phase..." -ForegroundColor Yellow
    
    # Check if Key Vault exists (indicates Phase 1 completed)
    try {
        $keyVaults = Get-AzKeyVault -ResourceGroupName $ResourceGroupName -ErrorAction SilentlyContinue
        $existingKeyVault = $keyVaults | Where-Object { $_.VaultName -like "jonor-*" } | Select-Object -First 1
        
        if ($existingKeyVault) {
            # Key Vault exists - check if VMs exist
            try {
                $existingVMs = Get-AzVM -ResourceGroupName $ResourceGroupName -ErrorAction SilentlyContinue
                if ($existingVMs -and $existingVMs.Count -gt 0) {
                    $DeployVMs = $true
                    Write-Host "🔄 Detected: Key Vault and VMs exist - Running Phase 2 (update/maintenance)" -ForegroundColor Cyan
                } else {
                    $DeployVMs = $true
                    Write-Host "📋 Detected: Key Vault exists, no VMs - Running Phase 2 (deploy VMs)" -ForegroundColor Cyan
                }
            } catch {
                $DeployVMs = $true
                Write-Host "📋 Detected: Key Vault exists - Running Phase 2 (deploy VMs)" -ForegroundColor Cyan
            }
        } else {
            $DeployVMs = $false
            Write-Host "🏗️  Detected: No Key Vault - Running Phase 1 (infrastructure)" -ForegroundColor Cyan
        }
    } catch {
        $DeployVMs = $false
        Write-Host "🏗️  Cannot detect existing resources - Running Phase 1 (infrastructure)" -ForegroundColor Cyan
    }
} else {
    if ($DeployVMs) {
        Write-Host "🎯 Manual override: Running Phase 2 (deploy VMs)" -ForegroundColor Cyan
    } else {
        Write-Host "🎯 Manual override: Running Phase 1 (infrastructure only)" -ForegroundColor Cyan
    }
}
Write-Host ""

# Prompt for required parameters
Write-Host "Enter deployment parameters:" -ForegroundColor Yellow
Write-Host ""

# Check if Key Vault exists and has secrets
$keyVaultName = ""
$hasExistingSecrets = $false

# Check for existing Key Vault and secrets regardless of phase
try {
    $keyVaults = Get-AzKeyVault -ResourceGroupName $ResourceGroupName -ErrorAction SilentlyContinue
    $keyVault = $keyVaults | Where-Object { $_.VaultName -like "jonor-*" } | Select-Object -First 1
    
    if ($keyVault) {
        $keyVaultName = $keyVault.VaultName
        Write-Host "Found existing Key Vault: $keyVaultName" -ForegroundColor Green
        
        # Check if secrets exist and are not placeholders
        try {
            $adminSecret = Get-AzKeyVaultSecret -VaultName $keyVaultName -Name "vm-admin-password" -AsPlainText -ErrorAction SilentlyContinue
            $vpnSecret = Get-AzKeyVaultSecret -VaultName $keyVaultName -Name "vpn-shared-key" -AsPlainText -ErrorAction SilentlyContinue
            
            if ($adminSecret -and $vpnSecret -and 
                $adminSecret -ne "PLACEHOLDER-UPDATE-THIS-PASSWORD" -and 
                $vpnSecret -ne "PLACEHOLDER-UPDATE-THIS-SHARED-KEY") {
                
                Write-Host "✅ Found existing real secrets in Key Vault - using stored values" -ForegroundColor Green
                $adminPassword = ConvertTo-SecureString $adminSecret -AsPlainText -Force
                $vpnSharedKey = ConvertTo-SecureString $vpnSecret -AsPlainText -Force
                $hasExistingSecrets = $true
            } else {
                Write-Host "⚠️  Key Vault contains placeholder secrets - will prompt for real values" -ForegroundColor Yellow
            }
        } catch {
            Write-Host "⚠️  Could not access Key Vault secrets - will prompt for new values" -ForegroundColor Yellow
        }
    } else {
        Write-Host "ℹ️  No Key Vault found - will create new with provided secrets" -ForegroundColor Cyan
    }
} catch {
    Write-Host "⚠️  Could not check for existing Key Vault - will prompt for new values" -ForegroundColor Yellow
}

# Only prompt for passwords if we don't have them from Key Vault
if (-not $hasExistingSecrets) {
    Write-Host "Enter new secrets (will be stored in Key Vault):" -ForegroundColor Yellow
    
    # Admin Password
    $adminPassword = Read-Host -Prompt "Enter admin password for VMs" -AsSecureString

    # VPN Shared Key
    $vpnSharedKey = Read-Host -Prompt "Enter S2S VPN Pre-Shared Key" -AsSecureString
}

# Test if Log Analytics workspace exists and set parameter accordingly
Write-Host ""
Write-Host "Checking for existing Log Analytics workspace..." -ForegroundColor Yellow
$workspaceName = "jonorLogs"

try {
    $null = Get-AzOperationalInsightsWorkspace -ResourceGroupName $ResourceGroupName -Name $workspaceName -ErrorAction Stop
    $createLogWorkspace = $false
    Write-Host "✅ Log Analytics workspace '$workspaceName' exists - will reference existing" -ForegroundColor Green
} catch {
    $createLogWorkspace = $true
    Write-Host "⚠️  Log Analytics workspace '$workspaceName' not found - will create new" -ForegroundColor Yellow
}

# Auto-detect deployGateway if not specified
if ($null -eq $DeployGateway) {
    try {
        $existingGw = Get-AzVirtualNetworkGateway -ResourceGroupName $ResourceGroupName -Name "VNet01-gw-vpn" -ErrorAction SilentlyContinue
        if ($existingGw) {
            $DeployGateway = $false
            Write-Host "✅ VPN Gateway already exists - skipping gateway deployment (faster)" -ForegroundColor Green
        } else {
            $DeployGateway = $true
            Write-Host "⚠️  VPN Gateway not found - will deploy gateway" -ForegroundColor Yellow
        }
    } catch {
        $DeployGateway = $true
        Write-Host "⚠️  Could not check for VPN Gateway - will deploy gateway" -ForegroundColor Yellow
    }
} else {
    if ($DeployGateway -in @(0, "false")) { $DeployGateway = $false }
    if ($DeployGateway -in @(1, "true")) { $DeployGateway = $true }
    Write-Host "🎯 Manual override: Deploy Gateway = $DeployGateway" -ForegroundColor Cyan
}

# Confirm deployment
Write-Host ""
Write-Host "Ready to deploy with the following settings:" -ForegroundColor Cyan
Write-Host "  Resource Group: $ResourceGroupName" -ForegroundColor White
Write-Host "  Location: $Location" -ForegroundColor White
Write-Host "  Create Log Workspace: $createLogWorkspace" -ForegroundColor White
Write-Host "  Deploy Gateway: $DeployGateway" -ForegroundColor White
Write-Host "  Deploy VMs: $DeployVMs" -ForegroundColor White
Write-Host "  Secrets Source: $(if ($hasExistingSecrets) { "Key Vault (auto-detected)" } else { "User input (prompted)" })" -ForegroundColor White
if ($DeployVMs) {
    Write-Host "  Phase: 2 (Full deployment with VMs)" -ForegroundColor Cyan
} else {
    Write-Host "  Phase: 1 (Infrastructure only)" -ForegroundColor Cyan
}
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
if ($DeployVMs) {
    if ($DeployGateway) {
        Write-Host "This may take 30-45 minutes due to VPN Gateway and VM provisioning." -ForegroundColor Yellow
    } else {
        Write-Host "This should be relatively quick (gateway skipped)." -ForegroundColor Yellow
    }
} else {
    if ($DeployGateway) {
        Write-Host "This may take 20-30 minutes due to VPN Gateway provisioning." -ForegroundColor Yellow
    } else {
        Write-Host "This should be relatively quick (gateway skipped)." -ForegroundColor Yellow
    }
}
Write-Host ""

$deploymentName = "MCCert-Deployment-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

try {
    $deployment = New-AzResourceGroupDeployment `
        -Name $deploymentName `
        -ResourceGroupName $ResourceGroupName `
        -TemplateFile $TemplateFile `
        -adminPassword $adminPassword `
        -vpnSharedKey $vpnSharedKey `
        -createLogWorkspace $createLogWorkspace `
        -deployGateway $DeployGateway `
        -deployVMs $DeployVMs `
        -Verbose

    if ($deployment.ProvisioningState -eq "Succeeded") {
        Write-Host ""
        Write-Host "============================================================" -ForegroundColor Green
        Write-Host " Deployment Successful!" -ForegroundColor Green
        Write-Host "============================================================" -ForegroundColor Green
        Write-Host ""
        Write-Host "Outputs:" -ForegroundColor Cyan
        
        # Phase 1 outputs (Infrastructure)
        Write-Host "  VPN Gateway Public IP: $($deployment.Outputs.vnet01VpnGatewayPublicIp.Value)" -ForegroundColor White
        Write-Host "  Key Vault Name: $($deployment.Outputs.keyVaultName.Value)" -ForegroundColor White
        Write-Host "  Key Vault URI: $($deployment.Outputs.keyVaultUri.Value)" -ForegroundColor White
        Write-Host "  P2S Client Pool: $($deployment.Outputs.p2sClientPool.Value)" -ForegroundColor White
        Write-Host "  P2S NAT Pool: $($deployment.Outputs.p2sNatPoolOutput.Value)" -ForegroundColor White
        
        # Phase 2 additional outputs (VMs)
        if ($DeployVMs) {
            Write-Host "  Linux Router Public IP: $($deployment.Outputs.vnet03RouterPublicIp.Value)" -ForegroundColor White
            Write-Host "  Bastion Host: $($deployment.Outputs.vnet02BastionName.Value)" -ForegroundColor White
        }
        
        Write-Host ""
        Write-Host "Next Steps:" -ForegroundColor Yellow
        
        if ($DeployVMs) {
            # Phase 2 - Full deployment complete
            Write-Host "  1. Verify strongSwan VPN tunnel is established:" -ForegroundColor White
            Write-Host "     SSH to VNet03-router01 and run: sudo ipsec statusall" -ForegroundColor Gray
            Write-Host "  2. Test connectivity between VNets:" -ForegroundColor White
            Write-Host "     From VNet03-router01: ping 10.1.0.4 (VNet01 VM)" -ForegroundColor Gray
            Write-Host "  3. Download P2S VPN client from Azure Portal (VNet01-gw-vpn)" -ForegroundColor White
            Write-Host "  4. Access Windows VM via Bastion (VNet02-bastion)" -ForegroundColor White
        } else {
            # Phase 1 - Infrastructure only
            Write-Host "  1. Run Phase 2 deployment to create VMs with automated strongSwan:" -ForegroundColor White
            Write-Host "     .\deploy.ps1 -DeployVMs $true" -ForegroundColor Gray
        }
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
