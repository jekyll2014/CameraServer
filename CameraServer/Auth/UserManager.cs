using CameraServer.Models;
using CameraServer.Services.AntiBruteForce;

using System.Net;
using System.Security.Authentication;

namespace CameraServer.Auth;

public class UserManager : IUserManager
{
    private const string TooManyAttemptsMessage = "Too many login attempts!";
    private const string UsersConfigSection = "Users";
    private const string DefaultUserConfigSection = "DefaultUser";
    private readonly IConfiguration _configuration;
    private readonly IBruteForceDetectionService? _antiBruteForceService;

    public UserManager(IConfiguration configuration, IBruteForceDetectionService? antiBruteForceService)
    {
        _configuration = configuration;
        _antiBruteForceService = antiBruteForceService;
    }

    public User? GetUser(string name, string password, IPAddress ipAddress)
    {
        if (_antiBruteForceService?.CheckThreat(name, ipAddress) ?? false)
            throw new AuthenticationException(TooManyAttemptsMessage);

        var user = GetUsers()?.FirstOrDefault(n => n.Login == name && n.Password == password);
        if (user == null)
        {
            _antiBruteForceService?.AddFailedAttempt(name, ipAddress);
            user = _configuration.GetSection(DefaultUserConfigSection).Get<User>();
        }
        else
            _antiBruteForceService?.ClearFailedAttempts(name, ipAddress);

        return user;
    }

    public UserDto? GetUserInfo(string name)
    {
        var user = GetUsers()?.FirstOrDefault(n => n.Login == name);
        if (user == null)
        {
            user = _configuration.GetSection(DefaultUserConfigSection).Get<User>();

            if (user == null)
                return null;

            user.TelegramId = 0;
        }

        return new UserDto() { Login = user.Login, Roles = user.Roles, TelegramId = user.TelegramId };
    }

    public UserDto? GetUserInfo(long telegramId)
    {
        var user = GetUsers()?.FirstOrDefault(n => n.TelegramId == telegramId);
        if (user == null)
        {
            user = _configuration.GetSection(DefaultUserConfigSection).Get<User>();

            if (user == null)
                return null;

            user.TelegramId = telegramId;
        }

        return new UserDto() { Login = user.Login, Roles = user.Roles, TelegramId = user.TelegramId };
    }

    public IEnumerable<User>? GetUsers()
    {
        var user = _configuration.GetSection(UsersConfigSection).Get<List<User>>();

        return user;
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