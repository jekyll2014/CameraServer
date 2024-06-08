namespace CameraServer.Services.MotionDetection;

public class MotionDetectionSettings
{
    public string StoragePath { get; set; } = "";
    public List<MotionDetectionCameraSettingDto> MotionDetectionCameras { get; set; } = new();
    public MotionDetectorParametersDto DefaultMotionDetectParametersDto { get; set; } = new();
}