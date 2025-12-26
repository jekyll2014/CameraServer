namespace CameraServer.Shared.DTO;

/// <summary>
/// DTO representing an active recording task for client display
/// </summary>
[Serializable]
public class RecordTaskDto
{
    public Guid Id { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public string CameraId { get; set; } = string.Empty;
    public string CameraName { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public FrameFormatDto FrameFormat { get; set; } = new();
    public byte Quality { get; set; }
    public string Codec { get; set; } = string.Empty;
    public string Status { get; set; } = "Recording";
}
