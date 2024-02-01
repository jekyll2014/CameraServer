namespace CameraServer;

public class ServerSettings
{
    public List<CustomCamera> CustomCameras { get; set; }
    public bool DiscoverIp { get; set; }
    public bool DiscoverUsb { get; set; }
}