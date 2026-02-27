# Quick Setup Guide

## Development Credentials Setup

### Step 1: Update appsettings.Development.json

Open `appsettings.Development.json` and replace these values with your Entra ID app registration:

- **TenantId**: Your Azure tenant ID (Directory ID)
- **ClientId**: Your app registration Application (client) ID
- **ClientSecret**: Your app registration client secret

### Step 2: Run the Application

bash
dotnet run


The app will automatically use `appsettings.Development.json` when running locally.

---

## Production Deployment

### Option 1: Environment Variables (Recommended)

Set these environment variables in your production environment:

bash
AzureAd__TenantId=your-prod-tenant-id
AzureAd__ClientId=your-prod-client-id
AzureAd__ClientSecret=your-prod-client-secret


### Option 2: Azure Key Vault

Store secrets in Azure Key Vault and reference them in your app.

### Option 3: Update appsettings.json

Update the placeholders in `appsettings.json` (not recommended for secrets).

---

## Security Notes

- ✅ `appsettings.Development.json` is in `.gitignore` - your credentials won't be committed
- ✅ `appsettings.json` should only have placeholders
- ✅ Use environment variables or Key Vault for production secrets
- ⚠️ Never commit `appsettings.Development.json` to source control
