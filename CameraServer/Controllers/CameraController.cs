using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.CameraHub;
using CameraServer.StreamHelpers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System.Collections.Concurrent;
using System.Drawing;
using System.Net;

using HttpGetAttribute = Microsoft.AspNetCore.Mvc.HttpGetAttribute;

namespace CameraServer.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class CameraController : ControllerBase
    {
        private const string Boundary = "--boundary";
        private readonly IUserManager _manager;
        private readonly CameraHubService _collection;

        public CameraController(IUserManager manager, CameraHubService collection)
        {
            _manager = manager;
            _collection = collection;
        }

        [HttpPost]
        [Route("RefreshCameraList")]
        public async Task<IActionResult> RefreshCameraList()
        {
            var user = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? "");
            if (_manager.HasAdminRole(user))
                return BadRequest("Only allowed for Admin");

            await _collection.RefreshCameraCollection();
            return Ok();
        }

        [HttpGet]
        [Route("GetCameraList")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(Dictionary<int, string>))]
        public IActionResult GetCameraList()
        {
            var userRoles = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? "").Roles;
            var cameras = _collection.Cameras
                .Where(n => n.AllowedRoles.Intersect(userRoles).Any());

            var i = 0;
            return Ok(cameras.Select(n => new Dictionary<int, string>() { { i++, n.Camera.Name } }));
        }

        [HttpGet]
        [Route("GetCameraDetails")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(CameraDescriptionDto))]
        public IActionResult GetCameraDetails(int cameraNumber)
        {
            if (cameraNumber < 0 || cameraNumber >= _collection.Cameras.Count())
                return BadRequest("No such camera");

            var userRoles = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? "").Roles;
            var cam = _collection.Cameras.ToArray()[cameraNumber];
            if (!cam.AllowedRoles.Intersect(userRoles).Any())
                return BadRequest("No such camera");

            return Ok(new CameraDescriptionDto(cam.Camera.Description));
        }

        [HttpGet]
        [Route("GetVideoContent")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(MemoryStream))]
        public async Task<IActionResult> GetVideoContent(int cameraNumber, int xResolution = 0, int yResolution = 0, string format = "")
        {
            if (cameraNumber < 0 || cameraNumber >= _collection.Cameras.Count())
                return BadRequest("No such camera");

            var userRoles = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? "").Roles;
            var imageQueue = new ConcurrentQueue<Bitmap>();
            var cam = _collection.Cameras.ToArray()[cameraNumber];
            if (!cam.AllowedRoles.Intersect(userRoles).Any())
                return BadRequest("No such camera");

            var id = _collection.Cameras.ToArray()[cameraNumber].Camera.Path;
            if (string.IsNullOrEmpty(id))
                return Problem("Can not find camera#", cameraNumber.ToString(), StatusCodes.Status204NoContent);

            var cameraCancellationToken = _collection.HookCamera(id, Request.HttpContext.TraceIdentifier, imageQueue,
                xResolution, yResolution, format);
            if (cameraCancellationToken == CancellationToken.None)
                return Problem("Can not connect to camera#", cameraNumber.ToString(), StatusCodes.Status204NoContent);

            Response.ContentType = "multipart/x-mixed-replace; boundary=" + Boundary;
            using (var wr = new MjpegWriter(Response.Body))
            {
                while (!Request.HttpContext.RequestAborted.IsCancellationRequested
                       && !Response.HttpContext.RequestAborted.IsCancellationRequested
                       && !HttpContext.RequestAborted.IsCancellationRequested
                       && !cameraCancellationToken.IsCancellationRequested)
                {
                    if (imageQueue.TryDequeue(out var image))
                    {
                        await wr.Write(image);
                        image.Dispose();
                    }

                    await Task.Delay(10, Response.HttpContext.RequestAborted);
                }
            }

            _collection.UnHookCamera(id, Request.HttpContext.TraceIdentifier);
            while (imageQueue.TryDequeue(out var image))
            {
                image.Dispose();
            }

            imageQueue.Clear();

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);

            return Empty;
        }
    }
}
