namespace CameraServer.Shared.DTO;

/// <summary>
/// DTO for camera settings
/// </summary>
[Serializable]
public class CameraSettingsDto
{
    public bool AutoSearchIp { get; set; } = true;
    public bool AutoSearchUsb { get; set; } = true;
    public bool AutoSearchUsbFC { get; set; } = true;
    public List<string> DefaultAllowedRoles { get; set; } = new();
    public int DiscoveryTimeOut { get; set; } = 1000;
    public bool ForceCameraConnect { get; set; } = false;
    public int MaxFrameBuffer { get; set; } = 10;
    public List<CameraConfigDto> CustomCameras { get; set; } = new();
    public int FrameTimeout { get; set; } = 30000;
}
