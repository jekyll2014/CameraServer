using Emgu.CV;

using System.Drawing;

namespace CameraServer.Services.Helpers
{
    public class VideoRecorder : IDisposable
    {
        private const double DefaultFps = 25.0;
        private readonly string _fileName;
        private readonly int _fourcc = VideoWriter.Fourcc('m', 'p', '4', 'v');
        private VideoWriter? _videoWriter;
        private readonly double _fps;
        private readonly byte _compressionQuality;
        private bool _disposedValue;

        public VideoRecorder(string fileName, double fps = DefaultFps, byte quality = 90)
        {
            _fileName = fileName;

            if (fps < 0)
                fps = DefaultFps;

            _fps = fps;
            _compressionQuality = quality;
        }

        public void SaveFrame(Mat frame)
        {
            // video stream record to file
            if (_videoWriter == null)
            {
                _videoWriter = new VideoWriter(_fileName,
                    _fourcc,
                    _fps,
                    new Size(frame.Width, frame.Height),
                    true);
                _videoWriter.Set(VideoWriter.WriterProperty.Quality, _compressionQuality);
            }

            _videoWriter.Write(frame);
        }

        public void Stop()
        {
            _videoWriter?.Dispose();
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
