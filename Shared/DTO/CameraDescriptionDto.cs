namespace CameraServer.Shared.DTO;

[Serializable]
public class CameraDescriptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public IEnumerable<FrameFormatDto>? FrameFormats { get; set; }
    public bool IsPtz { get; set; } = false;

    public CameraDescriptionDto()
    { }

    public CameraDescriptionDto(int id, string name, string type, bool isPtz, IEnumerable<FrameFormatDto>? frameFormats)
    {
        Id = id;
        Name = name;
        Type = type;
        IsPtz = isPtz;
        FrameFormats = frameFormats;
    }
}
