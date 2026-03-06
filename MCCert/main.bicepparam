using './main.bicep'

// ============================================================================
// Parameter Values for Multi-VNet Infrastructure Deployment
// Azure China (China North 3)
// ============================================================================

// Location - China North 3
param location = 'chinanorth3'

// Admin credentials
param adminUsername = 'azureuser'

// Key Vault Configuration
// Key Vault name will be auto-generated with unique suffix
// param keyVaultName = 'kv-vpngw-custom' // Optional: uncomment to use custom name
param adminPasswordSecretName = 'vm-admin-password'
param vpnSharedKeySecretName = 'vpn-shared-key'

// DEPLOYMENT PHASE CONTROL
// Phase 1: Set to false - deploys networks, Key Vault, VPN Gateway only
// Phase 2: Set to true - deploys VMs after secrets are added to Key Vault
param deployVMs = false

// Entra ID (Azure AD) Configuration for P2S VPN
// Replace with your actual Tenant ID from Azure China
param aadTenantId = '44810f93-2edf-4f4e-9300-b0807a76f21b' // Enter your Entra ID Tenant ID (GUID format)

// Azure VPN Enterprise Application Client ID
// This may differ in Azure China - verify in your tenant
param aadAudience = 'c632b3df-fb67-4d84-bdcf-b95ad541b5c8'

// ============================================================================
// DEPLOYMENT INSTRUCTIONS:
//
// PHASE 1 - Initial Deployment (deployVMs = false):
// 1. Deploy with deployVMs = false
// 2. This creates VNets, Key Vault, VPN Gateway (takes ~45 minutes)
//
// PHASE 2 - Add Secrets:
// 3. Add required secrets to Key Vault:
//    az keyvault secret set --vault-name <key-vault-name> --name vm-admin-password --value <password>
//    az keyvault secret set --vault-name <key-vault-name> --name vpn-shared-key --value <psk>
//
// PHASE 3 - VM Deployment (deployVMs = true):
// 4. Change deployVMs = true and redeploy
// 5. VMs will be created and automatically configured
// ============================================================================
