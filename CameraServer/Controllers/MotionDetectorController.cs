using CameraLib;

using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.CameraHub;
using CameraServer.Services.MotionDetection;

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
    public class MotionDetectorController : ControllerBase
    {
        private readonly IUserManager _manager;
        private readonly CameraHubService _collection;
        private readonly MotionDetectionService _motionDetector;

        public MotionDetectorController(IUserManager manager, CameraHubService collection, MotionDetectionService motionDetector)
        {
            _manager = manager;
            _collection = collection;
            _motionDetector = motionDetector;
        }

        [HttpGet]
        [Route("GetDetectorTasksList")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string[]))]
        public async Task<IActionResult> GetDetectorTasksList()
        {
            return Ok(_motionDetector.TaskList.ToArray());
        }

        [HttpGet]
        [Route("StartDetectorByName")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
        public async Task<IActionResult> StartDetectorByName(string cameraName,
            int? xResolution,
            int? yResolution,
            string? format,
            uint? changeLimit,
            byte? noiseThreshold,
            uint? detectorDelayMs,
            NotificationTransport? transport,
            string? destination,
            MessageType? messageType,
            string? message)
        {
            if (string.IsNullOrEmpty(cameraName))
                return BadRequest("Empty camera name");

            var cameras = _collection.Cameras.ToArray();
            var cameraNumber = -1;
            for (var i = 0; i < cameras.Length; i++)
            {
                if (cameras[i].CameraStream.Description.Name == cameraName)
                {
                    cameraNumber = i;
                    break;
                }
            }

            return await StartDetectorInternal(cameraNumber,
                xResolution,
                yResolution,
                format, changeLimit,
                noiseThreshold,
                detectorDelayMs,
                transport,
                destination,
                messageType,
                message);
        }

        [HttpGet]
        [Route("StartDetector")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
        public async Task<IActionResult> StartDetector(int cameraNumber,
            int? xResolution,
            int? yResolution,
            string? format,
            uint? changeLimit,
            byte? noiseThreshold,
            uint? detectorDelayMs,
            NotificationTransport? transport,
            string? destination,
            MessageType? messageType,
            string? message)
        {
            return await StartDetectorInternal(cameraNumber,
                xResolution,
                yResolution,
                format, changeLimit,
                noiseThreshold,
                detectorDelayMs,
                transport,
                destination,
                messageType,
                message);
        }

        private async Task<IActionResult> StartDetectorInternal(int cameraNumber,
            int? width,
            int? height,
            string? format,
            uint? changeLimit,
            byte? noiseThreshold,
            uint? detectorDelayMs,
            NotificationTransport? transport,
            string? destination,
            MessageType? messageType,
            string? message)
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
                var taskId = _motionDetector.Start(camera.CameraStream.Description.Path,
                    HttpContext.User.Identity?.Name ?? string.Empty,
                    new FrameFormatDto { Width = width ?? 0, Height = height ?? 0, Format = format ?? string.Empty },
                    new MotionDetectorParameters()
                    {
                        Width = width ?? 0,
                        Height = height ?? 0,
                        ChangeLimit = changeLimit ?? 0,
                        NoiseThreshold = noiseThreshold ?? 0,
                        DetectorDelayMs = detectorDelayMs ?? 0
                    },
                    new List<NotificationParameters>()
                        {
                            new NotificationParameters()
                            {
                                Transport = transport ?? NotificationTransport.None,
                                Destination = destination ?? string.Empty,
                                MessageType = messageType ?? MessageType.Text,
                                Message = message ?? string.Empty
                            }
                        });

                return Ok(taskId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Can't start recording: {ex}");
                return BadRequest(ex);
            }
        }

        [HttpGet]
        [Route("StopDetector")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
        public async Task<IActionResult> StopDetector(string taskId)
        {
            _motionDetector.Stop(taskId);

            return Ok();
        }
    }
}
