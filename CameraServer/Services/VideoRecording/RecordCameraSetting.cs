namespace CameraServer.Services.VideoRecording;

public class RecordCameraSetting
{
    public string CameraId { get; set; } = string.Empty;
    public int Width { get; set; } = 0;
    public int Height { get; set; } = 0;
    public string User { get; set; } = string.Empty;
    public string CameraFrameFormat { get; set; } = string.Empty;
    public byte Quality { get; set; } = 90;
    public int Fps { get; set; } = 0;
}