using CameraLib;

using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.CameraHub;
using CameraServer.Services.VideoRecording;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System.Net;

using HttpGetAttribute = Microsoft.AspNetCore.Mvc.HttpGetAttribute;

namespace CameraServer.Controllers
{
    //[Authorize]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    [Authorize(AuthenticationSchemes = Program.BasicAuthenticationSchemeName)]
    [ApiController]
    [Route("[controller]")]
    public class RecorderController : ControllerBase
    {
        private readonly IUserManager _manager;
        private readonly CameraHubService _collection;
        private readonly VideoRecorderService _recorder;

        public RecorderController(IUserManager manager, CameraHubService collection, VideoRecorderService recorder)
        {
            _manager = manager;
            _collection = collection;
            _recorder = recorder;
        }

        [HttpGet]
        [Route("GetRecordTasksList")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string[]))]
        public async Task<IActionResult> GetRecordTasksList()
        {
            return Ok(_recorder.TaskList.ToArray());
        }

        [HttpGet]
        [Route("StartRecordByName")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
        public async Task<IActionResult> StartRecordByName(string cameraName, int? xResolution = 0, int? yResolution = 0, int? fps = 0, string? format = "", byte? quality = 95)
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

            return await StartRecordInternal(cameraNumber, xResolution, yResolution, fps, format, quality);
        }

        [HttpGet]
        [Route("StartRecord")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
        public async Task<IActionResult> StartRecord(int cameraNumber, int? xResolution = 0, int? yResolution = 0, int? fps = 0, string? format = "", byte? quality = 90)
        {
            return await StartRecordInternal(cameraNumber, xResolution, yResolution, fps, format, quality);
        }

        private async Task<IActionResult> StartRecordInternal(int cameraNumber, int? width = 0, int? height = 0, int? fps = 0, string? format = "", byte? quality = 90)
        {
            if (cameraNumber < 0 || cameraNumber >= _collection.Cameras.Count())
                return BadRequest("No such camera");

            var userRoles = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty)?.Roles;
            if (userRoles == null || userRoles.Count == 0)
                return BadRequest("No such camera");

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
                return Problem("Can not find camera#", cameraNumber.ToString(), StatusCodes.Status204NoContent);
            }

            try
            {
                var taskId = _recorder.Start(camera.Camera.Description.Path,
                    HttpContext.User.Identity?.Name ?? string.Empty,
                    new FrameFormatDto
                    {
                        Width = width ?? 0,
                        Height = height ?? 0,
                        Format = format ?? string.Empty,
                        Fps = fps ?? 0
                    },
                    quality ?? 0);
                return Ok(taskId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Can't start recording: {ex}");
                return BadRequest(ex);
            }
        }

        [HttpGet]
        [Route("StopRecord")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
        public async Task<IActionResult> StopRecord(string taskId)
        {
            _recorder.Stop(taskId);

            return Ok();
        }
    }
}
