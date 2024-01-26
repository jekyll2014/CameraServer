using Microsoft.IdentityModel.Tokens;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace CameraServer.Auth;

public interface IUserManager
{
    public User? GetUser(string name);
    public IEnumerable<User> GetUsers();
    public IEnumerable<string> GetRoles(User user);
    public bool HasAdminRole(IEnumerable<string> userRoles);
    public bool HasUserRole(IEnumerable<string> userRoles);
    public JwtSecurityToken GetToken(IEnumerable<Claim> authClaims);
}

public class UserManager : IUserManager
{
    private const string Admin = "admin";
    private const string User = "user";
    private const string UsersConfigSection = "Users";
    private readonly IConfiguration _configuration;

    public UserManager(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public User? GetUser(string name)
    {
        return GetUsers().FirstOrDefault(n => n.Name == name);
    }

    public IEnumerable<User> GetUsers()
    {
        return _configuration.GetSection(UsersConfigSection).Get<List<User>>();
    }

    public IEnumerable<string> GetRoles(User user)
    {
        return user.Roles.FindAll(n => n == Admin || n == User);
    }

    public bool HasAdminRole(IEnumerable<string> userRoles)
    {
        return userRoles.Any(x => x == UserManager.Admin);
    }

    public bool HasUserRole(IEnumerable<string> userRoles)
    {
        return userRoles.Any(x => x == UserManager.User);
    }

    public JwtSecurityToken GetToken(IEnumerable<Claim> authClaims)
    {
        var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["JWT:Secret"]));

        var tokenExpireHours = 24;
        int.TryParse(_configuration["JWT:ExpireHours"], out tokenExpireHours);

        var token = new JwtSecurityToken(
            issuer: _configuration["JWT:ValidIssuer"],
            audience: _configuration["JWT:ValidAudience"],
            expires: DateTime.Now.AddHours(tokenExpireHours),
            claims: authClaims,
            signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
        );

        return token;
    }
}