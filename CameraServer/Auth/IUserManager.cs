using CameraServer.Models;

using System.Net;

namespace CameraServer.Auth;

public interface IUserManager
{
    public User? GetUser(string name, string password, IPAddress ipAddress);
    public UserDto? GetUserInfo(string name);
    public UserDto? GetUserInfo(long telegramId);
    public IEnumerable<User>? GetUsers();
    public bool HasAdminRole(ICameraUser webUser);
    public bool HasRole(ICameraUser webUser, Roles role);
}