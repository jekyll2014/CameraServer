using CameraServer.Auth;
using CameraServer.Views.Account;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

using System.Net;
using System.Security.Authentication;
using System.Security.Claims;

namespace CameraServer.Controllers
{
    public class AccountController : Controller
    {
        private const string LoginFailedMessage = "Invalid Credential";
        private readonly IConfiguration _configuration;
        private readonly IUserManager _manager;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            IConfiguration configuration,
            IUserManager manager,
            ILogger<AccountController> logger)
        {
            _logger = logger;
            _configuration = configuration;
            _manager = manager;
        }

        public IActionResult Login(string returnUrl = "/")
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var objLoginModel = new LoginModel
            {
                ReturnUrl = returnUrl
            };

            return View(objLoginModel);
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginModel loginModel)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    var user = _manager.GetUser(loginModel.Login ?? string.Empty,
                        loginModel.Password ?? string.Empty,
                        HttpContext.Connection.RemoteIpAddress ?? IPAddress.None);
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

                        _logger.Log(LogLevel.Information, $"User {user.Login} authenricated. Redirecting to {loginModel.ReturnUrl}");

                        return LocalRedirect(loginModel.ReturnUrl);
                    }
                }

                _logger.Log(LogLevel.Information, LoginFailedMessage);
                ViewBag.Message = LoginFailedMessage;
            }
            catch (AuthenticationException ex)
            {
                _logger.Log(LogLevel.Error, ex.ToString());
                ViewBag.Message = ex.Message;
            }

            return View(loginModel);
        }

        //[HttpPost]
        public async Task<IActionResult> LogOut()
        {
            await HttpContext.SignOutAsync();

            //Redirect to home page
            return LocalRedirect("/");
        }
    }
}
