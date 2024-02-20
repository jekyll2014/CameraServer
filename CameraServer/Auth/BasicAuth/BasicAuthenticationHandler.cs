using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

namespace CameraServer.Auth.BasicAuth
{
    public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly IUserManager _manager;

        public BasicAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            IConfiguration configuration,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IUserManager manager) :
            base(options, logger, encoder)
        {
            _configuration = configuration;
            _manager = manager;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var realm = _configuration["ValidAudience"] ?? Request.Host.ToString();
            Response.Headers.Append("WWW-Authenticate", $"Basic realm=\"{realm}\"");
            var allowBasicAuthentication = _configuration.GetSection("AllowBasicAuthentication").Get<bool>();

            if (!allowBasicAuthentication)
                return AuthenticateResult.NoResult();

            if (Request.HttpContext.User.Identity?.IsAuthenticated ?? false)
            {
                var authClaims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, Request.HttpContext.User.Identity.Name??""),
                };

                var userRoles = Request.HttpContext.User.FindAll(ClaimTypes.Role);
                authClaims.AddRange(userRoles.Select(userRole => new Claim(ClaimTypes.Role, userRole.ToString())));

                var identity = new ClaimsIdentity(authClaims, "Basic");
                var claimsPrincipal = new ClaimsPrincipal(identity);

                return await Task.FromResult(
                    AuthenticateResult.Success(new AuthenticationTicket(claimsPrincipal, Scheme.Name)));
            }

            var authorizationHeader = Request.Headers.Authorization.ToString();
            if (!string.IsNullOrEmpty(authorizationHeader)
                && authorizationHeader.StartsWith("basic", StringComparison.OrdinalIgnoreCase))
            {
                var authToken = authorizationHeader.Substring("Basic ".Length).Trim();
                var credentialsAsEncodedString = Encoding.UTF8.GetString(Convert.FromBase64String(authToken));
                var credentials = credentialsAsEncodedString.Split(':');

                var user = _manager.GetUser(credentials[0], credentials[1]);
                if (user != null)
                {
                    var authClaims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, user.Login),
                    };

                    var userRoles = user.Roles;
                    authClaims.AddRange(userRoles.Select(userRole => new Claim(ClaimTypes.Role, userRole.ToString())));

                    var identity = new ClaimsIdentity(authClaims, "Basic");
                    var claimsPrincipal = new ClaimsPrincipal(identity);
                    return await Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(claimsPrincipal, Scheme.Name)));
                }
            }

            Response.StatusCode = 401;
            return await Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));
        }
    }
}
