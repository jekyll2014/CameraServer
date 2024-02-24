using CameraServer.Services.AntiBruteForce;
using CameraServer.Settings;

using System.Net;
using System.Security.Authentication;

namespace CameraServer.Auth;

public class UserManager : IUserManager
{
    private const string TooManyAttemptsMessage = "Too many login attempts!";
    private const string UsersConfigSection = "WebUsers";
    private readonly IConfiguration _configuration;
    private readonly IBruteForceDetectionService? _antiBruteForceService;

    public UserManager(IConfiguration configuration, IBruteForceDetectionService? antiBruteForceService)
    {
        _configuration = configuration;
        _antiBruteForceService = antiBruteForceService;
    }

    public WebUser? GetUser(string name, string password, IPAddress ipAddress)
    {
        if (_antiBruteForceService?.CheckThreat(name, ipAddress) ?? false)
            throw new AuthenticationException(TooManyAttemptsMessage);

        var user = GetUsers()?.FirstOrDefault(n => n.Login == name && n.Password == password);
        if (user == null)
            _antiBruteForceService?.AddFailedAttempt(name, ipAddress);
        else
            _antiBruteForceService?.ClearFailedAttempts(name, ipAddress);

        return user;
    }

    public UserDto GetUserInfo(string name)
    {
        var user = GetUsers()?.FirstOrDefault(n => n.Login == name);
        if (user == null)
            return new UserDto();

        return new UserDto() { Login = user.Login, Roles = user.Roles };
    }

    public IEnumerable<WebUser>? GetUsers()
    {
        return _configuration.GetSection(UsersConfigSection).Get<List<WebUser>>();
    }

    public bool HasAdminRole(ICameraUser webUser)
    {
        return webUser.Roles.Contains(Roles.Admin);
    }

    public bool HasRole(ICameraUser webUser, Roles role)
    {
        return webUser.Roles.Contains(role);
    }
}