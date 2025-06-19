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

        delegate void ImageCapturedEventHandler(ICamera camera, Mat image);
        event ImageCapturedEventHandler? ImageCapturedEvent;

        CancellationToken CancellationToken { get; }

        Task<List<CameraDescription>> DiscoverCamerasAsync(int discoveryTimeout, CancellationToken token);
        Task<bool> Start(int width, int height, string format, CancellationToken token);
        void Stop();
        Task<Mat?> GrabFrame(CancellationToken token);
        IAsyncEnumerable<Mat> GrabFrames(CancellationToken token);
        FrameFormat GetNearestFormat(int width, int height, string format);
    }
}