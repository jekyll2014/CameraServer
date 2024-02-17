using CameraServer.Settings;

namespace CameraServer.Auth;

public class UserManager : IUserManager
{
    private const string UsersConfigSection = "WebUsers";
    private readonly IConfiguration _configuration;

    public UserManager(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public WebUserSettings? GetUser(string name, string password)
    {
        return GetUsers()?.FirstOrDefault(n => n.Login == name && n.Password == password);
    }

    public UserDto GetUserInfo(string name)
    {
        var user = GetUsers()?.FirstOrDefault(n => n.Login == name);
        if (user == null)
            return new UserDto();

        return new UserDto() { Login = user.Login, Roles = user.Roles };
    }

    public IEnumerable<WebUserSettings>? GetUsers()
    {
        return _configuration.GetSection(UsersConfigSection).Get<List<WebUserSettings>>();
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