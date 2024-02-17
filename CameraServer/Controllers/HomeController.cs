using CameraServer.Auth;
using CameraServer.Services.CameraHub;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CameraServer.Controllers
{
    public class HomeController : Controller
    {
        private readonly CameraHubService _collection;

        public HomeController(CameraHubService collection)
        {
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
