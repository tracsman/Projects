using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PathWeb.Data;
using PathWeb.Models;
using PathWeb.Services;

namespace PathWeb.Areas.Tenants.Controllers
{
    [Area("Tenants")]
    public class HomeController : Controller
    {
        private readonly LabConfigContext _context;
        private readonly ILogger<HomeController> _logger;
        private readonly ConfigGenerator _configGenerator;

        public HomeController(LabConfigContext context, ILogger<HomeController> logger, ConfigGenerator configGenerator)
        {
            _context = context;
            _logger = logger;
            _configGenerator = configGenerator;
        }

        private byte GetAuthLevel() => (byte)(HttpContext.Items["AuthLevel"] ?? (byte)0);
        private string GetUserEmail() => User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                                         ?? User.Identity?.Name ?? "unknown";

        public async Task<IActionResult> Index(string? sortOrder, string? searchString)
        {
            if (GetAuthLevel() < (byte)AuthLevels.TenantReadOnly)
            {
                _logger.LogWarning("Permission denied for Tenants.Index, user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            ViewData["LabSortParm"] = sortOrder == "Lab" ? "Lab_desc" : "Lab";
            ViewData["TenantSortParm"] = sortOrder == "Tenant" ? "Tenant_desc" : "Tenant";
            ViewData["NinjaSortParm"] = sortOrder == "Ninja" ? "Ninja_desc" : "Ninja";
            ViewData["DateSortParm"] = sortOrder == "Date" ? "Date_desc" : "Date";
            ViewData["UsageSortParm"] = sortOrder == "Usage" ? "Usage_desc" : "Usage";
            ViewData["CurrentSearch"] = searchString;

            var tenants = _context.Tenants.Where(t => t.DeletedDate == null).AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                tenants = tenants.Where(t => t.Lab.Contains(searchString)
                    || t.TenantId.ToString().Contains(searchString)
                    || t.NinjaOwner.Contains(searchString)
                    || (t.Contacts != null && t.Contacts.Contains(searchString))
                    || t.Usage.Contains(searchString));
            }

            tenants = sortOrder switch
            {
                "Lab_desc" => tenants.OrderByDescending(t => t.Lab).ThenByDescending(t => t.TenantId),
                "Tenant" => tenants.OrderBy(t => t.TenantId),
                "Tenant_desc" => tenants.OrderByDescending(t => t.TenantId),
                "Ninja" => tenants.OrderBy(t => t.NinjaOwner).ThenBy(t => t.Lab).ThenBy(t => t.TenantId),
                "Ninja_desc" => tenants.OrderByDescending(t => t.NinjaOwner).ThenByDescending(t => t.Lab).ThenByDescending(t => t.TenantId),
                "Date" => tenants.OrderBy(t => t.ReturnDate),
                "Date_desc" => tenants.OrderByDescending(t => t.ReturnDate),
                "Usage" => tenants.OrderBy(t => t.Usage),
                "Usage_desc" => tenants.OrderByDescending(t => t.Usage),
                _ => tenants.OrderBy(t => t.Lab).ThenBy(t => t.TenantId),
            };

            _logger.LogInformation("Tenants.Index requested by {User}, sort: {Sort}, search: {Search}", GetUserEmail(), sortOrder ?? "default", searchString ?? "none");
            return View(await tenants.ToListAsync());
        }

        public async Task<IActionResult> Details(Guid? id)
        {
            if (GetAuthLevel() < (byte)AuthLevels.TenantReadOnly)
            {
                _logger.LogWarning("Permission denied for Tenants.Details, user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            if (id == null)
            {
                TempData["Message"] = "Request missing required Tenant GUID!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == id);
            if (tenant == null)
            {
                TempData["Message"] = "Invalid Tenant GUID requested!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            // Look up Ninja display name
            var ninjaUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName.StartsWith(tenant.NinjaOwner + "@") || u.UserName == tenant.NinjaOwner);
            ViewData["NinjaDisplayName"] = ninjaUser?.Name ?? tenant.NinjaOwner;

            _logger.LogInformation("Tenants.Details for {TenantName} by {User}", tenant.TenantName, GetUserEmail());
            return View(tenant);
        }

        public async Task<IActionResult> Create()
        {
            if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            {
                _logger.LogWarning("Permission denied for Tenants.Create, user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            await SetDropDowns();

            var tenant = new Tenant
            {
                TenantGuid = Guid.NewGuid(),
                TenantId = 0,
                TenantVersion = 0,
                ReturnDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
                Ersku = "None",
                Erspeed = 50,
                EruplinkPort = "ECX",
                PvtPeering = false,
                ErgatewaySize = "None",
                ErfastPath = false,
                Msftpeering = false,
                Vpngateway = "None",
                Vpnbgp = true,
                Vpnconfig = "Active-Passive",
                VpnendPoint = "TBD,N/A",
                AzVm1 = "None",
                AzVm2 = "None",
                AzVm3 = "None",
                AzVm4 = "None",
                AddressFamily = "IPv4",
                LabVm1 = "None",
                LabVm2 = "None",
                LabVm3 = "None",
                LabVm4 = "None",
                WorkItemId = 0,
                AssignedDate = DateTime.Now,
                AssignedBy = GetUserEmail(),
                LastUpdateDate = DateTime.Now,
                LastUpdateBy = GetUserEmail()
            };

            _logger.LogInformation("Tenants.Create page requested by {User}", GetUserEmail());
            return View(tenant);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Tenant tenant)
        {
            if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            {
                _logger.LogWarning("Permission denied for Tenants.Create [POST], user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            if (ModelState.IsValid)
            {
                // Auto-assign next available TenantID for the selected lab
                tenant.TenantId = await GetNextTenantId(tenant.Lab);
                tenant.TenantGuid = Guid.NewGuid();
                tenant.AssignedDate = DateTime.Now;
                tenant.AssignedBy = GetUserEmail();
                tenant.LastUpdateDate = DateTime.Now;
                tenant.LastUpdateBy = GetUserEmail();

                _context.Tenants.Add(tenant);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Tenant created: {TenantName} by {User}", tenant.TenantName, GetUserEmail());
                TempData["Message"] = $"Tenant {tenant.TenantName} was successfully created!";
                TempData["MessageLevel"] = "success";
                return RedirectToAction(nameof(Details), new { id = tenant.TenantGuid });
            }

            await SetDropDowns();
            return View(tenant);
        }

        public async Task<IActionResult> Edit(Guid? id)
        {
            if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            {
                _logger.LogWarning("Permission denied for Tenants.Edit, user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            if (id == null)
            {
                TempData["Message"] = "Request missing required Tenant GUID!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            var tenant = await _context.Tenants.FindAsync(id);
            if (tenant == null)
            {
                TempData["Message"] = "Invalid Tenant GUID requested!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            await SetDropDowns();
            _logger.LogInformation("Tenants.Edit page for {TenantName} by {User}", tenant.TenantName, GetUserEmail());
            return View(tenant);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, Tenant tenant)
        {
            if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            {
                _logger.LogWarning("Permission denied for Tenants.Edit [POST], user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            if (id != tenant.TenantGuid)
                return NotFound();

            if (ModelState.IsValid)
            {
                tenant.LastUpdateDate = DateTime.Now;
                tenant.LastUpdateBy = GetUserEmail();

                try
                {
                    _context.Update(tenant);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Tenant updated: {TenantName} by {User}", tenant.TenantName, GetUserEmail());
                    TempData["Message"] = $"Tenant {tenant.TenantName} was successfully updated!";
                    TempData["MessageLevel"] = "success";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Tenants.AnyAsync(t => t.TenantGuid == tenant.TenantGuid))
                        return NotFound();
                    throw;
                }
                return RedirectToAction(nameof(Details), new { id = tenant.TenantGuid });
            }

            await SetDropDowns();
            return View(tenant);
        }

        public async Task<IActionResult> Release(Guid? id)
        {
            if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            {
                _logger.LogWarning("Permission denied for Tenants.Release, user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            if (id == null)
            {
                TempData["Message"] = "Request missing required Tenant GUID!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == id);
            if (tenant == null)
            {
                TempData["Message"] = "Invalid Tenant GUID requested!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            _logger.LogInformation("Tenants.Release confirmation for {TenantName} by {User}", tenant.TenantName, GetUserEmail());
            return View(tenant);
        }

        [HttpPost, ActionName("Release")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReleaseConfirmed(Guid id)
        {
            if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            {
                _logger.LogWarning("Permission denied for Tenants.Release [POST], user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            var tenant = await _context.Tenants.FindAsync(id);
            if (tenant == null)
            {
                TempData["Message"] = "Invalid Tenant GUID requested!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            // Release associated public IPs
            var publicIps = await _context.PublicIps.Where(ip => ip.TenantGuid == id).ToListAsync();
            foreach (var ip in publicIps)
            {
                _logger.LogInformation("Releasing IP {Range} from tenant {TenantName}", ip.Range, tenant.TenantName);
                ip.Device = null;
                ip.Purpose = null;
                ip.TenantGuid = null;
                ip.TenantId = null;
                ip.AssignedDate = null;
                ip.AssignedBy = null;
            }

            // Mark tenant as released
            tenant.DeletedDate = DateTime.Now;
            tenant.DeletedBy = GetUserEmail();
            tenant.LastUpdateDate = DateTime.Now;
            tenant.LastUpdateBy = GetUserEmail();

            await _context.SaveChangesAsync();

            _logger.LogInformation("Tenant released: {TenantName}, {IpCount} IPs freed, by {User}", tenant.TenantName, publicIps.Count, GetUserEmail());
            TempData["Message"] = $"Tenant {tenant.TenantName} and {publicIps.Count} associated public IP(s) have been released.";
            TempData["MessageLevel"] = "success";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Config(Guid? id)
        {
            if (GetAuthLevel() < (byte)AuthLevels.TenantReadOnly)
            {
                _logger.LogWarning("Permission denied for Tenants.Config, user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            if (id == null)
            {
                TempData["Message"] = "Request missing required Tenant GUID!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == id);
            if (tenant == null)
            {
                TempData["Message"] = "Invalid Tenant GUID requested!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            var configs = await _context.Configs
                .Where(c => c.TenantGuid == id && c.TenantVersion == tenant.TenantVersion)
                .ToListAsync();

            ViewData["TenantGuid"] = id;
            ViewData["TenantName"] = tenant.TenantName;
            ViewData["Contacts"] = tenant.Contacts;

            _logger.LogInformation("Tenants.Config for {TenantName} ({ConfigCount} configs) by {User}", tenant.TenantName, configs.Count, GetUserEmail());
            return View(configs);
        }

        public async Task<IActionResult> ConfigGen(Guid? id)
        {
            if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            {
                _logger.LogWarning("Permission denied for Tenants.ConfigGen, user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            if (id == null)
            {
                TempData["Message"] = "Request missing required Tenant GUID!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == id);
            if (tenant == null)
            {
                TempData["Message"] = "Invalid Tenant GUID requested!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            _logger.LogInformation("Tenants.ConfigGen for {TenantName} by {User}", tenant.TenantName, GetUserEmail());

            var messages = await _configGenerator.GenerateConfigAsync(id.Value, GetUserEmail());

            if (messages.Count == 1 && messages[0].Contains("generated and saved"))
            {
                TempData["Message"] = messages[0];
                TempData["MessageLevel"] = "success";
            }
            else
            {
                TempData["Message"] = string.Join("<br/>", messages);
                TempData["MessageLevel"] = "danger";
            }

            return RedirectToAction(nameof(Config), new { id });
        }

        private async Task<short> GetNextTenantId(string lab)
        {
            var usedIds = await _context.Tenants
                .Where(t => t.Lab == lab && t.DeletedDate == null)
                .Select(t => t.TenantId)
                .ToListAsync();

            // Start at 10, skip 1 (reserved for no-lab-resources)
            for (short id = 10; id < 1000; id++)
            {
                if (!usedIds.Contains(id))
                    return id;
            }
            return (short)(usedIds.Max() + 1);
        }

        private async Task SetDropDowns()
        {
            // Labs
            ViewBag.Labs = new SelectList(new[]
            {
                new { Value = "SEA", Text = "Seattle" },
                new { Value = "ASH", Text = "Ashburn" }
            }, "Value", "Text");

            // Tenant Types (radio buttons in old site, used for TenantID type selection)
            ViewBag.TenantTypes = new SelectList(new[]
            {
                new { Value = "0", Text = "Needs lab resources (e.g. Lab VMs and/or routers/firewall for ER or VPN)" },
                new { Value = "1", Text = "No lab resources needed (No lab resources will be configured!)" }
            }, "Value", "Text");

            // Bool options for dropdowns
            ViewBag.BoolOptions = new SelectList(new[]
            {
                new { Value = "True", Text = "Enabled" },
                new { Value = "False", Text = "Not Enabled" }
            }, "Value", "Text");

            // Ninjas from Users table (where Ninja = true)
            var ninjas = await _context.Users
                .Where(u => u.Ninja)
                .OrderBy(u => u.Name)
                .Select(u => new { Value = u.UserName.Split('@')[0], Text = u.Name })
                .ToListAsync();
            ViewBag.Ninjas = new SelectList(ninjas, "Value", "Text");

            // Regions from DB
            var regions = await _context.Regions
                .OrderBy(r => r.Region1)
                .Select(r => r.Region1)
                .ToListAsync();
            ViewBag.Regions = new SelectList(regions);

            // ER SKUs
            ViewBag.ErSkus = new SelectList(new[] { "None", "Standard", "Premium", "Local" });

            // ER Speeds
            ViewBag.ErSpeeds = new SelectList(new[]
            {
                new { Value = "50", Text = "50 Mbps" },
                new { Value = "100", Text = "100 Mbps" },
                new { Value = "200", Text = "200 Mbps" },
                new { Value = "500", Text = "500 Mbps" },
                new { Value = "1000", Text = "1 Gbps" },
                new { Value = "2000", Text = "2 Gbps" },
                new { Value = "5000", Text = "5 Gbps" },
                new { Value = "10000", Text = "10 Gbps" },
                new { Value = "40000", Text = "40 Gbps" },
                new { Value = "100000", Text = "100 Gbps" }
            }, "Value", "Text");

            // ER Uplinks per lab
            ViewBag.ErUplinksSea = new SelectList(new[] { "ECX", "100G Direct Juniper MSEE", "100G Direct Cisco MSEE", "10G Direct Juniper MSEE" });
            ViewBag.ErUplinksAsh = new SelectList(new[] { "ECX" });

            // ER Gateways
            ViewBag.ErGateways = new SelectList(new[] { "None", "Standard", "HighPerformance", "UltraPerformance", "ErGw1AZ", "ErGw2AZ", "ErGw3AZ" });

            // VPN Gateways
            ViewBag.VpnGateways = new SelectList(new[] { "None", "VpnGw2", "VpnGw3", "VpnGw4", "VpnGw5", "VpnGw2AZ", "VpnGw3AZ", "VpnGw4AZ", "VpnGw5AZ" });

            // VPN Configs
            ViewBag.VpnConfigs = new SelectList(new[] { "Active-Passive", "Active-Active" });

            // IP Versions
            ViewBag.IpVersions = new SelectList(new[] { "IPv4", "IPv6", "Dual" });

            // VM OS options
            ViewBag.VmOsList = new SelectList(new[] { "None", "Windows", "Ubuntu" });
        }
    }
}
