namespace CameraServer.Services.MotionDetection;

public class MotionDetectionSettings
{
    public List<MotionDetectionCameraSetting> MotionDetectionCameras { get; set; } = new();
    public MotionDetectorParameters DefaultMotionDetectParameters { get; set; } = new();
}