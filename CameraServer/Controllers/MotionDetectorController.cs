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

    [HttpGet("GetDetectorTasksList")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(MotionDetectionCameraTask[]))]
    public IActionResult GetDetectorTasksList()
    {
        return Ok(_motionDetector.TaskDescriptions
            .Where(n => n.User == (HttpContext.User.Identity?.Name ?? string.Empty))
            .ToArray());
    }

    [HttpGet("StartDetectorByName")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
    public IActionResult StartDetectorByName(string cameraName,
        int? xResolution,
        int? yResolution,
        string? format,
        uint? changeLimit,
        DetectionMethod? detectMethod,
        uint? detectorDelayMs,
        NotificationTransport transport,
        string destination,
        MessageType messageType,
        string? message,
        bool saveNotificationContent = false,
        uint? videoLengthSec = 15)
    {
        if (string.IsNullOrEmpty(cameraName))
            return BadRequest("Empty camera name");

        var cameraId = _collection.Cameras.FirstOrDefault(n => n.CameraStream.Description.Name == cameraName)?.Id ?? -1;

        return StartDetectorInternal(cameraId,
            xResolution,
            yResolution,
            format, changeLimit,
            detectMethod,
            detectorDelayMs,
            transport,
            destination,
            messageType,
            message,
            saveNotificationContent,
            videoLengthSec);
    }

    [HttpGet("StartDetector")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
    public IActionResult StartDetector(int cameraId,
        int? xResolution,
        int? yResolution,
        string? format,
        uint? changeLimit,
        DetectionMethod? detectMethod,
        uint? detectorDelayMs,
        NotificationTransport transport,
        string destination,
        MessageType messageType,
        string? message,
        bool saveNotificationContent = false,
        uint? videoLengthSec = 15)
    {
        return StartDetectorInternal(cameraId,
            xResolution,
            yResolution,
            format, changeLimit,
            detectMethod,
            detectorDelayMs,
            transport,
            destination,
            messageType,
            message,
            saveNotificationContent,
            videoLengthSec);
    }

    [HttpGet("StopDetector")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
    public async Task<IActionResult> StopDetector(Guid taskId)
    {
        await _motionDetector.Stop(taskId);

        return Ok();
    }

    [HttpGet("GetMotionDetectorStream")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(MemoryStream))]
    public async Task<IActionResult> GetMotionDetectorStream(Guid taskId)
    {
        if (_motionDetector.TaskDescriptions.All(n => n.Id != taskId))
            return BadRequest("No such detector task");

        await GetMotionDetectorStreamInternal(taskId);

        return new EmptyResult();
    }

    private IActionResult StartDetectorInternal(int cameraId,
        int? width,
        int? height,
        string? format,
        uint? changeLimit,
        DetectionMethod? detectMethod,
        uint? detectorDelayMs,
        NotificationTransport transport,
        string destination,
        MessageType messageType,
        string? message,
        bool saveNotificationContent = false,
        uint? videoLengthSec = 15)
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
            // Validate notification parameters
            if (string.IsNullOrWhiteSpace(destination))
                return BadRequest("Notification destination cannot be empty");

            // Log incoming parameters from UI for debugging
            LogIncomingParameters(width, height, format, changeLimit, detectMethod, detectorDelayMs, destination, message);

            var motionTask = new MotionDetectionCameraSettingDto()
            {
                CameraId = camera.CameraStream.Description.Path,
                User = HttpContext.User.Identity?.Name ?? string.Empty,
                FrameFormat = new FrameFormatDto { Width = width ?? 0, Height = height ?? 0, Format = format ?? string.Empty },
                MotionDetectParameters = new MotionDetectorParametersDto()
                {
                    DetectMethod = detectMethod ?? DetectionMethod.Knn,
                    Width = width ?? 0,
                    Height = height ?? 0,
                    ChangeLimit = changeLimit ?? 0,
                    DetectorDelayMs = detectorDelayMs ?? 0
                },
                Notifications =
                [
                    new()
                    {
                        Transport = transport,
                        Destination = destination,
                        MessageType = messageType,
                        Message = message ?? string.Empty,
                        VideoLengthSec = videoLengthSec ?? 15,
                        SaveNotificationContent = saveNotificationContent
                    }
                ]
            };

            _logger.LogInformation(
                "Starting motion detector - Camera: {CameraPath}, User: {User}, " +
                "FrameFormat: {Width}x{Height} {Format}, " +
                "DetectorParams: DelayMs={DelayMs}, DetectMethod={DetectMethod}, ChangeLimit={ChangeLimit}%",
                motionTask.CameraId,
                motionTask.User,
                motionTask.FrameFormat.Width,
                motionTask.FrameFormat.Height,
                motionTask.FrameFormat.Format,
                motionTask.MotionDetectParameters.DetectorDelayMs,
                motionTask.MotionDetectParameters.DetectMethod,
                motionTask.MotionDetectParameters.ChangeLimit);

            var taskId = _motionDetector.Start(motionTask);

            if (taskId == Guid.Empty)
            {
                _logger.LogWarning("Motion detector failed to start - returned empty GUID");
                return BadRequest("Failed to start motion detector");
            }

            _logger.LogInformation("Motion detector started successfully with ID: {TaskId}", taskId);

            return Ok(taskId);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, $"Can't start recording: {ex}");
            return BadRequest($"Error: {ex.Message}");
        }
    }

    private void LogIncomingParameters(int? width, int? height, string? format, uint? changeLimit,
        DetectionMethod? detectMethod, uint? detectorDelayMs, string? destination, string? message)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Incoming Motion Detector Parameters from UI ===");
        sb.AppendLine($"  Width: {(width.HasValue ? width.Value : "null (will use default)")}");
        sb.AppendLine($"  Height: {(height.HasValue ? height.Value : "null (will use default)")}");
        sb.AppendLine($"  Format: {(string.IsNullOrEmpty(format) ? "null/empty (will use default)" : format)}");
        sb.AppendLine($"  ChangeLimit: {(changeLimit.HasValue ? changeLimit.Value + "%" : "null (will use default)")}");
        sb.AppendLine($"  DetectMethod: {(detectMethod.HasValue ? detectMethod.Value.ToString() : "null (will use default)")} ");
        sb.AppendLine($"  DetectorDelayMs: {(detectorDelayMs.HasValue ? detectorDelayMs.Value + "ms" : "null (will use default)")}");
        sb.AppendLine($"  Destination: {(string.IsNullOrEmpty(destination) ? "EMPTY" : destination)}");
        sb.AppendLine($"  Message: {(string.IsNullOrEmpty(message) ? "null/empty" : message)}");
        sb.AppendLine("==================================================");

        _logger.LogInformation(sb.ToString());
    }

    private async Task<IActionResult> GetMotionDetectorStreamInternal(Guid detectorTaskId)
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
                if (image != null && detectorTask.Id == detectorTaskId)
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

            //GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        }
    }
}
