using CameraLib;

namespace CameraServer.Services.MotionDetection;

public class MotionDetectionCameraSetting
{
    public string CameraId { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public FrameFormatDto FrameFormat { get; set; } = new();
    public MotionDetectorParameters? MotionDetectParameters { get; set; }
    public List<NotificationParameters> Notifications { get; set; } = new();
}