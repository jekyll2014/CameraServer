using CameraLib;

using CameraServer.Server.Auth;
using CameraServer.Server.Services.CameraHub;
using CameraServer.Server.Services.VideoRecording;

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

    [HttpGet]
    [Route("GetRecordTasksList")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string[]))]
    public IActionResult GetRecordTasksList()
    {
        return Ok(_recorder.TaskList.ToArray());
    }

    [HttpGet]
    [Route("StartRecordByName")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
    public IActionResult StartRecordByName(string cameraName,
        int? xResolution = 0,
        int? yResolution = 0,
        int? fps = 0,
        string? format = "",
        byte? quality = 95)
    {
        if (string.IsNullOrEmpty(cameraName))
            return BadRequest("Empty camera name");

        var cameraId = _collection.Cameras.FirstOrDefault(n => n.CameraStream.Description.Name == cameraName)?.Id ?? -1;

        return StartRecordInternal(cameraId, xResolution, yResolution, fps, format, quality);
    }

    [HttpGet]
    [Route("StartRecord")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
    public IActionResult StartRecord(int cameraId,
        int? xResolution = 0,
        int? yResolution = 0,
        int? fps = 0,
        string? format = "",
        byte? quality = 90)
    {
        return StartRecordInternal(cameraId, xResolution, yResolution, fps, format, quality);
    }

    private IActionResult StartRecordInternal(int cameraId,
        int? width = 0,
        int? height = 0,
        int? fps = 0,
        string? format = "",
        byte? quality = 90)
    {
        if (_collection.Cameras.All(n => n.Id != cameraId))
            return BadRequest("No such camera");

        var userInfo = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty);
        var userRoles = userInfo?.Roles;
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
            _logger.Log(LogLevel.Error, $"Exception finding the camera[{cameraId}]: {e}");

            return Problem("Can not find camera#", cameraId.ToString(), StatusCodes.Status204NoContent);
        }

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
                Quality = quality ?? 0,
                Codec = userInfo?.DefaultCodec ?? "AVC"
            };

            var taskId = _recorder.Start(recordTask);

            return Ok(taskId);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, $"Can't start recording: {ex}");

            return BadRequest(ex);
        }
    }

    [HttpGet]
    [Route("StopRecord")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
    public IActionResult StopRecord(string taskId)
    {
        _recorder.Stop(taskId);

        return Ok();
    }
}
