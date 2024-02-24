namespace CameraServer.Services.AntiBruteForce;

public class BruteForceDetectionSettings
{
    public int RetriesPerMinute { get; set; } = 3;
    public int RetriesPerHour { get; set; } = 10;
}