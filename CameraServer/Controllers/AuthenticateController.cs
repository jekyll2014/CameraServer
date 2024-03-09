using CameraServer.Auth;
using CameraServer.Views.Account;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

using System.Net;
using System.Security.Authentication;
using System.Security.Claims;

namespace CameraServer.Controllers;

[ApiController]
[Route("[controller]")]
public class AuthenticateController : ControllerBase
{
    private const string LoginFailedMessage = "Invalid Credential";
    private readonly IConfiguration _configuration;
    private readonly IUserManager _manager;
    private readonly IHttpContextAccessor _accessor;

    public AuthenticateController(IConfiguration configuration, IUserManager manager, IHttpContextAccessor accessor)
    {
        _configuration = configuration;
        _manager = manager;
        _accessor = accessor;
    }

    [HttpPost]
    [Route("Login")]
    public async Task<IActionResult> Login([FromBody] LoginModel loginModel)
    {
        try
        {
            var user = _manager.GetUser(loginModel.Login ?? string.Empty,
                loginModel.Password ?? string.Empty,
                _accessor.HttpContext?.Connection.RemoteIpAddress ?? IPAddress.None);
            if (user != null)
            {
                var authClaims = new List<Claim>
                {
                    new(ClaimTypes.Name, user.Login),
                };

                foreach (var userRole in user.Roles)
                {
                    authClaims.Add(new Claim(ClaimTypes.Role, userRole.ToString()));
                }

                var expireTime = _configuration.GetValue<int>(Program.ExpireTimeSection, 60);
                var authProperties = new AuthenticationProperties
                {
                    AllowRefresh = true,
                    // Refreshing the authentication session should be allowed.

                    ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(expireTime),
                    // The time at which the authentication ticket expires. A 
                    // value set here overrides the ExpireTimeSpan option of 
                    // CookieAuthenticationOptions set with AddCookie.

                    IsPersistent = loginModel.RememberLogin,
                    // Whether the authentication session is persisted across 
                    // multiple requests. When used with cookies, controls
                    // whether the cookie's lifetime is absolute (matching the
                    // lifetime of the authentication ticket) or session-based.

                    IssuedUtc = DateTimeOffset.Now,
                    // The time at which the authentication ticket was issued.

                    RedirectUri = loginModel.ReturnUrl
                    // The full path or absolute URI to be used as an http 
                    // redirect response value.
                };

                var claimsIdentity = new ClaimsIdentity(
                    authClaims, CookieAuthenticationDefaults.AuthenticationScheme);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                return Ok();
            }

            return Unauthorized(LoginFailedMessage);
        }
        catch (AuthenticationException ex)
        {
            return Unauthorized(ex.Message);
        }
    }

    [HttpPost]
    [Route("Logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();

        return Ok();
    }
}