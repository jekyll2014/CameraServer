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
            ISystemClock clock,
            IUserManager manager) :
            base(options, logger, encoder, clock)
        {
            _configuration = configuration;
            _manager = manager;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var authorizationHeader = Request.Headers["Authorization"].ToString();
            if (authorizationHeader != null && authorizationHeader.StartsWith("basic", StringComparison.OrdinalIgnoreCase))
            {
                var authToken = authorizationHeader.Substring("Basic ".Length).Trim();
                var credentialsAsEncodedString = Encoding.UTF8.GetString(Convert.FromBase64String(authToken));
                var credentials = credentialsAsEncodedString.Split(':');

                var user = _manager.GetUser(credentials[0]);
                if (user != null && user.Password == credentials[1])
                {
                    var authClaims = new List<Claim>
                    {
                        new Claim("name", user.Name),
                    };

                    var userRoles = _manager.GetRoles(user);
                    authClaims.AddRange(userRoles.Select(userRole => new Claim(ClaimTypes.Role, userRole)));

                    var identity = new ClaimsIdentity(authClaims, "Basic");
                    var claimsPrincipal = new ClaimsPrincipal(identity);
                    return await Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(claimsPrincipal, Scheme.Name)));
                }
            }

            Response.StatusCode = 401;
            var realm = _configuration["JWT:ValidAudience"] ?? "";
            Response.Headers.Append("WWW-Authenticate", $"Basic realm=\"{realm}\"");
            return await Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));
        }
    }
}
