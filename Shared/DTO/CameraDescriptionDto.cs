using CameraLib;

namespace CameraServer.Shared.DTO;

[Serializable]
public class CameraDescriptionDto
{
    public CameraType Type { get; set; } = CameraType.Unknown;
    public string Name { get; set; } = string.Empty;
    public IEnumerable<FrameFormat>? FrameFormats { get; set; }

    public CameraDescriptionDto()
    { }

    public CameraDescriptionDto(CameraDescription cameraDescription)
    {
        Type = cameraDescription.Type;
        Name = cameraDescription.Name;
        FrameFormats = cameraDescription.FrameFormats;
    }
}
