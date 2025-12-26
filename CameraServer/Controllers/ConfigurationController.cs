using CameraLib;
using CameraServer.Server.Auth;
using CameraServer.Server.Models;
using CameraServer.Server.Services.CameraHub;
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
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationController> _logger;
    private readonly Config<CameraSettings> _cameraConfig;
    private readonly Config<List<User>> _userConfig;

    public ConfigurationController(
        IUserManager userManager,
        CameraHubService cameraHub,
        IConfiguration configuration,
        ILogger<ConfigurationController> logger)
    {
        _userManager = userManager;
        _cameraHub = cameraHub;
        _configuration = configuration;
        _logger = logger;

        // Initialize configuration managers
        _cameraConfig = new Config<CameraSettings>("appsettings.json");
        _userConfig = new Config<List<User>>("appsettings.json");
    }

    #region Camera Configuration

    [HttpGet("GetCameraSettings")]
    [SwaggerOperation(
        Summary = "Get camera configuration settings",
        Description = "Returns all camera-related configuration including auto-search settings and custom cameras",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<CameraSettings>))]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult GetCameraSettings()
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can access camera settings");

            var settings = _configuration.GetSection("CameraSettings").Get<CameraSettings>();
            return Ok(ApiResponse<CameraSettings>.SuccessResponse(settings ?? new CameraSettings()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving camera settings");
            return StatusCode(500, ApiResponse<CameraSettings>.ErrorResponse($"Failed to retrieve camera settings: {ex.Message}"));
        }
    }

    [HttpPost("UpdateCameraSettings")]
    [SwaggerOperation(
        Summary = "Update camera configuration settings",
        Description = "Updates camera-related configuration. Requires application restart to take effect.",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<bool>))]
    [SwaggerResponse((int)HttpStatusCode.BadRequest, "Invalid settings")]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult UpdateCameraSettings([FromBody] CameraSettings settings)
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can modify camera settings");

            ArgumentNullException.ThrowIfNull(settings);

            // Validate settings
            var validationErrors = ValidateCameraSettings(settings);
            if (validationErrors.Count > 0)
                return BadRequest(ApiResponse<bool>.ValidationErrorResponse(validationErrors));

            _cameraConfig.ConfigStorage = settings;
            var success = _cameraConfig.SaveConfig();

            if (success)
                return Ok(ApiResponse<bool>.SuccessResponse(true));
            else
                return StatusCode(500, ApiResponse<bool>.ErrorResponse("Failed to save configuration"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating camera settings");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"Failed to update camera settings: {ex.Message}"));
        }
    }

    [HttpPost("AddCamera")]
    [SwaggerOperation(
        Summary = "Add a new camera configuration",
        Description = "Adds a new custom camera to the configuration. Requires application restart to take effect.",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<bool>))]
    [SwaggerResponse((int)HttpStatusCode.BadRequest, "Invalid camera configuration")]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult AddCamera([FromBody] CameraConfigDto camera)
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can add cameras");

            ArgumentNullException.ThrowIfNull(camera);

            var validationErrors = ValidateCameraConfig(camera);
            if (validationErrors.Count > 0)
                return BadRequest(ApiResponse<bool>.ValidationErrorResponse(validationErrors));

            var settings = _configuration.GetSection("CameraSettings").Get<CameraSettings>() ?? new CameraSettings();
            
            // Check if camera already exists
            if (settings.CustomCameras.Any(c => c.Path == camera.Path))
                return BadRequest(ApiResponse<bool>.ErrorResponse("Camera with this path already exists"));

            // Parse enum values from strings
            if (!Enum.TryParse<CameraType>(camera.Type, out var cameraType))
                return BadRequest(ApiResponse<bool>.ErrorResponse($"Invalid camera type: {camera.Type}"));

            if (!Enum.TryParse<AuthType>(camera.AuthenticationType, out var authType))
                return BadRequest(ApiResponse<bool>.ErrorResponse($"Invalid authentication type: {camera.AuthenticationType}"));

            var customCamera = new CustomCameraDto
            {
                Type = cameraType,
                Name = camera.Name,
                Path = camera.Path,
                AllowedRoles = camera.AllowedRoles
                    .Select(r => Enum.TryParse<Roles>(r, out var role) ? role : Roles.Guest)
                    .ToList(),
                AuthenticationType = authType,
                Login = camera.Login,
                Password = camera.Password
            };

            settings.CustomCameras.Add(customCamera);
            _cameraConfig.ConfigStorage = settings;
            var success = _cameraConfig.SaveConfig();

            if (success)
                return Ok(ApiResponse<bool>.SuccessResponse(true));
            else
                return StatusCode(500, ApiResponse<bool>.ErrorResponse("Failed to save camera configuration"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding camera");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"Failed to add camera: {ex.Message}"));
        }
    }

    [HttpDelete("RemoveCamera")]
    [SwaggerOperation(
        Summary = "Remove a camera configuration",
        Description = "Removes a custom camera from the configuration by path. Requires application restart to take effect.",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<bool>))]
    [SwaggerResponse((int)HttpStatusCode.NotFound, "Camera not found")]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult RemoveCamera([FromQuery] string cameraPath)
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can remove cameras");

            if (string.IsNullOrWhiteSpace(cameraPath))
                return BadRequest(ApiResponse<bool>.ErrorResponse("Camera path is required"));

            var settings = _configuration.GetSection("CameraSettings").Get<CameraSettings>() ?? new CameraSettings();
            var camera = settings.CustomCameras.FirstOrDefault(c => c.Path == cameraPath);

            if (camera == null)
                return NotFound(ApiResponse<bool>.ErrorResponse("Camera not found"));

            settings.CustomCameras.Remove(camera);
            _cameraConfig.ConfigStorage = settings;
            var success = _cameraConfig.SaveConfig();

            if (success)
                return Ok(ApiResponse<bool>.SuccessResponse(true));
            else
                return StatusCode(500, ApiResponse<bool>.ErrorResponse("Failed to save camera configuration"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing camera");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"Failed to remove camera: {ex.Message}"));
        }
    }

    #endregion

    #region User Configuration

    [HttpGet("GetUsers")]
    [SwaggerOperation(
        Summary = "Get all configured users",
        Description = "Returns list of all users (passwords excluded)",
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

            var users = _userManager.GetUsers()?
                .Select(u => new UserManagementDto
                {
                    Login = u.Login,
                    Name = u.Name,
                    Roles = u.Roles.Select(r => r.ToString()).ToList(),
                    TelegramId = u.TelegramId,
                    TelegramName = u.TelegramName,
                    DefaultCodec = u.DefaultCodec
                })
                .ToList() ?? new List<UserManagementDto>();

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
        Description = "Creates a new user or updates an existing one. Requires application restart to take effect.",
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

            var users = _configuration.GetSection("Users").Get<List<User>>() ?? new List<User>();
            var existingUser = users.FirstOrDefault(u => u.Login == userDto.Login);

            var user = new User
            {
                Login = userDto.Login,
                Password = userDto.Password,
                Name = userDto.Name,
                Roles = userDto.Roles
                    .Select(r => Enum.TryParse<Roles>(r, out var role) ? role : Roles.Guest)
                    .ToList(),
                TelegramId = userDto.TelegramId,
                TelegramName = userDto.TelegramName,
                DefaultCodec = userDto.DefaultCodec
            };

            if (existingUser != null)
            {
                // Update existing user
                users.Remove(existingUser);
            }

            users.Add(user);
            _userConfig.ConfigStorage = users;
            var success = _userConfig.SaveConfig();

            if (success)
                return Ok(ApiResponse<bool>.SuccessResponse(true));
            else
                return StatusCode(500, ApiResponse<bool>.ErrorResponse("Failed to save user configuration"));
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
        Description = "Removes a user from the configuration. Requires application restart to take effect.",
        Tags = new[] { "Configuration" }
    )]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ApiResponse<bool>))]
    [SwaggerResponse((int)HttpStatusCode.NotFound, "User not found")]
    [SwaggerResponse((int)HttpStatusCode.Forbidden, "User is not an administrator")]
    public IActionResult DeleteUser([FromQuery] string login)
    {
        try
        {
            if (!IsAdmin())
                return Forbid("Only administrators can delete users");

            if (string.IsNullOrWhiteSpace(login))
                return BadRequest(ApiResponse<bool>.ErrorResponse("Login is required"));

            var users = _configuration.GetSection("Users").Get<List<User>>() ?? new List<User>();
            var user = users.FirstOrDefault(u => u.Login == login);

            if (user == null)
                return NotFound(ApiResponse<bool>.ErrorResponse("User not found"));

            users.Remove(user);
            _userConfig.ConfigStorage = users;
            var success = _userConfig.SaveConfig();

            if (success)
                return Ok(ApiResponse<bool>.SuccessResponse(true));
            else
                return StatusCode(500, ApiResponse<bool>.ErrorResponse("Failed to save user configuration"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"Failed to delete user: {ex.Message}"));
        }
    }

    #endregion

    #region System Configuration

    [HttpGet("GetSystemSettings")]
    [SwaggerOperation(
        Summary = "Get system configuration settings",
        Description = "Returns system-wide configuration settings",
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
                ServerUrls = _configuration["Urls"] ?? string.Empty,
                CookieExpireTimeMinutes = _configuration.GetValue<int>("CookieExpireTimeMinutes", 60),
                AllowBasicAuthentication = _configuration.GetValue<bool>("AllowBasicAuthentication", true),
                ExternalHostUrl = _configuration["ExternalHostUrl"] ?? string.Empty
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

    #region Validation

    private List<string> ValidateCameraSettings(CameraSettings settings)
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

    private List<string> ValidateCameraConfig(CameraConfigDto camera)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(camera.Name))
            errors.Add("Camera name is required");

        if (string.IsNullOrWhiteSpace(camera.Path))
            errors.Add("Camera path is required");

        if (camera.AllowedRoles == null || camera.AllowedRoles.Count == 0)
            errors.Add("At least one allowed role is required");

        if (string.IsNullOrWhiteSpace(camera.Type) || camera.Type == "Unknown")
            errors.Add("Valid camera type is required");

        return errors;
    }

    private List<string> ValidateUserDto(UserCreateUpdateDto user)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(user.Login))
            errors.Add("Login is required");

        if (string.IsNullOrWhiteSpace(user.Password))
            errors.Add("Password is required");

        if (user.Roles == null || user.Roles.Count == 0)
            errors.Add("At least one role is required");

        return errors;
    }

    private bool IsAdmin()
    {
        var userInfo = _userManager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty);
        return userInfo != null && _userManager.HasAdminRole(userInfo);
    }

    #endregion
}
