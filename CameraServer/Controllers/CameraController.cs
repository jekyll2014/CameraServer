using CameraServer.Server.Auth;
using CameraServer.Server.Services.CameraHub;
using CameraServer.Shared.DTO;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using MudBlazor;

using OpenCvSharp;

using Swashbuckle.AspNetCore.Annotations;

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using CameraLib.IP;
using static MudBlazor.CategoryTypes;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using Size = OpenCvSharp.Size;

namespace CameraServer.Server.Controllers;

[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
[Authorize(AuthenticationSchemes = Program.BasicAuthenticationSchemeName)]
[ApiController]
[Route("[controller]")]
public class CameraController : ControllerBase
{
    private const string Boundary = "--boundary";
    private readonly IUserManager _manager;
    private readonly CameraHubService _collection;
    private readonly ILogger<CameraController> _logger;

    public CameraController(IUserManager manager,
        CameraHubService collection,
        ILogger<CameraController> logger)
    {
        _logger = logger;
        _manager = manager;
        _collection = collection;
    }

    [HttpPost("RefreshCameraList")]
    //[Route("RefreshCameraList")]
    [SwaggerResponse((int)HttpStatusCode.OK)]
    public async Task<IActionResult> RefreshCameraList()
    {
        var user = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty);
        if (user == null || !_manager.HasAdminRole(user))
            return BadRequest("Only allowed for Admin");

        await _collection.RefreshCameraCollection(CancellationToken.None);

        return Ok();
    }

    [HttpGet("GetCameraList")]
    //[Route("GetCameraList")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(List<CameraDto>))]
    public IActionResult GetCameraList()
    {
        var userRoles = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty)?.Roles;
        if (userRoles == null || userRoles.Count == 0)
            return Ok(new List<(int, string)>());

        var cameras = _collection.Cameras
            .Where(n => n.AllowedRoles.Intersect(userRoles).Any())
            .ToArray();

        var cameraList = new List<CameraDto>();
        foreach (var camera in cameras)
        {
            FrameFormatDto? maxFrameDto = null;
            var maxFrame = camera.CameraStream.Description.FrameFormats.MaxBy(n => n.Width * n.Height);
            if (maxFrame != null)
            {
                maxFrameDto = new FrameFormatDto()
                {
                    Width = maxFrame.Width,
                    Height = maxFrame.Height,
                    Format = maxFrame.Format,
                    Fps = maxFrame.Fps,
                };
            }

            cameraList.Add(new CameraDto()
            {
                Id = camera.Id,
                Name = camera.CameraStream.Description.Name,
                Type = camera.CameraStream.Description.Type.ToString(),
                IsPtz = camera.CameraStream is IpCamera ipCam && ipCam.IsPtz,
                MaxFrameFormat = maxFrameDto,
                Url = GenerateCameraUrlInternal(camera.Id)
            });
        }

        return Ok(cameraList);
    }

    [HttpGet("GetCameraDetails")]
    //[Route("GetCameraDetails")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(CameraDescriptionDto))]
    public IActionResult GetCameraDetails(int cameraId)
    {
        if (_collection.Cameras.All(n => n.Id != cameraId))
            return BadRequest("No such camera");

        var userRoles = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty)?.Roles;
        if (userRoles == null || userRoles.Count == 0)
            return BadRequest("No such camera");

        var camera = _collection.Cameras.First(n => n.Id == cameraId);
        if (!camera.AllowedRoles.Intersect(userRoles).Any())
            return BadRequest("No such camera");

        var formats = camera.CameraStream.Description.FrameFormats
            .Select(n => new FrameFormatDto()
            {
                Width = n.Width,
                Height = n.Height,
                Format = n.Format,
                Fps = n.Fps
            });

        return Ok(new CameraDescriptionDto(cameraId,
            camera.CameraStream.Description.Name,
            camera.CameraStream.Description.Type.ToString(),
            camera.CameraStream is IpCamera ipCam && ipCam.IsPtz,
            formats));
    }

    [HttpGet("GetVideoContentByName")]
    //[Route("GetVideoContentByName")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(MemoryStream))]
    public async Task<IActionResult> GetVideoContentByName(string cameraName, int? xResolution, int? yResolution, string? format, byte? quality)
    {
        if (string.IsNullOrEmpty(cameraName))
            return BadRequest("Empty camera name");

        var cameraId = _collection.Cameras.FirstOrDefault(n => n.CameraStream.Description.Name == cameraName)?.Id ?? -1;
        await GetVideoContentInternal(cameraId, xResolution, yResolution, format, quality);

        return new EmptyResult();
    }

    [HttpGet("GetVideoContent")]
    //[Route("GetVideoContent")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(MemoryStream))]
    [SwaggerResponse((int)HttpStatusCode.BadRequest)]

    public async Task<IActionResult> GetVideoContent(int cameraId, int? xResolution, int? yResolution, string? format, byte? quality)
    {
        await GetVideoContentInternal(cameraId, xResolution, yResolution, format, quality);

        return new EmptyResult();
    }

    private async Task<IActionResult> GetVideoContentInternal(int cameraId, int? width, int? height, string? format, byte? quality)
    {
        if (_collection.Cameras.All(n => n.Id != cameraId))
            return BadRequest("No such camera");

        var userRoles = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty)?.Roles;
        if (userRoles == null || userRoles.Count == 0)
            return BadRequest("No such camera");

        var imageQueue = new ConcurrentQueue<Mat>();
        var camera = _collection.Cameras.FirstOrDefault(n => n.Id == cameraId);
        if (!(camera?.AllowedRoles.Intersect(userRoles).Any() ?? false))
            return BadRequest("No such camera");

        var frameFormat = new FrameFormatDto
        {
            Width = width ?? 0,
            Height = height ?? 0,
            Format = format ?? string.Empty
        };

        var newCameraItem = new CameraQueueItem(camera.CameraStream.Description.Path,
            Request.HttpContext.TraceIdentifier,
            frameFormat);
        var cameraCancellationToken = await _collection.HookCamera(newCameraItem, imageQueue);

        if (cameraCancellationToken == CancellationToken.None)
            return Problem("Can not connect to camera#",
                cameraId.ToString(),
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
                    Mat? outImage;
                    if (frameFormat.Width > 0
                        && frameFormat.Height > 0
                        && image.Width > frameFormat.Width
                        && image.Height > frameFormat.Height)
                    {
                        outImage = image
                            .Resize(new Size(frameFormat.Width, frameFormat.Height), interpolation: InterpolationFlags.Nearest);
                    }
                    else
                        outImage = image;

                    if (outImage != null)
                    {
                        var jpegBuffer = outImage.ToBytes(".jpg",
                            new ImageEncodingParam[]
                            {
                                    new(ImwriteFlags.JpegOptimize, 1),
                                    new(ImwriteFlags.JpegQuality, qlt)
                            });
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
            _logger.Log(LogLevel.Error, ex.ToString());
        }

        _collection.UnHookCamera(newCameraItem);

        while (imageQueue.TryDequeue(out var image))
            image?.Dispose();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);

        return new EmptyResult();
    }

    [HttpGet("GenerateCameraUrl")]
    //[Route("GenerateCameraUrl")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
    public static string GenerateCameraUrl(int cameraId, int? xResolution = 0, int? yResolution = 0, string? format = "", byte? quality = 90)
    {
        return GenerateCameraUrlInternal(cameraId, xResolution, yResolution, format, quality);
    }

    private static string GenerateCameraUrlInternal(int cameraId, int? xResolution = 0, int? yResolution = 0, string? format = "", byte? quality = 90)
    {
        return
            $"/{nameof(CameraController)[..^"Controller".Length]}/{nameof(GetVideoContent)}?{nameof(cameraId)}={cameraId}&{nameof(xResolution)}={xResolution ?? 0}&{nameof(yResolution)}={yResolution ?? 0}&{nameof(format)}={format ?? string.Empty}&{nameof(quality)}={quality ?? 90}";
    }

    [HttpPost("MoveCameraPtz")]
    //[Route("MoveCameraPtz")]
    [SwaggerResponse((int)HttpStatusCode.OK)]
    public async Task<IActionResult> MoveCameraPtz(int cameraId, int xSpeed = 0, int ySpeed = 0, int zoomSpeed = 0, int delay = 100)
    {
        var user = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty);
        if (user == null || !_manager.HasAdminRole(user))
            return BadRequest("Only allowed for Admin");

        var userRoles = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty)?.Roles;
        if (userRoles == null || userRoles.Count == 0)
            return BadRequest("No such camera");

        var camera = _collection.Cameras.FirstOrDefault(n => n.Id == cameraId);
        if (!(camera?.AllowedRoles.Intersect(userRoles).Any() ?? false))
            return BadRequest("No such camera");

        if (camera.CameraStream is not IpCamera ipCam)
            return BadRequest($"Camera is not {nameof(IpCamera)}");

        if (!ipCam.IsPtz)
            return BadRequest($"Camera is not PTZ-capable");

        await ipCam.PtzContinuousMove(xSpeed, ySpeed, zoomSpeed, delay);

        return Ok();
    }
}
