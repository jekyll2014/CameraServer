namespace CameraServerWatchdog;

public class WatchDogSettings
{
    public string ServiceName { get; set; } = "CameraServer";
    public string Url { get; set; } = "localhost:808/healthcheck";
    public int Timeout { get; set; } = 120; // sec.
    public int MaxFailCount { get; set; } = 3;
}
