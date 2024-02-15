using CameraServer.Auth;

namespace CameraServer.Settings;

public class CameraSettings
{
    public bool AutoSearchIp { get; set; } = true;
    public bool AutoSearchUsb { get; set; } = true;
    public List<Roles> DefaultAllowedRoles { get; set; } = [];
    public int MaxFrameBuffer { get; set; } = 10;
    public List<CustomCamera> CustomCameras { get; set; } = [];
}