using CameraLib;

namespace CameraServer.Services.VideoRecording;

public class RecordCameraSettingDto
{
    public string CameraId { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public FrameFormatDto FrameFormat { get; set; } = new();
    public byte Quality { get; set; } = 90;
    public string Codec { get; set; } = "AVC";
}