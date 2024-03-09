using CameraLib;
using CameraLib.IP;

using CameraServer.Auth;

namespace CameraServer.Models;

public class CustomCamera
{
    public CameraType Type { get; set; } = CameraType.Unknown;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public List<Roles> AllowedRoles { get; set; } = new();
    public AuthType AuthenicationType { get; set; } = AuthType.None;
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}