namespace CameraServer.Auth;

public interface IUserManager
{
    public WebUser? GetUser(string name, string password);
    public UserDto GetUserInfo(string name);
    public IEnumerable<WebUser>? GetUsers();
    public bool HasAdminRole(ICameraUser webUser);
    public bool HasRole(ICameraUser webUser, Roles role);
}