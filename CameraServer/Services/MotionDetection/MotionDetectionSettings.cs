namespace CameraServer.Services.MotionDetection;

public class MotionDetectionSettings
{
    public string StoragePath { get; set; } = "";
    public List<MotionDetectionCameraSetting> MotionDetectionCameras { get; set; } = new();
    public MotionDetectorParameters DefaultMotionDetectParameters { get; set; } = new();
}