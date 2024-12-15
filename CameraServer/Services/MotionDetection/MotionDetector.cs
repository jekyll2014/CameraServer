using OpenCvSharp;

using Size = OpenCvSharp.Size;

namespace CameraServer.Services.MotionDetection
{
    public class MotionDetector : IDisposable
    {
        private const int DETECTOR_RESTART_MS = 10000;

        private readonly uint _detectorDelayMs;
        private readonly byte _noiseThreshold;
        private readonly int _width;
        private readonly int _height;
        private readonly double _changeLimit;

        private Mat? _prevFrame;
        public Mat ProcessedFrame = new();

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
            if (_prevFrame != null && currentTime >= _nextFrameProcessTime)
            {
                // resize
                var currFrame = frame.Resize(new Size(_width, _height), interpolation: InterpolationFlags.Nearest);

                // compare
                var imgAbsDiff = new Mat();
                Cv2.Absdiff(currFrame, _prevFrame, imgAbsDiff);

                // filter out the noise
                var imgThreshold = new Mat();
                Cv2.Threshold(imgAbsDiff, imgThreshold, _noiseThreshold, 255, ThresholdTypes.Binary);
                imgAbsDiff.Dispose();

                // Find contours around the blobs
                Cv2.CvtColor(imgThreshold, ProcessedFrame, ColorConversionCodes.BGR2GRAY);
                imgThreshold.Dispose();
                Cv2.FindContours(ProcessedFrame, out var contours, out _, RetrievalModes.External,
                    ContourApproximationModes.ApproxTC89L1);

                //Find big blobs to activate alarm
                foreach (var c in contours)
                {
                    var r = Cv2.BoundingRect(c);
                    //var r2 = Cv2.MinAreaRect(c);
                    var pixelCount = CountPixels(ProcessedFrame, r);
                    //var pixelCount = Cv2.ContourArea(c);
                    if (((double)pixelCount / (_width * _height)) * 100 >= _changeLimit)
                    {
                        result = true;
                        break;
                    }
                }

                _prevFrame.Dispose();
                _prevFrame = currFrame;

                _nextFrameProcessTime = currentTime.AddMilliseconds(_detectorDelayMs);
            }
            else
                _prevFrame = frame.Resize(new Size(_width, _height), interpolation: InterpolationFlags.Nearest);

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
                    _prevFrame?.Dispose();
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
