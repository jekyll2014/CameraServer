﻿using CameraLib;

using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.CameraHub;

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System.Collections.Concurrent;
using System.Net;
using System.Text;

using HttpGetAttribute = Microsoft.AspNetCore.Mvc.HttpGetAttribute;

namespace CameraServer.Controllers
{
    //[Authorize]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    [Authorize(AuthenticationSchemes = Program.BasicAuthenticationSchemeName)]
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
            var user = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty);
            if (user == null || !_manager.HasAdminRole(user))
                return BadRequest("Only allowed for Admin");

            await _collection.RefreshCameraCollection(CancellationToken.None);
            return Ok();
        }

        [HttpGet]
        [Route("GetCameraList")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(Dictionary<int, string>))]
        public IActionResult GetCameraList()
        {
            var userRoles = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty)?.Roles;
            if (userRoles == null || userRoles.Count == 0)
                return Ok(new Dictionary<int, string>());

            var cameras = _collection.Cameras
                .Where(n => n.AllowedRoles.Intersect(userRoles).Any());

            var i = 0;
            return Ok(cameras.Select(n => new Dictionary<int, string>() { { i++, n.Camera.Description.Name } }));
        }

        [HttpGet]
        [Route("GetCameraDetails")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(CameraDescriptionDto))]
        public IActionResult GetCameraDetails(int cameraNumber)
        {
            if (cameraNumber < 0 || cameraNumber >= _collection.Cameras.Count())
                return BadRequest("No such camera");

            var userRoles = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty)?.Roles;
            if (userRoles == null || userRoles.Count == 0)
                return BadRequest("No such camera");

            var cam = _collection.Cameras.ToArray()[cameraNumber];
            if (!cam.AllowedRoles.Intersect(userRoles).Any())
                return BadRequest("No such camera");

            return Ok(new CameraDescriptionDto(cam.Camera.Description));
        }

        [HttpGet]
        [Route("GetVideoContentByName")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(MemoryStream))]
        public async Task<IActionResult> GetVideoContentByName(string cameraName, int? xResolution, int? yResolution, string? format, byte? quality)
        {
            if (string.IsNullOrEmpty(cameraName))
                return BadRequest("Empty camera name");

            var cameras = _collection.Cameras.ToArray();
            var cameraNumber = -1;
            for (var i = 0; i < cameras.Length; i++)
            {
                if (cameras[i].Camera.Description.Name == cameraName)
                {
                    cameraNumber = i;
                    break;
                }
            }

            await GetVideoContentInternal(cameraNumber, xResolution, yResolution, format, quality);

            return new EmptyResult();
        }

        [HttpGet]
        [Route("GetVideoContent")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(MemoryStream))]
        public async Task<IActionResult> GetVideoContent(int cameraNumber, int? xResolution, int? yResolution, string? format, byte? quality)
        {
            await GetVideoContentInternal(cameraNumber, xResolution, yResolution, format, quality);

            return new EmptyResult();
        }

        private async Task<IActionResult> GetVideoContentInternal(int cameraNumber, int? width, int? height, string? format, byte? quality)
        {
            if (cameraNumber < 0 || cameraNumber >= _collection.Cameras.Count())
                return BadRequest("No such camera");

            var userRoles = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty)?.Roles;
            if (userRoles == null || userRoles.Count == 0)
                return BadRequest("No such camera");

            var imageQueue = new ConcurrentQueue<Mat>();
            var cam = _collection.Cameras.ToArray()[cameraNumber];
            if (!cam.AllowedRoles.Intersect(userRoles).Any())
                return BadRequest("No such camera");

            ServerCamera camera;
            try
            {
                camera = _collection.Cameras.ToArray()[cameraNumber];
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception happened during finding the camera[{cameraNumber}]: {e}");
                return Problem("Can not find camera#",
                    cameraNumber.ToString(),
                    StatusCodes.Status204NoContent);
            }

            var frameFormat = new FrameFormatDto
            {
                Width = width ?? 0,
                Height = height ?? 0,
                Format = format ?? string.Empty
            };

            var cameraCancellationToken = await _collection.HookCamera(camera.Camera.Description.Path,
                Request.HttpContext.TraceIdentifier,
                imageQueue,
                frameFormat);

            if (cameraCancellationToken == CancellationToken.None)
                return Problem("Can not connect to camera#",
                    cameraNumber.ToString(),
                    StatusCodes.Status204NoContent);

            var qlt = quality ?? 95;
            if (qlt < 1)
                qlt = 1;
            else if (qlt > 100)
                qlt = 100;
            try
            {

                Response.ContentType = "multipart/x-mixed-replace; boundary=" + Boundary;
                while (!Request.HttpContext.RequestAborted.IsCancellationRequested
                       && !Response.HttpContext.RequestAborted.IsCancellationRequested
                       && !HttpContext.RequestAborted.IsCancellationRequested
                       && !cameraCancellationToken.IsCancellationRequested)
                {
                    if (imageQueue.TryDequeue(out var image))
                    {
                        Image<Rgb, byte> outImage;
                        if (frameFormat.Width > 0
                            && frameFormat.Height > 0
                            && image.Width > frameFormat.Width
                            && image.Height > frameFormat.Height)
                        {
                            outImage = image
                                .ToImage<Rgb, byte>()
                                .Resize(frameFormat.Width, frameFormat.Height, Inter.Nearest);
                        }
                        else
                            outImage = image.ToImage<Rgb, byte>();

                        if (outImage != null)
                        {
                            var jpegBuffer = outImage.ToJpegData(qlt);
                            var header = $"\r\n{Boundary}\r\n" +
                                         $"Content-Type: image/jpeg\r\n" +
                                         $"Content-Length: {jpegBuffer.Length}\r\n" +
                                         $"\r\n";
                            await Response.Body.WriteAsync(Encoding.ASCII.GetBytes(header), CancellationToken.None);
                            await Response.Body.WriteAsync(jpegBuffer, CancellationToken.None);
                            await Response.Body.WriteAsync(new byte[] { 0x0d, 0x0a }, CancellationToken.None);
                        }

                        outImage?.Dispose();
                        image?.Dispose();
                    }
                    else
                    {
                        await Task.Delay(10, Response.HttpContext.RequestAborted);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            await _collection.UnHookCamera(camera.Camera.Description.Path,
                Request.HttpContext.TraceIdentifier,
                frameFormat);

            while (imageQueue.TryDequeue(out var image))
            {
                image.Dispose();
            }

            imageQueue.Clear();

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);

            return new EmptyResult();
        }

        public static string GenerateCameraUrl(int cameraNumber, int? xResolution = 0, int? yResolution = 0, string? format = "", byte? quality = 90)
        {
            return
                $"/{nameof(CameraController)[..^"Controller".Length]}/{nameof(GetVideoContent)}?{nameof(cameraNumber)}={cameraNumber}&{nameof(xResolution)}={xResolution ?? 0}&{nameof(yResolution)}={yResolution ?? 0}&{nameof(format)}={format ?? string.Empty}&{nameof(quality)}={quality ?? 90}";
        }
    }
}
