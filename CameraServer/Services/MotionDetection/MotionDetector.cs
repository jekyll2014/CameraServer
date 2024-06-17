using OpenCvSharp;

using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace CameraServer.Services.MotionDetection
{
    public class MotionDetector : IDisposable
    {
        private const int DetectorRestartMs = 10000;

        private readonly uint _detectorDelayMs;
        private readonly byte _noiseThreshold;
        private readonly int _width;
        private readonly int _height;
        private readonly uint _changeLimit;

        private Mat? _prevFrame;
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
            if (_nextFrameProcessTime < currentTime.AddMilliseconds(-DetectorRestartMs - _detectorDelayMs))
                _nextFrameProcessTime = currentTime.AddMilliseconds(_detectorDelayMs);

            // movement detection
            if (_prevFrame != null && currentTime >= _nextFrameProcessTime)
            {
                // resize
                var currFrame = frame.Resize(new Size(_width, _height), interpolation: InterpolationFlags.Nearest);

                // compare
                var imgAbsDiff = new Mat();
                Cv2.Absdiff(currFrame, _prevFrame, imgAbsDiff);
#if DEBUG
                File.WriteAllBytes("diff.jpg", imgAbsDiff.ToBytes(".jpg"));
#endif

                // filter out the noise
                var imgThreshold = new Mat();
                Cv2.Threshold(imgAbsDiff, imgThreshold, _noiseThreshold, 255, ThresholdTypes.Binary);

                // Find contours around the blobs
                var imgThreshold2 = new Mat();
                Cv2.CvtColor(imgThreshold, imgThreshold2, ColorConversionCodes.BGR2GRAY); //COLOR_BGR2GRAY

                Cv2.FindContours(imgThreshold2, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxTC89L1);
#if DEBUG
                var colorFrame = imgThreshold.Clone();
#endif
                //Find big blobs to activate alarm
                var n = 0;
                foreach (var c in contours)
                {
                    var r = Cv2.BoundingRect(c);
                    var pixelCount = CountPixels(imgThreshold2, r);
                    if (pixelCount >= _changeLimit)
                    {
#if DEBUG
                        Cv2.DrawContours(colorFrame, contours, n, Scalar.Green);
                        Cv2.Rectangle(colorFrame, r, Scalar.Green);
#endif
                        result = true;
                        break;
                    }
#if DEBUG
                    else
                    {
                        Cv2.Rectangle(colorFrame, r, Scalar.Red);
                        Cv2.Rectangle(colorFrame, r, Scalar.Red);
                    }

                    for (var i = 1; i < c.Length; i++)
                    {
                        Cv2.Line(colorFrame, new Point(c[i - 1].X, c[i - 1].Y), new Point(c[i].X, c[i].Y), Scalar.Blue);
                    }
#endif
                    n++;
                }

#if DEBUG
                File.WriteAllBytes("threshold_cnt.jpg", colorFrame.ToBytes(".jpg"));
                colorFrame.Dispose();
#endif
                _prevFrame.Dispose();
                _prevFrame = currFrame;
                _nextFrameProcessTime = currentTime.AddMilliseconds(_detectorDelayMs);

                imgAbsDiff.Dispose();
                imgThreshold.Dispose();
                imgThreshold2.Dispose();
            }
            else _prevFrame ??= frame.Resize(new Size(_width, _height), interpolation: InterpolationFlags.Nearest);

            return result;
        }

        private static int CountPixels(Mat image, Rect r)
        {
            var count = image.Clone(r).CountNonZero();

            return count;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
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
