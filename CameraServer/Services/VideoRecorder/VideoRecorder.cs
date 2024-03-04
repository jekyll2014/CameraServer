using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

using System.Drawing;

namespace CameraServer.Services.VideoRecorder
{
    public class VideoRecorder : IDisposable
    {
        private const double DefaultFps = 20.0;
        private readonly string _fileName;
        private readonly int _fourcc = VideoWriter.Fourcc('m', 'p', '4', 'v');
        private VideoWriter? _videoWriter;
        private int _width;
        private int _height;
        private readonly double _fps;
        private readonly byte _compressionQuality;
        private bool _disposedValue;

        public VideoRecorder(string fileName, int width = 0, int height = 0, double fps = DefaultFps, byte quality = 90)
        {
            _fileName = fileName;
            _width = width;
            _height = height;

            if (fps <= 0)
                fps = DefaultFps;

            _fps = fps;
            _compressionQuality = quality;
        }

        public void SaveFrame(Mat frame)
        {
            Image<Rgb, byte> outImage;
            if (_width > 0 && _height > 0 && frame.Width > _width && frame.Height > _height)
            {
                outImage = frame
                    .ToImage<Rgb, byte>()
                    .Resize(_width, _height, Inter.Nearest);
            }
            else
                outImage = frame.ToImage<Rgb, byte>();


            // video stream record to file
            if (_videoWriter == null)
            {
                _videoWriter = new VideoWriter(_fileName,
                    _fourcc,
                    _fps,
                    new Size(outImage.Width, outImage.Height),
                    true);
                _videoWriter.Set(VideoWriter.WriterProperty.Quality, _compressionQuality);
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
