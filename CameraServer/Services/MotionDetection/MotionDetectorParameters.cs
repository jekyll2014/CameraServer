namespace CameraServer.Services.MotionDetection;

public class MotionDetectorParameters
{
    public int Width { get; set; } = 640;
    public int Height { get; set; } = 480;
    public uint DetectorDelayMs { get; set; } = 1000;
    public byte NoiseThreshold { get; set; } = 70;
    public double ChangeLimit { get; set; } = 0.003;
    public uint NotificationDelay { get; set; } = 10;
    public uint KeepImageBuffer { get; set; } = 10;
}