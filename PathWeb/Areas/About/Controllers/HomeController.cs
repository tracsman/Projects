using Microsoft.AspNetCore.Mvc;

namespace PathWeb.Areas.About.Controllers
{
    [Area("About")]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
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
}
