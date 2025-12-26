using CameraServer.Server.Auth;
using CameraServer.Server.Services.CameraHub;
using CameraServer.Server.Services.VideoRecording;
using CameraServer.Shared.DTO;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using Swashbuckle.AspNetCore.Annotations;

using System.Net;

namespace CameraServer.Server.Controllers;

[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
[Authorize(AuthenticationSchemes = Program.BasicAuthenticationSchemeName)]
[ApiController]
[Route("[controller]")]
public class RecorderController : ControllerBase
{
    private readonly IUserManager _manager;
    private readonly CameraHubService _collection;
    private readonly VideoRecorderService _recorder;
    private readonly ILogger<RecorderController> _logger;

    public RecorderController(
        IUserManager manager,
        CameraHubService collection,
        VideoRecorderService recorder,
        ILogger<RecorderController> logger)
    {
        _logger = logger;
        _manager = manager;
        _collection = collection;
        _recorder = recorder;
    }

    [HttpGet("GetRecordTasksList")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<List<RecordTaskDto>>))]
    public IActionResult GetRecordTasksList()
    {
        try
        {
            var tasks = _recorder.GetRecordTasks()
                .Select(task => new RecordTaskDto
                {
                    Id = task.Id,
                    TaskId = task.TaskId,
                    CameraId = task.CameraId,
                    CameraName = _collection.Cameras.FirstOrDefault(c => c.CameraStream.Description.Path == task.CameraId)?.CameraStream.Description.Name ?? string.Empty,
                    User = task.User,
                    StartTime = task.CreationDateTime,
                    FrameFormat = task.FrameFormat,
                    Quality = task.Quality,
                    Codec = task.Codec,
                    Status = "Recording"
                })
                .ToList();

            return Ok(ApiResponse<List<RecordTaskDto>>.SuccessResponse(tasks));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving record tasks list");
            return StatusCode(500, ApiResponse<List<RecordTaskDto>>.ErrorResponse($"Failed to retrieve recording tasks: {ex.Message}"));
        }
    }

    [HttpGet("StartRecordByName")]
    [SwaggerOperation(
        Summary = "Start recording by camera name",
        Description = "Starts a new recording task for a camera identified by its name",
        Tags = new[] { "Recording" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<string>))]
    [SwaggerResponse((int)HttpStatusCode.BadRequest, "Invalid camera name or parameters")]
    public IActionResult StartRecordByName(string cameraName,
        int? xResolution = 0,
        int? yResolution = 0,
        int? fps = 0,
        string? format = "",
        byte? quality = 90,
        string? codec = "AVC")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cameraName))
                return BadRequest(ApiResponse<string>.ErrorResponse("Camera name is required"));

            var cameraId = _collection.Cameras.FirstOrDefault(n => n.CameraStream.Description.Name == cameraName)?.Id ?? -1;

            if (cameraId == -1)
                return BadRequest(ApiResponse<string>.ErrorResponse($"Camera '{cameraName}' not found"));

            return StartRecordInternal(cameraId, xResolution, yResolution, fps, format, quality, codec);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting recording by name");
            return StatusCode(500, ApiResponse<string>.ErrorResponse($"Failed to start recording: {ex.Message}"));
        }
    }

    [HttpGet("StartRecord")]
    [SwaggerOperation(
        Summary = "Start recording by camera ID",
        Description = "Starts a new recording task for a camera identified by its ID",
        Tags = new[] { "Recording" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<string>))]
    [SwaggerResponse((int)HttpStatusCode.BadRequest, "Invalid camera ID or parameters")]
    public IActionResult StartRecord(int cameraId,
        int? xResolution = 0,
        int? yResolution = 0,
        int? fps = 0,
        string? format = "",
        byte? quality = 90,
        string? codec = "AVC")
    {
        try
        {
            return StartRecordInternal(cameraId, xResolution, yResolution, fps, format, quality, codec);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting recording");
            return StatusCode(500, ApiResponse<string>.ErrorResponse($"Failed to start recording: {ex.Message}"));
        }
    }

    [HttpGet("StopRecord")]
    [SwaggerOperation(
        Summary = "Stop recording",
        Description = "Stops an active recording task by its task ID",
        Tags = new[] { "Recording" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<bool>))]
    [SwaggerResponse((int)HttpStatusCode.BadRequest, "Invalid task ID")]
    public IActionResult StopRecord(string taskId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(taskId))
                return BadRequest(ApiResponse<bool>.ErrorResponse("Task ID is required"));

            _recorder.Stop(taskId);
            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping recording");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"Failed to stop recording: {ex.Message}"));
        }
    }

    private IActionResult StartRecordInternal(int cameraId,
        int? width = 0,
        int? height = 0,
        int? fps = 0,
        string? format = "",
        byte? quality = 90,
        string? codec = "AVC")
    {
        if (_collection.Cameras.All(n => n.Id != cameraId))
            return BadRequest(ApiResponse<string>.ErrorResponse($"Camera with ID {cameraId} not found"));

        var userInfo = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty);
        var userRoles = userInfo?.Roles;
        if (userRoles == null || userRoles.Count == 0)
            return BadRequest(ApiResponse<string>.ErrorResponse("User not authorized"));

        var camera = _collection.Cameras.FirstOrDefault(n => n.Id == cameraId);
        if (camera == null || !camera.AllowedRoles.Intersect(userRoles).Any())
            return BadRequest(ApiResponse<string>.ErrorResponse("User not authorized to access this camera"));

        try
        {
            var recordTask = new RecordCameraSettingDto()
            {
                CameraId = camera.CameraStream.Description.Path,
                User = HttpContext.User.Identity?.Name ?? string.Empty,
                FrameFormat = new FrameFormatDto
                {
                    Width = width ?? 0,
                    Height = height ?? 0,
                    Format = format ?? string.Empty,
                    Fps = fps ?? 0
                },
                Quality = quality ?? 90,
                Codec = codec ?? "AVC"
            };

            var taskId = _recorder.Start(recordTask);

            if (string.IsNullOrEmpty(taskId))
                return StatusCode(500, ApiResponse<string>.ErrorResponse("Failed to start recording - task already exists or internal error"));

            return Ok(ApiResponse<string>.SuccessResponse(taskId));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid recording parameters");
            return BadRequest(ApiResponse<string>.ErrorResponse(ex.Message));
        }
        catch (ApplicationException ex)
        {
            _logger.LogWarning(ex, "Recording authorization failed");
            return BadRequest(ApiResponse<string>.ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting recording");
            return StatusCode(500, ApiResponse<string>.ErrorResponse($"Internal server error: {ex.Message}"));
        }
    }
}
