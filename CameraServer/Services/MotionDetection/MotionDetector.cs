using CameraLib;

using CameraServer.Shared.DTO;

using Microsoft.Extensions.Logging;

using OpenCvSharp;

using Size = OpenCvSharp.Size;

namespace CameraServer.Server.Services.MotionDetection;

public class MotionDetector : IDisposable
{
    public Mat ProcessedFrame = new();

    private const int DETECTOR_RESTART_MS = 10000;
    private readonly uint _detectorDelayMs;
    private readonly byte _noiseThreshold;
    private readonly int _width;
    private readonly int _height;
    private readonly double _changeLimit;
    private readonly MatPoolManager _matPoolManager;
    private readonly ILogger? _logger;
    private Mat? _backgroundFrame = null;
    private DateTime _nextFrameProcessTime = DateTime.Now;
    private bool _disposedValue;

    public MotionDetector(MotionDetectorParametersDto parametersDto, ILogger? logger = null)
    {
        _changeLimit = parametersDto.ChangeLimit;
        _width = parametersDto.Width;
        _height = parametersDto.Height;
        _noiseThreshold = parametersDto.NoiseThreshold;
        _detectorDelayMs = parametersDto.DetectorDelayMs;
        _logger = logger;

        // Create pool manager with appropriate size for motion detection
        // We typically need 5-6 Mats per detection cycle
        _matPoolManager = new MatPoolManager(maxPoolSize: 20, logger);

        _logger?.LogInformation(
            "MotionDetector initialized with Mat pooling: {Width}x{Height}",
            _width, _height);
    }

    public bool DetectMovement(Mat? frame, out Mat? contourFrame)
    {
        contourFrame = null;
        if (frame == null)
            return false;

        var result = false;
        var currentTime = DateTime.Now;
        if (_nextFrameProcessTime < currentTime.AddMilliseconds(-DETECTOR_RESTART_MS - _detectorDelayMs))
            _nextFrameProcessTime = currentTime.AddMilliseconds(_detectorDelayMs);

        if (_backgroundFrame != null)
        {
            if (currentTime < _nextFrameProcessTime)
                return false;

            // Use pooled Mats for all intermediate operations
            using var resized = _matPoolManager.RentScoped(_width, _height);
            using var gray = _matPoolManager.RentScoped(_width, _height, MatType.CV_8UC1);
            using var blurred = _matPoolManager.RentScoped(_width, _height, MatType.CV_8UC1);
            using var equalized = _matPoolManager.RentScoped(_width, _height, MatType.CV_8UC1);
            using var backgroundGray = _matPoolManager.RentScoped(_width, _height, MatType.CV_8UC1);
            using var diff = _matPoolManager.RentScoped(_width, _height, MatType.CV_8UC1);
            using var thresh = _matPoolManager.RentScoped(_width, _height, MatType.CV_8UC1);
            using var accumulateMask = _matPoolManager.RentScoped(_width, _height, MatType.CV_8UC1);

            // Process input frame into grayscale
            Cv2.Resize(frame, resized.Mat, new Size(_width, _height), interpolation: InterpolationFlags.Nearest);
            Cv2.CvtColor(resized.Mat, gray.Mat, ColorConversionCodes.BGR2GRAY);
            Cv2.GaussianBlur(gray.Mat, blurred.Mat, new Size(21, 21), 0);
            Cv2.EqualizeHist(blurred.Mat, equalized.Mat);

            // Convert background to grayscale
            Cv2.ConvertScaleAbs(_backgroundFrame, backgroundGray.Mat);

            // Calculate difference
            Cv2.Absdiff(equalized.Mat, backgroundGray.Mat, diff.Mat);

            // Update background frame
            var alpha = result ? 0.1 : 0.5;
            Cv2.AccumulateWeighted(equalized.Mat, _backgroundFrame, alpha, accumulateMask.Mat);

            // Threshold to find changed pixels
            Cv2.Threshold(diff.Mat, thresh.Mat, _noiseThreshold, 255, ThresholdTypes.Binary);

            // Copy thresholded result to ProcessedFrame for external access
            thresh.Mat.CopyTo(ProcessedFrame);

            // Find contours to detect motion regions
            Cv2.FindContours(thresh.Mat,
                out var contours,
                out _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            // Check if motion detected
            var foundContours = new List<Point[]>();
            foreach (var c in contours)
            {
                var r = Cv2.BoundingRect(c);
                var pixelCount = CountPixels(thresh.Mat, r);
                if ((double)pixelCount / (_width * _height) * 100.0d >= _changeLimit)
                {
                    result = true;
                    foundContours.Add(c);
                }
            }

            if (result && foundContours.Any())
            {
                if (contourFrame == null && resized.Mat != null)
                {
                    contourFrame = new Mat();
                    resized.Mat.CopyTo(contourFrame);
                }

                if (contourFrame != null && foundContours.Any())
                    Cv2.DrawContours(contourFrame, foundContours, -1, Scalar.OrangeRed, 2);
            }

            _nextFrameProcessTime = currentTime.AddMilliseconds(_detectorDelayMs);
        }
        else
        {
            // Initialize background frame on first run
            using var resized = _matPoolManager.RentScoped(_width, _height);
            using var gray = _matPoolManager.RentScoped(_width, _height, MatType.CV_8UC1);
            using var blurred = _matPoolManager.RentScoped(_width, _height, MatType.CV_8UC1);
            using var equalized = _matPoolManager.RentScoped(_width, _height, MatType.CV_8UC1);

            Cv2.Resize(frame, resized.Mat, new Size(_width, _height), interpolation: InterpolationFlags.Nearest);
            Cv2.CvtColor(resized.Mat, gray.Mat, ColorConversionCodes.BGR2GRAY);
            Cv2.GaussianBlur(gray.Mat, blurred.Mat, new Size(21, 21), 0);
            Cv2.EqualizeHist(blurred.Mat, equalized.Mat);

            _backgroundFrame = new Mat(_width, _height, MatType.CV_32FC1);
            equalized.Mat.AssignTo(_backgroundFrame, MatType.CV_32FC1);
        }

        return result;
    }

    private int CountPixels(Mat image, Rect r)
    {
        // Use pooled Mat for region extraction
        using var region = _matPoolManager.RentScoped(r.Width, r.Height, MatType.CV_8UC1);

        // Extract region
        var roi = new Mat(image, r);
        roi.CopyTo(region.Mat);

        var count = region.Mat.CountNonZero();
        return count;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // Log pooling statistics before disposal
                _logger?.LogInformation("MotionDetector pooling statistics:");
                _matPoolManager?.LogStatistics();

                ProcessedFrame?.Dispose();
                _backgroundFrame?.Dispose();
                _matPoolManager?.Dispose();
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
