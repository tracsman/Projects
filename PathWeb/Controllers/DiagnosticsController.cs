using Microsoft.AspNetCore.Mvc;

namespace PathWeb.Controllers;

[Route("diag")]
public class DiagnosticsController : Controller
{
    [HttpGet("view")]
    public IActionResult Index([FromQuery] bool deep = false)
    {
        ViewData["Title"] = "Diagnostics";
        ViewData["DeepRequested"] = deep;
        return View();
    }
}
