using CameraServer.Server.Auth;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Swashbuckle.AspNetCore.Annotations;

using System.Diagnostics;
using System.Net;

namespace CameraServer.Server.Controllers;

[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
[Authorize(AuthenticationSchemes = Program.BasicAuthenticationSchemeName)]
[ApiController]
[Route("[controller]")]
public class SystemController : ControllerBase
{
    private readonly IUserManager _manager;
    private readonly HealthCheckService _healthCheckService;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<CameraController> _logger;

    public SystemController(IUserManager manager,
        HealthCheckService healthCheckService,
        IHostApplicationLifetime appLifetime,
        ILogger<CameraController> logger)
    {
        _manager = manager;
        _healthCheckService = healthCheckService;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    [HttpGet("Get")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(HealthReport))]
    public async Task<IActionResult> Get()
    {
        var report = await _healthCheckService.CheckHealthAsync();

        return report.Status == HealthStatus.Healthy ? Ok(report) : StatusCode((int)HttpStatusCode.ServiceUnavailable, report);
    }

    [HttpPost("Restart")]
    [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(string))]
    public IActionResult Restart()
    {
        var user = _manager.GetUserInfo(HttpContext.User.Identity?.Name ?? string.Empty);
        _logger.LogInformation($"Restart requested by {HttpContext.User.Identity?.Name}");

        if (user == null || !_manager.HasAdminRole(user))
            return BadRequest("Only allowed for Admin");

        _appLifetime.StopApplication();
        var processPath = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(processPath))
        {
            var currentProcess = Path.GetFullPath(processPath);
            Process.Start(currentProcess);
        }

        return Ok();
    }
}
