using CameraServer.Auth;

namespace CameraServer.Settings;

public class CustomCamera
{
    public CameraType Type { get; set; } = CameraType.Undefined;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public List<Roles> AllowedRoles { get; set; } = [];
}