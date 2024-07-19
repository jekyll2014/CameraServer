using CameraServer.Models;
using CameraServer.Services.CameraHub;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CameraServer.Controllers
{
    public class HomeController : Controller
    {
        private readonly CameraHubService _collection;
        private readonly ILogger<HomeController> _logger;

        public HomeController(CameraHubService collection, ILogger<HomeController> logger)
        {
            _logger = logger;
            _collection = collection;
        }

        public IActionResult Index()
        {
            return View();
        }

        [Authorize]
        public IActionResult ConfidentialData()
        {
            return View();
        }

        [Authorize]
        public async Task<IActionResult> RefreshCameraList()
        {
            if (HttpContext.User.IsInRole(Roles.Admin.ToString()))
                await _collection.RefreshCameraCollection(CancellationToken.None);

            return RedirectToAction("Index");
        }
    }
}
