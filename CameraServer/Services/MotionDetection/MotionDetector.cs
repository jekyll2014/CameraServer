using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace CameraServer.Services.MotionDetection
{
    public class MotionDetector : IDisposable
    {
        private const int DetectorRestartMs = 10000;

        private readonly uint _detectorDelayMs;
        private readonly byte _noiseThreshold;
        private readonly int _width;
        private readonly int _height;
        private readonly double _changeLimit;

        private Image<Gray, byte>? _prevFrame;
        private DateTime _nextFrameProcess = DateTime.Now;

        private bool _disposedValue;

        public MotionDetector(MotionDetectorParameters parameters)
        {
            _changeLimit = parameters.ChangeLimit;
            _width = parameters.Width;
            _height = parameters.Height;
            _noiseThreshold = parameters.NoiseThreshold;
            _detectorDelayMs = parameters.DetectorDelayMs;
        }

        public bool DetectMovement(Mat frame)
        {
            var result = false;
            var currentTime = DateTime.Now;
            if (_nextFrameProcess < currentTime.AddMilliseconds(-DetectorRestartMs - _detectorDelayMs))
                _nextFrameProcess = currentTime.AddMilliseconds(_detectorDelayMs);
            // movement detection
            if (_prevFrame != null && currentTime >= _nextFrameProcess)
            {
                // resize
                var currFrame = frame.ToImage<Gray, byte>().Resize(_width, _height, Inter.Nearest);

                // compare
                var imgAbsDiff = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                CvInvoke.AbsDiff(currFrame, _prevFrame, imgAbsDiff);
                //File.WriteAllBytes("diff.jpg", imgAbsDiff.ToJpegData());

                // filter out slight variations of color
                var imgThreshold = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                CvInvoke.Threshold(imgAbsDiff, imgThreshold, _noiseThreshold, 255, ThresholdType.Binary);
                //File.WriteAllBytes("threshold.jpg", imgThreshold.ToJpegData());

                // denoise
                //var imgThresholdDenoise = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                //CvInvoke.FastNlMeansDenoising(imgAbsDiff, imgThresholdDenoise);

                /*var element = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(2, 2), new Point(-1, -1));
                var imgEroded = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                CvInvoke.Erode(imgThreshold, imgEroded, element, new Point(-1, -1), 2, BorderType.Constant, new MCvScalar(255, 255, 255));
                File.WriteAllBytes("eroded.jpg", imgEroded.ToJpegData());*/

                // count changed pixels
                var pixelCount = CvInvoke.CountNonZero(imgThreshold);
                var totalPixelCount = currFrame.Width * currFrame.Height;
                result = pixelCount / (double)totalPixelCount > _changeLimit;

                //var imgThresholdDenoise = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                //CvInvoke.FastNlMeansDenoising(imgThreshold, imgThresholdDenoise);

                /*CvInvoke.PutText(imgThreshold, pixelCount.ToString(), new Point(200, 200), FontFace.HersheyPlain, 2,
                    new MCvScalar(128.0, 128.0, 128.0), 3);*/

                //_frame = imgAbsDiff.ToUMat().GetMat(AccessType.Fast);
                //frame = imgThreshold.ToUMat().GetMat(AccessType.Fast);
                //_frame = imgEroded.ToUMat().GetMat(AccessType.Fast);

                _prevFrame.Dispose();
                _prevFrame = currFrame;
                _nextFrameProcess = currentTime.AddMilliseconds(_detectorDelayMs);

                imgAbsDiff.Dispose();
                imgThreshold.Dispose();
                //imgEroded.Dispose();
            }
            else if (_prevFrame == null)
            {
                _prevFrame = frame.ToImage<Gray, byte>().Resize(_width, _height, Inter.Nearest);
            }

            return result;
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
