using System.Collections.Generic;

namespace CameraLib
{
    public class CameraDescription
    {
        public CameraType Type { get; }

        public string Path { get; set; }

        public string Name { get; set; }

        public IEnumerable<FrameFormat> FrameFormats { get; set; }

        public string ServiceAddress { get; set; }


        public CameraDescription(CameraType type, string path, string name = "", IEnumerable<FrameFormat>? frameFormats = null)
        {
            Type = type;
            Path = path;
            Name = name;
            FrameFormats = frameFormats ?? [];
        }
    }
}