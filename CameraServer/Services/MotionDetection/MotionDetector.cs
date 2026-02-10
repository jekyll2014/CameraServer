using CameraServer.Shared.DTO;
using CameraServer.Shared.Enum;

using Microsoft.Extensions.Logging;

using OpenCvSharp;

namespace CameraServer.Server.Services.MotionDetection;

public class MotionDetector : IDisposable
{
    public Mat ProcessedFrame = new();

    private DetectionMethod DetectMethod { get; set; } = DetectionMethod.Knn;
    private const int DetectorRestartMs = 10000;

    private readonly uint _detectorDelayMs;
    private readonly int _width;
    private readonly int _height;
    private readonly double _changeLimit;
    private readonly ILogger? _logger;
    private readonly BackgroundSubtractor _backgroundSubtractor;
    private Mat? _resizedFrame = null;
    private Mat? _backgroundFrame = null;
    private DateTime _nextFrameProcessTime = DateTime.Now;
    private bool _disposedValue;

    public MotionDetector(MotionDetectorParametersDto parametersDto, ILogger? logger = null)
    {
        DetectMethod = parametersDto.DetectMethod;
        _changeLimit = parametersDto.ChangeLimit;
        _width = parametersDto.Width;
        _height = parametersDto.Height;
        _detectorDelayMs = parametersDto.DetectorDelayMs;
        _logger = logger;

        if (DetectMethod == DetectionMethod.Mog)
            _backgroundSubtractor = BackgroundSubtractorMOG.Create();
        else if (DetectMethod == DetectionMethod.Mog2)
            _backgroundSubtractor = BackgroundSubtractorMOG2.Create();
        else if (DetectMethod == DetectionMethod.Gmg)
            _backgroundSubtractor = BackgroundSubtractorGMG.Create();
        else
            _backgroundSubtractor = BackgroundSubtractorKNN.Create();

        _logger?.LogInformation("MotionDetector initialized");
    }

    public bool DetectMovement(Mat? frame, out Mat? contourFrame)
    {
        contourFrame = null;
        if (frame == null)
            return false;

        var result = false;
        var currentTime = DateTime.Now;
        if (_nextFrameProcessTime < currentTime.AddMilliseconds(-DetectorRestartMs - _detectorDelayMs))
            _nextFrameProcessTime = currentTime.AddMilliseconds(_detectorDelayMs);

        if (_backgroundFrame != null && _resizedFrame != null)
        {
            if (currentTime < _nextFrameProcessTime)
                return false;

            Cv2.Resize(frame, _resizedFrame, new Size(_width, _height), interpolation: InterpolationFlags.Nearest);

            // Update background frame
            _backgroundSubtractor.Apply(_resizedFrame, _backgroundFrame);

            // Copy thresholded result to ProcessedFrame for external access
            _backgroundFrame.CopyTo(ProcessedFrame);
            Cv2.FindContours(_backgroundFrame,
                out var contours,
                out _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            // Check if motion detected
            var foundContours = new List<Point[]>();
            foreach (var c in contours)
            {
                var pixelCount = Cv2.ContourArea(c);
                if ((double)pixelCount / (_backgroundFrame.Width * _backgroundFrame.Height) * 100.0d >= _changeLimit)
                {
                    foundContours.Add(c);
                    result = true;
                }
            }

            if (result && foundContours.Any())
            {
                if (contourFrame == null && _resizedFrame != null)
                {
                    contourFrame = new Mat();
                    _resizedFrame.CopyTo(contourFrame);
                }

                if (contourFrame != null && foundContours.Any())
                    Cv2.DrawContours(contourFrame, foundContours, -1, Scalar.OrangeRed, 2);
            }

            _nextFrameProcessTime = currentTime.AddMilliseconds(_detectorDelayMs);
        }
        else
        {
            // Initialize background frame on first run
            _resizedFrame = new Mat(_width, _height, MatType.CV_32FC1);
            _backgroundFrame = new Mat(_width, _height, MatType.CV_32FC1);
            Cv2.Resize(frame, _resizedFrame, new Size(_width, _height), interpolation: InterpolationFlags.Nearest);
            _backgroundSubtractor.Apply(_resizedFrame, _backgroundFrame);
            _backgroundFrame.CopyTo(ProcessedFrame);
        }

        return result;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // Log pooling statistics before disposal
                _logger?.LogInformation("MotionDetector pooling statistics:");

                ProcessedFrame?.Dispose();
                _backgroundFrame?.Dispose();
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
