using CameraServer.Settings;

namespace CameraServer.Auth;

public interface IUserManager
{
    public WebUserSettings? GetUser(string name, string password);
    public UserDto GetUserInfo(string name);
    public IEnumerable<WebUserSettings>? GetUsers();
    public bool HasAdminRole(ICameraUser webUser);
    public bool HasRole(ICameraUser webUser, Roles role);
}