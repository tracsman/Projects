using Microsoft.AspNetCore.Mvc;
using PathWeb.Services;

namespace PathWeb.Controllers;

[Route("diag")]
public class DiagnosticsController : BaseController
{
    [HttpGet("view")]
    public IActionResult Index([FromQuery] bool deep = false)
    {
        if (GetAuthLevel() < (byte)AuthLevels.SiteAdmin)
        {
            ViewData["Title"] = "Permission Denied";
            return View("PermissionError");
        }

        ViewData["Title"] = "Diagnostics";
        ViewData["DeepRequested"] = deep;
        return View();
    }
}
