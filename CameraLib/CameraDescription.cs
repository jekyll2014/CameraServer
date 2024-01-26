using System;
using System.Collections.Generic;

namespace CameraLib
{
    public class CameraDescription
    {
        public readonly CameraType Type;
        public readonly string Id;
        public string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(_cameraName))
                    return _cameraName;

                return Id;
            }
            set => _cameraName = value;
        }

        public readonly IEnumerable<FrameFormat> FrameFormats;

        private string _cameraName = string.Empty;

        public CameraDescription(CameraType type, string id, string name = "", IEnumerable<FrameFormat>? frameFormats = null)
        {
            Type = type;
            Id = id;
            Name = name;

            FrameFormats = frameFormats ?? Array.Empty<FrameFormat>();
        }
    }
}