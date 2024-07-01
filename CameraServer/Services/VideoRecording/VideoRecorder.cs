using CameraLib;

using OpenCvSharp;

namespace CameraServer.Services.VideoRecording
{
    public class VideoRecorder : iVideoRecorder, IDisposable
    {
        private const double DEFAULT_FPS = 20.0;

        public string Codec
        {
            get => _fourCcCodec.Value.ToString();
            set
            {
                if (value == "MP4V")
                    _fourCcCodec = FourCC.MP4V;
                else
                    _fourCcCodec = FourCC.AVC;
            }
        }

        private FourCC _fourCcCodec = FourCC.AVC; // FourCC.FromFourChars('a', 'v', 'c', '1'), FourCC.AVC, +FourCC.MP4V, +FourCC.XVID
        public string FileName { get; }
        public int Width { get; }
        public int Height { get; }
        public double Fps { get; }
        public byte CompressionQuality { get; }
        private VideoWriter? _videoWriter;
        private bool _disposedValue;

        public VideoRecorder(string fileName, FrameFormatDto frameFormat, byte quality = 90)
        {
            FileName = fileName;
            Width = frameFormat.Width;
            Height = frameFormat.Height;
            Fps = frameFormat.Fps;
            if (Fps <= 0)
                Fps = DEFAULT_FPS;

            CompressionQuality = quality;
        }

        public void SaveFrame(Mat? frame)
        {
            if (frame == null)
                return;

            Mat outImage;
            if (Width > 0 && Height > 0 && frame.Width > Width && frame.Height > Height)
            {
                outImage = frame
                    .Resize(new Size(Width, Height), interpolation: InterpolationFlags.Nearest);
            }
            else
                outImage = frame.Clone();


            if (_videoWriter == null)
            {
                _videoWriter = new VideoWriter(FileName,
                    _fourCcCodec,
                    Fps,
                    new Size(outImage.Width, outImage.Height),
                    true);
                _videoWriter.Set(VideoWriterProperties.Quality, CompressionQuality);
            }

            _videoWriter.Write(outImage);
        }

        public void Stop()
        {
            _videoWriter?.Dispose();
        }

        public static string SanitizeFileName(string path)
        {
            var invalids = Path.GetInvalidFileNameChars();
            path = invalids.Aggregate(path, (current, invalidChar) => current.Replace(invalidChar, '_'));

            return path.TrimEnd('.');
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Stop();
                    _videoWriter?.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
