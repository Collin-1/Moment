using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MomentApp.Models;

namespace MomentApp.Controllers;

/// <summary>
/// Home controller for landing page and main navigation
/// </summary>
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IWebHostEnvironment _env;

    public HomeController(ILogger<HomeController> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    /// <summary>
    /// Landing page (serves the static marketing page at the site root)
    /// </summary>
    public IActionResult Index()
    {
        var indexPath = Path.Combine(_env.WebRootPath, "index.html");
        return PhysicalFile(indexPath, "text/html");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
