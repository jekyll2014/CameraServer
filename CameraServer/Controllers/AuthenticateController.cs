using CameraServer.Auth;
using CameraServer.Views.Account;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

using System.Security.Claims;

namespace CameraServer.Controllers;

[ApiController]
[Route("[controller]")]
public class AuthenticateController : ControllerBase
{

    private readonly IConfiguration _configuration;
    private readonly IUserManager _manager;

    public AuthenticateController(IConfiguration configuration, IUserManager manager)
    {
        _configuration = configuration;
        _manager = manager;
    }

    [HttpPost]
    [Route("Login")]
    public async Task<IActionResult> Login([FromBody] LoginModel loginModel)
    {
        var user = _manager.GetUser(loginModel.Login ?? "", loginModel.Password ?? "");
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

        return Unauthorized();
    }

    [HttpPost]
    [Route("Logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();

        return Ok();
    }
}