using System.Collections.Generic;
using System.Drawing;
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

        public delegate void ImageCapturedEventHandler(ICamera camera, Bitmap image);
        public event ImageCapturedEventHandler? ImageCapturedEvent;
        public CancellationToken CancellationToken { get; }

        public List<CameraDescription> DiscoverCamerasAsync(int discoveryTimeout, CancellationToken token);
        public Task<bool> Start(int x, int y, string format, CancellationToken token);
        public void Stop(CancellationToken token);
        public Task<Image?> GrabFrame(CancellationToken token);
        public IAsyncEnumerable<Image> GrabFrames(CancellationToken token);
    }
}