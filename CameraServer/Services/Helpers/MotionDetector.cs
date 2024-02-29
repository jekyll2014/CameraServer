using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace CameraServer.Services.Helpers
{
    public class MotionDetector : IDisposable
    {
        private readonly byte _frameSkipCount;
        private readonly byte _noiseThreshold;
        private readonly double _frameScale;
        private readonly double _changeLimit;

        private Image<Gray, byte>? _prevFrame;
        private byte _motionFrameCount = 0;
        private bool _disposedValue;

        public MotionDetector(double changeLimit = 0.001, double frameScale = 0.25, byte frameSkipCount = 10, byte noiseThreshold = 50)
        {
            _changeLimit = changeLimit;
            _frameScale = frameScale;
            _frameSkipCount = frameSkipCount;
            _noiseThreshold = noiseThreshold;
        }

        public bool DetectMovement(Mat frame)
        {
            var result = false;
            // movement detection
            if (_prevFrame != null && _motionFrameCount > _frameSkipCount)
            {
                // resize
                var currFrame = frame.ToImage<Gray, byte>().Resize(_frameScale, Inter.Nearest);

                // compare
                var imgAbsDiff = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                CvInvoke.AbsDiff(currFrame, _prevFrame, imgAbsDiff);

                // denoise
                var imgThreshold = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                CvInvoke.Threshold(imgAbsDiff, imgThreshold, _noiseThreshold, 255, ThresholdType.Binary);

                // count changed pixels
                var pixelCount = CvInvoke.CountNonZero(imgThreshold);
                var totalPixelCount = currFrame.Width * currFrame.Height;
                result = (double)pixelCount / (double)totalPixelCount > _changeLimit;

                //var imgThresholdDenoise = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                //CvInvoke.FastNlMeansDenoising(imgThreshold, imgThresholdDenoise);

                //var element = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(3, 3), new Point(-1, -1));
                //var imgEroded = new Image<Gray, byte>(_frame.Width, _frame.Height);
                //CvInvoke.Erode(imgThreshold, imgEroded, element, new Point(-1, -1), 2, BorderType.Constant, new MCvScalar(255, 255, 255));

                /*CvInvoke.PutText(imgThreshold, pixelCount.ToString(), new Point(200, 200), FontFace.HersheyPlain, 2,
                    new MCvScalar(128.0, 128.0, 128.0), 3);*/

                //_frame = imgAbsDiff.ToUMat().GetMat(AccessType.Fast);
                //frame = imgThreshold.ToUMat().GetMat(AccessType.Fast);
                //_frame = imgEroded.ToUMat().GetMat(AccessType.Fast);

                _prevFrame = currFrame.Clone();
                _motionFrameCount = 0;

                currFrame.Dispose();
                imgAbsDiff.Dispose();
                imgThreshold.Dispose();
                //imgEroded.Dispose();
            }
            else if (_prevFrame == null)
            {
                _prevFrame = frame.ToImage<Gray, byte>().Resize(_frameScale, Inter.Nearest);
            }

            _motionFrameCount++;

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
