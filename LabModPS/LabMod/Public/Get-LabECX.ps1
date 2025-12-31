function Get-LabECX {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory=$false, HelpMessage='Enter Connection UUID to retrieve specific connection')]
        [string]$ConnectionUUID="3ee2848c-80b4-4f00-808e-69cb091e4480",
        [Parameter(Mandatory=$false, HelpMessage='Enter Connection Name to search for')]
        [string]$ConnectionName,
        [Parameter(Mandatory=$false, HelpMessage='Output full JSON response')]
        [switch]$FullJson
    )

    <#
    .SYNOPSIS
        Retrieves Equinix Fabric Connection details from the Equinix API
    
    .DESCRIPTION
        This script retrieves connection information from the Equinix Fabric API.
        Use this to examine the structure of working connections and compare with what you're sending.
    
    .EXAMPLE
        Get-LabECX
        Lists all connections in your account
    
    .EXAMPLE
        Get-LabECX -ConnectionUUID "12345678-1234-1234-1234-123456789012"
        Retrieves a specific connection by UUID
    
    .EXAMPLE
        Get-LabECX -ConnectionName "ASH-Cust10-ER1-pri" -FullJson
        Searches for a connection by name and displays the full JSON response
    #>

    # 1. Get ECX Token
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Getting ECX OAuth Token" -ForegroundColor Cyan
    Write-Host "  Grabbing ECX secrets..." -NoNewline
    
    $kvName = "LabSecrets"
    Try {
        $ECXClientID = Get-AzKeyVaultSecret -VaultName $kvName -Name "ECXClientID" -AsPlainText -ErrorAction Stop
        $ECXSecret = Get-AzKeyVaultSecret -VaultName $kvName -Name "ECXSecret" -AsPlainText -ErrorAction Stop
        Write-Host "Success" -ForegroundColor Green
    }
    Catch {
        Write-Warning "Failed to retrieve secrets from Key Vault. Ensure you're logged in to Azure and have access to the $kvName Key Vault."
        Return
    }

    # Get REST OAuth Token
    Write-Host "  Getting OAuth Token...." -NoNewline
    $TokenURI = "https://api.equinix.com/oauth2/v1/token"
    $TokenBody = @{
        grant_type = "client_credentials"
        client_id = $ECXClientID
        client_secret = $ECXSecret
    } | ConvertTo-Json

    Try {
        $token = Invoke-RestMethod -Method Post -Uri $TokenURI -Body $TokenBody -ContentType application/json
        $ConnHeader = @{"Authorization" = "Bearer $($token.access_token)"}
        Write-Host "Success" -ForegroundColor Green
    }
    Catch {
        Write-Warning "An error occurred getting an OAuth token from Equinix."
        Write-Host $error[0].ErrorDetails.Message
        Return
    }

    # 2. Retrieve Connection(s)
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Retrieving ECX Connection(s)" -ForegroundColor Cyan

    If ($ConnectionUUID) {
        # Get specific connection by UUID
        $ConnURI = "https://api.equinix.com/fabric/v4/connections/$ConnectionUUID"
        Try {
            $connection = Invoke-RestMethod -Method Get -Uri $ConnURI -Headers $ConnHeader
            Write-Host "  Retrieved connection:" -NoNewline
            Write-Host " $($connection.name)" -ForegroundColor Yellow
        }
        Catch {
            Write-Warning "Failed to retrieve connection with UUID: $ConnectionUUID"
            Write-Host $error[0].ErrorDetails.Message
            Return
        }
    }
    Else {
        # List all connections
        $ConnURI = "https://api.equinix.com/fabric/v4/connections"
        Try {
            $response = Invoke-RestMethod -Method Get -Uri $ConnURI -Headers $ConnHeader
            
            If ($ConnectionName) {
                # Filter by connection name
                $connection = $response.data | Where-Object { $_.name -like "*$ConnectionName*" }
                If ($connection) {
                    Write-Host "  Found $($connection.Count) matching connection(s)" -ForegroundColor Green
                }
                Else {
                    Write-Warning "No connections found matching: $ConnectionName"
                    Write-Host "`nAvailable connections:"
                    $response.data | ForEach-Object { Write-Host "  - $($_.name) (UUID: $($_.uuid))" }
                    Return
                }
            }
            Else {
                # Display all connections
                Write-Host "  Retrieved $($response.pagination.total) total connection(s)" -ForegroundColor Green
                $connection = $response.data
            }
        }
        Catch {
            Write-Warning "Failed to retrieve connections list"
            Write-Host $error[0].ErrorDetails.Message
            Return
        }
    }

    # 3. Display Results
    Write-Host "`n" -NoNewline
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Connection Details" -ForegroundColor Cyan
    Write-Host ("=" * 80)

    If ($FullJson) {
        # Output full JSON for comparison
        Write-Host "`nFull JSON Response:" -ForegroundColor Yellow
        Write-Host ("=" * 80)
        $connection | ConvertTo-Json -Depth 10
    }
    Else {
        # Display formatted connection details
        ForEach ($conn in $connection) {
            Write-Host "`nConnection Name: " -NoNewline
            Write-Host $conn.name -ForegroundColor Yellow
            Write-Host "UUID:            $($conn.uuid)"
            Write-Host "Type:            $($conn.type)"
            Write-Host "State:           " -NoNewline
            Switch ($conn.state) {
                "ACTIVE" { Write-Host $conn.state -ForegroundColor Green }
                "PROVISIONING" { Write-Host $conn.state -ForegroundColor Yellow }
                "PROVISIONED" { Write-Host $conn.state -ForegroundColor Green }
                "DEPROVISIONING" { Write-Host $conn.state -ForegroundColor Yellow }
                "FAILED" { Write-Host $conn.state -ForegroundColor Red }
                Default { Write-Host $conn.state }
            }
            Write-Host "Bandwidth:       $($conn.bandwidth) Mbps"
            Write-Host "Operation Status: $($conn.operation.equinixStatus)"
            
            Write-Host "`nA-Side (Source):"
            Write-Host "  Type:          $($conn.aSide.accessPoint.type)"
            Write-Host "  Port UUID:     $($conn.aSide.accessPoint.port.uuid)"
            Write-Host "  Link Protocol: $($conn.aSide.accessPoint.linkProtocol.type)"
            Write-Host "  VLAN S-Tag:    $($conn.aSide.accessPoint.linkProtocol.vlanSTag)"
            Write-Host "  VLAN C-Tag:    $($conn.aSide.accessPoint.linkProtocol.vlanCTag)"
            
            Write-Host "`nZ-Side (Destination):"
            Write-Host "  Type:          $($conn.zSide.accessPoint.type)"
            Write-Host "  Profile UUID:  $($conn.zSide.accessPoint.profile.uuid)"
            Write-Host "  Metro Code:    $($conn.zSide.accessPoint.location.metroCode)"
            Write-Host "  Link Protocol: $($conn.zSide.accessPoint.linkProtocol.type)"
            Write-Host "  VLAN C-Tag:    $($conn.zSide.accessPoint.linkProtocol.vlanCTag)"
            
            If ($conn.redundancy) {
                Write-Host "`nRedundancy:"
                Write-Host "  Group:         $($conn.redundancy.group)"
                Write-Host "  Priority:      $($conn.redundancy.priority)"
            }
            
            Write-Host ("-" * 80)
        }

        # Offer to display full JSON
        Write-Host "`nTo see the complete JSON structure, run with the -FullJson switch" -ForegroundColor Cyan
    }

    Write-Host "`n"
}
