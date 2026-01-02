using CameraLib;

using CameraServer.Server.Auth;
using CameraServer.Server.Models;
using CameraServer.Server.Services.CameraHub;
using CameraServer.Server.Services.Configuration;
using CameraServer.Server.Services.Telegram;
using CameraServer.Server.Services.VideoRecording;
using CameraServer.Shared.DTO;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Swashbuckle.AspNetCore.Annotations;

using System.Net;

namespace CameraServer.Server.Controllers;

[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
[Authorize(AuthenticationSchemes = Program.BasicAuthenticationSchemeName)]
[ApiController]
[Route("[controller]")]
public class ConfigurationController : ControllerBase
{
    private readonly IUserManager _userManager;
    private readonly CameraHubService _cameraHub;
    private readonly IApplicationConfigurationService _applicationConfig;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationController> _logger;

    public ConfigurationController(
        IUserManager userManager,
        CameraHubService cameraHub,
        IApplicationConfigurationService applicationConfig,
        IConfiguration configuration,
        ILogger<ConfigurationController> logger)
    {
        _userManager = userManager;
        _cameraHub = cameraHub;
        _applicationConfig = applicationConfig;
        _configuration = configuration;
        _logger = logger;
    }

    #region System Configuration

    [HttpGet("GetSystemSettings")]
    [SwaggerOperation(
        Summary = "Get system configuration settings",
        Description = "Returns system-wide configuration settings (read-only, sourced from appsettings.json)",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<SystemSettingsDto>))]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult GetSystemSettings()
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can view system settings");

            var settings = new SystemSettingsDto
            {
                ServerUrls = _configuration["Urls"] ?? "http://0.0.0.0:8080",
                CookieExpireTimeMinutes = _applicationConfig.GetCookieExpireTimeMinutes(),
                AllowBasicAuthentication = _applicationConfig.GetAllowBasicAuthentication(),
                ExternalHostUrl = _applicationConfig.GetExternalHostUrl()
            };

            return Ok(ApiResponse<SystemSettingsDto>.SuccessResponse(settings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving system settings");
            return StatusCode(500, ApiResponse<SystemSettingsDto>.ErrorResponse($"Failed to retrieve system settings: {ex.Message}"));
        }
    }

    #endregion

    #region Telegram Settings

    [HttpGet("GetTelegramSettings")]
    [SwaggerOperation(
        Summary = "Get Telegram bot settings",
        Description = "Returns current Telegram configuration",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<TelegramSettingsDto>))]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult GetTelegramSettings()
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can access Telegram settings");

            var telegramSettings = _applicationConfig.GetTelegramSettings();
            var settings = new TelegramSettingsDto
            {
                Token = telegramSettings.Token,
                ReconnectTimeout = telegramSettings.ReconnectTimeout,
                DefaultVideoTime = telegramSettings.DefaultVideoTime,
                DefaultVideoQuality = telegramSettings.DefaultVideoQuality,
                DefaultImageQuality = telegramSettings.DefaultImageQuality
            };

            return Ok(ApiResponse<TelegramSettingsDto>.SuccessResponse(settings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Telegram settings");
            return StatusCode(500, ApiResponse<TelegramSettingsDto>.ErrorResponse($"Failed to retrieve Telegram settings: {ex.Message}"));
        }
    }

    [HttpPost("UpdateTelegramSettings")]
    [SwaggerOperation(
        Summary = "Update Telegram bot settings",
        Description = "Updates Telegram configuration without affecting other settings",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<bool>))]
    [SwaggerResponse((int)HttpStatusCode.BadRequest, "Invalid settings")]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult UpdateTelegramSettings([FromBody] TelegramSettingsDto settings)
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can modify Telegram settings");

            ArgumentNullException.ThrowIfNull(settings);

            var telegramSettings = new TelegeramSettings
            {
                Token = settings.Token,
                ReconnectTimeout = settings.ReconnectTimeout,
                DefaultVideoTime = settings.DefaultVideoTime,
                DefaultVideoQuality = settings.DefaultVideoQuality,
                DefaultImageQuality = settings.DefaultImageQuality
            };

            _applicationConfig.UpdateTelegramSettings(telegramSettings);
            _applicationConfig.SaveConfiguration();

            _logger.LogInformation("Telegram settings updated");
            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<bool>.ErrorResponse($"Validation error: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Telegram settings");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"Failed to update Telegram settings: {ex.Message}"));
        }
    }

    #endregion

    #region Brute Force Detection Settings

    [HttpGet("GetBruteForceSettings")]
    [SwaggerOperation(
        Summary = "Get brute force detection settings",
        Description = "Returns current brute force detection configuration",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<BruteForceDetectionSettingsDto>))]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult GetBruteForceSettings()
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can access brute force detection settings");

            var bruteForceSettings = _applicationConfig.GetBruteForceDetectionSettings();
            var settings = new BruteForceDetectionSettingsDto
            {
                RetriesPerMinute = bruteForceSettings.RetriesPerMinute,
                RetriesPerHour = bruteForceSettings.RetriesPerHour
            };

            return Ok(ApiResponse<BruteForceDetectionSettingsDto>.SuccessResponse(settings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving brute force detection settings");
            return StatusCode(500, ApiResponse<BruteForceDetectionSettingsDto>.ErrorResponse($"Failed to retrieve brute force detection settings: {ex.Message}"));
        }
    }

    [HttpPost("UpdateBruteForceSettings")]
    [SwaggerOperation(
        Summary = "Update brute force detection settings",
        Description = "Updates brute force detection configuration without affecting other settings",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<bool>))]
    [SwaggerResponse((int)HttpStatusCode.BadRequest, "Invalid settings")]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult UpdateBruteForceSettings([FromBody] BruteForceDetectionSettingsDto settings)
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can modify brute force detection settings");

            ArgumentNullException.ThrowIfNull(settings);

            var bruteForceSettings = new Services.AntiBruteForce.BruteForceDetectionSettings
            {
                RetriesPerMinute = settings.RetriesPerMinute,
                RetriesPerHour = settings.RetriesPerHour
            };

            _applicationConfig.UpdateBruteForceDetectionSettings(bruteForceSettings);
            _applicationConfig.SaveConfiguration();

            _logger.LogInformation("Brute force detection settings updated");
            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<bool>.ErrorResponse($"Validation error: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating brute force detection settings");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"Failed to update brute force detection settings: {ex.Message}"));
        }
    }

    #endregion

    #region Motion Detection Settings

    [HttpGet("GetMotionDetectionSettings")]
    [SwaggerOperation(
        Summary = "Get motion detection settings",
        Description = "Returns current motion detection configuration",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<MotionDetectionRuntimeSettingsDto>))]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult GetMotionDetectionSettings()
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can access motion detection settings");

            var motionDetectionSettings = _applicationConfig.GetMotionDetectionSettings();
            var settings = new MotionDetectionRuntimeSettingsDto
            {
                StoragePath = motionDetectionSettings.StoragePath,
                DefaultMotionDetectParameters = motionDetectionSettings.DefaultMotionDetectParameters
            };

            return Ok(ApiResponse<MotionDetectionRuntimeSettingsDto>.SuccessResponse(settings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving motion detection settings");
            return StatusCode(500, ApiResponse<MotionDetectionRuntimeSettingsDto>.ErrorResponse($"Failed to retrieve motion detection settings: {ex.Message}"));
        }
    }

    [HttpPost("UpdateMotionDetectionSettings")]
    [SwaggerOperation(
        Summary = "Update motion detection settings",
        Description = "Updates motion detection configuration without affecting other settings",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<bool>))]
    [SwaggerResponse((int)HttpStatusCode.BadRequest, "Invalid settings")]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult UpdateMotionDetectionSettings([FromBody] MotionDetectionRuntimeSettingsDto settings)
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can modify motion detection settings");

            ArgumentNullException.ThrowIfNull(settings);

            var motionDetectionSettings = new Services.MotionDetection.MotionDetectionSettings
            {
                StoragePath = settings.StoragePath,
                DefaultMotionDetectParameters = settings.DefaultMotionDetectParameters,
                MotionDetectionCameras = new List<MotionDetectionCameraSettingDto>()
            };

            _applicationConfig.UpdateMotionDetectionSettings(motionDetectionSettings);
            _applicationConfig.SaveConfiguration();

            _logger.LogInformation("Motion detection settings updated");
            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<bool>.ErrorResponse($"Validation error: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating motion detection settings");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"Failed to update motion detection settings: {ex.Message}"));
        }
    }

    #endregion

    #region Video Recording Settings

    [HttpGet("GetVideoRecordingSettings")]
    [SwaggerOperation(
        Summary = "Get video recording settings",
        Description = "Returns current video recording configuration",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<VideoRecordingRuntimeSettingsDto>))]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult GetVideoRecordingSettings()
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can access video recording settings");

            var videoRecordingSettings = _applicationConfig.GetVideoRecordingSettings();
            var settings = new VideoRecordingRuntimeSettingsDto
            {
                StoragePath = videoRecordingSettings.StoragePath,
                VideoFileLengthSeconds = videoRecordingSettings.VideoFileLengthSeconds,
                DefaultVideoQuality = videoRecordingSettings.DefaultVideoQuality
            };

            return Ok(ApiResponse<VideoRecordingRuntimeSettingsDto>.SuccessResponse(settings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving video recording settings");
            return StatusCode(500, ApiResponse<VideoRecordingRuntimeSettingsDto>.ErrorResponse($"Failed to retrieve video recording settings: {ex.Message}"));
        }
    }

    [HttpPost("UpdateVideoRecordingSettings")]
    [SwaggerOperation(
        Summary = "Update video recording settings",
        Description = "Updates video recording configuration without affecting other settings",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<bool>))]
    [SwaggerResponse((int)HttpStatusCode.BadRequest, "Invalid settings")]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult UpdateVideoRecordingSettings([FromBody] VideoRecordingRuntimeSettingsDto settings)
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can modify video recording settings");

            ArgumentNullException.ThrowIfNull(settings);

            var videoRecordingSettings = new RecorderSettings
            {
                StoragePath = settings.StoragePath,
                VideoFileLengthSeconds = settings.VideoFileLengthSeconds,
                DefaultVideoQuality = settings.DefaultVideoQuality,
                RecordCameras = new List<RecordCameraSettingDto>()
            };

            _applicationConfig.UpdateVideoRecordingSettings(videoRecordingSettings);
            _applicationConfig.SaveConfiguration();

            _logger.LogInformation("Video recording settings updated");
            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<bool>.ErrorResponse($"Validation error: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating video recording settings");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"Failed to update video recording settings: {ex.Message}"));
        }
    }

    #endregion

    #region Camera Settings

    [HttpGet("GetCameraSettings")]
    [SwaggerOperation(
        Summary = "Get camera configuration settings",
        Description = "Returns current camera configuration",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<CameraSettingsDto>))]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult GetCameraSettings()
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can access camera settings");

            var cameraSettings = _applicationConfig.GetCameraSettings();
            var settings = new CameraSettingsDto
            {
                AutoSearchIp = cameraSettings.AutoSearchIp,
                AutoSearchUsb = cameraSettings.AutoSearchUsb,
                AutoSearchUsbFC = cameraSettings.AutoSearchUsbFC,
                DefaultAllowedRoles = cameraSettings.DefaultAllowedRoles?.Select(r => r.ToString()).ToList() ?? new List<string>(),
                DiscoveryTimeOut = cameraSettings.DiscoveryTimeOut,
                ForceCameraConnect = cameraSettings.ForceCameraConnect,
                MaxFrameBuffer = cameraSettings.MaxFrameBuffer,
                CustomCameras = cameraSettings.CustomCameras?.Select(c => new CameraConfigDto
                {
                    Type = c.Type.ToString(),
                    Name = c.Name,
                    Path = c.Path,
                    AllowedRoles = c.AllowedRoles.Select(r => r.ToString()).ToList(),
                    AuthenticationType = c.AuthenticationType.ToString(),
                    Login = c.Login,
                    Password = c.Password
                }).ToList() ?? new List<CameraConfigDto>(),
                FrameTimeout = cameraSettings.FrameTimeout
            };

            return Ok(ApiResponse<CameraSettingsDto>.SuccessResponse(settings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving camera settings");
            return StatusCode(500, ApiResponse<CameraSettingsDto>.ErrorResponse($"Failed to retrieve camera settings: {ex.Message}"));
        }
    }

    [HttpPost("UpdateCameraSettings")]
    [SwaggerOperation(
        Summary = "Update camera configuration settings",
        Description = "Updates camera settings. Changes require application restart to take effect.",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<bool>))]
    [SwaggerResponse((int)HttpStatusCode.BadRequest, "Invalid camera settings")]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult UpdateCameraSettings([FromBody] CameraSettingsDto settingsDto)
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can modify camera settings");

            ArgumentNullException.ThrowIfNull(settingsDto);

            var validationErrors = ValidateCameraSettingsDto(settingsDto);
            if (validationErrors.Count > 0)
                return BadRequest(ApiResponse<bool>.ValidationErrorResponse(validationErrors));

            var cameraSettings = new CameraSettings
            {
                AutoSearchIp = settingsDto.AutoSearchIp,
                AutoSearchUsb = settingsDto.AutoSearchUsb,
                AutoSearchUsbFC = settingsDto.AutoSearchUsbFC,
                DefaultAllowedRoles = settingsDto.DefaultAllowedRoles?.Select(r => Enum.Parse<Roles>(r)).ToList() ?? new List<Roles>(),
                DiscoveryTimeOut = settingsDto.DiscoveryTimeOut,
                ForceCameraConnect = settingsDto.ForceCameraConnect,
                MaxFrameBuffer = settingsDto.MaxFrameBuffer,
                FrameTimeout = settingsDto.FrameTimeout,
                CustomCameras = settingsDto.CustomCameras?.Select(c => new CustomCameraDto
                {
                    Type = Enum.Parse<CameraType>(c.Type),
                    Name = c.Name,
                    Path = c.Path,
                    AllowedRoles = c.AllowedRoles.Select(r => Enum.Parse<Roles>(r)).ToList(),
                    AuthenticationType = Enum.Parse<AuthType>(c.AuthenticationType),
                    Login = c.Login,
                    Password = c.Password
                }).ToList() ?? new List<CustomCameraDto>()
            };

            _applicationConfig.UpdateCameraSettings(cameraSettings);
            _applicationConfig.SaveConfiguration();

            _logger.LogInformation("Camera settings updated");
            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<bool>.ErrorResponse($"Validation error: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating camera settings");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"Failed to update camera settings: {ex.Message}"));
        }
    }

    #endregion

    #region User Management

    [HttpGet("GetUsers")]
    [SwaggerOperation(
        Summary = "Get all users",
        Description = "Returns list of all configured users",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<List<UserManagementDto>>))]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult GetUsers()
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can view users");

            var users = _applicationConfig.GetUsers()
                .Select(u => new UserManagementDto
                {
                    Login = u.Login,
                    Name = u.Name,
                    Roles = u.Roles.Select(r => r.ToString()).ToList(),
                    TelegramId = u.TelegramId,
                    TelegramName = u.TelegramName,
                    DefaultCodec = u.DefaultCodec,
                    DefaultUser = u.DefaultUser
                }).ToList();

            return Ok(ApiResponse<List<UserManagementDto>>.SuccessResponse(users));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users");
            return StatusCode(500, ApiResponse<List<UserManagementDto>>.ErrorResponse($"Failed to retrieve users: {ex.Message}"));
        }
    }

    [HttpPost("SaveUser")]
    [SwaggerOperation(
        Summary = "Create or update a user",
        Description = "Saves user to settings.json. Changes require application restart to take effect.",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<bool>))]
    [SwaggerResponse((int)HttpStatusCode.BadRequest, "Invalid user data")]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult SaveUser([FromBody] UserCreateUpdateDto userDto)
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can manage users");

            ArgumentNullException.ThrowIfNull(userDto);

            var validationErrors = ValidateUserDto(userDto);
            if (validationErrors.Count > 0)
                return BadRequest(ApiResponse<bool>.ValidationErrorResponse(validationErrors));

            var user = new User
            {
                Login = userDto.Login,
                Password = userDto.Password,
                Name = userDto.Name,
                Roles = userDto.Roles.Select(r => Enum.Parse<Roles>(r)).ToList(),
                TelegramId = userDto.TelegramId,
                TelegramName = userDto.TelegramName,
                DefaultCodec = userDto.DefaultCodec,
                DefaultUser = userDto.DefaultUser
            };

            var existingUsers = _applicationConfig.GetUsers();
            var existingUser = existingUsers.FirstOrDefault(u => u.Login == userDto.Login);

            if (existingUser != null)
            {
                // Update existing user
                if (string.IsNullOrWhiteSpace(user.Password))
                {
                    user.Password = existingUser.Password;
                }

                _applicationConfig.UpdateUser(user);
                _applicationConfig.SaveConfiguration();

                _logger.LogInformation($"User '{user.Login}' updated");
                return Ok(ApiResponse<bool>.SuccessResponse(true));
            }
            else
            {
                // Add new user
                _applicationConfig.AddUser(user);
                _applicationConfig.SaveConfiguration();

                _logger.LogInformation($"User '{user.Login}' created");
                return Ok(ApiResponse<bool>.SuccessResponse(true));
            }
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<bool>.ErrorResponse($"Validation error: {ex.Message}"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<bool>.ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving user");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"Failed to save user: {ex.Message}"));
        }
    }

    [HttpDelete("DeleteUser")]
    [SwaggerOperation(
        Summary = "Delete a user",
        Description = "Deletes user from settings.json. Changes require application restart to take effect.",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<bool>))]
    [SwaggerResponse((int)HttpStatusCode.BadRequest, "Invalid user login")]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult DeleteUser([FromQuery] string login)
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can delete users");

            if (string.IsNullOrWhiteSpace(login))
                return BadRequest(ApiResponse<bool>.ErrorResponse("User login is required"));

            if (HttpContext.User.Identity?.Name == login)
                return BadRequest(ApiResponse<bool>.ErrorResponse("Cannot delete currently logged in user"));

            _applicationConfig.DeleteUser(login);
            _applicationConfig.SaveConfiguration();

            _logger.LogInformation($"User '{login}' deleted");
            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<bool>.ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"Failed to delete user: {ex.Message}"));
        }
    }

    #endregion

    #region Validation

    private List<string> ValidateCameraSettingsDto(CameraSettingsDto settings)
    {
        var errors = new List<string>();

        if (settings.DiscoveryTimeOut < 100)
            errors.Add("DiscoveryTimeOut must be at least 100ms");

        if (settings.MaxFrameBuffer < 1)
            errors.Add("MaxFrameBuffer must be at least 1");

        if (settings.FrameTimeout < 1000)
            errors.Add("FrameTimeout must be at least 1000ms");

        return errors;
    }

    private List<string> ValidateUserDto(UserCreateUpdateDto user)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(user.Login))
            errors.Add("Login is required");

        var existingUsers = _applicationConfig.GetUsers();

        if (user.Roles == null || user.Roles.Count == 0)
            errors.Add("At least one role is required");

        // Validate DefaultUser constraint: only one user can be marked as default
        if (user.DefaultUser)
        {
            var otherDefaultUsers = existingUsers
                .Where(u => u.DefaultUser && u.Login != user.Login)
                .ToList();

            if (otherDefaultUsers.Any())
            {
                errors.Add($"Only one user can be marked as Default User. Currently '{otherDefaultUsers.First().Login}' is set as default.");
            }
        }

        return errors;
    }

    private bool IsAdmin()
    {
        var userInfo = _userManager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty);
        return userInfo != null && _userManager.HasAdminRole(userInfo);
    }

    #endregion
}
