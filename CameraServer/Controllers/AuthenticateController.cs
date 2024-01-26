using CameraServer.Auth;
using CameraServer.Auth.JwtAuth;

using Microsoft.AspNetCore.Mvc;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace IodinePrintModuleService.Controllers;

// comment for JWT auth
[ApiExplorerSettings(IgnoreApi = true)]
[ApiController]
[Route("[controller]")]
public class AuthenticateController : ControllerBase
{
    private IUserManager _manager;

    public AuthenticateController(IUserManager manager)
    {
        _manager = manager;
    }

    [HttpPost]
    [Route("Login")]
    public async Task<IActionResult> Login([FromBody] LoginModel model)
    {
        var user = _manager.GetUser(model.Username ?? "");
        if (user != null && user.Password == model.Password)
        {
            var userRoles = _manager.GetRoles(user);

            var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };

            foreach (var userRole in userRoles)
            {
                authClaims.Add(new Claim(ClaimTypes.Role, userRole));
            }

            var token = _manager.GetToken(authClaims);

            return Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token),
                expiration = token.ValidTo
            });
        }

        return Unauthorized();
    }
}