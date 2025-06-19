namespace CameraServer.Shared.DTO;

public class MotionDetectorParametersDto
{
    public int Width { get; set; } = 640;
    public int Height { get; set; } = 480;
    public uint DetectorDelayMs { get; set; } = 1000;
    public byte NoiseThreshold { get; set; } = 70;
    //percent of the total image area
    public double ChangeLimit { get; set; } = 0.1;
    public uint TextNotificationDelay { get; set; } = 10;
    public uint ImageNotificationDelay { get; set; } = 10;
    public uint VideoNotificationDelay { get; set; } = 1;
    public uint KeepImageBuffer { get; set; } = 10;

    public override bool Equals(object? obj)
    {
        var result = false;
        if (obj is MotionDetectorParametersDto setting)
        {
            if (setting.Width == Width
                && setting.Height == Height
                && setting.DetectorDelayMs == DetectorDelayMs
                && setting.NoiseThreshold == NoiseThreshold
                && setting.ChangeLimit == ChangeLimit
                && setting.TextNotificationDelay == TextNotificationDelay
                && setting.ImageNotificationDelay == ImageNotificationDelay
                && setting.VideoNotificationDelay == VideoNotificationDelay
                && setting.KeepImageBuffer == KeepImageBuffer)
                result = true;
        }

        return result;
    }

    public override int GetHashCode()
    {
        return $"{Width}{Height}{DetectorDelayMs}{NoiseThreshold}{ChangeLimit}{TextNotificationDelay}{ImageNotificationDelay}{VideoNotificationDelay}{KeepImageBuffer}".GetHashCode();
    }
}