using Emgu.CV;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CameraLib
{
    public interface ICamera
    {
        public string Name { get; set; }
        public string Path { get; }
        public CameraDescription Description { get; set; }
        public bool IsRunning { get; }

        public delegate void ImageCapturedEventHandler(ICamera camera, Mat image);
        public event ImageCapturedEventHandler? ImageCapturedEvent;

        public CancellationToken CancellationToken { get; }

        public List<CameraDescription> DiscoverCamerasAsync(int discoveryTimeout, CancellationToken token);
        public Task<bool> Start(int width, int height, string format, CancellationToken token);
        public Task Stop(CancellationToken token);
        public Task<Mat?> GrabFrame(CancellationToken token);
        public IAsyncEnumerable<Mat> GrabFrames(CancellationToken token);
    }
}