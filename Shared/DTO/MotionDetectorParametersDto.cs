using CameraServer.Shared.Enum;

namespace CameraServer.Shared.DTO;

public class MotionDetectorParametersDto
{
    public DetectionMethod DetectMethod { get; set; } = DetectionMethod.Knn;
    public int Width { get; set; } = 640;
    public int Height { get; set; } = 480;
    public uint DetectorDelayMs { get; set; } = 500;

    //percent of the total image area
    public double ChangeLimit { get; set; } = 10.0;
    public uint TextNotificationDelay { get; set; } = 10;
    public uint ImageNotificationDelay { get; set; } = 10;
    public uint VideoNotificationDelay { get; set; } = 1;
    public uint KeepImageBuffer { get; set; } = 10;

    public override bool Equals(object? obj)
    {
        var result = false;
        if (obj is MotionDetectorParametersDto setting)
        {
            if (setting.DetectMethod == DetectMethod
                && setting.Width == Width
                && setting.Height == Height
                && setting.DetectorDelayMs == DetectorDelayMs
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
        return $"{DetectMethod}{Width}{Height}{DetectorDelayMs}{ChangeLimit}{TextNotificationDelay}{ImageNotificationDelay}{VideoNotificationDelay}{KeepImageBuffer}".GetHashCode();
    }
}