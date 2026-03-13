using Microsoft.EntityFrameworkCore;
using PathWeb.Data;
using PathWeb.Models;

namespace PathWeb.Services;

public class ConfigGenerator
{
    private readonly LabConfigContext _context;
    private readonly ILogger<ConfigGenerator> _logger;
    private string _strDelete = "";
    private string _userEmail = "";
    private const string StrSubID = "4bffbb15-d414-4874-a2e4-c548c6d45e2a";

    public ConfigGenerator(LabConfigContext context, ILogger<ConfigGenerator> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <param name="Asn">BGP ASN for the on-premises lab (e.g. 65020)</param>
    /// <param name="Octet">Second octet / IPv6 hextet indicating lab location (SEA=1, ASH=2)</param>
    /// <param name="VpnIP">Public IP advertised from the lab for VPN tunnels</param>
    /// <param name="INetNAT">Public IP advertised from the lab for static NAT (VM remote access)</param>
    /// <param name="IPv6Prefix">Provider-assigned /48 prefix used for lab VMs</param>
    private record LabConstants(string Asn, string Octet, string VpnIP, string INetNAT, string IPv6Prefix);

    private static LabConstants GetLabConstants(string lab) => lab switch
    {
        "SEA" => new("65020", "1", "63.243.229.124", "63.243.229.125", "2001:5a0:4406:"),
        "ASH" => new("65021", "2", "66.198.12.124", "66.198.12.125", "2001:5a0:3c06:"),
        _ => throw new ArgumentException($"Invalid lab value: {lab}")
    };

    private static string ResolveVpnEndPoint(Tenant tenant)
    {
        if (tenant.VpnendPoint != null)
        {
            if (tenant.VpnendPoint == "TBD,N/A" || tenant.VpnendPoint == "Active-Passive")
                return tenant.Vpnconfig == "Active-Active" ? "TBD,TBD" : "TBD,N/A";
            return tenant.VpnendPoint;
        }
        return tenant.Vpnconfig == "Active-Active" ? "TBD,TBD" : "TBD,N/A";
    }

        public async Task<List<string>> GenerateConfigAsync(Guid id, string userEmail)
        {
            // 1. Initialize variables
            _userEmail = userEmail;
            _strDelete = "";
            bool IsError = false;
            List<string> strMessages = new List<string> { };
            
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Pulling Tenant from SQL");
            Tenant tenant = await _context.Tenants.FindAsync(id) ?? throw new InvalidOperationException($"Tenant {id} not found");
            tenant.TenantVersion = (short)(tenant.TenantVersion + 1);

            // Assign or release public IPs for Microsoft peering before generating config
            string p2pResult = await AssignPublicIp(tenant, true);
            if (p2pResult == "No Available P2P")
            {
                strMessages.Add("[AssignPublicIP] No available P2P ranges");
                return strMessages;
            }
            string natResult = await AssignPublicIp(tenant, false);
            if (natResult == "No Available NAT")
            {
                strMessages.Add("[AssignPublicIP] No available NAT ranges");
                return strMessages;
            }

            // 2. Generate Config
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Calling GenerateSPConfig()");
            if (!await GenerateSpConfig(tenant))
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Generate SP Config Failed");
                strMessages.Add("[GenerateSPConfig] Generate SP Config Failed");
                IsError = true;
            }

            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Calling GenerateERConfig()");
            if (!await GenerateErConfig(tenant))
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Generate ExpressRoute Config Failed");
                strMessages.Add("[GenerateERConfig] Generate ExpressRoute Config Failed");
                IsError = true;
            }

            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Calling GenerateAzureConfig()");
            if (!await GenerateAzureConfig(tenant))
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Generate Azure PowerShell Failed");
                strMessages.Add("[GenerateAzureConfig] Generate Azure PowerShell Failed");
                IsError = true;
            }

            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Calling GenerateFirewallConfig()");
            if (!await GenerateFirewallConfig(tenant))
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Generate Firewall Config Failed");
                strMessages.Add("[GenerateFirewallConfig] Generate Firewall Config Failed");
                IsError = true;
            }

            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Calling GenerateRouterConfig(Primary)");
            if (!await GenerateRouterConfig(tenant, true))
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Generate Primary Router Config Failed");
                strMessages.Add("[GenerateRouterConfig] Generate Primary Router Config Failed");
                IsError = true;
            }

            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Calling GenerateRouterConfig(Secondary)");
            if (!await GenerateRouterConfig(tenant, false))
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Generate Secondary Router Config Failed");
                strMessages.Add("[GenerateRouterConfig] Generate Secondary Router Config Failed");
                IsError = true;
            }

            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Calling GenerateSwitchConfig(Primary)");
            if (!await GenerateSwitchConfig(tenant, true))
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Generate Primary Switch Config Failed");
                strMessages.Add("[GenerateSwitchConfig] Generate Primary Switch Config Failed");
                IsError = true;
            }

            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Calling GenerateSwitchConfig(Secondary)");
            if (!await GenerateSwitchConfig(tenant, false))
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Generate Secondary Switch Config Failed");
                strMessages.Add("[GenerateSwitchConfig] Generate Secondary Switch Config Failed");
                IsError = true;
            }

            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Calling GenerateLabVMConfig()");
            if (!await GenerateLabVmConfig(tenant))
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Generate Lab VM PowerShell Failed");
                strMessages.Add("[GenerateLabVMConfig] Generate Lab VM PowerShell Failed");
                IsError = true;
            }

            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Calling GenerateEMailConfig()");
            if (!await GenerateEmailConfig(tenant))
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Generate Email Failed");
                strMessages.Add("[GenerateEMailConfig] Generate Email Failed");
                IsError = true;
            }

            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Calling SaveToSql(BackoutConfig)");
            if (!await SaveToSql("BackoutConfig", tenant,_strDelete))
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Generate Backout Config Failed");
                strMessages.Add("[GenerateConfig] Generate Backout Config Failed");
                IsError = true;
            }

            if (IsError)
            {
                await SaveToSql("RollbackConfig", tenant, "None", true);
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "All generated config rolled back (removed) from SQL");
                strMessages.Add("[GenerateConfig] All generated config rolled back (removed) from SQL");
            }
            else
            {
                // Save updated Tenant Version
                
                await _context.SaveChangesAsync();
                strMessages.Clear();
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "All configuration generated and saved to SQL");
                strMessages.Add("All configuration generated and saved to SQL!");
            }

            // 3. Return Messages
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateConfig", "Complete");
            return strMessages;
        }

        private async Task<bool> GenerateSpConfig(Tenant tenant)
        {
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateSPConfig", "Starting");
            string strDB;

            if (tenant.Ersku == "None")
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateSPConfig", "No ExpressRoute requested, no Service Provider actions required");
                strDB = "No ExpressRoute requested, no Service Provider actions required\r\n\r\n";
            }
            else if (tenant.EruplinkPort == "ECX")
            {
                string labCity = tenant.Lab == "SEA" ? "Seattle" : "Ashburn";
                strDB = "# ECX Provisioning Information\r\n" +
                        "# Run this script on any machine with the LabMod module installed.\r\n" +
                        "#\r\n\r\n" +
                        $"New-LabECX {tenant.TenantId} {labCity}\r\n";
                var strBackout = "# Run this PowerShell script on any lab physical server.\r\n" +
                              $"Remove-LabECX {tenant.TenantId} {labCity}\r\n\r\n";
               _strDelete += "#######\r\n" +
                              "### Deprovision at ECX\r\n" +
                              "#######\r\n" +
                              strBackout + "\r\n";
                await SaveToSql("ServiceProviderInstructions-out", tenant, strBackout);
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateSPConfig", "ECX ExpressRoute requested, Provision and Deprovision instructions set");
            }
            else
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateSPConfig", "ExpressRoute Direct requested, no Service Provider actions required");
                strDB = "ExpressRoute Direct requested, no Service Provider actions required.\r\n\r\n";
            }
            bool results = await SaveToSql("ServiceProviderInstructions", tenant, strDB);
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateSPConfig", "SP data saved to SQL");
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateSPConfig", "Complete");
            return results;
        }

        private async Task<bool> GenerateErConfig(Tenant tenant)
        {
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateERConfig", "Starting");
            string strDB;
            bool HasER;
            bool IsERDirect;
            HasER = tenant.Ersku != "None";
            IsERDirect = tenant.EruplinkPort != "ECX";

            if (HasER)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateERConfig", "ER Requested, defining variables");
                string strRGName = $"{tenant.Lab}-Cust{tenant.TenantId}";
                string strLocation = (tenant.Lab == "ASH") ? "Washington DC" : "Seattle";
                string strCircuitRegion = (tenant.Lab == "ASH") ? "East US" : "West US 2";
                string strProvider = (tenant.EruplinkPort == "ECX") ? "Equinix" : "";

                // Set Variables
                strDB = "# Initialize\r\n" +
                         $"$RGName = '{strRGName}'\r\n" +
                         $"$Region = '{tenant.AzureRegion}'\r\n" +
                         (!IsERDirect ? $"$ERProvider = '{strProvider}'\r\n" : "") +
                         $"$ERLocation = '{strLocation}'\r\n" +
                         $"$ERRegion = '{strCircuitRegion}'\r\n" +
                         $"$ERSku = '{tenant.Ersku}'\r\n" +
                         $"$ERBandwidth = {(IsERDirect ? tenant.Erspeed / 1000 : tenant.Erspeed)}\r\n" +
                         $"$RGTagExpireDate = '{tenant.ReturnDate.ToString("MM/dd/yy")}'\r\n" +
                         $"$RGTagContact = '{tenant.Contacts}'\r\n" +
                         $"$RGTagNinja = '{tenant.NinjaOwner}'\r\n" +
                         $"$RGTagUsage = '{tenant.Usage.Substring(0, Math.Min(tenant.Usage.Length, 253))}'\r\n\r\n";

                // Login Check
                strDB += "# Login Check\r\n" +
                         "Try {Write-Host 'Using Subscription: ' -NoNewline\r\n" +
                         "     Write-Host $((Get-AzContext).Name) -ForegroundColor Green}\r\n" +
                         "Catch {\r\n" +
                         "    Write-Warning 'You are not logged in dummy. Login and try again!'\r\n" +
                         "    Return}\r\n\r\n";

                // Check/Create Resource Group
                strDB += "# Create Resource Group\r\n" +
                         "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "Write-Host 'Creating Resource Group' -ForegroundColor Cyan\r\n" +
                         "Try {$rg = Get-AzResourceGroup -Name $RGName -ErrorAction Stop\r\n" +
                         "     Write-Host 'Resource exists, skipping'}\r\n" +
                         "Catch {$rg = New-AzResourceGroup -Name $RGName -Location \"$Region\"}\r\n\r\n" +
                         "# Add Tag Values to the Resource Group\r\n" +
                         "Set-AzResourceGroup -Name $RGName -Tag @{Expires=$RGTagExpireDate; Contacts=$RGTagContact; Pathfinder=$RGTagNinja; Usage=$RGTagUsage} | Out-Null\r\n\r\n";

                // Create ER Circuit
                strDB += "# Create ExpressRoute Circuit\r\n" +
                         "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "Write-Host 'Creating ExpressRoute Circuit' -ForegroundColor Cyan\r\n";

                // If Direct, load Direct Port
                if (IsERDirect && tenant.Lab == "SEA")
                {
                    if (tenant.EruplinkPort == "100G Direct Cisco MSEE")
                    {
                        strDB += "$ERDirect = Get-AzExpressRoutePort -Name SEA-100Gb-Port-01 -ResourceGroupName LabInfrastructure # wst-09xgmr-cis-1/2\r\n";
                    }
                    else if (tenant.EruplinkPort == "100G Direct Juniper MSEE")
                    {
                        strDB += "$ERDirect = Get-AzExpressRoutePort -Name SEA-100Gb-Port-02 -ResourceGroupName LabInfrastructure # exr01/2.wst\r\n";
                    }
                    else
                    {
                        strDB += "$ERDirect = \"\"\r\n";
                    }
                }
                else if (IsERDirect && tenant.Lab == "ASH")
                {
                    if (tenant.EruplinkPort == "10G Direct Juniper MSEE")
                    {
                        strDB += "$ERDirect = Get-AzExpressRoutePort -Name ASH-10Gb-PortPair-01 -ResourceGroupName LabInfrastructure # exr03/4.ash\r\n";
                    }
                    else
                    {
                        strDB += "$ERDirect = \"\"\r\n";
                    }
                    
                }
                else if (IsERDirect)
                {
                    strDB += "$ERDirect = \"\"\r\n";
                }

                strDB += "Try {$er = Get-AzExpressRouteCircuit -ResourceGroupName $RGName -Name $RGName-ER -ErrorAction Stop\r\n" +
                         "     Write-Host 'Resource exists, skipping'}\r\n";
                if (IsERDirect)
                {
                    strDB += "Catch {$er = New-AzExpressRouteCircuit -BandwidthinGbps $ERBandwidth -Location $ERDirect.Location " +
                             "-Name $RGName-ER -ResourceGroupName $RGName -ExpressRoutePort $ERDirect " +
                             "-SkuFamily MeteredData -SkuTier $ERSku -ErrorAction Stop}\r\n\r\n";
                }
                else
                {
                    strDB += "Catch {$er = New-AzExpressRouteCircuit -BandwidthInMbps $ERBandwidth -Location $ERRegion " +
                             "-Name $RGName-ER -ResourceGroupName $RGName -ServiceProviderName $ERProvider " +
                             "-PeeringLocation $ERLocation -SkuFamily MeteredData -SkuTier $ERSku -ErrorAction Stop}\r\n\r\n";
                }

                // Get New SKey and copy to clipboard
                strDB += "# Copy Service Key to clipboard\r\n" +
                         "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "Write-Host 'Copying Service Key to Clipboard' -ForegroundColor Cyan\r\n" +
                         "$er.ServiceKey | Set-Clipboard\r\n" +
                         "Write-Host 'The Service Key is now in the clipboard, please paste into the xls asap.' -ForegroundColor Green\r\n\r\n";
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateERConfig", "PowerShell strings created");
            }
            else
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateERConfig", "ExpressRoute not requested, no PowerShell required");
                strDB = "# ExpressRoute not requested, no PowerShell required\r\n";
            }
            bool results = await SaveToSql("CreateERPowerShell", tenant, strDB);
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateERConfig", "ER PowerShell saved to SQL");
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateERConfig", "Complete");
            return results;
        }

        private async Task<bool> GenerateAzureConfig(Tenant tenant)
        {
            // 1. Initialize variables
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Starting");
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Defining variables");
            
            string strDB;
            string strRGName = $"{tenant.Lab}-Cust{tenant.TenantId}";
            List<string> lstRawContacts = tenant.Contacts?.Split(',').ToList() ?? [];
            List<string> lstContacts = new List<string> { };
            foreach (string contact in lstRawContacts) { lstContacts.Add($"'{contact.Trim()}'"); }
            bool HasER = tenant.Ersku != "None";
            bool HasPrivate = tenant.PvtPeering == true;
            bool HasMicrosoft = tenant.Msftpeering == true;
            bool HasERGateway = tenant.ErgatewaySize != "None";
            bool HasERFastPath = tenant.ErfastPath == true;
            bool HasVPNGateway = tenant.Vpngateway != "None";
            bool HasVPNAA = tenant.Vpnconfig == "Active-Active";
            bool HasIPv6 = tenant.AddressFamily == "IPv6" || tenant.AddressFamily == "Dual";
            bool HasERDirect = tenant.EruplinkPort != "ECX"; ;

            var lab = GetLabConstants(tenant.Lab);
            string strASN = lab.Asn;
            string strLabOctet = lab.Octet;
            string strLabVpnIP = lab.VpnIP;

            // For IPv4: First Octet = 10. (RFC1918), Second Octet = Azure Region, Third Octet = Tenant Number
            // eg "10.11.12" note, no forth octet, this will be assigned at time of use
            // For IPv6: First Hextet = fd: (RFC4193), Second Hextet is the network layer, Third Hextet = Tenant Number and subnet function
            // eg "fd:1:2:31FF"; fd=private, 1=Azure to Lab, 2=Ashburn lab, 31FF=Tenant number 31, FF indicates a P2P prefix remaining hextets are for the host segment
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Pulling Azure Region data from SQL");
            Region region = await _context.Regions.FirstOrDefaultAsync(r => r.Region1 == tenant.AzureRegion) ?? throw new InvalidOperationException($"Region '{tenant.AzureRegion}' not found");
            string strVNetPrefix = $"10.{region.Ipv4}.{tenant.TenantId}";
            string strVNet6Prefix = $"fd:0:{region.Ipv6}:{tenant.TenantId}";
            string strP2P6Prefix = $"fd:1:{strLabOctet}:{tenant.TenantId}FF::";

            string[] azVms = [tenant.AzVm1 ?? "None", tenant.AzVm2 ?? "None", tenant.AzVm3 ?? "None", tenant.AzVm4 ?? "None"];
            int intVMCount = 0;
            bool HasAzureVM = false;
            string strVMOS = "$VMOS = @()\r\n";
            foreach (string vm in azVms)
            {
                if (vm != "None")
                {
                    HasAzureVM = true;
                    intVMCount += 1;
                    strVMOS += $"$VMOS += '{vm}'\r\n";
                }
            }

            // 2. Generate PowerShell script
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Creating Azure PowerShell");
            // Set Variables
            strDB = "# Initialize\r\n" +
                    "$StartTime = Get-Date\r\n" +
                    $"$TenantID = '{tenant.TenantId}'\r\n" +
                    $"$RGName = '{strRGName}'\r\n" +
                    $"$Region = '{tenant.AzureRegion}'\r\n" +
                    $"$RGTagExpireDate = '{tenant.ReturnDate.ToString("MM/dd/yy")}'\r\n" +
                    $"$RGTagContact = '{tenant.Contacts}'\r\n" +
                    $"$RGTagNinja = '{tenant.NinjaOwner}'\r\n" +
                    $"$RGTagUsage = '{tenant.Usage.Substring(0, Math.Min(tenant.Usage.Length, 253))}'\r\n";

            if (HasPrivate || HasAzureVM || HasVPNGateway)
            {
                strDB += "$VNetName = $RGName + '-VNet01'\r\n" +
                         $"$VNetAddress = '{strVNetPrefix}.0/24'\r\n" +
                         $"$VNetTenant = '{strVNetPrefix}.0/25'\r\n" +
                         $"$VNetGateway = '{strVNetPrefix}.128/25'\r\n";
                if (HasIPv6)
                {
                    strDB += $"$VNet6Address = '{strVNet6Prefix}00::/56'\r\n" +
                             $"$VNet6Tenant = '{strVNet6Prefix}00::/64'\r\n" +
                             $"$VNet6Gateway = '{strVNet6Prefix}FE::/64'\r\n";
                }
            }
            if (HasPrivate || HasVPNGateway)
            {
                strDB += $"$PvtASN = '{strASN}'\r\n";
            }
            if (HasPrivate)
            {
                strDB += $"$PvtP2PA = '192.168.{tenant.TenantId}.16/30'\r\n" +
                         $"$PvtP2PB = '192.168.{tenant.TenantId}.20/30'\r\n" +
                         $"$PvtVLAN = {tenant.TenantId}0\r\n";
                if (HasIPv6)
                {
                    strDB += $"$Pvt6P2PA = '{strP2P6Prefix}/126'\r\n" +
                             $"$Pvt6P2PB = '{strP2P6Prefix}4/126'\r\n";
                }
            }
            if (HasMicrosoft)
            {
                strDB += $"$MsftTags = '{tenant.Msfttags}'\r\n" +
                         $"$MsftP2PA = '{GetP2P(tenant.Msftp2p!, true)}'\r\n" +
                         $"$MsftP2PB = '{GetP2P(tenant.Msftp2p!, false)}'\r\n" +
                         $"$MsftASN = '{strASN}'\r\n" +
                         $"$MsftVLAN = {tenant.TenantId}1\r\n" +
                         $"$MsftNAT ='{tenant.Msftadv}'\r\n";
            }
            if (HasVPNGateway)
            {
                strDB += "$LabSharedKey = Get-AzKeyVaultSecret -VaultName LabSecrets -Name AzureVPNSecret -AsPlainText\r\n" +
                         $"$LabPIP = '{strLabVpnIP}'\r\n" +
                         $"$LabBGPIP = '192.168.{tenant.TenantId}.88'\r\n";
            }
            if (HasAzureVM)
            {
                strDB += "$VMPrefix = $RGName + '-VM'\r\n" +
                         "$VMSize = 'Standard_B2s_v2'\r\n" +
                         "$VMPostDeploy = 'ICMPv4'\r\n" +
                         strVMOS;
            }

            strDB += "$username = 'PathLabUser'\r\n" +
                     "$RegEx='^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d)(?=.*[^a-zA-Z\\d]).{12,}$'\r\n" +
                     "Do {$password = ([char[]](Get-Random -Input $(@(33,35) + 45..46 + 48..57 + @(61) + 65..90 + @(95) + 97..122 + @(126)) -Count 20)) -join \"\"}\r\n" +
                     "While ($password -cnotmatch $RegEx)\r\n\r\n" +
                     "$securePassword = ConvertTo-SecureString $password -AsPlainText -Force\r\n" +
                     "$KeyVaultAccessList = " + String.Join(",", lstContacts) + "\r\n\r\n";

            // Login Check
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script for Login Check");
            strDB += "# Login Check\r\n" +
                     "Try {Write-Host 'Using Subscription: ' -NoNewline\r\n" +
                     "     Write-Host $((Get-AzContext).Name) -ForegroundColor Green}\r\n" +
                     "Catch {\r\n" +
                     "    Write-Warning 'You are not logged in dummy. Login and try again!'\r\n" +
                     "    Return}\r\n\r\n";

            // Check/Create Resource Group
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script to Create Resource Group");
            strDB += $"# Create Resource Group {strRGName}\r\n" +
                     "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                     "Write-Host \"Creating Resource Group $RGName\" -ForegroundColor Cyan\r\n" +
                     "Try {$rg = Get-AzResourceGroup -Name $RGName -ErrorAction Stop\r\n" +
                     "     Write-Host '  resource exists, skipping'}\r\n" +
                     "Catch {$rg = New-AzResourceGroup -Name $RGName -Location \"$Region\"}\r\n\r\n" +
                     "# Add Tag Values to the Resource Group\r\n" +
                     "Set-AzResourceGroup -Name $RGName -Tag @{Expires=$RGTagExpireDate; Contacts=$RGTagContact; Pathfinder=$RGTagNinja; Usage=$RGTagUsage} | Out-Null\r\n\r\n";

            // Create KeyVault
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script to Create Key Vault");
            strDB += "# Create KeyVault and Password\r\n" +
                     "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                     "Write-Host 'Creating KeyVault' -ForegroundColor Cyan\r\n" +
                     "$kvName = $RGName + '-kv'\r\n" +
                     "$kv = Get-AzKeyVault -VaultName $kvName -ResourceGroupName $RGName\r\n" +
                     "If ($kv -eq $null) {\r\n" +
                     "      Write-Host '  clearing soft-deleted vault if present'\r\n" +
                     "      Remove-AzKeyVault -VaultName $kvName -InRemovedState -Force -Location $Region -ErrorAction SilentlyContinue\r\n" +
                     "      Write-Host '  creating new Key Vault (RBAC enabled by default)'\r\n" +
                     "      $kv = New-AzKeyVault -VaultName $kvName -ResourceGroupName $RGName -Location $Region}\r\n" +
                     "Else {Write-Host '  resource Exists, Skipping'}\r\n" +
                     "$kvScope = (Get-AzKeyVault -VaultName $kvName -ResourceGroupName $RGName).ResourceId\r\n\r\n" +
                     "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                     "Write-Host 'Setting KeyVault RBAC' -ForegroundColor Cyan\r\n" +
                     "ForEach ($User in $KeyVaultAccessList.Split(';')) {\r\n" +
                     "    $User = $User.Trim()\r\n" +
                     "    Write-Host \"  Adding $User\"\r\n" +
                     "    $userId = (Get-AzADUser -UserPrincipalName $User).Id\r\n" +
                     "    New-AzRoleAssignment -ObjectId $userId -RoleDefinitionName 'Key Vault Secrets Officer' -Scope $kvScope -ErrorAction SilentlyContinue | Out-Null\r\n" +
                     "    If ((Get-AzRoleAssignment -SignInName $User -RoleDefinitionName Contributor -ResourceGroupName $RGName -ErrorAction Stop).Count -gt 0) {\r\n" +
                     "        Write-Host '    account already assigned, skipping'}\r\n" +
                     "    Else {New-AzRoleAssignment -SignInName $User -RoleDefinitionName Contributor -ResourceGroupName $RGName | Out-Null }\r\n" +
                     "}\r\n\r\n" +
                     "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                     "Write-Host 'Creating KeyVault Secret' -ForegroundColor Cyan\r\n" +
                     "$kvs = Get-AzKeyVaultSecret -VaultName $kvName -Name $username -ErrorAction SilentlyContinue\r\n" +
                     "If ($kvs -eq $null) {Try {Set-AzKeyVaultSecret -VaultName $kvName -Name $username -SecretValue $securePassword -ErrorAction Stop | Out-Null}\r\n" +
                     "                     Catch {Write-Host '  RBAC propagating, waiting 15 seconds and trying again.'\r\n" +
                     "                            Sleep -Seconds 15\r\n" +
                     "                            Set-AzKeyVaultSecret -VaultName $kvName -Name $username -SecretValue $securePassword -ErrorAction Stop | Out-Null}}\r\n" +
                     "Else {Write-Host '  resource Exists, Skipping'}\r\n" +
                     "$cred = New-Object System.Management.Automation.PSCredential ($username, $securePassword)\r\n\r\n";

            // Check/Create Virtual Network
            if (HasPrivate || HasAzureVM || HasVPNGateway)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script to Create Virtual Network");
                strDB += "# Create Virtual Network\r\n" +
                         "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "Write-Host 'Creating Virtual Network' -ForegroundColor Cyan\r\n" +
                         "Try {$VNet = Get-AzVirtualNetwork -ResourceGroupName $RGName -Name $VNetName -ErrorAction Stop\r\n" +
                         "     Write-Host '  resource exists, skipping'}\r\n" +
                         "Catch {$VNet = New-AzVirtualNetwork -ResourceGroupName $RGName -Name $VNetName -AddressPrefix $VNetAddress" + (HasIPv6 ? ",$VNet6Address" : "") + " -Location $Region\r\n" +
                         "       # Add Subnets\r\n" +
                         "       Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "       Write-Host 'Adding subnets' -ForegroundColor Cyan\r\n" +
                         "       Add-AzVirtualNetworkSubnetConfig -Name 'Tenant' -VirtualNetwork $VNet -AddressPrefix $VNetTenant" + (HasIPv6 ? ",$VNet6Tenant" : "") + " | Out-Null\r\n" +
                         "       Add-AzVirtualNetworkSubnetConfig -Name 'GatewaySubnet' -VirtualNetwork $VNet -AddressPrefix $VNetGateway" + (HasIPv6 ? ",$VNet6Gateway" : "") + " | Out-Null\r\n" +
                         "       Set-AzVirtualNetwork -VirtualNetwork $VNet | Out-Null\r\n" +
                         "       } # End Try\r\n\r\n";
            }

            // Check/Create ER Gateway
            if (HasERGateway)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script to Create ER Gateway");
                strDB += "# Create ER Gateway\r\n" +
                         "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "Write-Host 'Submitting ER Gateway creation job' -ForegroundColor Cyan\r\n" +
                         "Try {$gw = Get-AzVirtualNetworkGateway -Name $VNetName-gw-er -ResourceGroupName $RGName -ErrorAction Stop\r\n" +
                         "     Write-Host '  resource exists, skipping'}\r\n" +
                         "Catch {\r\n" +
                         "    $VNet = Get-AzVirtualNetwork -ResourceGroupName $RGName -Name $VNetName\r\n" +
                         "    $subnet = Get-AzVirtualNetworkSubnetConfig -Name GatewaySubnet -VirtualNetwork $VNet\r\n" +
                         "    Try {$pip = Get-AzPublicIpAddress -Name $VNetName-gw-er-pip -ResourceGroupName $RGName -ErrorAction Stop}\r\n" +
                         "    Catch {$pip = New-AzPublicIpAddress -Name $VNetName-gw-er-pip -ResourceGroupName $RGName -Location $Region -AllocationMethod Static -Sku Standard}\r\n" +
                         "    $ipconf = New-AzVirtualNetworkGatewayIpConfig -Name gwipconf -Subnet $subnet -PublicIpAddress $pip\r\n" +
                         "    New-AzVirtualNetworkGateway -Name $VNetName-gw-er -ResourceGroupName $RGName -Location $Region -IpConfigurations $ipconf -GatewayType Expressroute -GatewaySku " + tenant.ErgatewaySize + " -AsJob | Out-Null\r\n" +
                         "    } # End Try\r\n\r\n";
            }

            // Check/Create VPN & Local Gatways
            if (HasVPNGateway)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script to Create VPN and Local Gateway");
                strDB += "# Create VPN Gateway\r\n" +
                         "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "Write-Host 'Submitting VPN Gateway creation job' -ForegroundColor Cyan\r\n" +
                         "Try {$gw = Get-AzVirtualNetworkGateway -Name $VNetName-gw-vpn -ResourceGroupName $RGName -ErrorAction Stop\r\n" +
                         "     Write-Host '  resource exists, skipping'}\r\n" +
                         "Catch {\r\n" +
                         "    $VNet = Get-AzVirtualNetwork -ResourceGroupName $RGName -Name $VNetName\r\n" +
                         "    $subnet = Get-AzVirtualNetworkSubnetConfig -Name GatewaySubnet -VirtualNetwork $VNet\r\n" +
                         "    Try {$pip1 = Get-AzPublicIpAddress -Name $VNetName-gw-vpn-pip1 -ResourceGroupName $RGName -ErrorAction Stop}\r\n" +
                         "    Catch {$pip1 = New-AzPublicIpAddress -Name $VNetName-gw-vpn-pip1 -ResourceGroupName $RGName -Location $Region -AllocationMethod Static -Sku Standard}\r\n" +
                         (HasVPNAA ? "    Try {$pip2 = Get-AzPublicIpAddress -Name $VNetName-gw-vpn-pip2 -ResourceGroupName $RGName -ErrorAction Stop}\r\n" : "") +
                         (HasVPNAA ? "    Catch {$pip2 = New-AzPublicIpAddress -Name $VNetName-gw-vpn-pip2 -ResourceGroupName $RGName -Location $Region -AllocationMethod Static -Sku Standard}\r\n" : "") +
                         "    $ipconf1 = New-AzVirtualNetworkGatewayIpConfig -Name gwipconf1 -Subnet $subnet -PublicIpAddress $pip1\r\n" +
                         (HasVPNAA ? "    $ipconf2 = New-AzVirtualNetworkGatewayIpConfig -Name gwipconf2 -Subnet $subnet -PublicIpAddress $pip2\r\n" : "") +
                         "    New-AzVirtualNetworkGateway -Name $VNetName-gw-vpn -ResourceGroupName $RGName -Location $Region -IpConfigurations $ipconf1" + (HasVPNAA ? ",$ipconf2" : "") + " -GatewayType Vpn -VpnType RouteBased -VpnGatewayGeneration Generation2 -GatewaySku " + tenant.Vpngateway + (HasVPNAA ? " -EnableActiveActiveFeature" : "") + " -AsJob | Out-Null\r\n" +
                         "    } # End Try\r\n\r\n";

                strDB += "# Create Local Gateway\r\n" +
                         "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "Write-Host 'Creating Local Gateway Object' -ForegroundColor Cyan\r\n" +
                         "Try {$lgw = Get-AzLocalNetworkGateway -Name $RGName-OnPrem-gw -ResourceGroupName $RGName -ErrorAction Stop\r\n" +
                         "     Write-Host '  resource exists, skipping'}\r\n" +
                         "Catch {$lgw = New-AzLocalNetworkGateway -Name $RGName-OnPrem-gw -ResourceGroupName $RGName -Location $Region -GatewayIpAddress $LabPIP -Asn $PvtASN -BgpPeeringAddress $LabBGPIP}\r\n\r\n";
            }

            // Check/Create Azure VMs
            if (HasAzureVM)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script to Create Azure VMs");
                // Set VM Create Loop and Check/Create VMs
                strDB += "# Loop through VM Creation\r\n" +
                         "For ($i=1; $i -le " + intVMCount + "; $i++) {\r\n" +
                         "    $VMName = $VMPrefix + $i.ToString(\"00\")\r\n" +
                         "    If ($VMName.Length -gt 15) {\r\n" +
                         "        Write-Warning \"VM Name is too long, the name must be 15 characters or less, it's currently $($VMName.Length) characters. Please reduce name length and rerun.\"\r\n" +
                         "        Return}\r\n\r\n";

                // Check/Create NSG
                strDB += "    # Create an inbound network security group rule for the admin port\r\n" +
                         "    Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "    Write-Host 'Creating NSG and Admin rule' -ForegroundColor Cyan\r\n" +
                         "    Try {$nsg = Get-AzNetworkSecurityGroup -Name $VMName'-nic-nsg' -ResourceGroupName $RGName -ErrorAction Stop\r\n" +
                         "         Write-Host '  resource exists, skipping'}\r\n" +
                         "    Catch {\r\n" +
                         "           Switch ($VMOS[$i-1])\r\n" +
                         "           {'Windows' {$nsgRule = New-AzNetworkSecurityRuleConfig -Name myRDPRule -Protocol Tcp -Direction Inbound -Priority 1000 -SourceAddressPrefix * -SourcePortRange * -DestinationAddressPrefix * -DestinationPortRange 3389 -Access Allow}\r\n" +
                         "             default  {$nsgRule = New-AzNetworkSecurityRuleConfig -Name mySSHRule -Protocol Tcp -Direction Inbound -Priority 1000 -SourceAddressPrefix * -SourcePortRange * -DestinationAddressPrefix * -DestinationPortRange 22 -Access Allow}\r\n" +
                         "           } # End Switch\r\n" +
                         "           $nsg = New-AzNetworkSecurityGroup -ResourceGroupName $RGName -Location $Region -Name $VMName'-nic-nsg' -SecurityRules $nsgRule\r\n" +
                         "          } # End Try\r\n\r\n";

                // Check/Create PIP
                strDB += "    # Create a public IPv4 address\r\n" +
                         "    Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "    Write-Host 'Creating Public IPv4 address' -ForegroundColor Cyan\r\n" +
                         "    Try {$pip4 = Get-AzPublicIpAddress -ResourceGroupName $RGName -Name $VMName'-nic-pip4' -ErrorAction Stop\r\n" +
                         "         Write-Host '  resource exists, skipping'}\r\n" +
                         "    Catch {$pip4 = New-AzPublicIpAddress -ResourceGroupName $RGName -Name $VMName'-nic-pip4' -Location $Region -sku Standard -AllocationMethod Static -IpAddressVersion IPv4}\r\n\r\n";
                if (HasIPv6)
                {
                    strDB += "    # Create a public IPv6 address\r\n" +
                             "    Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                             "    Write-Host 'Creating Public IPv6 address' -ForegroundColor Cyan\r\n" +
                             "    Try {$pip6 = Get-AzPublicIpAddress -ResourceGroupName $RGName -Name $VMName'-nic-pip6' -ErrorAction Stop\r\n" +
                             "         Write-Host '  resource exists, skipping'}\r\n" +
                             "    Catch {$pip6 = New-AzPublicIpAddress -ResourceGroupName $RGName -Name $VMName'-nic-pip6' -Location $Region -sku Standard -AllocationMethod Static -IpAddressVersion IPv6}\r\n\r\n";
                }

                // Check/Create NIC
                strDB += "    # Create a virtual network card and associate with public IP address and NSG\r\n" +
                          "    Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                          "    Write-Host 'Creating NIC' -ForegroundColor Cyan\r\n" +
                          "    Try {$nic = Get-AzNetworkInterface -Name $VMName'-nic' -ResourceGroupName $RGName -ErrorAction Stop\r\n" +
                          "         Write-Host '  resource exists, skipping'}\r\n" +
                          "    Catch {$vnet = Get-AzVirtualNetwork -ResourceGroupName $RGName -Name $VNetName\r\n" +
                          "           $subnet = Get-AzVirtualNetworkSubnetConfig -Name Tenant -VirtualNetwork $vnet\r\n" +
                          "           $ipconfig1 = New-AzNetworkInterfaceIpConfig -Name ipconfig1 -Subnet $Subnet -PrivateIpAddressVersion IPv4 -PublicIpAddress $pip4 -Primary\r\n" +
                          (HasIPv6 ? "           $ipconfig2 = New-AzNetworkInterfaceIpConfig -Name ipconfig2 -Subnet $Subnet -PrivateIpAddressVersion IPv6 -PublicIpAddress $pip6\r\n" : "") +
                          "           $nic = New-AzNetworkInterface -Name $VMName'-nic' -ResourceGroupName $RGName -Location $Region -IpConfiguration $ipconfig1" + (HasIPv6 ? ",$ipconfig2" : "") + " -NetworkSecurityGroupId $nsg.Id -ErrorAction Stop}\r\n\r\n";

                // Check/Create VM Config
                strDB += "    # Create a virtual machine configuration\r\n" +
                         "    Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "    Write-Host 'Creating VM' -ForegroundColor Cyan\r\n" +
                         "    Switch ($VMOS[$i-1]) {\r\n" +
                         "    'Windows' {$vmConfig = New-AzVMConfig -VMName $VMName -VMSize $VMSize | `\r\n" +
                         "               Set-AzVMOperatingSystem -Windows -ComputerName $VMName -Credential $cred -EnableAutoUpdate -ProvisionVMAgent | `\r\n" +
                         "               Set-AzVMSourceImage -PublisherName MicrosoftWindowsServer -Offer WindowsServer `\r\n" +
                         "               -Skus 2022-datacenter-azure-edition-core -Version latest | Add-AzVMNetworkInterface -Id $nic.Id | Set-AzVMBootDiagnostic -Disable}\r\n" +
                         "    'Ubuntu'  {$vmConfig = New-AzVMConfig -VMName $VMName -VMSize $VMSize | `\r\n" +
                         "               Set-AzVMOperatingSystem -Linux -ComputerName $VMName -Credential $cred | `\r\n" +
                         "               Set-AzVMSourceImage -PublisherName Canonical -Offer ubuntu-24_04-lts -Skus server -Version latest | `\r\n" +
                         "               Add-AzVMNetworkInterface -Id $nic.Id | Set-AzVMBootDiagnostic -Disable}\r\n" +
                         "           } # End Switch\r\n\r\n";

                // Check/Create VM (AsJob)
                strDB += "    # Create the VM\r\n" +
                         "    Try {Get-AzVM -ResourceGroupName $RGName -Name $VMName -ErrorAction Stop | Out-Null\r\n" +
                         "         Write-Host '  resource exists, skipping'}\r\n" +
                         "    Catch {New-AzVM -ResourceGroupName $RGName -Location $Region -VM $vmConfig -AsJob -ErrorAction Stop | Out-Null}\r\n" +
                         "}\r\n\r\n";

                // Wait Job for VMs
                strDB += "# Wait for jobs to finish\r\n" +
                         "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "Write-Host 'Waiting for VM Jobs to finish, script will continue after 10 minutes or when VMs complete, whichever is first.' -ForegroundColor Cyan\r\n" +
                         "Get-Job -Command 'New-AzVM' | wait-job -Timeout 600 | Out-Null\r\n\r\n";

                // Kick-off post VM deploy scripts
                strDB += "# Push extension to open ICMPv4 to Windows VMs\r\n" +
                         "For ($i=1; $i -le " + intVMCount + "; $i++) {\r\n" +
                         "    $ScriptBlobAccount = 'scriptrepository'\r\n" +
                         "    $timestamp = (Get-Date).Ticks\r\n" +
                         "    $VMName = $VMPrefix + $i.ToString(\"00\")\r\n" +
                         "    If ($VMOS[$i-1] -eq 'Windows') {\r\n" +
                         "        Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "        Write-Host 'Submitting post-deploy VM extension job' -ForegroundColor Cyan\r\n" +
                         "        Switch ($VMPostDeploy) {\r\n" +
                         "        'ICMPv4' {$ScriptName = 'AllowICMPv4.ps1'\r\n" +
                         "                  $ExtensionName = 'AllowICMPv4'}\r\n" +
                         "                 } # End Switch\r\n" +
                         "        $ScriptLocation = 'https://' + $ScriptBlobAccount + '.blob.core.windows.net/scripts/' + $ScriptName\r\n" +
                         "        $ScriptExe = \".\\$ScriptName\"\r\n" +
                         "        $PublicConfiguration = @{'fileUris' = [Object[]]\"$ScriptLocation\";'timestamp' = \"$timestamp\";'commandToExecute' = \"powershell.exe -ExecutionPolicy Unrestricted -Command $ScriptExe\"}\r\n\r\n" +
                         "        Set-AzVMExtension -ResourceGroupName $RGName -VMName $VMName -Location $Region `\r\n" +
                         "        -Name $ExtensionName -Publisher 'Microsoft.Compute' -ExtensionType 'CustomScriptExtension' -TypeHandlerVersion '1.10' `\r\n" +
                         "        -Settings $PublicConfiguration -ErrorAction Stop | Out-Null }\r\n" +
                         "    Else {# For future Linux extensions if needed\r\n" +
                         "         }# End If\r\n" +
                         "    } # End For\r\n\r\n";
            }

            // Create the VPN Connection
            if (HasVPNGateway)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script to Create VPN Connection");
                strDB += "# Create the VPN Connection(s)\r\n" +
                         "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "Write-Host 'Connecting VPN Gateway to Lab SRX' -ForegroundColor Cyan\r\n" +
                         "Try {$connection = Get-AzVirtualNetworkGatewayConnection -Name $VNetName-gw-vpn-conn -ResourceGroupName $RGName -ErrorAction Stop\r\n" +
                         "     Write-Host '  resource exists, skipping'}\r\n" +
                         "Catch {\r\n" +
                         "    $gw = Get-AzVirtualNetworkGateway -Name $VNetName-gw-vpn -ResourceGroupName $RGName\r\n" +
                         "    $i=0\r\n" +
                         "    If ($gw.ProvisioningState -eq 'Updating') {Write-Host '  waiting for VPN gateway to finish provisioning: ' -NoNewline\r\n" +
                         "                                               Sleep 10}\r\n" +
                         "    While ($gw.ProvisioningState -eq 'Updating') {\r\n" +
                         "        $i++\r\n" +
                         "        If ($i%6) {Write-Host '*' -NoNewline}\r\n" +
                         "        Else {Write-Host \"$($i/6)\" -NoNewline}\r\n" +
                         "        Sleep 10\r\n" +
                         "        $gw = Get-AzVirtualNetworkGateway -Name $VNetName-gw-vpn -ResourceGroupName $RGName}\r\n\r\n" +
                         "    If ($i -gt 0) {\r\n" +
                         "        Write-Host\r\n" +
                         "        Write-Host '  VPN Gateway deployment complete.'\r\n" +
                         "        Write-Host '  Building connection'\r\n" +
                         "    }\r\n\r\n" +
                         "    If ($gw.ProvisioningState -eq 'Succeeded') {\r\n" +
                         "        Write-Host '  creating tunnel'\r\n" +
                         "        $lgw = Get-AzLocalNetworkGateway -Name $RGName-OnPrem-gw -ResourceGroupName $RGName -ErrorAction Stop\r\n" +
                         "        $connection = New-AzVirtualNetworkGatewayConnection -Name $VNetName-gw-vpn-conn -ResourceGroupName $RGName `\r\n" +
                         "                      -Location $Region -VirtualNetworkGateway1 $gw -LocalNetworkGateway2 $lgw -ConnectionType IPsec `\r\n" +
                         "                      -SharedKey $LabSharedKey -EnableBgp $True}\r\n" +
                         "    Else {Write-Warning 'An issue occured with VPN gateway provisioning.'\r\n" +
                         "          Write-Host 'Current Gateway Provisioning State' -NoNewLine\r\n" +
                         "          Write-Host $gw.ProvisioningState}\r\n" +
                         "    }\r\n\r\n";
            }

            // Get Circuit & Check Circuit Provision State
            if (HasER)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script to Check ExpressRoute Circuit state");
                strDB += "# Check ExpressRoute Circuit\r\n" +
                         "Try {$circuit = Get-AzExpressRouteCircuit -ResourceGroupName $RGName -Name $RGName-ER -ErrorAction Stop}\r\n" +
                         "Catch {\r\n" +
                         "    Write-Warning 'An ER Circuit was not found. Run the ERCreate PowerShell first, then provision the circuit before running this script!'\r\n" +
                         "    Return}\r\n\r\n";

                strDB += "If ($circuit.ServiceProviderProvisioningState -ne 'Provisioned') {\r\n" +
                         "    Write-Warning 'The ER Circuit has not been provisioned by the Service Provider, provision the circuit before running this script!'\r\n" +
                         "    Return}\r\n\r\n";
            }

            // Check/Set Private Peerings
            if (HasPrivate)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script to Create ER Private Peering");
                strDB += "# Create Private Peering\r\n" +
                         "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "Write-Host 'Creating Private Peering' -ForegroundColor Cyan\r\n" +
                         "Try {$peering = Get-AzExpressRouteCircuitPeeringConfig -Name AzurePrivatePeering -ExpressRouteCircuit $circuit -ErrorAction Stop\r\n" +
                         "     Write-Host '  resource exists, skipping'}\r\n" +
                         "Catch {Add-AzExpressRouteCircuitPeeringConfig -Name AzurePrivatePeering -ExpressRouteCircuit $circuit `\r\n" +
                         "          -PrimaryPeerAddressPrefix $PvtP2PA -SecondaryPeerAddressPrefix $PvtP2PB `\r\n" +
                         "          -PeeringType AzurePrivatePeering -PeerASN $PvtASN -VlanId $PvtVLAN -PeerAddressType IPv4 | Out-Null";
                if (HasIPv6)
                {
                    strDB += "\r\n\r\n" +
                             "       Set-AzExpressRouteCircuitPeeringConfig -Name AzurePrivatePeering -ExpressRouteCircuit $circuit `\r\n" +
                             "          -PrimaryPeerAddressPrefix $Pvt6P2PA -SecondaryPeerAddressPrefix $Pvt6P2PB `\r\n" +
                             "          -PeeringType AzurePrivatePeering -PeerASN $PvtASN -VlanId $PvtVLAN -PeerAddressType IPv6 | Out-Null";
                }
                strDB += "}\r\n\r\n";
            }

            // Check/Set Route Filter and Microsoft Peerings
            if (HasMicrosoft)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script to Create ER Microsoft Peering");
                strDB += "# Create a Route Filter for Microsoft Peering\r\n" +
                         "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "Write-Host 'Creating Route Filter' -ForegroundColor Cyan\r\n" +
                         "Try {$RouteFilter = Get-AzRouteFilter -Name $RGName-ER-rf -ResourceGroupName $RGName -ErrorAction Stop\r\n" +
                         "     Write-Host '  resource exists, skipping'}\r\n" +
                         "Catch {$RouteFilter = New-AzRouteFilter -Name $RGName-ER-rf -ResourceGroupName $RGName -Location $circuit.Location}\r\n\r\n";
                // TODO: Add populating the RouteFilter

                strDB += "# Create Microsoft Peering\r\n" +
                         "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "Write-Host 'Creating Microsoft Peering' -ForegroundColor Cyan\r\n" +
                         "Try {$peering = Get-AzExpressRouteCircuitPeeringConfig -Name MicrosoftPeering -ExpressRouteCircuit $circuit -ErrorAction Stop\r\n" +
                         "     Write-Host '  resource exists, skipping'}\r\n" +
                         "Catch {Add-AzExpressRouteCircuitPeeringConfig -Name MicrosoftPeering -ExpressRouteCircuit $circuit `\r\n" +
                         "       -PrimaryPeerAddressPrefix $MsftP2PA -SecondaryPeerAddressPrefix $MsftP2PB `\r\n" +
                         "       -PeeringType MicrosoftPeering -PeerASN $MsftASN -VlanId $MsftVLAN `\r\n" +
                         "       -MicrosoftConfigAdvertisedPublicPrefixes @($MsftNAT) -PeerAddressType IPv4 -RouteFilter $RouteFilter | Out-Null}\r\n\r\n";
            }

            // Save Circuit
            if (HasPrivate || HasMicrosoft)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script to Save ER Circuit peering updates");
                strDB += "# Save Peering to Circuit\r\n" +
                         "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "Write-Host 'Saving Peerings to Circuit' -ForegroundColor Cyan\r\n" +
                         "Try {Set-AzExpressRouteCircuit -ExpressRouteCircuit $circuit -ErrorAction Stop | Out-Null}\r\n" +
                         "Catch {\r\n" +
                         "    Write-Warning 'Some or all of the ER Circuit peerings were NOT saved. Please manually verify and correct.'}\r\n\r\n";
            }

            // Create the ER Connection
            if (HasERGateway)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script to Create ER connection to ER gateway");
                // Note: due to validation logic, if HasERGateway is true, then HasER and HasPrivate are both true as well
                strDB += "# Create the Connection\r\n" +
                         "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                         "Write-Host 'Connecting Gateway to ExpressRoute' -ForegroundColor Cyan\r\n" +
                         "Try {$connection = Get-AzVirtualNetworkGatewayConnection -Name $VNetName-gw-er-conn -ResourceGroupName $RGName -ErrorAction Stop\r\n" +
                         "     Write-Host '  resource exists, skipping'}\r\n" +
                         "Catch {\r\n" +
                         "    $gw = Get-AzVirtualNetworkGateway -Name $VNetName-gw-er -ResourceGroupName $RGName\r\n" +
                         "    $i=0\r\n" +
                         "    If ($gw.ProvisioningState -eq 'Updating') {Write-Host '  waiting for ER gateway to finish provisioning: ' -NoNewline\r\n" +
                         "                                               Sleep 10}\r\n" +
                         "    While ($gw.ProvisioningState -eq 'Updating') {\r\n" +
                         "        $i++\r\n" +
                         "        If ($i%6) {Write-Host '*' -NoNewline}\r\n" +
                         "        Else {Write-Host \"$($i/6)\" -NoNewline}\r\n" +
                         "        Sleep 10\r\n" +
                         "        $gw = Get-AzVirtualNetworkGateway -Name $VNetName-gw-er -ResourceGroupName $RGName}\r\n\r\n" +
                         "    If ($i -gt 0) {\r\n" +
                         "        Write-Host\r\n" +
                         "        Write-Host '  ER Gateway deployment complete'\r\n" +
                         "        Write-Host '  building connection'\r\n" +
                         "    }\r\n\r\n" +
                         "    If ($gw.ProvisioningState -eq 'Succeeded') {\r\n" +
                         "        $circuit = Get-AzExpressRouteCircuit -ResourceGroupName $RGName -Name $RGName-ER -ErrorAction Stop\r\n" +
                         "        $connection = New-AzVirtualNetworkGatewayConnection -Name $VNetName-gw-er-conn -ResourceGroupName $RGName `\r\n" +
                         "                                                            -Location $Region -VirtualNetworkGateway1 $gw -PeerId $circuit.Id `\r\n" +
                         "                                                            -ConnectionType ExpressRoute" + (HasERFastPath ? " -ExpressRouteGatewayBypass" : "") + "}\r\n" +
                         "    Else {Write-Warning 'An issue occured with ER gateway provisioning.'\r\n" +
                         "          Write-Host 'Current Gateway Provisioning State' -NoNewLine\r\n" +
                         "          Write-Host $gw.ProvisioningState}\r\n" +
                         "    }\r\n\r\n";
            }

            // Get tunnel and PIP information
            if (HasVPNGateway)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script to Show VPN Tunnel Info");
                strDB += "# Get tunnel information\r\n" +
                         "$gw = Get-AzVirtualNetworkGateway -Name $VNetName-gw-vpn -ResourceGroupName $RGName\r\n" +
                         "$pip1 = Get-AzPublicIpAddress -Name $VNetName-gw-vpn-pip1 -ResourceGroupName $RGName\r\n" +
                         (HasVPNAA ? "$pip2 = Get-AzPublicIpAddress -Name $VNetName-gw-vpn-pip2 -ResourceGroupName $RGName\r\n" : "") +
                         "$AzureBGPIP = $gw.BgpSettings.BgpPeeringAddress\r\n\r\n";
            }

            // Write a happy ending
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding script to End Nicely");
            strDB += "# End nicely\r\n" +
                     "$EndTime = Get-Date\r\n" +
                     "$TimeDiff = New-TimeSpan $StartTime $EndTime\r\n" +
                     "$Mins = $TimeDiff.Minutes\r\n" +
                     "$Secs = $TimeDiff.Seconds\r\n" +
                     "$RunTime = '{0:00}:{1:00} (M:S)' -f $Mins,$Secs\r\n" +
                     "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                     "Write-Host \"Script completed\" -ForegroundColor Green\r\n" +
                     "Write-Host \"  Time to complete: $RunTime\"\r\n" +
                     "Write-Host\r\n" +
                     (HasVPNGateway ? "Write-Host '  VPN Tunnel and IP Information:'\r\n" : "") +
                     (HasVPNGateway ? "Write-Host \"    Public Tunnel Endpoint 1: $($pip1.IpAddress)\"\r\n" : "") +
                     (HasVPNGateway && HasVPNAA ? "Write-Host \"    Public Tunnel Endpoint 2: $($pip2.IpAddress)\"\r\n" : "") +
                     (HasVPNGateway ? "Write-Host \"    BGP Peering IP Address  : $AzureBGPIP\"\r\n" : "") +
                     (HasVPNGateway ? "Write-Host" : "") + "\r\n";

            // Back out script
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Adding backout script for Azure resources");
            var strBackout = $"$RGName='{strRGName}'\r\n" +
                          $"$UserName='{_userEmail.Split('@')[0]}'\r\n" +
                          $"$TenantGUID='{tenant.TenantGuid}'\r\n";
            if (HasERDirect)
            {
                strBackout += "$OkToDelete = $true\r\n\r\n";
            }
            else
            {
                strBackout += "$OkToDelete = $false\r\n\r\n" +
                              "Write-Host (Get-Date)' - ' -NoNewline\r\n" +
                              "Write-Host 'Deleting ' -NoNewline -ForegroundColor Cyan\r\n" +
                              "Write-Host $RGName -ForegroundColor Cyan\r\n" +
                              "Write-Host '  Checking ER circuit..........' -NoNewline\r\n" +
                              "Try {$circuit = Get-AzExpressRouteCircuit -ResourceGroupName $RGName -ErrorAction Stop}\r\n" +
                              "Catch {Write-Host 'None found' -ForegroundColor Green\r\n" +
                              "       $OkToDelete = $true}\r\n\r\n" +
                              "If (-not $OkToDelete) {\r\n" +
                              "    If ($circuit.ServiceProviderProvisioningState -contains 'Provisioned') {\r\n" +
                              "        Write-Host 'Failed' -ForegroundColor Red\r\n" +
                              "        Write-Warning 'An ExpressRoute Circuit in this resource group is still provisioned, and as such this delete opertaion cannot continue, get this circuit into the \"Not Provisioned\" state before running this script!'\r\n" +
                              "        Return}\r\n" +
                              "    Else {Write-Host 'NotProvisioned' -ForegroundColor Green\r\n" +
                              "          $OkToDelete = $true}\r\n" +
                              "}\r\n\r\n";
            }
            strBackout += "Try {Write-Host '  Pulling config for archive...' -NoNewline\r\n" +
                          "     mkdir \"$env:TEMP\\ConfigGen\\\" -Force -ErrorAction Stop | Out-Null\r\n" +
                          "     Export-AzResourceGroup -ResourceGroupName $RGName -Path \"$env:TEMP\\ConfigGen\\$TenantGUID\" -IncludeComments -SkipAllParameterization -Force -ErrorAction Stop | Out-Null\r\n" +
                          "     Write-Host 'Archive File Created' -ForegroundColor Green}\r\n" +
                          "Catch {Write-Host 'Failed' -ForegroundColor Red\r\n" +
                          "       Write-Warning 'Pulling or creating config file failed, the resource group was not found or was not deleted.'\r\n" +
                          "       Return}\r\n\r\n" +
                          "Try {Write-Host '  Writing file to archive......' -NoNewline\r\n" +
                          "     $sa = Get-AzStorageAccount -ResourceGroupName LabInfrastructure -Name labconfig\r\n" +
                          "     $ctx = $sa.Context\r\n" +
                          "     Set-AzStorageBlobContent -Context $ctx -Container archive -Blob \"$TenantGUID.json\" -File \"$env:TEMP\\ConfigGen\\$TenantGUID.json\" -Metadata @{ArchiveDate=(Get-Date -Format s);ArchiveBy=$UserName;ResourceGroup=$RGName} -Force | Out-Null\r\n" +
                          "     Write-Host 'File saved' -ForegroundColor Green}\r\n" +
                          "Catch {Write-Host 'Failed' -ForegroundColor Red\r\n" +
                          "       Write-Warning 'Saving config to the archive storage account failed, the resource group was not deleted.'\r\n" +
                          "       Return}\r\n\r\n" +
                          "If ($OkToDelete) {Try {Get-AzResourceGroup -Name $RGName -ErrorAction Stop | Out-Null\r\n" +
                          "                       Remove-AzResourceGroup -Name $RGName -Force -AsJob | Out-Null\r\n" +
                          "                       Write-Host '  resource group deletion requested as a job (Run Get-Job to see status)'}\r\n" +
                          "                  Catch {Write-Warning 'Something happened and the resource group was not found or was not deleted.'}\r\n" +
                          "}\r\n\r\n";
           _strDelete += "#######\r\n### Remove Azure Resources\r\n#######\r\n" + strBackout + "\r\n";
            await SaveToSql("CreateAzurePowerShell-out", tenant, strBackout);
            
            bool results = await SaveToSql("CreateAzurePowerShell", tenant, strDB);
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Azure PowerShell saved to SQL");
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateAzureConfig", "Complete");
            return results;
        }

        private async Task<bool> GenerateFirewallConfig(Tenant tenant)
        {
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Starting");
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Setting variables");
            // Set variables
            
            string strDB;
            string strDeviceName = (tenant.Lab == "SEA") ? "SEA-SRX42-01" : "ASH-SRX42-01";

            bool HasERGateway = tenant.ErgatewaySize != "None";
            bool HasPrivate = tenant.PvtPeering == true;
            bool HasMicrosoft = tenant.Msftpeering == true;
            bool HasVPNGateway = tenant.Vpngateway != "None";
            bool HasVPNAA = tenant.Vpnconfig == "Active-Active";
            bool HasIPv6 = tenant.AddressFamily == "IPv6" || tenant.AddressFamily == "Dual";

            string[] labVms = [tenant.LabVm1 ?? "None", tenant.LabVm2 ?? "None", tenant.LabVm3 ?? "None", tenant.LabVm4 ?? "None"];
            bool HasLabVM = labVms.Any(vm => vm != "None");

            if (HasVPNGateway || HasLabVM || HasMicrosoft)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Firewall config required");
                var lab = GetLabConstants(tenant.Lab);
                string strLabOctet = lab.Octet;
                string strINetNAT = lab.INetNAT;
                string strLabVpnIP = lab.VpnIP;
                string strLabPrefix = lab.IPv6Prefix;
                string strASN = lab.Asn;

                string strREth1IntIP = $"192.168.{tenant.TenantId}.1";                    // IP of REth 1 Interface facing Router 1
                string strREth1NIP = $"192.168.{tenant.TenantId}.0";                      // IP of REth 1 Neighbor (Router 1)
                string strREth1IntIPv6 = $"fd:2:{strLabOctet}:{tenant.TenantId}FF::1";   // IPv6 of REth 1 Interface facing Router 1
                string strREth1NIPv6 = $"fd:2:{strLabOctet}:{tenant.TenantId}FF::";      // IPv6 of REth 1 Neighbor (Router 1)
                string strREth2IntIP = $"192.168.{tenant.TenantId}.3";                    // IP of REth 2 Interface facing Router 1
                string strREth2NIP = $"192.168.{tenant.TenantId}.2";                      // IP of REth 2 Neighbor (Router 1)
                string strREth2IntIPv6 = $"fd:2:{strLabOctet}:{tenant.TenantId}FF::3";   // IPv6 of REth 2 Interface facing Router 1
                string strREth2NIPv6 = $"fd:2:{strLabOctet}:{tenant.TenantId}FF::2";     // IPv6 of REth 2 Neighbor (Router 1)
                string strREth3IntIP = $"10.{strLabOctet}.{tenant.TenantId}.1";          // IP of REth 3 Interface facing both switches, default gateway for Lab VMs
                string strREth3IntIPv6 = $"{strLabPrefix}{tenant.TenantId}::1";                    // IPv6 of REth 3 Interface facing both switches, default gateway for Lab VMs
                string strAzASN = "65515";                                                          // ASN for Azure

                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Pulling Azure Region data from SQL");
                Region region = await _context.Regions.FirstOrDefaultAsync(r => r.Region1 == tenant.AzureRegion) ?? throw new InvalidOperationException($"Region '{tenant.AzureRegion}' not found");
                string strVpnBgpPriNIP;                                                             // Private IP BGP Neighbor in the Azure for primary BGP session
                string strVpnBgpSecNIP;                                                             // Private IP BGP Neighbor in the Azure for secondary BGP session
                if (HasVPNAA && HasERGateway)
                {
                    strVpnBgpPriNIP = $"10.{region.Ipv4}.{tenant.TenantId}.142";
                    strVpnBgpSecNIP = $"10.{region.Ipv4}.{tenant.TenantId}.143";
                }
                else if (HasVPNAA)
                {
                    strVpnBgpPriNIP = $"10.{region.Ipv4}.{tenant.TenantId}.132";
                    strVpnBgpSecNIP = $"10.{region.Ipv4}.{tenant.TenantId}.133";
                }
                else
                {
                    strVpnBgpPriNIP = $"10.{region.Ipv4}.{tenant.TenantId}.254";
                    strVpnBgpSecNIP = "";
                }
                string strLabVpnBgpIP = $"192.168.{tenant.TenantId}.88";
                string strERNATIP = tenant.Msftadv ?? string.Empty;

                string strVPNEndPoint = ResolveVpnEndPoint(tenant);

                // Add banner
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Starting Firewall Config");
                strDB = "#######\r\n### Firewall\r\n#######\r\n\r\n";
                var strBackout = "";

                // Routing Options
                strDB += "# Define Routing Options\r\n" +
                         $"set routing-options rib-groups to-Cust{tenant.TenantId}-instance import-rib inet.0\r\n" +
                         $"set routing-options rib-groups to-Cust{tenant.TenantId}-instance import-rib Cust{tenant.TenantId}.inet.0\r\n" +
                         $"set routing-options rib-groups to-Cust{tenant.TenantId}-instance-v6 import-rib inet6.0\r\n" +
                         $"set routing-options rib-groups to-Cust{tenant.TenantId}-instance-v6 import-rib Cust{tenant.TenantId}.inet6.0\r\n" +
                         $"set routing-options rib-groups import-internet-routes import-rib Cust{tenant.TenantId}.inet.0\r\n" +
                         $"set routing-options rib-groups import-internet-routes-v6 import-rib Cust{tenant.TenantId}.inet6.0\r\n\r\n";
               strBackout += $"delete routing-options rib-groups to-Cust{tenant.TenantId}-instance\r\n" +
                             $"delete routing-options rib-groups import-internet-routes import-rib Cust{tenant.TenantId}.inet.0\r\n" +
                             $"delete routing-options rib-groups to-Cust{tenant.TenantId}-instance-v6\r\n" +
                             $"delete routing-options rib-groups import-internet-routes-v6 import-rib Cust{tenant.TenantId}.inet6.0\r\n";

                // Interfaces
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Setting interfaces");
                strDB += "# Define Interfaces\r\n" +
                         $"set interfaces reth3 unit {tenant.TenantId} description \"Cust{tenant.TenantId} to LAN\"\r\n" +
                         $"set interfaces reth3 unit {tenant.TenantId} vlan-id {tenant.TenantId}\r\n" +
                         $"set interfaces reth3 unit {tenant.TenantId} family inet address {strREth3IntIP}/25\r\n" +
                         $"set interfaces reth3 unit {tenant.TenantId} family inet6 address {strREth3IntIPv6}/64\r\n";
               strBackout += $"delete interfaces reth3 unit {tenant.TenantId}\r\n";

                if (HasPrivate || HasMicrosoft)
                {
                    strDB += $"set interfaces reth1 unit {tenant.TenantId} description \"Cust{tenant.TenantId} to Primary Router\"\r\n" +
                             $"set interfaces reth1 unit {tenant.TenantId} vlan-id {tenant.TenantId}\r\n" +
                             $"set interfaces reth1 unit {tenant.TenantId} family inet address {strREth1IntIP}/31\r\n" +
                             (HasIPv6 ? $"set interfaces reth1 unit {tenant.TenantId} family inet6 address {strREth1IntIPv6}/127\r\n" : "") +
                             $"set interfaces reth2 unit {tenant.TenantId} description \"Cust{tenant.TenantId} to Secondary Router\"\r\n" +
                             $"set interfaces reth2 unit {tenant.TenantId} vlan-id {tenant.TenantId}\r\n" +
                             $"set interfaces reth2 unit {tenant.TenantId} family inet address {strREth2IntIP}/31\r\n" +
                             (HasIPv6 ? $"set interfaces reth2 unit {tenant.TenantId} family inet6 address {strREth2IntIPv6}/127\r\n" : "");
                   strBackout += $"delete interfaces reth1 unit {tenant.TenantId}\r\n" +
                                 $"delete interfaces reth2 unit {tenant.TenantId}\r\n";
                }

                if (HasVPNGateway)
                {
                    strDB += $"set interfaces lo0 unit {tenant.TenantId} description \"Cust{tenant.TenantId} VPN Loopback\"\r\n" +
                             $"set interfaces lo0 unit {tenant.TenantId} family inet address {strLabVpnBgpIP}/32\r\n" +
                             $"set interfaces st0 unit {tenant.TenantId}8 description \"Cust{tenant.TenantId} VPN Tunnel Primary\"\r\n" +
                             $"set interfaces st0 unit {tenant.TenantId}8 family inet mtu 1436\r\n" +
                             $"set interfaces st0 unit {tenant.TenantId}8 family inet address 169.254.{tenant.TenantId}.1/32\r\n";
                   strBackout += $"delete interfaces lo0 unit {tenant.TenantId}\r\n" +
                                 $"delete interfaces st0 unit {tenant.TenantId}8\r\n";

                }

                if (HasVPNGateway && HasVPNAA)
                {
                    strDB += $"set interfaces st0 unit {tenant.TenantId}9 description \"Cust{tenant.TenantId} VPN Tunnel Secondary\"\r\n" +
                             $"set interfaces st0 unit {tenant.TenantId}9 family inet mtu 1436\r\n" +
                             $"set interfaces st0 unit {tenant.TenantId}9 family inet address 169.254.{tenant.TenantId}.2/32\r\n";
                   strBackout += $"delete interfaces st0 unit {tenant.TenantId}9\r\n";

                }
                strDB += "\r\n";

                // Routing Instances
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Setting routing instances");
                strDB += "# Define Routing Instances (VRFs)\r\n" +
                         $"set routing-instances Cust{tenant.TenantId} instance-type virtual-router\r\n" +
                         $"set routing-instances Cust{tenant.TenantId} interface reth3.{tenant.TenantId}\r\n" +
                         $"set routing-instances Cust{tenant.TenantId} routing-options interface-routes rib-group inet to-Cust{tenant.TenantId}-instance\r\n" +
                         $"set routing-instances Cust{tenant.TenantId} routing-options interface-routes rib-group inet6 to-Cust{tenant.TenantId}-instance-v6\r\n" +
                         $"set routing-instances Cust{tenant.TenantId} routing-options instance-import import-internet-routes\r\n";
               strBackout += $"delete routing-instances Cust{tenant.TenantId}\r\n";


                if (HasPrivate || HasMicrosoft)
                {
                    strDB += $"set routing-instances Cust{tenant.TenantId} interface reth1.{tenant.TenantId}\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} interface reth2.{tenant.TenantId}\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp type internal\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp export Cust{tenant.TenantId}-onprem\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp multipath\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp neighbor {strREth1NIP}\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp neighbor {strREth2NIP}\r\n";
                    if (HasIPv6)
                    {
                        strDB += $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp6 type internal\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp6 export Cust{tenant.TenantId}-onprem\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp6 family inet6 unicast\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp6 multipath\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp6 neighbor {strREth1NIPv6}\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp6 neighbor {strREth2NIPv6}\r\n";
                    }
                }

                if (HasMicrosoft)
                {
                    strDB += $"set routing-instances Cust{tenant.TenantId} routing-options static route {strERNATIP} discard\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp export Cust{tenant.TenantId}-nat\r\n";
                }

                if (HasVPNGateway)
                {
                    strDB += $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp multihop\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp export Cust{tenant.TenantId}-onprem\r\n" +
                             $"#set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp export ibgp-outbound\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp peer-as {strAzASN}\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp local-as {strASN}\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp multipath\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} interface lo0.{tenant.TenantId}\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} interface st0.{tenant.TenantId}8\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp neighbor {strVpnBgpPriNIP}\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} routing-options static route {strVpnBgpPriNIP}/32 next-hop st0.{tenant.TenantId}8\r\n";
                }

                if (HasVPNGateway && HasVPNAA)
                {
                    strDB += $"set routing-instances Cust{tenant.TenantId} interface st0.{tenant.TenantId}9\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp neighbor {strVpnBgpSecNIP}\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} routing-options static route {strVpnBgpSecNIP}/32 next-hop st0.{tenant.TenantId}9\r\n";
                }
                strDB += "\r\n";

                // Policy Options
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Setting policy options");
                strDB += "# Define Policy Options\r\n" +
                         $"set policy-options policy-statement Cust{tenant.TenantId}-onprem term pvt from interface reth3.{tenant.TenantId}\r\n" +
                         $"set policy-options policy-statement Cust{tenant.TenantId}-onprem term pvt then accept\r\n";
               strBackout += $"delete policy-options policy-statement Cust{tenant.TenantId}-onprem\r\n";

                if (HasMicrosoft)
                {
                    strDB += $"set policy-options policy-statement Cust{tenant.TenantId}-nat term msft-peering from route-filter {strERNATIP} exact\r\n" +
                             $"set policy-options policy-statement Cust{tenant.TenantId}-nat term msft-peering then accept\r\n";
                   strBackout += $"delete policy-options policy-statement Cust{tenant.TenantId}-nat\r\n";

                }
                strDB += "\r\n";

                // Crypto Gateway & VPN
                if (HasVPNGateway)
                {
                    _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Setting crypto gateway and VPN");
                    strDB += "# Set Gateway\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_1 ike-policy azure_ike_policy\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_1 address {strVPNEndPoint.Split(',')[0]}\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_1 dead-peer-detection interval 10\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_1 dead-peer-detection threshold 5\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_1 no-nat-traversal\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_1 local-identity inet {strLabVpnIP}\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_1 external-interface lo0.7\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_1 version v2-only\r\n\r\n" +
                             "# Set VPN\r\n" +
                             $"set security ipsec vpn vpn_Azure_Cust{tenant.TenantId}_1 bind-interface st0.{tenant.TenantId}8\r\n" +
                             $"set security ipsec vpn vpn_Azure_Cust{tenant.TenantId}_1 ike gateway gw_Cust{tenant.TenantId}_1\r\n" +
                             $"set security ipsec vpn vpn_Azure_Cust{tenant.TenantId}_1 ike ipsec-policy azure_ipsec_policy\r\n" +
                             $"set security ipsec vpn vpn_Azure_Cust{tenant.TenantId}_1 establish-tunnels immediately\r\n\r\n";
                   strBackout += $"delete security ike gateway gw_Cust{tenant.TenantId}_1\r\n" +
                                 $"delete security ipsec vpn vpn_Azure_Cust{tenant.TenantId}_1\r\n";
                }

                if (HasVPNGateway && HasVPNAA)
                {
                    strDB += "# Active-Active Gateway and VPN\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_2 ike-policy azure_ike_policy\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_2 address {strVPNEndPoint.Split(',')[1]}\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_2 dead-peer-detection interval 10\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_2 dead-peer-detection threshold 5\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_2 no-nat-traversal\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_2 local-identity inet {strLabVpnIP}\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_2 external-interface lo0.7\r\n" +
                             $"set security ike gateway gw_Cust{tenant.TenantId}_2 version v2-only\r\n\r\n" +
                             $"set security ipsec vpn vpn_Azure_Cust{tenant.TenantId}_2 bind-interface st0.{tenant.TenantId}9\r\n" +
                             $"set security ipsec vpn vpn_Azure_Cust{tenant.TenantId}_2 ike gateway gw_Cust{tenant.TenantId}_2\r\n" +
                             $"set security ipsec vpn vpn_Azure_Cust{tenant.TenantId}_2 ike ipsec-policy azure_ipsec_policy\r\n" +
                             $"set security ipsec vpn vpn_Azure_Cust{tenant.TenantId}_2 establish-tunnels immediately\r\n";
                   strBackout += $"delete security ike gateway gw_Cust{tenant.TenantId}_2\r\n" +
                                 $"delete security ipsec vpn vpn_Azure_Cust{tenant.TenantId}_2\r\n";
                }
                if (HasVPNGateway) { strDB += "\r\n"; }

                // Security Zones
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Setting security zones");
                strDB += "# Define Security Zones\r\n" +
                         $"set security zones security-zone Cust{tenant.TenantId} host-inbound-traffic system-services ping\r\n" +
                         $"set security zones security-zone Cust{tenant.TenantId} host-inbound-traffic system-services traceroute\r\n" +
                         $"set security zones security-zone Cust{tenant.TenantId} host-inbound-traffic protocols bgp\r\n" +
                         $"set security zones security-zone Cust{tenant.TenantId} interfaces reth3.{tenant.TenantId}\r\n";
               strBackout += $"delete security zones security-zone Cust{tenant.TenantId}\r\n";

                if (HasPrivate || HasMicrosoft)
                {
                    strDB += $"set security zones security-zone Cust{tenant.TenantId} interfaces reth1.{tenant.TenantId}\r\n" +
                             $"set security zones security-zone Cust{tenant.TenantId} interfaces reth2.{tenant.TenantId}\r\n";
                }

                if (HasVPNGateway)
                {
                    strDB += $"set security zones security-zone Cust{tenant.TenantId} interfaces lo0.{tenant.TenantId}\r\n" +
                             $"set security zones security-zone Cust{tenant.TenantId} interfaces st0.{tenant.TenantId}8\r\n";
                }

                if (HasVPNGateway && HasVPNAA)
                {
                    strDB += $"set security zones security-zone Cust{tenant.TenantId} interfaces st0.{tenant.TenantId}9\r\n";
                }
                strDB += "\r\n";

                // Security NAT Source
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Setting source NATs");
                strDB += "# Define Source NAT rules\r\n" +
                         $"set security nat source rule-set Cust{tenant.TenantId}_InternetNAT from zone Cust{tenant.TenantId}\r\n" +
                         $"set security nat source rule-set Cust{tenant.TenantId}_InternetNAT to zone internet\r\n" +
                         $"set security nat source rule-set Cust{tenant.TenantId}_InternetNAT rule Internet{tenant.TenantId}-NAT match source-address 0.0.0.0/0\r\n" +
                         $"set security nat source rule-set Cust{tenant.TenantId}_InternetNAT rule Internet{tenant.TenantId}-NAT then source-nat pool Internet-Out\r\n" +
                         $"set security nat source rule-set Cust{tenant.TenantId}_ER from zone Cust{tenant.TenantId}\r\n" +
                         $"set security nat source rule-set Cust{tenant.TenantId}_ER to zone Cust{tenant.TenantId}\r\n";
               strBackout += $"delete security nat source rule-set Cust{tenant.TenantId}_InternetNAT\r\n" +
                             $"delete security nat source rule-set Cust{tenant.TenantId}_ER\r\n";

                if (HasPrivate || HasVPNGateway)
                {
                    strDB += $"set security nat source rule-set Cust{tenant.TenantId}_ER rule Cust{tenant.TenantId}_No_NAT match destination-address-name vnet_add_pvt\r\n" +
                             $"set security nat source rule-set Cust{tenant.TenantId}_ER rule Cust{tenant.TenantId}_No_NAT match application any\r\n" +
                             $"set security nat source rule-set Cust{tenant.TenantId}_ER rule Cust{tenant.TenantId}_No_NAT then source-nat off\r\n";
                }

                if (HasMicrosoft)
                {
                    strDB += $"set security nat source pool Cust{tenant.TenantId}_ToMSFT address {strERNATIP}\r\n" +
                             $"set security nat source rule-set Cust{tenant.TenantId}_ER rule Cust{tenant.TenantId}_NAT match destination-address 0.0.0.0/0\r\n" +
                             $"set security nat source rule-set Cust{tenant.TenantId}_ER rule Cust{tenant.TenantId}_NAT then source-nat pool Cust{tenant.TenantId}_ToMSFT\r\n";
                   strBackout += $"delete security nat source pool Cust{tenant.TenantId}_ToMSFT\r\n" +
                                 $"delete security nat source rule-set Cust{tenant.TenantId}_ER\r\n";
                }
                strDB += "\r\n";

                // Security NAT Static
                if (HasLabVM)
                {
                    _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Setting static NATs");
                    int intVMIP = 10;                                                               // Starting third octet for IP of the NAT VMs
                    strDB += "# Define Static NATs\r\n";
                    for (int i = 0; i < labVms.Length; i++)
                    {
                        if (labVms[i] != "None")
                        {
                            // v4 Inbound NATs
                            strDB += $"set security nat static rule-set incoming-cust-nat rule Cust{tenant.TenantId}_{intVMIP} match destination-address {strINetNAT}\r\n" +
                                     $"set security nat static rule-set incoming-cust-nat rule Cust{tenant.TenantId}_{intVMIP} match destination-port {tenant.TenantId}{intVMIP}\r\n" +
                                     $"set security nat static rule-set incoming-cust-nat rule Cust{tenant.TenantId}_{intVMIP} then static-nat prefix 10.{strLabOctet}.{tenant.TenantId}.{intVMIP}/32\r\n";

                            if (labVms[i] == "Windows")
                            {
                                strDB += $"set security nat static rule-set incoming-cust-nat rule Cust{tenant.TenantId}_{intVMIP} then static-nat prefix mapped-port 3389\r\n";
                            }
                            else
                            {
                                strDB += $"set security nat static rule-set incoming-cust-nat rule Cust{tenant.TenantId}_{intVMIP} then static-nat prefix mapped-port 22\r\n";
                            }
                           strBackout += $"delete security nat static rule-set incoming-cust-nat rule Cust{tenant.TenantId}_{intVMIP}\r\n";

                            // v6 Inbound NATs
                            //if (HasIPv6)
                            //{
                            //    string strVMPublicIPv6 = strLabPrefix + tenant.TenantId + "::" + intVMIP + "/128";
                            //    strDB += "set security nat static rule-set incoming-cust-nat rule Cust" + tenant.TenantId + "_" + intVMIP + "-v6 match destination-address " + strVMPublicIPv6 + "\r\n" +
                            //             "set security nat static rule-set incoming-cust-nat rule Cust" + tenant.TenantId + "_" + intVMIP + "-v6 match destination-port " + tenant.TenantId + intVMIP + "\r\n" +
                            //             "set security nat static rule-set incoming-cust-nat rule Cust" + tenant.TenantId + "_" + intVMIP + "-v6 then static-nat prefix " + strVMPublicIPv6 + "\r\n";

                            //    if (tenant.GetType().GetProperty("LabVm" + i.ToString()).GetValue(tenant).ToString() == "Windows")
                            //    {
                            //        strDB += "set security nat static rule-set incoming-cust-nat rule Cust" + tenant.TenantId + "_" + intVMIP + "-v6 then static-nat prefix mapped-port 3389\r\n";
                            //    }
                            //    else
                            //    {
                            //        strDB += "set security nat static rule-set incoming-cust-nat rule Cust" + tenant.TenantId + "_" + intVMIP + "-v6 then static-nat prefix mapped-port 22\r\n";
                            //    }
                            //   _strDelete += "delete security nat static rule-set incoming-cust-nat rule Cust" + tenant.TenantId + "_" + intVMIP + "-v6\r\n";
                            //}

                            intVMIP++;
                        }
                    }
                    strDB += "\r\n";
                }

                // Security Policies (C to C, C to I, I to C)
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Setting security policies");
                strDB += "# Define Security Zone policies\r\n" +
                         $"set security policies from-zone Cust{tenant.TenantId} to-zone Cust{tenant.TenantId} policy allow-intrazone match source-address any\r\n" +
                         $"set security policies from-zone Cust{tenant.TenantId} to-zone Cust{tenant.TenantId} policy allow-intrazone match destination-address any\r\n" +
                         $"set security policies from-zone Cust{tenant.TenantId} to-zone Cust{tenant.TenantId} policy allow-intrazone match application any\r\n" +
                         $"set security policies from-zone Cust{tenant.TenantId} to-zone Cust{tenant.TenantId} policy allow-intrazone then permit\r\n" +
                         $"set security policies from-zone Cust{tenant.TenantId} to-zone internet policy allow-outbound match source-address any\r\n" +
                         $"set security policies from-zone Cust{tenant.TenantId} to-zone internet policy allow-outbound match destination-address any\r\n" +
                         $"set security policies from-zone Cust{tenant.TenantId} to-zone internet policy allow-outbound match application any\r\n" +
                         $"set security policies from-zone Cust{tenant.TenantId} to-zone internet policy allow-outbound then permit\r\n" +
                         $"set security policies from-zone internet to-zone Cust{tenant.TenantId} policy allow-inbound-mgmt match source-address any\r\n" +
                         $"set security policies from-zone internet to-zone Cust{tenant.TenantId} policy allow-inbound-mgmt match destination-address any\r\n" +
                         $"set security policies from-zone internet to-zone Cust{tenant.TenantId} policy allow-inbound-mgmt match application junos-rdp\r\n" +
                         $"set security policies from-zone internet to-zone Cust{tenant.TenantId} policy allow-inbound-mgmt match application junos-ssh\r\n" +
                         $"set security policies from-zone internet to-zone Cust{tenant.TenantId} policy allow-inbound-mgmt then permit\r\n\r\n";
                   strBackout += $"delete security policies from-zone Cust{tenant.TenantId} to-zone Cust{tenant.TenantId}\r\n" +
                                 $"delete security policies from-zone Cust{tenant.TenantId} to-zone internet\r\n" +
                                 $"delete security policies from-zone internet to-zone Cust{tenant.TenantId}\r\n\r\n";
                   _strDelete += "#######\r\n### Firewall\r\n#######\r\n\r\n" + strBackout;
                   await SaveToSql($"{strDeviceName}-out", tenant, strBackout);
               }
               else
               {
                   _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Firewall config not required");
                   strDB = "#######\r\n### Firewall\r\n#######\r\n\r\nNeither VPN nor Lab VMs requested, no firewall config required\r\n\r\n";
                  _strDelete += "#######\r\n### Firewall\r\n#######\r\n\r\nNeither VPN nor Lab VMs requested, no firewall config required\r\n\r\n";
                   await SaveToSql($"{strDeviceName}-out", tenant, "");
               }

            bool results = await SaveToSql(strDeviceName, tenant, strDB);
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Firewall Config saved to SQL");
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateFirewallConfig", "Complete");
            return results;
        }

        private async Task<bool> GenerateRouterConfig(Tenant tenant, bool IsPrimary)
        {
            _logger.LogDebug("GenerateRouterConfig: Setting {Type} variables", IsPrimary ? "Primary" : "Secondary");
            // Set variables
            string strDB;
            string strDeviceName;
            if (tenant.Lab == "SEA")
            {
                strDeviceName = (IsPrimary) ? "SEA-MX03-01" : "SEA-MX03-02";
            }
            else
            {
                strDeviceName = (IsPrimary) ? "ASH-ASR06X-01" : "ASH-ASR06X-02";
            }

            string strUplinkInt;    // Interface name for the selected ER uplink
            var lab = GetLabConstants(tenant.Lab);
            string strASN = lab.Asn;

            bool HasIPv6 = tenant.AddressFamily == "IPv6" || tenant.AddressFamily == "Dual";
            bool HasVPNGateway = tenant.Vpngateway != "None";
            bool HasPrivate = tenant.PvtPeering == true;
            bool HasMicrosoft = tenant.Msftpeering == true;

            if (HasPrivate || HasMicrosoft)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Router config required");
                // Add banner, set variables
                string strThirdHextet = lab.Octet;
                string strOuterTag = (tenant.EruplinkPort == "ECX") ? tenant.TenantId.ToString() : "<<Get from ER circuit in Azure>>"; // String containing the outer tag of the ER QinQ tag
                string strMSFTIntIP = "";    // Microsoft Peering Interface IPv4 Address (no mask or CIDR, just the IP)
                string strMSFTNIP = "";      // Microsoft Peering Neighbor  IPv4 Address (no mask or CIDR, just the IP)
                string strPvtIntIP;          // Private   Peering Interface IPv4 Address (no mask or CIDR, just the IP)
                string strPvtNIP;            // Private   Peering Neighbor  IPv4 Address (no mask or CIDR, just the IP)
                string strPvtIntIPv6;        // Private   Peering Interface IPv6 Address (no mask or CIDR, just the IP)
                string strPvtNIPv6;          // Private   Peering Neighbor  IPv6 Address (no mask or CIDR, just the IP)
                string strFWIntIP;           // Firewall  Peering Interface IPv4 Address (no mask or CIDR, just the IP)
                string strFWNIP;             // Firewall  Peering Neighbor  IPv4 Address (no mask or CIDR, just the IP)
                string strFWIntIPv6;         // Firewall  Peering Interface IPv6 Address (no mask or CIDR, just the IP)
                string strFWNIPv6;           // Firewall  Peering Neighbor  IPv6 Address (no mask or CIDR, just the IP)
                if (IsPrimary)
                {
                    strDB = "#######\r\n### Primary Router\r\n#######\r\n\r\n";
                    strPvtIntIP = $"192.168.{tenant.TenantId}.17";
                    strPvtNIP = $"192.168.{tenant.TenantId}.18";
                    strPvtIntIPv6 = $"fd:1:{strThirdHextet}:{tenant.TenantId}FF::1";
                    strPvtNIPv6 = $"fd:1:{strThirdHextet}:{tenant.TenantId}FF::2";
                    strFWIntIP = $"192.168.{tenant.TenantId}.0";
                    strFWNIP = $"192.168.{tenant.TenantId}.1";
                    strFWIntIPv6 = $"fd:2:{strThirdHextet}:{tenant.TenantId}FF::";
                    strFWNIPv6 = $"fd:2:{strThirdHextet}:{tenant.TenantId}FF::1";
                    if (HasMicrosoft)
                    {
                        strMSFTIntIP = tenant.Msftp2p!.Substring(0, tenant.Msftp2p.LastIndexOf('.') + 1) + (int.Parse(tenant.Msftp2p.Substring(tenant.Msftp2p.LastIndexOf('.') + 1, tenant.Msftp2p.LastIndexOf('/') - tenant.Msftp2p.LastIndexOf('.') - 1)) + 1);
                        strMSFTNIP = tenant.Msftp2p.Substring(0, tenant.Msftp2p.LastIndexOf('.') + 1) + (int.Parse(tenant.Msftp2p.Substring(tenant.Msftp2p.LastIndexOf('.') + 1, tenant.Msftp2p.LastIndexOf('/') - tenant.Msftp2p.LastIndexOf('.') - 1)) + 2);
                    }

                   _strDelete += "#######\r\n### Primary Router\r\n#######\r\n\r\n";
                }
                else
                {
                    strDB = "#######\r\n### Secondary Router\r\n#######\r\n\r\n";
                    strPvtIntIP = $"192.168.{tenant.TenantId}.21";
                    strPvtNIP = $"192.168.{tenant.TenantId}.22";
                    strPvtIntIPv6 = $"fd:1:{strThirdHextet}:{tenant.TenantId}FF::5";
                    strPvtNIPv6 = $"fd:1:{strThirdHextet}:{tenant.TenantId}FF::6";
                    strFWIntIP = $"192.168.{tenant.TenantId}.2";
                    strFWNIP = $"192.168.{tenant.TenantId}.3";
                    strFWIntIPv6 = $"fd:2:{strThirdHextet}:{tenant.TenantId}FF::2";
                    strFWNIPv6 = $"fd:2:{strThirdHextet}:{tenant.TenantId}FF::3";
                    if (HasMicrosoft)
                    {
                        strMSFTIntIP = tenant.Msftp2p!.Substring(0, tenant.Msftp2p.LastIndexOf('.') + 1) + (int.Parse(tenant.Msftp2p.Substring(tenant.Msftp2p.LastIndexOf('.') + 1, tenant.Msftp2p.LastIndexOf('/') - tenant.Msftp2p.LastIndexOf('.') - 1)) + 5);
                        strMSFTNIP = tenant.Msftp2p.Substring(0, tenant.Msftp2p.LastIndexOf('.') + 1) + (int.Parse(tenant.Msftp2p.Substring(tenant.Msftp2p.LastIndexOf('.') + 1, tenant.Msftp2p.LastIndexOf('/') - tenant.Msftp2p.LastIndexOf('.') - 1)) + 6);
                    }

                   _strDelete += "#######\r\n### Secondary Router\r\n#######\r\n\r\n";
                }

                // Start the delete config string
                var strBackout = "# Delete VRF and Interfaces\r\n";

                if (tenant.Lab == "SEA")
                {
                    // **************************
                    // ***                    ***
                    // ***   Juniper Config   ***
                    // ***                    ***
                    // **************************
                    _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Starting Juniper Router Config");

                    // Define Uplink Interface from the MX (strUplinkInt)
                    _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Setting uplink interfaces");
                    if (tenant.EruplinkPort == "ECX")
                    {
                        strUplinkInt = "xe-0/0/0:0";
                    }
                    else if (tenant.EruplinkPort == "100G Direct Juniper MSEE")
                    {
                        strUplinkInt = "et-0/1/4";
                    }
                    else if (tenant.EruplinkPort == "100G Direct Cisco MSEE")
                    {
                        strUplinkInt = "et-0/1/1";
                    }
                    else
                    {
                        // Invalid ER uplink detected, using ECX Port
                        _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Invalid ER uplink, using ECX Port");
                        strUplinkInt = "xe-0/0/0:0";
                    }

                    // Create VRF, Internal Interfaces, and Internal BGP
                    _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Setting internal interfaces");
                    strDB += "# Define Internal Interfaces\r\n" +
                             $"set interfaces ae0 unit {tenant.TenantId} description \"Cust{tenant.TenantId} to Firewall\"\r\n" +
                             $"set interfaces ae0 unit {tenant.TenantId} vlan-id {tenant.TenantId}\r\n" +
                             $"set interfaces ae0 unit {tenant.TenantId} family inet address {strFWIntIP}/31\r\n" +
                             (HasIPv6 ? $"set interfaces ae0 unit {tenant.TenantId} family inet6 address {strFWIntIPv6}/127\r\n" : "") +
                             $"set routing-instances Cust{tenant.TenantId} description \"Cust{tenant.TenantId} VRF\"\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} instance-type virtual-router\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} interface ae0.{tenant.TenantId}\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp type internal\r\n" +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp export nhs-vnet\r\n" +
                             (HasVPNGateway ? $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp local-preference 150\r\n" : "") +
                             $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp neighbor {strFWNIP}\r\n";
                    if (HasIPv6)
                    {
                        strDB += $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp6 type internal\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp6 family inet6 unicast\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp6 export nhs-vnet\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ibgp6 neighbor {strFWNIPv6}\r\n";
                    }
                    strDB += "\r\n";

                   strBackout += $"delete interfaces ae0 unit {tenant.TenantId}\r\n" +
                                 $"delete routing-instances Cust{tenant.TenantId}\r\n";

                    // Add Private Peering Interface and BGP if needed
                    if (HasPrivate)
                    {
                        _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Setting ER Private Peering interfaces and BGP");
                        strDB += "# Private Peering Config\r\n" +
                                 $"set interfaces {strUplinkInt} unit {tenant.TenantId}0 description \"Cust{tenant.TenantId} Private Peering to Azure\"\r\n" +
                                 $"set interfaces {strUplinkInt} unit {tenant.TenantId}0 vlan-tags outer {strOuterTag}\r\n" +
                                 $"set interfaces {strUplinkInt} unit {tenant.TenantId}0 vlan-tags inner {tenant.TenantId}0\r\n" +
                                 $"set interfaces {strUplinkInt} unit {tenant.TenantId}0 family inet address {strPvtIntIP}/30\r\n" +
                                 (HasIPv6 ? $"set interfaces {strUplinkInt} unit {tenant.TenantId}0 family inet6 address {strPvtIntIPv6}/126\r\n" : "") +
                                 $"set routing-instances Cust{tenant.TenantId} interface {strUplinkInt}.{tenant.TenantId}0\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp peer-as 12076\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp neighbor {strPvtNIP}\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp bfd-liveness-detection minimum-interval 300\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp bfd-liveness-detection multiplier 3\r\n";
                        if (HasIPv6)
                        {
                            strDB += $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp6 peer-as 12076\r\n" +
                                     $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp6 family inet6 unicast\r\n" +
                                     $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp6 neighbor {strPvtNIPv6}\r\n";
                        }
                        strDB += "\r\n";

                       strBackout += $"delete interfaces {strUplinkInt} unit {tenant.TenantId}0\r\n";
                    }

                    // Add Microsoft Peering Interface and BGP if needed
                    if (HasMicrosoft)
                    {
                        _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Setting ER Microsoft Peering interfaces and BGP");
                        strDB += "# Microsoft Peering Config\r\n" +
                                 $"set interfaces {strUplinkInt} unit {tenant.TenantId}1 description \"Cust{tenant.TenantId} Microsoft Peering to Azure\"\r\n" +
                                 $"set interfaces {strUplinkInt} unit {tenant.TenantId}1 vlan-tags outer {strOuterTag}\r\n" +
                                 $"set interfaces {strUplinkInt} unit {tenant.TenantId}1 vlan-tags inner {tenant.TenantId}1\r\n" +
                                 $"set interfaces {strUplinkInt} unit {tenant.TenantId}1 family inet address {strMSFTIntIP}/30\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} interface {strUplinkInt}.{tenant.TenantId}1\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp peer-as 12076\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp neighbor {strMSFTNIP}\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp bfd-liveness-detection minimum-interval 300\r\n" +
                                 $"set routing-instances Cust{tenant.TenantId} protocols bgp group ebgp bfd-liveness-detection multiplier 3\r\n\r\n";

                       strBackout += $"delete interfaces {strUplinkInt} unit {tenant.TenantId}1\r\n\r\n";
                    }
                }
                else
                {
                    // **************************
                    // ***                    ***
                    // ***    Cisco Config    ***
                    // ***                    ***
                    // **************************
                    _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Starting Cisco Router Config");

                    // Define Uplink Interface from the ASR (strUplinkInt)
                    _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Setting uplink interfaces");
                    if (tenant.EruplinkPort == "ECX")
                    {
                        strUplinkInt = "TenGigabitEthernet0/1/0";
                    }
                    else if (tenant.EruplinkPort == "10G Direct Juniper MSEE")
                    {
                        strUplinkInt = "TenGigabitEthernet0/1/2";
                    }
                    else
                    {
                        // Invalid ER uplink detected, using ECX Port
                        _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Invalid ER uplink, using ECX Port");
                        strUplinkInt = "TenGigabitEthernet0/1/0";
                    }

                    // Add VRF if needed
                    _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Setting VRF");
                    strDB += "# Define VRF\r\n" +
                             $"vrf definition {tenant.TenantId}\r\n" +
                             $" description Cust{tenant.TenantId} VRF\r\n" +
                             $" rd {strASN}:{tenant.TenantId}\r\n" +
                             " address-family ipv4\r\n" +
                             " address-family ipv6\r\n\r\n";

                    // Add Private Peering Interface if needed
                    if (HasPrivate)
                    {
                        _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Setting ER Private Peering interface");
                        strDB += "# Define Private ER Interface\r\n" +
                                 $"interface {strUplinkInt}.{tenant.TenantId}0\r\n" +
                                 $" description Cust{tenant.TenantId} Private Peering to Azure\r\n" +
                                 $" encapsulation dot1Q {strOuterTag} second-dot1q {tenant.TenantId}0\r\n" +
                                 $" vrf forwarding {tenant.TenantId}\r\n" +
                                 $" ip address {strPvtIntIP} 255.255.255.252\r\n" +
                                 (HasIPv6 ? $" ipv6 address {strPvtIntIPv6}/126\r\n" : "") +
                                 " bfd interval 300 min_rx 300 multiplier 3\r\n" +
                                 " no bfd echo\r\n" +
                                 " no shutdown\r\n\r\n";
                       strBackout += $"no interface {strUplinkInt}.{tenant.TenantId}0\r\n";
                    }

                    // Add Microsoft Peering Interface if needed
                    if (HasMicrosoft)
                    {
                        _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Setting ER Microsoft Peering interface");
                        strDB += "# Define Microsoft ER Interface\r\n" +
                                 $"interface {strUplinkInt}.{tenant.TenantId}1\r\n" +
                                 $" description Cust{tenant.TenantId} Microsoft Peering to Azure\r\n" +
                                 $" encapsulation dot1Q {strOuterTag} second-dot1q {tenant.TenantId}1\r\n" +
                                 $" vrf forwarding {tenant.TenantId}\r\n" +
                                 $" ip address {strMSFTIntIP} 255.255.255.252\r\n" +
                                 " bfd interval 300 min_rx 300 multiplier 3\r\n" +
                                 " no bfd echo\r\n" +
                                 " no shutdown\r\n\r\n";
                       strBackout += $"no interface {strUplinkInt}.{tenant.TenantId}1\r\n";
                    }

                    // Add Firewall Config
                    _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Setting Firewall interface");
                    strDB += "# Define Interface to Firewall\r\n" +
                             $"interface Port-channel1.{tenant.TenantId}\r\n" +
                             $" description Cust{tenant.TenantId} to Firewall\r\n" +
                             $" encapsulation dot1Q {tenant.TenantId}\r\n" +
                             $" vrf forwarding {tenant.TenantId}\r\n" +
                             $" ip address {strFWIntIP} 255.255.255.254\r\n" +
                             (HasIPv6 ? $" ipv6 address {strFWIntIPv6}/127\r\n" : "") +
                             " no shutdown\r\n\r\n";
                   strBackout += $"no interface Port-channel1.{tenant.TenantId}\r\n";

                    // Add Route Map
                    if (HasVPNGateway)
                    {
                        _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Setting VPN Route Map");
                        strDB += "# Define Route Map\r\n" +
                                 $"route-map Cust{tenant.TenantId} permit 10\r\n" +
                                 "  set local-preference 150\r\n\r\n";
                       strBackout += $"no route-map Cust{tenant.TenantId}\r\n";
                    }

                    // Add BGP
                    _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Setting BGP");
                    strDB += "# Define BGP Address Family\r\n" +
                             $"router bgp {strASN}\r\n" +
                             $" address-family ipv4 vrf {tenant.TenantId}\r\n" +
                             $"  neighbor {strFWNIP} remote-as {strASN}\r\n" +
                             $"  neighbor {strFWNIP} activate\r\n" +
                             $"  neighbor {strFWNIP} next-hop-self\r\n" +
                             (HasVPNGateway ? $"  neighbor {strFWNIP} route-map Cust{tenant.TenantId} out\r\n" : "");

                    if (HasPrivate)
                    {
                        strDB += $"  neighbor {strPvtNIP} remote-as 12076\r\n" +
                                 $"  neighbor {strPvtNIP} activate\r\n" +
                                 $"  neighbor {strPvtNIP} next-hop-self\r\n" +
                                 $"  neighbor {strPvtNIP} soft-reconfiguration inbound\r\n" +
                                 (HasMicrosoft ? $"  neighbor {strPvtNIP} route-map only-advertise-private out\r\n" : "");
                    }

                    if (HasMicrosoft)
                    {
                        strDB += $"  neighbor {strMSFTNIP} remote-as 12076\r\n" +
                                 $"  neighbor {strMSFTNIP} activate\r\n" +
                                 $"  neighbor {strMSFTNIP} next-hop-self\r\n" +
                                 $"  neighbor {strMSFTNIP} soft-reconfiguration inbound\r\n";
                    }

                    if (HasIPv6 && HasPrivate)
                    {
                        strDB += $" address-family ipv6 vrf {tenant.TenantId}\r\n" +
                                 $"  neighbor {strFWNIPv6} remote-as {strASN}\r\n" +
                                 $"  neighbor {strFWNIPv6} activate\r\n" +
                                 $"  neighbor {strFWNIPv6} next-hop-self\r\n" +
                                 $"  neighbor {strPvtNIPv6} remote-as 12076\r\n" +
                                 $"  neighbor {strPvtNIPv6} activate\r\n" +
                                 $"  neighbor {strPvtNIPv6} next-hop-self\r\n" +
                                 $"  neighbor {strPvtNIPv6} soft-reconfiguration inbound\r\n";
                    }

                    strDB += " exit-address-family\r\n\r\n";
                   strBackout += $"router bgp {strASN}\r\n" +
                                 $" no address-family ipv4 vrf {tenant.TenantId}\r\n" +
                                 $"no vrf definition {tenant.TenantId}\r\n\r\n";
                }
               strBackout += "\r\n";
                _strDelete += strBackout;
                await SaveToSql($"{strDeviceName}-out", tenant, strBackout);
            }
            else
            {
                var routerLabel = IsPrimary ? "Primary" : "Secondary";
                if (IsPrimary)
                {
                    strDB = "#######\r\n### Primary Router\r\n#######\r\n\r\n";
                }
                else
                {
                    strDB = "#######\r\n### Primary Router\r\n#######\r\n\r\n";
                }
                _strDelete += $"#######\r\n### {routerLabel} Router\r\n#######\r\n\r\n" +
                              "ExpressRoute peerings, Pvt or MSFT, were not requested, no router backout config required\r\n\r\n";
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Router config not required");
                strDB += "ExpressRoute peerings, Pvt or MSFT, were not requested, no router config required\r\n\r\n";
                await SaveToSql($"{strDeviceName}-out", tenant, "");
            }
            bool results = await SaveToSql(strDeviceName, tenant, strDB);
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Router Config saved to SQL");
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateRouterConfig", "Complete");
            return results;
        }

        private async Task<bool> GenerateSwitchConfig(Tenant tenant, bool IsPrimary)
        {
            _logger.LogDebug("GenerateSwitchConfig: Setting {Type} variables", IsPrimary ? "Primary" : "Secondary");
            // Set variables
            string strDB;
            string strDeviceName;
            if (tenant.Lab == "SEA")
            {
                strDeviceName = (IsPrimary) ? "SEA-NX9K-01" : "SEA-NX9K-02";
            }
            else
            {
                strDeviceName = (IsPrimary) ? "ASH-NX9K-01" : "ASH-NX9K-02";
            }

            string[] labVms = [tenant.LabVm1 ?? "None", tenant.LabVm2 ?? "None", tenant.LabVm3 ?? "None", tenant.LabVm4 ?? "None"];
            bool HasLabVM = labVms.Any(vm => vm != "None");

            // Add banner, set variables
            var switchLabel = IsPrimary ? "Primary" : "Secondary";
            strDB = $"#######\r\n### {switchLabel} Nexus\r\n#######\r\n\r\n";

            if (HasLabVM)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateSwitchConfig", "Switch config required");
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateSwitchConfig", "Starting Switch Config");
                strDB += "# Define VLAN\r\n" +
                         $"vlan {tenant.TenantId}\r\n" +
                         $"  name Cust{tenant.TenantId}\r\n";
                var strBackout = $"no vlan {tenant.TenantId}\r\n\r\n";
               _strDelete += $"#######\r\n### {switchLabel} Nexus\r\n#######\r\n\r\n" + strBackout;
                await SaveToSql($"{strDeviceName}-out", tenant, strBackout);
            }
            else
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateSwitchConfig", "Lab VMs not requested, no switch config required");
                strDB += "Lab VMs not requested, no switch config required\r\n\r\n";
               _strDelete += $"#######\r\n### {switchLabel} Nexus\r\n#######\r\n\r\nLab VMs not requested, no switch backout config required\r\n\r\n";
                await SaveToSql($"{strDeviceName}-out", tenant, "");
            }
            bool results = await SaveToSql(strDeviceName, tenant, strDB);
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateSwitchConfig", "Switch Config saved to SQL");
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateSwitchConfig", "Complete");
            return results;
        }

        private async Task<bool> GenerateLabVmConfig(Tenant tenant)
        {
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateLabVMConfig", "Starting");
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateLabVMConfig", "Setting variables");
            // Set variables
            string strDB;
            string strServer = $"{tenant.Lab}-ER-{((int)tenant.TenantId / 10):00}";

            strDB = "# Lab VM Config\r\n" +
                    $"# Run this script in elevated PS on ***{strServer}***.\r\n\r\n" +
                    "# The default subscription is ExpressRoute-Lab,\r\n" +
                    "# If the pathfinder subscription is needed add\r\n" +
                    "# -Subscription Pathfinder\r\n" +
                    "# to each New-LabVM call.\r\n" +
                    "#\r\n\r\n";

            if (tenant.TenantId > 99)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateLabVMConfig", "Lab VMs not requested");
                strDB += "# Lab VM Config\r\nNo Lab VMs requested\r\n";
            }
            else
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateLabVMConfig", "Creating Lab VM Instructions");
                string[] labVms = [tenant.LabVm1 ?? "None", tenant.LabVm2 ?? "None", tenant.LabVm3 ?? "None", tenant.LabVm4 ?? "None"];
                int intVMCount = 0;
                foreach (string vm in labVms)
                {
                    if (vm == "Ubuntu")
                    {
                        strDB += $"New-LabVM {tenant.TenantId} -OS Ubuntu\r\n";
                    }
                    else if (vm == "Windows")
                    {
                        strDB += $"New-LabVM {tenant.TenantId}\r\n";
                    }
                    if (vm != "None") { intVMCount++; }
                }

                if (intVMCount == 0)
                {
                    _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateLabVMConfig", "Lab VMs not requested");
                    strDB = "# Lab VM Config\r\nNo Lab VMs requested\r\n";
                }

                if (intVMCount > 0)
                {
                    var strBackout = $"# Run this script in elevated PS on ***{strServer}***.\r\n" +
                                  $"Remove-LabVM {tenant.TenantId}\r\n\r\n";
                   _strDelete += "#######\r\n" +
                                  "### Remove Lab VMs\r\n" +
                                  "#######\r\n" +
                                  strBackout + "\r\n";
                    await SaveToSql("LabVMPowerShell-out", tenant, strBackout);
                }
            }
            bool results = await SaveToSql("LabVMPowerShell", tenant, strDB);
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateLabVMConfig", "Lab VM Instructions saved to SQL");
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateLabVMConfig", "Complete");
            return results;
        }

        private async Task<bool> GenerateEmailConfig(Tenant tenant)
        {
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateEMailConfig", "Starting");
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateEMailConfig", "Setting variables");
            // Set variables
            
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateEMailConfig", "Pulling Azure Region data from SQL");
            Region region = await _context.Regions.FirstOrDefaultAsync(r => r.Region1 == tenant.AzureRegion) ?? throw new InvalidOperationException($"Region '{tenant.AzureRegion}' not found");
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateEMailConfig", "Pulling Ninja data from SQL");
            User ninja = _context.Users.Where(u => u.UserName.Contains(tenant.NinjaOwner)).FirstOrDefault() ?? throw new InvalidOperationException($"Ninja '{tenant.NinjaOwner}' not found");
            string strDB;
            string strRGName = $"{tenant.Lab}-Cust{tenant.TenantId}";
            var lab = GetLabConstants(tenant.Lab);
            string strERLocation = (tenant.Lab == "SEA" ? "Seattle" : "Washington DC");
            string strLabLocationOctect = lab.Octet;
            string strLabVpnEP = lab.VpnIP;
            string strLabPrefixv6 = lab.IPv6Prefix;

            string[] labVms = [tenant.LabVm1 ?? "None", tenant.LabVm2 ?? "None", tenant.LabVm3 ?? "None", tenant.LabVm4 ?? "None"];
            bool HasLabVM = labVms.Any(vm => vm != "None");

            string[] azVms = [tenant.AzVm1 ?? "None", tenant.AzVm2 ?? "None", tenant.AzVm3 ?? "None", tenant.AzVm4 ?? "None"];
            bool HasAzureVM = azVms.Any(vm => vm != "None");

            bool HasER = tenant.Ersku != "None";
            bool HasPrivate = tenant.PvtPeering == true;
            bool HasMicrosoft = tenant.Msftpeering == true;
            bool HasVPNGateway = tenant.Vpngateway != "None";
            bool HasVPNAA = tenant.Vpnconfig == "Active-Active";
            bool HasIPv6 = tenant.AddressFamily == "IPv6" || tenant.AddressFamily == "Dual";

            string strERSpeed = tenant.Erspeed < 1000 ? $"{tenant.Erspeed} Mbps" : $"{tenant.Erspeed / 1000} Gbps";
            string strERRouteFilter = (tenant.Msfttags == "" ? "None requested" : tenant.Msfttags) ?? "None requested";

            string strVPNEndPoint = ResolveVpnEndPoint(tenant);

            // Header
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateEMailConfig", "Creating email header");
            strDB = "<!DOCTYPE HTML>\r\n" +
                    "<html>\r\n" +
                    "<head>\r\n" +
                    "    <title>Your PathLab Environment is Ready!</title>\r\n" +
                    "    <!-- For the Style section, the following abbreviations are used\r\n" +
                    "         l1     List part one; the bullet cell\r\n" +
                    "         l2     List part two; the text after the bullet cell\r\n" +
                    "         i      Indent; indents cell text\r\n" +
                    "         b      border; adds borders to cell\r\n" +
                    "         s      section; format for a section spans\r\n" +
                    "         h      header; format for banner spans\r\n" +
                    "    -->\r\n" +
                    "    <style>\r\n" +
                    "        body   {font-size: 12pt;font-family: 'Calibri', sans-serif;}\r\n" +
                    "        table  {border-collapse: collapse;border: 0px;}\r\n" +
                    "        td     {padding: 5px 5px 5px 5px;}\r\n" +
                    "        td.l1  {text-align: right;width:10px;vertical-align: text-top;font-size: 20px;font-weight: bold;}\r\n" +
                    "        td.l2  {text-align: left;width:640px}\r\n" +
                    "        td.i   {text-indent: 20px;}\r\n" +
                    "        td.b   {border: solid 1pt;text-align: center;border-color: black;}\r\n" +
                    "        th     {padding: 5px 5px 5px 5px;border: solid 1pt;border-color: black;background: #0070C0;color: white;text-align: center;font-weight:bold;}\r\n" +
                    "        span.s {font-size: 16pt;color: #0070C0;font-weight:bold;}\r\n" +
                    "        span.h {font-size: 40pt;color: white;font-family: Arial, sans-serif;}\r\n" +
                    "    </style>\r\n" +
                    "</head>\r\n\r\n" +
                    "<body>\r\n" +
                    "    <table>\r\n" +
                    "        <tr height=150>\r\n" +
                    "            <td style='width:160px;background:#0070C0;padding:15px 0px 5px 20px;'><img src='https://microsoft.sharepoint.com/teams/Pathfinders/SiteAssets/PFLogoEMail.png' alt='Pathfinder Badge'></td>\r\n" +
                    "            <td style='width:600px;background:#0070C0;padding:0px 15px 0px 0px;'>\r\n" +
                    "                <p align=center><b><span class='h'>Your lab environment</span></b></p>\r\n" +
                    "                <p align=center><b><span class='h'>has been created!</span></b></p>\r\n" +
                    "            </td>\r\n" +
                    "        </tr>\r\n" +
                    "    </table>\r\n\r\n" +
                    "    <!-- Header -->\r\n" +
                    "    <br/><span class='s'>Information to access your resources is below</span><br/><br/>\r\n" +
                    "    <table width=780 style='text-align: justify'>\r\n" +
                    "        <tr>\r\n" +
                    "            <td>Congratulations, your PathLab environment is active and ready to go, however you must finish use of this environment by\r\n" +
                    $"                <b>{tenant.ReturnDate.ToString("d")}</b>, after that date your environment and all data therein will be removed with no backup or retention of data\r\n" +
                    "                or config.</td>\r\n" +
                    "        </tr>\r\n" +
                    "        <tr>\r\n" +
                    "            <td>It's also important to note that this is a shared lab environment, and as such is subject to congestion, downtime, and other\r\n" +
                    "                conditions of limited or complete unavailability. It's critical that this is taken into account when planning your testing\r\n" +
                    "                and/or experiments, and have the ability to restart, reset, etc any activities performed in this environment.</td>\r\n" +
                    "        </tr>\r\n" +
                    "        <tr>\r\n" +
                    "            <td><em><strong>Disclaimer:</strong>&nbsp;&nbsp; No customer, PII, MBI or higher data is allowed in the lab. All Microsoft standards\r\n" +
                    "                of business conduct and Microsoft Employee Handbook policies apply to these Microsoft owned and managed assets. Any data that\r\n" +
                    "                needs to be preserved should not be stored in the lab, assume that any lab environment can be deleted at any time.</em></td>\r\n" +
                    "        </tr>\r\n" +
                    "        <tr><td><mark>&nbsp;<b>There is no SLA or up-time expectation with this lab!</b></mark></td></tr>\r\n" +
                    "    </table>\r\n" +
                    "    <br/>\r\n\r\n";
            // Azure Info
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateEMailConfig", "Creating Azure Info");
            strDB += "    <!-- Azure Info -->\r\n" +
                     $"    <span class='s'>Azure Info\r\n" +
                     $"                (<a href='https://ms.portal.azure.com/#@microsoft.onmicrosoft.com/resource/subscriptions/{StrSubID}/resourceGroups/{strRGName}/overview'>Azure Portal</a>)</span><br/>\r\n" +
                     "    <table>\r\n" +
                     $"        <tr><td width=150><b>Resource Group</b></td><td width=450>{strRGName}</td></tr>\r\n" +
                     $"        <tr><td><b>Azure Region</b></td><td>{tenant.AzureRegion}</td></tr>\r\n" +
                     $"        <tr style='padding-bottom: 15px;'><td><b>Authorized Users</b></td><td width=450>{tenant.Contacts}</td></tr>\r\n" +
                     "    </table>\r\n";

            if (HasAzureVM)
            {
                strDB += "    <table>\r\n" +
                         "        <tr><th width=170>VM Name</th><th width=130>OS</th><th width=200>RDP/SSH IP</th><th width=150>Private IP</th></tr>\r\n";

                int intVMCount = 0;
                foreach (string vm in azVms)
                {
                    if (vm != "None")
                    {
                        intVMCount++;
                        string strPublicIP = $"{strRGName}-VM0{intVMCount}-pip4";
                        string strPublicIPv6 = $"{strRGName}-VM0{intVMCount}-pip6";
                        string strPrivateIP = $"10.{region.Ipv4}.{tenant.TenantId}.{intVMCount + 3}";
                        string strPrivateIPv6 = $"fd:0:{region.Ipv6}:{tenant.TenantId}00::{intVMCount + 3}";
                        strDB += "        " +
                                 $"<tr><td class='b'>{strRGName}-VM0{intVMCount}</td>" +
                                 $"<td class='b'>{vm}</td>" +
                                 $"<td class='b'>{strPublicIP}{(HasIPv6 ? $"<br/>{strPublicIPv6}" : "")}</td>" +
                                 $"<td class='b'>{strPrivateIP}{(HasIPv6 ? $"<br/>{strPrivateIPv6}" : "")}</td></tr>\r\n";
                    }
                }
                strDB += "    </table>\r\n" +
                         "    <br/>\r\n" +
                         "    See &quot;User info&quot; below for Username and Password\r\n";
            }
            strDB += "    <hr/>\r\n\r\n";

            // ExpressRoute Info
            if (HasER)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateEMailConfig", "Creating ExpressRoute Info");
                strDB += "    <!-- ExpressRoute Info -->\r\n" +
                         "    <span class='s'>ExpressRoute Circuit Info:</span><br/>\r\n" +
                         "    <table>\r\n" +
                         $"        <tr><td width=150><b>SKU</b></td><td width=450>{tenant.Ersku}</td></tr>\r\n" +
                         $"        <tr><td><b>Uplink/Provider</b></td><td>{tenant.EruplinkPort}</td></tr>\r\n" +
                         $"        <tr><td><b>Location</b></td><td>{strERLocation}</td></tr>\r\n" +
                         $"        <tr><td><b>Speed</b></td><td>{strERSpeed}</td></tr>\r\n";

                if (HasPrivate)
                {
                    strDB += $"        <tr><td><b>ER Gateway Size</b></td><td>{tenant.ErgatewaySize}</td></tr>\r\n";

                }
                strDB += $"        <tr><td><b>Private Peering</b></td><td>{(tenant.PvtPeering == true ? "Enabled" : "Not Enabled")}</td></tr>\r\n" +
                         $"        <tr><td><b>Microsoft Peering</b></td><td>{(tenant.Msftpeering == true ? "Enabled" : "Not Enabled")}</td></tr>\r\n";

                if (HasMicrosoft)
                {
                    strDB += $"        <tr><td class='i'><b>&bull; Route Filter</b></td><td>{strERRouteFilter}</td></tr>\r\n" +
                             $"        <tr><td class='i'><b>&bull; NAT Address</b></td><td>{tenant.Msftadv}</td></tr>\r\n";

                }
                strDB += "    </table>\r\n" +
                         "    <hr/>\r\n\r\n";
            }

            // VPN Info
            if (HasVPNGateway)
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateEMailConfig", "Creating VPN Info");
                string strAzureVNet = $"10.{region.Ipv4}.{tenant.TenantId}.0/24";
                string strLabVNet = $"10.{strLabLocationOctect}.{tenant.TenantId}.0/25";
                strDB += "    <!-- VPN Info -->\r\n" +
                         "    <span class='s'>VPN Info:</span><br/>\r\n" +
                         "    <table>\r\n" +
                         $"        <tr><td width=150><b>Gateway Size</b></td><td width=450>{tenant.Vpngateway}</td></tr>\r\n" +
                         $"        <tr><td><b>BGP</td><td>{tenant.Vpnbgp}</td></tr>\r\n" +
                         $"        <tr><td><b>Configuration</td><td>{tenant.Vpnconfig}</td></tr>\r\n" +
                         $"        <tr><td><b>Azure Endpoint(s)</b></td><td>{(tenant.Vpnconfig == "Active-Active" ? strVPNEndPoint : strVPNEndPoint.Split(',')[0])}</td></tr>\r\n" +
                         $"        <tr><td><b>Azure Subnet</td><td>{strAzureVNet}</td></tr>\r\n" +
                         $"        <tr><td><b>Lab Endpoint</td><td>{strLabVpnEP}</td></tr>\r\n" +
                         $"        <tr><td><b>Lab Subnet</td><td>{strLabVNet}</td></tr>\r\n" +
                         "    </table>\r\n" +
                         "    <hr/>\r\n\r\n";
            }

            // Lab Info
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateEMailConfig", "Creating Lab Info");
            strDB += "    <!-- Lab Info -->\r\n" +
                     "    <span class='s'>Lab Info:</span><br/>\r\n" +
                     "    <table width=650>\r\n" +
                     $"            <tr><td width=150><b>Location</b></td><td width=450>{(tenant.Lab == "SEA" ? "Seattle" : "Ashburn")}</td></tr>\r\n" +
                     "    </table>\r\n";

            if (HasLabVM)
            {
                strDB += "    <table>\r\n" +
                         "        <tr><th width=170>VM Name</th><th width=130>OS</th><th width=200>RDP/SSH IP</th><th width=150>Private IP</th></tr>\r\n";

                int intVMCount = 0;
                foreach (string vm in labVms)
                {
                    if (vm != "None")
                    {
                        intVMCount++;
                        string strPublicIP = $"{tenant.Lab.ToLower()}.pathlab.xyz:{tenant.TenantId}{intVMCount + 9}";
                        string strPrivateIP = $"10.{strLabLocationOctect}.{tenant.TenantId}.{intVMCount + 9}";
                        string strPrivateIPv6 = $"{strLabPrefixv6}{tenant.TenantId}::{intVMCount + 9}";

                        strDB += "        " +
                                 $"<tr><td class='b'>{tenant.Lab}-ER-{tenant.TenantId}-VM0{intVMCount}</td>" +
                                 $"<td class='b'>{vm}</td>" +
                                 $"<td class='b'>{strPublicIP}</td>" +
                                 $"<td class='b'>{strPrivateIP}{(HasIPv6 ? $"<br/>{strPrivateIPv6}" : "")}</td></tr>\r\n";
                    }
                }
                strDB += "    </table>\r\n" +
                         "    <br/>\r\n" +
                         "    See &quot;User info&quot; below for Username and Password\r\n";
            }
            strDB += "    <hr/>\r\n\r\n";

            // User Info
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateEMailConfig", "Creating User Info");
            strDB += "    <!-- User Info -->\r\n" +
                     "    <span class='s'>User Info:</span><br/>\r\n" +
                     "    For any servers in this environment, Lab or Azure, use the following local administrator account:\r\n" +
                     "    <table>\r\n" +
                     "        <tr><td width=150><b>User Name</b></td><td width=450>PathLabUser</td></tr>\r\n" +
                     $"        <tr><td><b>Password</b></td><td><a href='https://ms.portal.azure.com/#@microsoft.onmicrosoft.com/asset/Microsoft_Azure_KeyVault/Secret/https://{strRGName.ToLower()}-kv.vault.azure.net/secrets/PathLabUser/'>Key Vault</a></td></tr>\r\n" +
                     "    </table>\r\n" +
                     "    <table>\r\n" +
                     "        <tr><td colspan=2 width=650>To retrieve the password:</td></tr>\r\n" +
                     $"        <tr><td class='l1'>&bull;</td><td class='l2'>Click the <a href='https://ms.portal.azure.com/#@microsoft.onmicrosoft.com/asset/Microsoft_Azure_KeyVault/Secret/https://" +
                              $"{strRGName.ToLower()}-kv.vault.azure.net/secrets/PathLabUser/'>Key Vault</a> link above which will launch the Azure Portal (the accounts listed as &quot;Authorized Users&quot; above should have " +
                              $"access to the resource group and the Key Vault, {strRGName}-kv, linked above)</td></tr>\r\n" +
                     "        <tr><td class='l1'>&bull;</td><td class='l2'>The link will take you to the Key Vault and directly to the Secret named PathLabUser</td></tr>\r\n" +
                     "        <tr><td class='l1'>&bull;</td><td class='l2'>Click the &quot;Show secret value&quot; button to display the password</td></tr>\r\n" +
                     "        <tr><td class='l1'>&bull;</td><td class='l2'>Click the copy icon to the right of the password to load the password into the clipboard</td></tr>\r\n" +
                     "    </table>\r\n" +
                     "    <hr/>\r\n\r\n";

            // Footer
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateEMailConfig", "Creating email footer");
            strDB += "    <!-- Footer -->\r\n" +
                     "    <span class='s'>Questions or Issues?</span><br/>\r\n" +
                     $"    If you have issues or question please contact {ninja.Name} (<a href='mailto:{ninja.UserName}?subject=Lab%20{strRGName}%20Question'>send mail</a>).<br/>\r\n" +
                     "    <p>Thanks,</p>\r\n" +
                     "    <p>Pathfinders Lab Team</p>\r\n" +
                     "</body>\r\n" +
                     "</html>\r\n";
            bool results = await SaveToSql("eMailHTML", tenant, strDB);
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateEMailConfig", "Email HTML saved to SQL");
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GenerateEMailConfig", "Complete");
            return results;
        }

        private async Task<bool> SaveToSql(string configType, Tenant tenant, string strDB, bool backout = false)
        {
            if (backout)
            {
                var configs = _context.Configs.Where(c => c.TenantGuid == tenant.TenantGuid && c.TenantVersion == tenant.TenantVersion);
                _context.Configs.RemoveRange(configs);
                await _context.SaveChangesAsync();
            }
            else
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.SaveToSQL", "Saving config changes to SQL");
                Config config = new Config
                {
                    ConfigId = Guid.NewGuid(),
                    ConfigType = configType,
                    TenantGuid = tenant.TenantGuid,
                    TenantId = tenant.TenantId,
                    TenantVersion = tenant.TenantVersion,
                    NinjaOwner = tenant.NinjaOwner,
                    Config1 = strDB,
                    CreatedDate = DateTime.Now,
                    CreatedBy = _userEmail.Split('@')[0]
                };
                _context.Configs.Add(config);
                await _context.SaveChangesAsync();
            }
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.SaveToSQL", "Complete");
            return true;
        }

        private string GetP2P(string strPrefix, bool isPrimary)
        {
            string myPrefix;
            int intSlash;
            int intDot;
            int intNext;

            if (isPrimary)
            {
                myPrefix = $"{strPrefix.Substring(0, strPrefix.IndexOf("/"))}/30";
            }
            else
            {
                intSlash = strPrefix.IndexOf("/");
                intDot = strPrefix.LastIndexOf(".") + 1;
                intNext = int.Parse(strPrefix.Substring(intDot, intSlash - intDot)) + 4;
                myPrefix = $"{strPrefix.Substring(0, intDot)}{intNext}/30";
            }
            _logger.LogDebug("GetP2P: {Type} P2P Set ({Prefix})", isPrimary ? "Primary" : "Secondary", myPrefix);
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.GetP2P", "Complete");
            return myPrefix;
        }

        private async Task<string> AssignPublicIp(Tenant tenant, bool isP2P)
        {
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.AssignPublicIP", "Starting");
            
            if (tenant.Ersku != "None" && tenant.Msftpeering == true && tenant.TenantId < 100) // Am I assigning or releasing? 
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.AssignPublicIP", "Assigning");
                string strQueryType = isP2P ? "P2P" : "NAT";
                string strDevice;
                string strPurpose;
                if (isP2P)
                {
                    strDevice = tenant.Lab == "SEA" ? "SEA-MX03-01/02" : "ASH-ASR06X-01/02";
                    strPurpose = $"Cust {tenant.TenantId} MSFT Peering";
                }
                else
                {
                    strDevice = tenant.Lab == "SEA" ? "SEA-SRX42-01" : "ASH-SRX42-01";
                    strPurpose = $"Cust {tenant.TenantId} MSFT NAT";
                }

                _logger.LogDebug("{Method}: {Msg}", "AppLogic.AssignPublicIP", "Pulling Public IPs from SQL");
                string? strRange = _context.PublicIps.Where(ip => ip.RangeType == strQueryType && ip.TenantGuid == tenant.TenantGuid).Select(ip => ip.Range).FirstOrDefault();
                if (strRange != null)
                {
                    _logger.LogDebug("{Method}: {Msg}", "AppLogic.AssignPublicIP", "Assigned range found, setting tenant to found range");
                    if (isP2P)
                    {
                        tenant.Msftp2p = strRange;
                    }
                    else
                    {
                        tenant.Msftadv = strRange;
                    }
                }
                else
                {
                    _logger.LogDebug("{Method}: {Msg}", "AppLogic.AssignPublicIP", "Assigned range not found, pulling next available range");
                    PublicIp? ipRange = _context.PublicIps.Where(ip => ip.RangeType == strQueryType && ip.Lab == tenant.Lab && ip.AssignedBy == null).FirstOrDefault();
                    if (ipRange != null)
                    {
                        if (isP2P)
                        {
                            tenant.Msftp2p = ipRange.Range;
                        }
                        else
                        {
                            tenant.Msftadv = ipRange.Range;
                        }
                        ipRange.Device = strDevice;
                        ipRange.Purpose = strPurpose;
                        ipRange.TenantGuid = tenant.TenantGuid;
                        ipRange.TenantId = tenant.TenantId;
                        ipRange.AssignedDate = DateTime.Now;
                        ipRange.AssignedBy = _userEmail.Split('@')[0];
                        _logger.LogDebug("{Method}: {Msg}", "AppLogic.AssignPublicIP", "Saving new range assignment to PublicIP table in SQL");
                        
                        await _context.SaveChangesAsync();
                    }
                    else
                    {
                        _logger.LogDebug("{Method}: {Msg}", "AppLogic.AssignPublicIP", "No available ranges to be assinged");
                        if (isP2P)
                        {
                            return "No Available P2P";
                        }
                        else
                        {
                            return "No Available NAT";
                        }
                    }
                }
            }
            else
            {
                _logger.LogDebug("{Method}: {Msg}", "AppLogic.AssignPublicIP", "Releasing");
                await ClearPublicIps(tenant.TenantGuid);
                tenant.Msftp2p = null;
                tenant.Msftadv = null;
            }
            _logger.LogDebug("{Method}: {Msg}", "AppLogic.AssignPublicIP", "Complete");
            return "All Good";
        }

            private async Task ClearPublicIps(Guid id)
            {
                var lstRanges = _context.PublicIps.Where(ip => ip.TenantGuid == id).ToList();
                foreach (var range in lstRanges)
                {
                    range.Device = null;
                    range.Purpose = null;
                    range.TenantGuid = null;
                    range.TenantId = null;
                    range.AssignedDate = null;
                    range.AssignedBy = null;
                }
                await _context.SaveChangesAsync();
            }


        }