using './main.bicep'

// ============================================================================
// Parameter Values for Multi-VNet Infrastructure Deployment
// Azure China (China North 3)
// ============================================================================

// Location - China North 3
param location = 'chinanorth3'

// Admin credentials
param adminUsername = 'azureuser'
param adminPassword = '' // Enter at deployment time

// Entra ID (Azure AD) Configuration for P2S VPN
// Replace with your actual Tenant ID from Azure China
param aadTenantId = '' // Enter your Entra ID Tenant ID (GUID format)

// Azure VPN Enterprise Application Client ID
// This may differ in Azure China - verify in your tenant
param aadAudience = 'c632b3df-fb67-4d84-bdcf-b95ad541b5c8'

// S2S VPN Pre-Shared Key
param vpnSharedKey = '' // Enter at deployment time
