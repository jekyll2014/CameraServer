using CameraLib;

using Microsoft.Extensions.Logging;

using OpenCvSharp;

using System.Collections.Concurrent;

namespace CameraServer.Server.Services.CameraHub
{
    /// <summary>
    /// Example service demonstrating Mat pooling integration.
    /// This can be used as a reference for updating existing services.
    /// </summary>
    public class CameraFrameProcessor : IDisposable
    {
        private readonly MatPoolManager _matPoolManager;
        private readonly ILogger<CameraFrameProcessor> _logger;
        private readonly ConcurrentQueue<Mat> _frameQueue = new();
        private bool _disposedValue;

        public CameraFrameProcessor(ILogger<CameraFrameProcessor> logger)
        {
            _logger = logger;
            _matPoolManager = new MatPoolManager(maxPoolSize: 50, logger);
        }

        /// <summary>
        /// Example: Process a frame using pooled Mats for intermediate operations.
        /// </summary>
        public Mat ProcessFrameWithPooling(Mat sourceFrame, int targetWidth, int targetHeight)
        {
            // Rent pooled Mats for intermediate operations
            using var resizedPooled = _matPoolManager.RentScoped(targetWidth, targetHeight);
            using var grayPooled = _matPoolManager.RentScoped(targetWidth, targetHeight, MatType.CV_8UC1);

            // Resize into pooled Mat
            Cv2.Resize(sourceFrame, resizedPooled.Mat, new Size(targetWidth, targetHeight));

            // Convert to grayscale into another pooled Mat
            Cv2.CvtColor(resizedPooled.Mat, grayPooled.Mat, ColorConversionCodes.BGR2GRAY);

            // Return a clone (caller owns this)
            return grayPooled.Mat.Clone();

            // Pooled Mats are automatically returned on disposal
        }

        /// <summary>
        /// Example: Process frame without pooling (old way) - for comparison.
        /// </summary>
        public Mat ProcessFrameWithoutPooling(Mat sourceFrame, int targetWidth, int targetHeight)
        {
            Mat? resized = null;
            Mat? gray = null;
            try
            {
                resized = new Mat();
                Cv2.Resize(sourceFrame, resized, new Size(targetWidth, targetHeight));

                gray = new Mat();
                Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);

                return gray.Clone();
            }
            finally
            {
                resized?.Dispose();
                gray?.Dispose();
            }
        }

        /// <summary>
        /// Example: Camera event handler using pooling for frame cloning.
        /// </summary>
        public void OnFrameCaptured(ICamera camera, Mat frame)
        {
            // Get pool for this frame size
            var pool = _matPoolManager.GetPool(frame.Width, frame.Height, frame.Type());

            // Rent a Mat and copy the frame into it
            var pooledFrame = pool.Rent();
            try
            {
                frame.CopyTo(pooledFrame);

                // Queue for processing (ownership transferred)
                _frameQueue.Enqueue(pooledFrame);
            }
            catch
            {
                // Return to pool on error
                pool.Return(pooledFrame);
                throw;
            }
            finally
            {
                // Always dispose the original frame (we don't own it)
                frame?.Dispose();
            }
        }

        /// <summary>
        /// Example: Process queued frames and return pooled Mats.
        /// </summary>
        public async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_frameQueue.TryDequeue(out var frame))
                {
                    try
                    {
                        // Process the frame...
                        await ProcessFrameAsync(frame, cancellationToken);
                    }
                    finally
                    {
                        // Return to pool instead of disposing
                        _matPoolManager.Return(frame);
                    }
                }
                else
                {
                    await Task.Delay(10, cancellationToken);
                }
            }

            // Cleanup remaining frames
            while (_frameQueue.TryDequeue(out var frame))
            {
                _matPoolManager.Return(frame);
            }
        }

        private async Task ProcessFrameAsync(Mat frame, CancellationToken cancellationToken)
        {
            // Simulate processing
            await Task.Delay(1, cancellationToken);
        }

        /// <summary>
        /// Example: Motion detection using pooled Mats.
        /// </summary>
        public bool DetectMotionWithPooling(Mat frame, Mat? backgroundFrame, int width, int height)
        {
            if (backgroundFrame == null)
                return false;

            // Use scoped pooled Mats for all intermediate operations
            using var resized = _matPoolManager.RentScoped(width, height);
            using var gray = _matPoolManager.RentScoped(width, height, MatType.CV_8UC1);
            using var bgGray = _matPoolManager.RentScoped(width, height, MatType.CV_8UC1);
            using var diff = _matPoolManager.RentScoped(width, height, MatType.CV_8UC1);
            using var thresh = _matPoolManager.RentScoped(width, height, MatType.CV_8UC1);

            // All operations use pooled Mats
            Cv2.Resize(frame, resized.Mat, new Size(width, height));
            Cv2.CvtColor(resized.Mat, gray.Mat, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(backgroundFrame, bgGray.Mat, ColorConversionCodes.BGR2GRAY);
            Cv2.Absdiff(gray.Mat, bgGray.Mat, diff.Mat);
            Cv2.Threshold(diff.Mat, thresh.Mat, 25, 255, ThresholdTypes.Binary);

            var nonZero = Cv2.CountNonZero(thresh.Mat);
            var totalPixels = width * height;
            var changePercent = (double)nonZero / totalPixels * 100.0;

            return changePercent > 5.0; // 5% change threshold

            // All pooled Mats automatically returned here
        }

        /// <summary>
        /// Get pooling statistics.
        /// </summary>
        public void LogPoolingStatistics()
        {
            _matPoolManager.LogStatistics();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // Log final statistics
                    LogPoolingStatistics();

                    // Cleanup queue
                    while (_frameQueue.TryDequeue(out var frame))
                    {
                        _matPoolManager.Return(frame);
                    }

                    // Dispose pool manager
                    _matPoolManager.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
