using OpenCvSharp;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CameraLib
{
    public interface ICamera : IDisposable
    {
        CameraDescription Description { get; set; }
        bool IsRunning { get; }
        FrameFormat? CurrentFrameFormat { get; }
        double CurrentFps { get; }
        int FrameTimeout { get; set; }

        /// <summary>
        /// Event raised when a frame is captured. 
        /// CRITICAL: Subscribers MUST dispose the Mat object after use to prevent memory leaks.
        /// The Mat object ownership is transferred to the subscriber.
        /// </summary>
        delegate void ImageCapturedEventHandler(ICamera camera, Mat image);
        event ImageCapturedEventHandler? ImageCapturedEvent;

        CancellationToken CancellationToken { get; }

        public Task<bool> GetImageDataAsync(int discoveryTimeout = 5000);
        public List<CameraDescription> DiscoverCameras(int discoveryTimeout);
        public Task<bool> StartAsync(int width, int height, string format, CancellationToken token);
        void Stop();
        public Task<Mat?> GrabFrameAsync(CancellationToken token, int width = 0, int height = 0, string format = "");
        IAsyncEnumerable<Mat> GrabFrames(CancellationToken token);
        FrameFormat GetNearestFormat(int width, int height, string format);
    }
}