using CameraLib;

namespace CameraServer.Models
{
    public class CameraDescriptionDto
    {
        public CameraType Type { get; }
        public string Name { get; }
        public IEnumerable<FrameFormat> FrameFormats { get; }

        public CameraDescriptionDto(CameraDescription cameraDescription)
        {
            Type = cameraDescription.Type;
            Name = cameraDescription.Name;
            FrameFormats = cameraDescription.FrameFormats;
        }
    }
}