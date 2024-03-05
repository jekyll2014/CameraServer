namespace CameraServer.Services.VideoRecorder;

public class RecordCameraSetting
{
    public string CameraId { get; set; } = "";
    public int Width { get; set; } = 0;
    public int Height { get; set; } = 0;
    public string User { get; set; } = "";
    public string CameraFrameFormat { get; set; } = "";
    public byte Quality { get; set; } = 90;
    public int Fps { get; set; } = 0;
}