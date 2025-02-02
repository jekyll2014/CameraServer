using CameraServer.Server.Auth;
using CameraServer.Server.Services.CameraHub;
using CameraServer.Server.Services.MotionDetection;
using CameraServer.Shared.DTO;
using CameraServer.Shared.Enum;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using OpenCvSharp;

using Swashbuckle.AspNetCore.Annotations;

using System.Net;
using System.Text;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CameraServer.Server.Controllers;

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
    public IActionResult GetDetectorTasksList()
    {
        return Ok(_motionDetector.TaskList.ToArray());
    }

    [HttpGet]
    [Route("StartDetectorByName")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
    public IActionResult StartDetectorByName(string cameraName,
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

        var cameraId = _collection.Cameras.FirstOrDefault(n => n.CameraStream.Description.Name == cameraName)?.Id ?? -1;

        return StartDetectorInternal(cameraId,
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
    public IActionResult StartDetector(int cameraId,
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
        return StartDetectorInternal(cameraId,
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

    private IActionResult StartDetectorInternal(int cameraId,
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
        if (_collection.Cameras.All(n => n.Id != cameraId))
            return BadRequest("No such camera");

        var userinfo = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty);
        var userRoles = userinfo?.Roles;
        if (userRoles == null || userRoles.Count == 0)
            return BadRequest("No such camera");

        var camera = _collection.Cameras.First(n => n.Id == cameraId);
        if (!camera.AllowedRoles.Intersect(userRoles).Any())
            return BadRequest("No such camera");

        try
        {
            camera = _collection.Cameras.ToArray()[cameraId];
        }
        catch (Exception e)
        {
            _logger.Log(LogLevel.Error, $"Exception happened during finding the camera[{cameraId}]: {e}");

            return Problem("Can not find camera#", cameraId.ToString(), StatusCodes.Status204NoContent);
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
    public IActionResult StopDetector(string taskId)
    {
        _motionDetector.Stop(taskId);

        return Ok();
    }

    [HttpGet]
    [Route("StopDetector")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
    public IActionResult StopDetector(Guid taskId)
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

        async void _motionDetector_ImageProcessedEvent(MotionDetectionCameraTask detectorTask, Mat? image)
        {
            try
            {
                if (image != null && detectorTask.TaskId == detectorTaskId)
                {
                    var jpegBuffer = image.ToBytes(".jpg",
                        new ImageEncodingParam[]
                        {
                                    new(ImwriteFlags.JpegOptimize, 1),
                                    new(ImwriteFlags.JpegQuality, 90)
                        });
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
