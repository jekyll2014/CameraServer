namespace CameraServer.Shared.DTO;

[Serializable]
public class CameraDto
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsPtz { get; set; } = false;
    public FrameFormatDto? MaxFrameFormat { get; set; } = null;
    public string Url { get; set; } = string.Empty;
    public IEnumerable<FrameFormatDto>? FrameFormats { get; set; }
}