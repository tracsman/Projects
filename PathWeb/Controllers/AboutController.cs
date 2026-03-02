using Microsoft.AspNetCore.Mvc;

namespace PathWeb.Controllers;

public class AboutController : Controller
{
    private readonly ILogger<AboutController> _logger;

    public AboutController(ILogger<AboutController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Lab()
    {
        return View();
    }

    public IActionResult Tenant()
    {
        return View();
    }

    public IActionResult Progress()
    {
        return View();
    }
}
