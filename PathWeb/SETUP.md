# Entra ID Configuration Guide

## Setup Steps

### 1. Register Application in Azure Portal

1. Navigate to [Azure Portal](https://portal.azure.com)
2. Go to Microsoft Entra ID > App registrations
3. Click 'New registration'
4. Enter application name: PathWeb
5. Select 'Accounts in this organizational directory only'
6. Click Register

### 2. Configure Authentication

1. In the app registration, go to Authentication
2. Click 'Add a platform' > Web
3. Add Redirect URI: https://localhost:5001/signin-oidc (adjust port as needed)
4. Add Logout URL: https://localhost:5001/signout-callback-oidc
5. Check 'ID tokens' under Implicit grant and hybrid flows
6. Click Configure

### 3. Create Client Secret

1. Go to Certificates & secrets
2. Click 'New client secret'
3. Add description and set expiration
4. Copy the secret value immediately

### 4. Update appsettings.json

Replace the placeholders with your values:

- TenantId: Found in app registration Overview
- ClientId: Application (client) ID from Overview
- ClientSecret: The secret value you copied

### 5. Run the Application

bash
dotnet run


Navigate to https://localhost:<port> and test authentication.
