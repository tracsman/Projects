using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PathWeb.Data;
using PathWeb.Models;
using PathWeb.Services;

namespace PathWeb.Controllers;

public class RequestsController : Controller
{
    private readonly LabConfigContext _context;
    private readonly ILogger<RequestsController> _logger;
    private readonly LogicAppService _logicAppService;

    public RequestsController(LabConfigContext context, ILogger<RequestsController> logger, LogicAppService logicAppService)
    {
        _context = context;
        _logger = logger;
        _logicAppService = logicAppService;
    }

    private byte GetAuthLevel() => (byte)(HttpContext.Items["AuthLevel"] ?? (byte)0);
    private string GetUserEmail() => User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                                     ?? User.Identity?.Name ?? "unknown";

    public async Task<IActionResult> Index(int page = 1)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Challenge();

        var email = GetUserEmail();
        var isAdmin = GetAuthLevel() >= (byte)AuthLevels.TenantAdmin;
        const int pageSize = 25;

        var query = _context.TenantRequests.AsQueryable();

        if (!isAdmin)
        {
            // Show requests where the user is the requestor or listed in the Contacts field
            query = query.Where(r => r.RequestedBy == email
                || (r.Contacts != null && r.Contacts.Contains(email)));
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var requests = await query
            .OrderByDescending(r => r.RequestedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.IsAdmin = isAdmin;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalCount = totalCount;
        await SetUserDisplayNames(requests.Select(r => r.RequestedBy));
        _logger.LogInformation("Requests.Index requested by {User}, page {Page}", email, page);
        return View(requests);
    }

    public async Task<IActionResult> Create()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Challenge();

        await SetDropDowns();

        var request = new TenantRequest
        {
            RequestId = Guid.NewGuid(),
            Status = "Pending",
            RequestedDate = DateTime.Now,
            RequestedBy = GetUserEmail(),
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
            AddressFamily = "IPv4",
            AzVm1 = "None",
            AzVm2 = "None",
            AzVm3 = "None",
            AzVm4 = "None",
            LabVm1 = "None",
            LabVm2 = "None",
            LabVm3 = "None",
            LabVm4 = "None"
        };

        _logger.LogInformation("Requests.Create page requested by {User}", GetUserEmail());
        return View(request);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TenantRequest request)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Challenge();

        if (ModelState.IsValid)
        {
            request.RequestId = Guid.NewGuid();
            request.Status = "Pending";
            request.RequestedDate = DateTime.Now;
            request.RequestedBy = GetUserEmail();

            _context.TenantRequests.Add(request);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Lab request created by {User} for {Lab}", GetUserEmail(), request.Lab);
            TempData["Message"] = "Your lab request has been submitted!";
            TempData["MessageLevel"] = "success";
            return RedirectToAction(nameof(Index));
        }

