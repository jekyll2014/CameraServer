using CameraServer.Shared.DTO;

namespace CameraServer.Server.Services.MotionDetection;

public class MotionDetectionSettings
{
    public string StoragePath { get; set; } = "";
    public List<MotionDetectionCameraSettingDto> MotionDetectionCameras { get; set; } = new();
    public MotionDetectorParametersDto DefaultMotionDetectParameters { get; set; } = new();
}