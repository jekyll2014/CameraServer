using CameraLib;

using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.CameraHub;
using CameraServer.Services.MotionDetection;

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System.Net;
using System.Text;

using HttpGetAttribute = Microsoft.AspNetCore.Mvc.HttpGetAttribute;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CameraServer.Controllers
{
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    [Authorize(AuthenticationSchemes = Program.BasicAuthenticationSchemeName)]
    [ApiController]
    [Route("[controller]")]
    public class MotionDetectorController : ControllerBase
    {
        private const string Boundary = "--boundary";

        private readonly IUserManager _manager;
        private readonly CameraHubService _collection;
        private readonly MotionDetectionService _motionDetector;
        private readonly ILogger<MotionDetectorController> _logger;

        public MotionDetectorController(
            IUserManager manager,
            CameraHubService collection,
            MotionDetectionService motionDetector,
            ILogger<MotionDetectorController> logger)
        {
            _logger = logger;
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

            var userinfo = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty);
            var userRoles = userinfo?.Roles;
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
                _logger.Log(LogLevel.Error, $"Exception happened during finding the camera[{cameraNumber}]: {e}");

                return Problem("Can not find camera#", cameraNumber.ToString(), StatusCodes.Status204NoContent);
            }

            try
            {
                var motionTask = new MotionDetectionCameraSettingDto()
                {
                    CameraId = camera.CameraStream.Description.Path,
                    User = HttpContext.User.Identity?.Name ?? string.Empty,
                    FrameFormat = new FrameFormatDto { Width = width ?? 0, Height = height ?? 0, Format = format ?? string.Empty },
                    MotionDetectParameters = new MotionDetectorParametersDto()
                    {
                        Width = width ?? 0,
                        Height = height ?? 0,
                        ChangeLimit = changeLimit ?? 0,
                        NoiseThreshold = noiseThreshold ?? 0,
                        DetectorDelayMs = detectorDelayMs ?? 0
                    },
                    Notifications =
                    [
                        new()
                        {
                            Transport = transport ?? NotificationTransport.None,
                            Destination = destination ?? string.Empty,
                            MessageType = messageType ?? MessageType.Text,
                            Message = message ?? string.Empty
                        }
                    ]
                };

                var taskId = _motionDetector.Start(motionTask);

                return Ok(taskId);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Can't start recording: {ex}");
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

        [HttpGet]
        [Route("GetMotionDetectorStream")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(MemoryStream))]
        public async Task<IActionResult> GetMotionDetectorStream(int detectorTask)
        {
            if (_motionDetector.TaskList.Count() <= detectorTask)
                return BadRequest("No such detector task");

            string detectorTaskId = _motionDetector.TaskList.ToArray()[detectorTask];

            await GetMotionDetectorStreamInternal(detectorTaskId);

            return new EmptyResult();
        }

        private async Task<IActionResult> GetMotionDetectorStreamInternal(string detectorTaskId)
        {
            if (!_motionDetector.TaskList.Contains(detectorTaskId))
                return BadRequest("No such detector task");

            var userRoles = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty)?.Roles;
            if (userRoles == null || userRoles.Count == 0)
                return BadRequest("No such detector task");

            _motionDetector.ImageProcessedEvent += _motionDetector_ImageProcessedEvent;

            Response.ContentType = "multipart/x-mixed-replace; boundary=" + Boundary;

            while (!Request.HttpContext.RequestAborted.IsCancellationRequested
                && !Response.HttpContext.RequestAborted.IsCancellationRequested
                && !HttpContext.RequestAborted.IsCancellationRequested
                && _motionDetector.TaskList.Contains(detectorTaskId))
            {
                await Task.Delay(100);
            }

            _motionDetector.ImageProcessedEvent -= _motionDetector_ImageProcessedEvent;

            return new EmptyResult();

            async void _motionDetector_ImageProcessedEvent(MotionDetectionCameraTask detectorTask, Image<Gray, byte>? image)
            {
                try
                {
                    if (image != null && detectorTask.TaskId == detectorTaskId)
                    {
                        var jpegBuffer = image.ToJpegData(100);
                        var header = $"\r\n{Boundary}\r\n" +
                                     $"Content-Type: image/jpeg\r\n" +
                                     $"Content-Length: {jpegBuffer.Length}\r\n" +
                                     $"\r\n";
                        await Response.Body.WriteAsync(Encoding.ASCII.GetBytes(header), CancellationToken.None);
                        await Response.Body.WriteAsync(jpegBuffer, CancellationToken.None);
                        await Response.Body.WriteAsync(new byte[] { 0x0d, 0x0a }, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, ex.ToString());
                }

                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            }
        }
    }
}