        await SetDropDowns();
        return View(request);
    }

    public async Task<IActionResult> Details(Guid? id)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Challenge();

        if (id == null)
        {
            TempData["Message"] = "Request missing required ID!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Index));
        }

        var request = await _context.TenantRequests.FirstOrDefaultAsync(r => r.RequestId == id);
        if (request == null)
        {
            TempData["Message"] = "Invalid Request ID!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Index));
        }

        // Non-admins can only view their own requests
        var isAdmin = GetAuthLevel() >= (byte)AuthLevels.TenantAdmin;
        if (!isAdmin && request.RequestedBy != GetUserEmail())
        {
            TempData["Message"] = "You can only view your own requests.";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Index));
        }

        await SetUserDisplayNames([request.RequestedBy, request.ReviewedBy]);
        _logger.LogInformation("Requests.Details for {RequestId} by {User}", id, GetUserEmail());
        return View(request);
    }

    public async Task<IActionResult> Queue()
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
        {
            _logger.LogWarning("Permission denied for Requests.Queue, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        var pending = await _context.TenantRequests
            .Where(r => r.Status == "Pending")
            .OrderBy(r => r.RequestedDate)
            .ToListAsync();

        await SetUserDisplayNames(pending.Select(r => r.RequestedBy));
        _logger.LogInformation("Requests.Queue requested by {User}, {Count} pending", GetUserEmail(), pending.Count);
        return View(pending);
    }

    public async Task<IActionResult> Review(Guid? id)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
        {
            _logger.LogWarning("Permission denied for Requests.Review, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        if (id == null)
        {
            TempData["Message"] = "Request missing required ID!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Queue));
        }

        var request = await _context.TenantRequests.FirstOrDefaultAsync(r => r.RequestId == id);
        if (request == null)
        {
            TempData["Message"] = "Invalid Request ID!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Queue));
        }

        await SetUserDisplayNames([request.RequestedBy]);
        _logger.LogInformation("Requests.Review for {RequestId} by {User}", id, GetUserEmail());
        return View(request);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id, short serverPreference = 0)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
        {
            _logger.LogWarning("Permission denied for Requests.Approve, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        var request = await _context.TenantRequests.FindAsync(id);
        if (request == null || request.Status != "Pending")
        {
            TempData["Message"] = "Invalid or already processed request!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Queue));
        }

        try
        {
            var adminEmail = GetUserEmail();

            // Create the Tenant from the request data
            var tenant = new Tenant
            {
                TenantGuid = Guid.NewGuid(),
                TenantId = await GetNextTenantId(request.Lab, serverPreference),
                TenantVersion = 0,
                Lab = request.Lab,
                NinjaOwner = adminEmail.Contains('@') ? adminEmail[..adminEmail.IndexOf('@')] : adminEmail,
                Contacts = request.Contacts,
                ReturnDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
                Usage = request.Usage,
                AzureRegion = request.AzureRegion,
                Ersku = request.Ersku,
                Erspeed = request.Erspeed,
                EruplinkPort = request.EruplinkPort,
                PvtPeering = request.PvtPeering,
                ErgatewaySize = request.ErgatewaySize,
                ErfastPath = request.ErfastPath,
                Msftpeering = request.Msftpeering,
                Vpngateway = request.Vpngateway,
                Vpnbgp = request.Vpnbgp,
                Vpnconfig = request.Vpnconfig,
                VpnendPoint = "TBD,N/A",
                AddressFamily = request.AddressFamily,
                AzVm1 = request.AzVm1,
                AzVm2 = request.AzVm2,
                AzVm3 = request.AzVm3,
                AzVm4 = request.AzVm4,
                LabVm1 = request.LabVm1,
                LabVm2 = request.LabVm2,
                LabVm3 = request.LabVm3,
                LabVm4 = request.LabVm4,
                WorkItemId = 0,
                AssignedDate = DateTime.Now,
                AssignedBy = adminEmail,
                LastUpdateDate = DateTime.Now,
                LastUpdateBy = adminEmail
            };

            _context.Tenants.Add(tenant);

            // Update request status
            request.Status = "Approved";
            request.ReviewedDate = DateTime.Now;
            request.ReviewedBy = adminEmail;
            request.TenantGuid = tenant.TenantGuid;

            await _context.SaveChangesAsync();

            // Create ADO work item via Logic App
            var (workItemId, adoError) = await _logicAppService.CreateWorkItemAsync(tenant, request);
            if (workItemId > 0)
            {
                tenant.WorkItemId = workItemId;
                tenant.LastUpdateDate = DateTime.Now;
                tenant.LastUpdateBy = adminEmail;
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("Request {RequestId} approved by {User}, created tenant {TenantName}, ADO work item {WorkItemId}",
                id, adminEmail, tenant.TenantName, workItemId);
            var adoNote = workItemId > 0
                ? $" ADO work item {workItemId} created."
                : adoError?.Contains("not configured") == true ? "" : $" (ADO: {adoError})";
            TempData["Message"] = $"Request approved! Tenant {tenant.TenantName} created.{adoNote} Please review and update the details below.";
            TempData["MessageLevel"] = workItemId > 0 || string.IsNullOrEmpty(adoNote) ? "success" : "warning";
            return RedirectToAction("Edit", "Tenants", new { id = tenant.TenantGuid });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to approve request {RequestId}", id);
            TempData["Message"] = $"Approval failed: {ex.Message} | Inner: {ex.InnerException?.Message}";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Queue));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id, string reviewNotes)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
        {
            _logger.LogWarning("Permission denied for Requests.Reject, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        var request = await _context.TenantRequests.FindAsync(id);
        if (request == null || request.Status != "Pending")
        {
            TempData["Message"] = "Invalid or already processed request!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Queue));
        }

        request.Status = "Rejected";
        request.ReviewedDate = DateTime.Now;
        request.ReviewedBy = GetUserEmail();
        request.ReviewNotes = reviewNotes;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Request {RequestId} rejected by {User}", id, GetUserEmail());
        TempData["Message"] = "Request has been rejected.";
        TempData["MessageLevel"] = "warning";
        return RedirectToAction(nameof(Queue));
    }

    private async Task<short> GetNextTenantId(string lab, short serverPreference = 0)
    {
        var usedIds = await _context.Tenants
            .Where(t => t.Lab == lab && t.DeletedDate == null)
            .Select(t => t.TenantId)
            .ToListAsync();

        // Server preference determines which physical server (and TenantID range) to use
        // Preference 0 = any server (range 10-69), Preference 1-6 = specific server (e.g., 4 = 40-49)
        // Preference 100 = no on-prem needed (range 101+)
        short rangeStart, rangeEnd;
        if (serverPreference >= 100)
        {
            rangeStart = 101;
            rangeEnd = 999;
        }
        else if (serverPreference > 0)
        {
            rangeStart = (short)(serverPreference * 10);
            rangeEnd = (short)(serverPreference * 10 + 9);
        }
        else
        {
            rangeStart = 10;
            rangeEnd = 69;
        }

        for (short id = rangeStart; id <= rangeEnd; id++)
        {
            if (!usedIds.Contains(id))
                return id;
        }

        // Fallback: if preferred range is full, search extended range
        for (short id = 101; id < 1000; id++)
        {
            if (!usedIds.Contains(id))
                return id;
        }

        return (short)(usedIds.Max() + 1);
    }

    private async Task SetDropDowns()
    {
        ViewBag.Labs = new SelectList(new[]
        {
            new { Value = "SEA", Text = "Seattle" },
            new { Value = "ASH", Text = "Ashburn" }
        }, "Value", "Text");

        ViewBag.BoolOptions = new SelectList(new[]
        {
            new { Value = "True", Text = "Enabled" },
            new { Value = "False", Text = "Not Enabled" }
        }, "Value", "Text");

        var regions = await _context.Regions
            .OrderBy(r => r.Region1)
            .Select(r => r.Region1)
            .ToListAsync();
        ViewBag.Regions = new SelectList(regions);

        ViewBag.ErSkus = new SelectList(new[] { "None", "Standard", "Premium", "Local" });

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

        ViewBag.ErUplinksSea = new SelectList(new[] { "ECX", "100G Direct Juniper MSEE", "100G Direct Cisco MSEE", "10G Direct Juniper MSEE" });
        ViewBag.ErUplinksAsh = new SelectList(new[] { "ECX" });

        ViewBag.ErGateways = new SelectList(new[] { "None", "Standard", "HighPerformance", "UltraPerformance", "ErGw1AZ", "ErGw2AZ", "ErGw3AZ" });

        ViewBag.VpnGateways = new SelectList(new[] { "None", "VpnGw2", "VpnGw3", "VpnGw4", "VpnGw5", "VpnGw2AZ", "VpnGw3AZ", "VpnGw4AZ", "VpnGw5AZ" });

        ViewBag.VpnConfigs = new SelectList(new[] { "Active-Passive", "Active-Active" });

        ViewBag.IpVersions = new SelectList(new[] { "IPv4", "IPv6", "Dual" });

        ViewBag.VmOsList = new SelectList(new[] { "None", "Windows", "Ubuntu" });
    }

    /// <summary>
    /// Builds a dictionary of email → "Full Name (email)" for display purposes.
    /// Falls back to just the email if the user isn't in the Users table.
    /// </summary>
    private async Task SetUserDisplayNames(IEnumerable<string?> emails)
    {
        var uniqueEmails = emails.Where(e => !string.IsNullOrEmpty(e)).Distinct().ToList();
        var users = await _context.Users
            .Where(u => uniqueEmails.Contains(u.UserName))
            .Select(u => new { u.UserName, u.Name })
            .ToListAsync();

        var lookup = new Dictionary<string, string>();
        foreach (var email in uniqueEmails)
        {
            var user = users.FirstOrDefault(u => u.UserName == email);
            lookup[email!] = user != null ? $"{user.Name} ({email})" : email!;
        }
        ViewBag.UserNames = lookup;
    }
}
