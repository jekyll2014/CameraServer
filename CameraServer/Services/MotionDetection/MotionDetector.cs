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

        public bool DetectMovement(Mat? frame)
        {
            if (frame == null)
                return false;

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
#if DEBUG
                //File.WriteAllBytes("diff.jpg", imgAbsDiff.ToJpegData());
#endif

                // filter out the noise
                var imgThreshold = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                CvInvoke.Threshold(imgAbsDiff, imgThreshold, _noiseThreshold, 255, ThresholdType.Binary);

                var contours = new Emgu.CV.Util.VectorOfVectorOfPoint();
                CvInvoke.FindContours(imgThreshold, contours, null, RetrType.External, ChainApproxMethod.ChainApproxTc89L1);
#if DEBUG
                var colorFrame = imgThreshold.Convert<Rgb, byte>();
#endif
                foreach (var c in contours.ToArrayOfArray())
                {
                    var r = CvInvoke.BoundingRectangle(c);
                    //
                    if (r.Width * r.Height >= _changeLimit)
                    {
#if DEBUG
                        CvInvoke.Rectangle(colorFrame, r, new MCvScalar(0, 255, 0));
#endif
                        result = true;
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
                }

                contours.Dispose();
#if DEBUG
                File.WriteAllBytes("threshold.jpg", imgThreshold.ToJpegData());
                File.WriteAllBytes("threshold_cnt.jpg", colorFrame.ToJpegData());
#endif

                /*var at1 = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                CvInvoke.MedianBlur(imgAbsDiff, at1, 3);
                var at2 = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                CvInvoke.AdaptiveThreshold(at1, at2, 255, AdaptiveThresholdType.GaussianC, ThresholdType.Binary, 5, 3);

                CvInvoke.FindContours(at2, contours, null, RetrType.External, ChainApproxMethod.ChainApproxTc89L1);
                colorFrame = at2.Convert<Rgb, byte>();
                foreach (var c in contours.ToArrayOfArray())
                {
                    for (var i = 1; i < c.Length; i++)
                    {
                        CvInvoke.Line(colorFrame, new Point(c[i - 1].X, c[i - 1].Y), new Point(c[i].X, c[i].Y), new MCvScalar(0, 0, 255));
                    }

                    var r = CvInvoke.BoundingRectangle(c);
                    CvInvoke.Rectangle(colorFrame, r, new MCvScalar(0, 255, 0));
                }

                File.WriteAllBytes("adaptive_threshold.jpg", at2.ToJpegData());
                File.WriteAllBytes("adaptive_threshold_cnt.jpg", colorFrame.ToJpegData());
                at1.Dispose();* /
                at2.Dispose();*/
#if DEBUG
                colorFrame.Dispose();
#endif

                // denoise
                //var imgThresholdDenoise = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                //CvInvoke.FastNlMeansDenoising(imgAbsDiff, imgThresholdDenoise);

                /*var element = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(2, 2), new Point(-1, -1));
                var imgEroded = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                CvInvoke.Erode(imgThreshold, imgEroded, element, new Point(-1, -1), 2, BorderType.Constant, new MCvScalar(255, 255, 255));
                File.WriteAllBytes("eroded.jpg", imgEroded.ToJpegData());*/

                // count changed pixels
                //var pixelCount = CvInvoke.CountNonZero(imgThreshold);
                //result = pixelCount > _changeLimit;

                //var imgThresholdDenoise = new Image<Gray, byte>(currFrame.Width, currFrame.Height);
                //CvInvoke.FastNlMeansDenoising(imgThreshold, imgThresholdDenoise);

                /*CvInvoke.PutText(imgThreshold, pixelCount.ToString(), new Point(200, 200), FontFace.HersheyPlain, 2,
                    new MCvScalar(128.0, 128.0, 128.0), 3);*/

                //_frame = imgAbsDiff.ToUMat().GetMat(AccessType.Fast);
                //frame = imgThreshold.ToUMat().GetMat(AccessType.Fast);
                //_frame = imgEroded.ToUMat().GetMat(AccessType.Fast);
                //imgEroded.Dispose();

                _prevFrame.Dispose();
                _prevFrame = currFrame;
                _nextFrameProcess = currentTime.AddMilliseconds(_detectorDelayMs);

                imgAbsDiff.Dispose();
                imgThreshold.Dispose();
            }
            else _prevFrame ??= frame.ToImage<Gray, byte>().Resize(_width, _height, Inter.Nearest);

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
