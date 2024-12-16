using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

using System.Drawing;

namespace CameraServer.Services.MotionDetection
{
    public class MotionDetector : IDisposable
    {
        public Image<Gray, byte>? ProcessedFrame = null;

        private const int DETECTOR_RESTART_MS = 10000;
        private readonly uint _detectorDelayMs;
        private readonly byte _noiseThreshold;
        private readonly int _width;
        private readonly int _height;
        private readonly double _changeLimit;
        private Image<Gray, float>? _backgroundFrame = null; //new Mat(,CV_32FC1);
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
                var img1 = frame.ToImage<Bgr, byte>().Resize(_width, _height, Inter.Nearest);
                var currFrame = new Image<Gray, byte>(_width, _height);
                CvInvoke.CvtColor(img1, currFrame, ColorConversion.Bgr2Gray);
                img1.Dispose();
                var img3 = new Image<Gray, byte>(_width, _height);
                CvInvoke.GaussianBlur(currFrame, img3, new Size(21, 21), 0);
                CvInvoke.EqualizeHist(img3, currFrame);
                img3.Dispose();

                // compare
                var backgroundFrameGray = new Image<Gray, byte>(_width, _height);
                CvInvoke.ConvertScaleAbs(_backgroundFrame, backgroundFrameGray, 1, 0);

                var imgAbsDiff = new Image<Gray, byte>(_width, _height);
                CvInvoke.AbsDiff(currFrame, backgroundFrameGray, imgAbsDiff);
#if DEBUG
                //File.WriteAllBytes("diff.jpg", imgAbsDiff.ToJpegData());
#endif
                // update background frame
                if (result)
                    CvInvoke.AccumulateWeighted(currFrame, _backgroundFrame, 0.1);
                else
                    CvInvoke.AccumulateWeighted(currFrame, _backgroundFrame, 0.5);
                currFrame.Dispose();

                // filter out the noise
                ProcessedFrame ??= new Image<Gray, byte>(_width, _height);
                CvInvoke.Threshold(imgAbsDiff, ProcessedFrame, _noiseThreshold, 255, ThresholdType.Binary);
                imgAbsDiff.Dispose();

                // Find contours around the blobs                
                var contours = new Emgu.CV.Util.VectorOfVectorOfPoint();
                CvInvoke.FindContours(ProcessedFrame, contours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);
#if DEBUG
                var colorFrame = ProcessedFrame.Convert<Rgb, byte>();
                var n = 0;
#endif
                //Find big blobs to activate alarm
                foreach (var contour in contours.ToArrayOfArray())
                {
                    var r = CvInvoke.BoundingRectangle(contour);
                    var pixelCount = CountPixels(ProcessedFrame, r);
                    if (((double)pixelCount / (_width * _height)) * 100 >= _changeLimit)
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

                    for (var i = 1; i < contour.Length; i++)
                    {
                        CvInvoke.Line(colorFrame,
                            new Point(contour[i - 1].X,
                            contour[i - 1].Y),
                            new Point(contour[i].X,
                            contour[i].Y),
                            new MCvScalar(0, 0, 255));
                    }

                    n++;
#endif
                }

                contours.Dispose();
#if DEBUG
                File.WriteAllBytes("threshold_cnt.jpg", colorFrame.ToJpegData());
                colorFrame.Dispose();
#endif
                _nextFrameProcessTime = currentTime.AddMilliseconds(_detectorDelayMs);
            }
            else
            {
                var img1 = frame.ToImage<Bgr, byte>().Resize(_width, _height, Inter.Nearest);
                var img2 = new Image<Gray, byte>(_width, _height);
                CvInvoke.CvtColor(img1, img2, ColorConversion.Bgr2Gray);
                img1.Dispose();
                var img3 = new Image<Gray, byte>(_width, _height);
                CvInvoke.GaussianBlur(img2, img3, new Size(21, 21), 0);
                CvInvoke.EqualizeHist(img3, img2);
                img3.Dispose();
                _backgroundFrame?.Dispose();
                _backgroundFrame = img2.Convert<Gray, float>();
                img2.Dispose();
            }

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
}
