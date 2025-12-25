using CameraLib;

using Microsoft.Extensions.Logging;

using OpenCvSharp;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CameraServer.Server.Services.VideoRecording;

public class VideoRecorder : IVideoRecorder, IDisposable
{
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

    public string FileName { get; }
    public int Width { get; }
    public int Height { get; }
    public double Fps { get; }
    public byte CompressionQuality { get; }

    private FourCC _fourCcCodec = FourCC.AVC;
    private const double DEFAULT_FPS = 20.0;
    private VideoWriter? _videoWriter;
    private readonly ILogger<VideoRecorderService> _logger;
    private readonly MatPoolManager? _matPoolManager;
    private bool _disposedValue;

    public VideoRecorder(
        string fileName,
        CameraServer.Shared.DTO.FrameFormatDto frameFormat,
        byte quality,
        ILogger<VideoRecorderService> logger,
        MatPoolManager? matPoolManager = null)
    {
        _logger = logger;
        _matPoolManager = matPoolManager;
        FileName = fileName;
        Width = frameFormat.Width;
        Height = frameFormat.Height;
        Fps = frameFormat.Fps;
        if (Fps <= 0)
            Fps = DEFAULT_FPS;

        CompressionQuality = quality;

        if (_matPoolManager != null)
        {
            _logger.Log(LogLevel.Debug,
                "VideoRecorder initialized with Mat pooling for {Width}x{Height}",
                Width, Height);
        }
    }

    public void SaveFrame(Mat? image)
    {
        if (image == null)
            return;

        // Use pooling if available, otherwise fall back to direct allocation
        if (_matPoolManager != null && Width > 0 && Height > 0)
        {
            SaveFrameWithPooling(image);
        }
        else
        {
            SaveFrameWithoutPooling(image);
        }
    }

    private void SaveFrameWithPooling(Mat image)
    {
        if (Width > 0 && Height > 0 && (image.Width != Width || image.Height != Height))
        {
            // Use pooled Mat for resizing
            using var resized = _matPoolManager!.RentScoped(Width, Height);
            Cv2.Resize(image, resized.Mat, new Size(Width, Height), interpolation: InterpolationFlags.Nearest);
            WriteFrame(resized.Mat);
        }
        else
        {
            // No resize needed, write directly
            WriteFrame(image);
        }
    }

    private void SaveFrameWithoutPooling(Mat image)
    {
        Mat? outImage = null;
        try
        {
            if (Width > 0 && Height > 0 && image.Width > Width && image.Height > Height)
                outImage = image.Resize(new Size(Width, Height), interpolation: InterpolationFlags.Nearest);
            else
                outImage = image.Clone();

            if (outImage != null)
            {
                WriteFrame(outImage);
            }
        }
        finally
        {
            outImage?.Dispose();
        }
    }

    private void WriteFrame(Mat frame)
    {
        try
        {
            if (_videoWriter == null)
            {
                _logger.Log(LogLevel.Information, $"Starting new file record [{_fourCcCodec}]: {FileName}");
                _videoWriter = new VideoWriter(FileName,
                    _fourCcCodec,
                    Fps,
                    new Size(frame.Width, frame.Height),
                    true);

                _videoWriter.Set(VideoWriterProperties.Quality, CompressionQuality);
            }

            _videoWriter.Write(frame);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, $"Exception saving video frame: {ex}");
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
