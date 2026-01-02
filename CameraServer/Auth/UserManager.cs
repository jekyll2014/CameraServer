using CameraServer.Server.Models;
using CameraServer.Server.Services.AntiBruteForce;
using CameraServer.Server.Services.Configuration;

using Microsoft.Extensions.Configuration;

using System.Net;
using System.Security.Authentication;

namespace CameraServer.Server.Auth;

public class UserManager : IUserManager
{
    private const string TooManyAttemptsMessage = "Too many login attempts!";
    private const string UsersConfigSection = "Users";
    private const string DefaultUserConfigSection = "DefaultUser";
    private readonly IConfiguration _configuration;
    private readonly IBruteForceDetectionService? _antiBruteForceService;
    private readonly IApplicationConfigurationService _applicationConfigurationService;

    public UserManager(IConfiguration configuration, IBruteForceDetectionService? antiBruteForceService, IApplicationConfigurationService applicationConfigurationService)
    {
        _configuration = configuration;
        _antiBruteForceService = antiBruteForceService;
        _applicationConfigurationService = applicationConfigurationService;
    }

    // ToDo: Shall I block repetitive logins for anonymous/unknown user?
    public User? GetUser(string name, string password, IPAddress ipAddress)
    {
        if (_antiBruteForceService?.CheckThreat(name, ipAddress) ?? false)
            throw new AuthenticationException(TooManyAttemptsMessage);

        var users = GetUsers()?.ToArray();
        var user = users?.FirstOrDefault(n => n.Login == name && n.Password == password);
        if (user == null)
        {
            if (!(users?.Any(n => n.Login == name) ?? false))
                user = _applicationConfigurationService.GetDefaultUser();
            else
                _antiBruteForceService?.AddFailedAttempt(name, ipAddress);
        }
        else
            _antiBruteForceService?.ClearFailedAttempts(name, ipAddress);

        return user;
    }

    public UserDto? GetUserInfo(string name)
    {
        User? user;
        if (string.IsNullOrEmpty(name))
            user = _applicationConfigurationService.GetDefaultUser();
        else
            user = GetUsers()?.FirstOrDefault(n => n.Login == name);

        return user == null ? null : new UserDto()
        {
            Login = user.Login,
            Name = user.Name,
            Roles = user.Roles,
            TelegramId = user.TelegramId,
            TelegramName = user.TelegramName,
            DefaultCodec = user.DefaultCodec,
        };
    }

    public UserDto? GetUserInfo(long telegramId)
    {
        User? user;
        if (telegramId <= 0)
        {
            user = _applicationConfigurationService.GetDefaultUser();

            if (user != null)
                user.TelegramId = telegramId;
        }
        else
            user = GetUsers()?.FirstOrDefault(n => n.TelegramId == telegramId);

        return user == null ? null : new UserDto()
        {
            Login = user.Login,
            Name = user.Name,
            Roles = user.Roles,
            TelegramId = user.TelegramId,
            TelegramName = user.TelegramName,
            DefaultCodec = user.DefaultCodec
        };
    }

    public IEnumerable<User>? GetUsers()
    {
        return _applicationConfigurationService.GetUsers();
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