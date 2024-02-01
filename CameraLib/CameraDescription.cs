using System;
using System.Collections.Generic;

namespace CameraLib
{
    public class CameraDescription
    {
        public CameraType Type { get; }

        public string Path { get; private set; }

        public string Name
        {
            get => _cameraName;
            set => _cameraName = value;
        }

        public IEnumerable<FrameFormat> FrameFormats { get; }

        private string _cameraName = string.Empty;

        public CameraDescription(CameraType type, string path, string name = "", IEnumerable<FrameFormat>? frameFormats = null)
        {
            Type = type;
            Path = path;
            Name = name;

            FrameFormats = frameFormats ?? Array.Empty<FrameFormat>();
        }
    }
}