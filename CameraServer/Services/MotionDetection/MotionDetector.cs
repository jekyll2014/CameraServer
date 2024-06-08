using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

using System.Drawing;

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

        private Image<Gray, byte>? _prevFrame;
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
                var currFrame = frame.ToImage<Gray, byte>().Resize(_width, _height, Inter.Nearest);

                // compare
                var imgAbsDiff = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                CvInvoke.AbsDiff(currFrame, _prevFrame, imgAbsDiff);
#if DEBUG
                //File.WriteAllBytes("diff.jpg", imgAbsDiff.ToJpegData());
#endif

                // filter out the noise
                var imgThreshold = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                CvInvoke.Threshold(imgAbsDiff, imgThreshold, _noiseThreshold, 255, ThresholdType.Binary);

                // Find contours around the blobs
                var contours = new Emgu.CV.Util.VectorOfVectorOfPoint();
                CvInvoke.FindContours(imgThreshold, contours, null, RetrType.External, ChainApproxMethod.ChainApproxTc89L1);
#if DEBUG
                var colorFrame = imgThreshold.Convert<Rgb, byte>();
#endif
                //Find big blobs to activate alarm
                var n = 0;
                foreach (var c in contours?.ToArrayOfArray())
                {
                    var r = CvInvoke.BoundingRectangle(c);
                    var pixelCount = CountPixels(imgThreshold, r);
                    if (pixelCount >= _changeLimit)
                    {
#if DEBUG
                        CvInvoke.DrawContours(colorFrame, contours, n, new MCvScalar(0, 255, 0));
                        CvInvoke.Rectangle(colorFrame, r, new MCvScalar(0, 255, 0));
#endif
                        result = true;
                        break;
                    }
#if DEBUG
                    else
                    {
                        CvInvoke.Rectangle(colorFrame, r, new MCvScalar(255, 0, 0));
                    }

                    for (var i = 1; i < c.Length; i++)
                    {
                        CvInvoke.Line(colorFrame, new Point(c[i - 1].X, c[i - 1].Y), new Point(c[i].X, c[i].Y), new MCvScalar(0, 0, 255));
                    }
#endif
                    n++;
                }

                contours.Dispose();
#if DEBUG
                File.WriteAllBytes("threshold_cnt.jpg", colorFrame.ToJpegData());
                colorFrame.Dispose();
#endif
                _prevFrame.Dispose();
                _prevFrame = currFrame;
                _nextFrameProcessTime = currentTime.AddMilliseconds(_detectorDelayMs);

                imgAbsDiff.Dispose();
                imgThreshold.Dispose();
            }
            else _prevFrame ??= frame.ToImage<Gray, byte>().Resize(_width, _height, Inter.Nearest);

            return result;
        }

        private static int CountPixels(Image<Gray, byte> image, Rectangle r)
        {
            image.ROI = r;
            var count = image.CountNonzero()[0];
            image.ROI = Rectangle.Empty;

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
