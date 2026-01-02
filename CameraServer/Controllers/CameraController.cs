using CameraLib.IP;

using CameraServer.Server.Auth;
using CameraServer.Server.Services.CameraHub;
using CameraServer.Shared.DTO;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using OpenCvSharp;

using Swashbuckle.AspNetCore.Annotations;

using System.Net;
using System.Text;
using System.Threading.Channels;

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

    public CameraController(IUserManager manager, CameraHubService collection, ILogger<CameraController> logger)
    {
        _logger = logger;
        _manager = manager;
        _collection = collection;
    }

    [HttpPost("RefreshCameraList")]
    [SwaggerResponse((int)HttpStatusCode.OK)]
    [SwaggerResponse((int)HttpStatusCode.Conflict, Description = "Refresh already in progress")]
    public async Task<IActionResult> RefreshCameraList()
    {
        var user = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty);
        if (user == null || !_manager.HasAdminRole(user))
            return BadRequest("Only allowed for Admin");

        // Check if refresh already in progress
        if (_collection.IsRefreshing)
        {
            _logger.LogWarning("Camera refresh requested while refresh already in progress");
            return Conflict("Camera refresh already in progress. Please wait...");
        }

        await _collection.RefreshCameraCollection(CancellationToken.None);

        return Ok();
    }

    [HttpGet("GetCameraRefreshStatus")]
    [SwaggerResponse((int)HttpStatusCode.OK, Description = "Returns IsRefreshing (bool) and CameraCount (int)")]
    public IActionResult GetCameraRefreshStatus()
    {
        return Ok(new { IsRefreshing = _collection.IsRefreshing, CameraCount = _collection.Cameras.Count() });
    }

    [HttpGet("GetCameraList")]
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
                Path = camera.CameraStream.Description.Path,
                Name = camera.CameraStream.Description.Name,
                Type = camera.CameraStream.Description.Type.ToString(),
                IsPtz = camera.CameraStream is IpCamera ipCam && ipCam.IsPtz,
                MaxFrameFormat = maxFrameDto,
                Url = GenerateCameraUrlInternal(camera.Id),
                FrameFormats = camera.CameraStream.Description.FrameFormats
                    .Select(n => new FrameFormatDto()
                    {
                        Width = n.Width,
                        Height = n.Height,
                        Format = n.Format,
                        Fps = n.Fps
                    })
            });
        }

        return Ok(cameraList);
    }

    [HttpGet("GetVideoContentByName")]
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
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(MemoryStream))]
    [SwaggerResponse((int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetVideoContent(int cameraId, int? xResolution, int? yResolution, string? format, byte? quality)
    {
        await GetVideoContentInternal(cameraId, xResolution, yResolution, format, quality);

        return new EmptyResult();
    }

    [HttpGet("GenerateCameraUrl")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
    public static string GenerateCameraUrl(int cameraId, int? xResolution = 0, int? yResolution = 0, string? format = "", byte? quality = 90)
    {
        return GenerateCameraUrlInternal(cameraId, xResolution, yResolution, format, quality);
    }

    [HttpPost("ReCheckPtz")]
    [SwaggerResponse((int)HttpStatusCode.OK)]
    public async Task<IActionResult> ReCheckPtz(int cameraId)
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

        if (!string.IsNullOrEmpty(camera.CameraStream.Description.ServiceAddress))
            await ipCam.GetPtzControllerAsync(camera.CameraStream.Description.ServiceAddress);
        else
            await ipCam.GetPtzControllerAsync(camera.CameraStream.Description.Path, 5000);

        return Ok(); // ? Ok() : BadRequest($"Camera is not PTZ-capable or PTZ controller is not available");
    }

    [HttpPost("MoveCameraPtz")]
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

    private async Task<IActionResult> GetVideoContentInternal(int cameraId, int? width, int? height, string? format, byte? quality)
    {
        if (_collection.Cameras.All(n => n.Id != cameraId))
            return BadRequest("No such camera");

        var userRoles = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty)?.Roles;
        if (userRoles == null || userRoles.Count == 0)
            return BadRequest("No such camera");

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

        // Use Channel-based async frame delivery (eliminates polling)
        var (cameraCancellationToken, channelReader) = await _collection.HookCamera(
            newCameraItem,
            HttpContext.RequestAborted,
            BoundedChannelFullMode.DropNewest);  // Drop newest for smooth streaming (keep buffered frames)

        if (channelReader == null || cameraCancellationToken == CancellationToken.None)
        {
            _logger.LogWarning($"Cannot connect to camera #{cameraId}");
            return Problem("Can not connect to camera#",
                cameraId.ToString(),
                StatusCodes.Status204NoContent);
        }

        var qlt = quality ?? 95;
        if (qlt < 1)
            qlt = 1;
        else if (qlt > 100)
            qlt = 100;

        Mat? outImage = null;
        try
        {
            Response.ContentType = "multipart/x-mixed-replace; boundary=" + Boundary;

            _logger.LogInformation($"Camera #{cameraId} streaming started with Channel-based delivery (client: {Request.HttpContext.TraceIdentifier})");

            // Event-driven frame streaming - NO POLLING! ✅
            await foreach (var image in channelReader.ReadAllAsync(HttpContext.RequestAborted))
            {
                if (image == null || image.IsDisposed || image.Empty())
                {
                    _collection.ReturnOrDisposeMat(image);
                    continue;
                }

                try
                {
                    // Resize if needed
                    if (frameFormat.Width > 0
                        && frameFormat.Height > 0
                        && image.Width > frameFormat.Width
                        && image.Height > frameFormat.Height)
                    {
                        if (outImage == null || outImage.IsDisposed || outImage.Empty())
                            outImage = new Mat();

                        Cv2.Resize(image, outImage, new Size(frameFormat.Width, frameFormat.Height),
                            interpolation: InterpolationFlags.Nearest);
                    }
                    else
                    {
                        outImage = image;
                    }

                    if (!outImage.IsDisposed && !outImage.Empty())
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

                        await Response.Body.WriteAsync(Encoding.ASCII.GetBytes(header), HttpContext.RequestAborted);
                        await Response.Body.WriteAsync(jpegBuffer, HttpContext.RequestAborted);
                        await Response.Body.WriteAsync(new byte[] { 0x0d, 0x0a }, HttpContext.RequestAborted);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Client disconnected
                    _logger.LogInformation($"Camera #{cameraId} streaming cancelled by client (client: {Request.HttpContext.TraceIdentifier})");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error streaming frame from camera #{cameraId}: {ex.Message}");
                }
                finally
                {
                    _collection.ReturnOrDisposeMat(image);
                }
            }

            _logger.LogInformation($"Camera #{cameraId} streaming stopped (client: {Request.HttpContext.TraceIdentifier})");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation($"Camera #{cameraId} streaming cancelled (client: {Request.HttpContext.TraceIdentifier})");
        }
        catch (ChannelClosedException)
        {
            _logger.LogInformation($"Camera #{cameraId} streaming channel closed (client: {Request.HttpContext.TraceIdentifier})");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception in camera streaming: {ex}");
        }
        finally
        {
            // Clean up
            if (outImage != null && outImage != null)
                outImage?.Dispose();

            _collection.UnHookCamera(newCameraItem);

            _logger.LogInformation($"Camera #{cameraId} streaming cleanup complete (client: {Request.HttpContext.TraceIdentifier})");
        }

        return new EmptyResult();
    }

    private static string GenerateCameraUrlInternal(int cameraId, int? xResolution = 0, int? yResolution = 0, string? format = "", byte? quality = 90)
    {
        return
            $"/{nameof(CameraController)[..^"Controller".Length]}/{nameof(GetVideoContent)}?{nameof(cameraId)}={cameraId}&{nameof(xResolution)}={xResolution ?? 0}&{nameof(yResolution)}={yResolution ?? 0}&{nameof(format)}={format ?? string.Empty}&{nameof(quality)}={quality ?? 90}";
    }
}
