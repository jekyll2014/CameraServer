using CameraServer.Shared.DTO;

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
    private Mat? _backgroundFrame = null; //new Mat(,CV_32FC1);
    private DateTime _nextFrameProcessTime = DateTime.Now;
    private bool _disposedValue;

    public MotionDetector(MotionDetectorParametersDto parametersDto)
    {
        _changeLimit = parametersDto.ChangeLimit;
        _width = parametersDto.Width;
        _height = parametersDto.Height;
        _noiseThreshold = parametersDto.NoiseThreshold;
        _detectorDelayMs = parametersDto.DetectorDelayMs;
    }

    public bool DetectMovement(Mat? frame)
    {
        if (frame == null)
            return false;

        var result = false;
        var currentTime = DateTime.Now;
        if (_nextFrameProcessTime < currentTime.AddMilliseconds(-DETECTOR_RESTART_MS - _detectorDelayMs))
            _nextFrameProcessTime = currentTime.AddMilliseconds(_detectorDelayMs);

        // movement detection
        if (_backgroundFrame != null)
        {
            if (currentTime < _nextFrameProcessTime)
                return false;

            // resize
            var currFrame = frame.Resize(new Size(_width, _height), interpolation: InterpolationFlags.Nearest)
                .CvtColor(ColorConversionCodes.BGR2GRAY)
                .GaussianBlur(new Size(21, 21), 0)
                .EqualizeHist();

            // compare
            var backgroundFrameGray = new Mat();
            Cv2.ConvertScaleAbs(_backgroundFrame, backgroundFrameGray);
            var imgAbsDiff = new Mat();
            Cv2.Absdiff(currFrame, backgroundFrameGray, imgAbsDiff);
            backgroundFrameGray.Dispose();

            // update background frame
            if (result)
                Cv2.AccumulateWeighted(currFrame, _backgroundFrame, 0.1, new Mat());
            else
                Cv2.AccumulateWeighted(currFrame, _backgroundFrame, 0.5, new Mat());

            currFrame.Dispose();

            // filter out the noise
            Cv2.Threshold(imgAbsDiff, ProcessedFrame, _noiseThreshold, 255, ThresholdTypes.Binary);
            imgAbsDiff.Dispose();

            // Find contours around the blobs
            Cv2.FindContours(ProcessedFrame,
                out var contours,
                out _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple); // ApproxTC89L1, ApproxSimple

            //Find big blobs to activate alarm
            //var n = 0;
            foreach (var c in contours)
            {
                var r = Cv2.BoundingRect(c);
                //var r2 = Cv2.MinAreaRect(c);
                var pixelCount = CountPixels(ProcessedFrame, r);
                //var pixelCount = Cv2.ContourArea(c);
                if ((double)pixelCount / (_width * _height) * 100.0d >= _changeLimit)
                {
                    result = true;

                    break;
                }

                // draw metainfo on the frame
                // only possible for RGB image
                /*Cv2.DrawContours(ProcessedFrame, contours, n, new Scalar(0, 255, 0));
                Cv2.Rectangle(ProcessedFrame, r, new Scalar(0, 0, 255));
                n++; */
            }

            _nextFrameProcessTime = currentTime.AddMilliseconds(_detectorDelayMs);
        }
        else
        {
            _backgroundFrame = new Mat(_width, _height, MatType.CV_32FC1);
            frame.Resize(new Size(_width, _height), interpolation: InterpolationFlags.Nearest)
                .CvtColor(ColorConversionCodes.BGR2GRAY)
                .GaussianBlur(new Size(21, 21), 0)
                .EqualizeHist()
                .AssignTo(_backgroundFrame, MatType.CV_32FC1);
        }

        return result;
    }

    private static int CountPixels(Mat image, Rect r)
    {
        var region = image.Clone(r);
        var count = region?.CountNonZero();
        region?.Dispose();

        return count ?? 0;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
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
