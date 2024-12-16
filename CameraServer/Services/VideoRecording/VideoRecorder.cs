using CameraLib;

using Emgu.CV;

using System.Drawing;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CameraServer.Services.VideoRecording
{
    public class VideoRecorder : IVideoRecorder, IDisposable
    {
        public string Codec
        {
            get => _fourCcCodec.ToString();
            set
            {
                if (value == "MP4V")
                    _fourCcCodec = VideoWriter.Fourcc('m', 'p', '4', 'v');//FourCC.MP4V;
                else
                    _fourCcCodec = VideoWriter.Fourcc('a', 'v', 'c', '1');//FourCC.AVC;
            }
        }

        public string FileName { get; }
        public int Width { get; }
        public int Height { get; }
        public double Fps { get; }
        public byte CompressionQuality { get; }
        private int _fourCcCodec = VideoWriter.Fourcc('a', 'v', 'c', '1');
        private const double DEFAULT_FPS = 20.0;
        private VideoWriter? _videoWriter;
        private readonly ILogger<VideoRecorderService> _logger;
        private bool _disposedValue;

        public VideoRecorder(
            string fileName,
            FrameFormatDto frameFormat,
            byte quality,
            ILogger<VideoRecorderService> logger)
        {
            _logger = logger;
            FileName = fileName;
            Width = frameFormat.Width;
            Height = frameFormat.Height;
            Fps = frameFormat.Fps;
            if (Fps <= 0)
                Fps = DEFAULT_FPS;

            CompressionQuality = quality;
        }

        public void SaveFrame(Mat? image)
        {
            if (image == null)
                return;

            var outImage = image;
            try
            {
                /*if (Width > 0 && Height > 0 && image.Width > Width && image.Height > Height)
                {
                    outImage = image
                        .ToImage<Rgb, byte>()
                        .Resize(Width, Height, Inter.Nearest);
                }
                else*/
                //outImage = image.ToImage<Rgb, byte>();
                //outImage = image.Clone();

                //if (outImage != null)
                {
                    if (_videoWriter == null)
                    {
                        _logger.Log(LogLevel.Information, $"Starting new file record [{_fourCcCodec}]: {FileName}");

                        _videoWriter = new VideoWriter(FileName,
                            _fourCcCodec,
                            Fps,
                            new Size(outImage.Width, outImage.Height),
                            true);
                        _videoWriter.Set(VideoWriter.WriterProperty.Quality, CompressionQuality);
                    }

                    _videoWriter.Write(outImage);
                    //outImage?.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Information, $"Exception saving video frame: {ex}");
                //outImage?.Dispose();
                throw;
            }
        }

        public void Stop()
        {
            _videoWriter?.Dispose();
            _logger.Log(LogLevel.Information, $"File record stopped [{_fourCcCodec}]: {FileName}");
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
