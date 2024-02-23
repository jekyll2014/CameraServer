namespace CameraServer.Services.AntiBruteForce;

public class AntiBruteForceSettings
{
    public int RetriesPerMinute { get; set; } = 3;
    public int RetriesPerHour { get; set; } = 10;
}