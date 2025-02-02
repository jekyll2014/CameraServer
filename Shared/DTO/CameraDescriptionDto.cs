namespace CameraServer.Shared.DTO;

[Serializable]
public class CameraDescriptionDto
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public IEnumerable<FrameFormatDto>? FrameFormats { get; set; }

    public CameraDescriptionDto()
    { }

    public CameraDescriptionDto(string name, string type, IEnumerable<FrameFormatDto>? frameFormats)
    {
        Type = type;
        Name = name;
        FrameFormats = frameFormats;
    }
}
