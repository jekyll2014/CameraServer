namespace CameraServer.Services.MotionDetection;

public class MotionDetectorParametersDto
{
    public int Width { get; set; } = 640;
    public int Height { get; set; } = 480;
    public uint DetectorDelayMs { get; set; } = 1000;
    public byte NoiseThreshold { get; set; } = 70;
    public uint ChangeLimit { get; set; } = 900;
    public uint NotificationDelay { get; set; } = 10;
    public uint KeepImageBuffer { get; set; } = 10;

    public override bool Equals(object? obj)
    {
        var result = false;
        if (obj != null && obj is MotionDetectorParametersDto setting)
        {
            if (setting.Width == Width
                && setting.Height == Height
                && setting.DetectorDelayMs == DetectorDelayMs
                && setting.NoiseThreshold == NoiseThreshold
                && setting.ChangeLimit == ChangeLimit
                && setting.NotificationDelay == NotificationDelay
                && setting.KeepImageBuffer == KeepImageBuffer)
                result = true;
        }

        return result;
    }
}