using CameraLib;
using CameraLib.IP;
using CameraLib.USB;

using System.Collections.Concurrent;
using System.Drawing;

namespace CameraServer
{
    public class CamerasCollection
    {
        private const string CustomCameraSection = "CustomCameras";
        private readonly IConfiguration _configuration;
        public IEnumerable<ICamera> Cameras => _cameras.Keys;

        private readonly Dictionary<ICamera, Dictionary<string, ConcurrentQueue<Bitmap>>> _cameras = new Dictionary<ICamera, Dictionary<string, ConcurrentQueue<Bitmap>>>();

        public CamerasCollection(IConfiguration configuration)
        {
            _configuration = configuration;
            var customCameras = _configuration.GetSection(CustomCameraSection).Get<List<CustomCamera>>() ?? new List<CustomCamera>();
            foreach (var c in customCameras)
            {
                _cameras.Add(new IpCamera(c.Url, c.Name), new Dictionary<string, ConcurrentQueue<Bitmap>>());
            }

            foreach (var c in UsbCamera.DiscoverUsbCameras())
            {
                _cameras.Add(new UsbCamera(c.Path), new Dictionary<string, ConcurrentQueue<Bitmap>>());
            }

            foreach (var c in IpCamera.DiscoverOnvifCamerasAsync(1000, CancellationToken.None).Result)
            {
                _cameras.Add(new IpCamera(c.Path), new Dictionary<string, ConcurrentQueue<Bitmap>>());
            }
        }

        public async Task RefreshCamerasCollection()
        {
            var customCameras = _configuration.GetSection(CustomCameraSection).Get<List<CustomCamera>>() ?? new List<CustomCamera>();
            foreach (var c in customCameras)
            {
                _cameras.Add(new IpCamera(c.Url, c.Name), new Dictionary<string, ConcurrentQueue<Bitmap>>());
            }

            var usbCameras = UsbCamera.DiscoverUsbCameras();
            foreach (var c in usbCameras)
            {
                if (!_cameras.Any(n => n.Key.Path == c.Path))
                    _cameras.Add(new UsbCamera(c.Path), new Dictionary<string, ConcurrentQueue<Bitmap>>());
            }

            foreach (var c in _cameras.Where(n => n.Key is UsbCamera))
            {
                if (!usbCameras.Any(n => n.Path == c.Key.Path))
                    _cameras.Remove(c.Key);
            }

            var ipCameras = await IpCamera.DiscoverOnvifCamerasAsync(1000, CancellationToken.None);
            foreach (var c in ipCameras)
            {
                if (!_cameras.Any(n => n.Key.Path == c.Path))
                    _cameras.Add(new IpCamera(c.Path), new Dictionary<string, ConcurrentQueue<Bitmap>>());
            }

            foreach (var c in _cameras.Where(n => n.Key is IpCamera))
            {
                if (!ipCameras.Any(n => n.Path == c.Key.Path))
                    _cameras.Remove(c.Key);
            }
        }

        public bool HookCamera(string cameraId, string userId, ConcurrentQueue<Bitmap> srcImageQueue, int xResolution = 0, int yResolution = 0, string format = "")
        {
            if (!_cameras.Any(n => n.Key.Path == cameraId))
                return false;

            var camera = _cameras.FirstOrDefault(n => n.Key.Path == cameraId);
            if (!camera.Value.TryAdd(cameraId + userId, srcImageQueue))
                return false;

            if (camera.Value.Count == 1)
            {
                camera.Key.ImageCapturedEvent += GetImageFromCamera;
                camera.Key.Start(xResolution, yResolution, format, CancellationToken.None);
            }

            return true;
        }

        public bool UnHookCamera(string cameraId, string userId, ConcurrentQueue<Bitmap> srcImageQueue)
        {
            if (!_cameras.Any(n => n.Key.Path == cameraId))
                return false;

            var camera = _cameras.FirstOrDefault(n => n.Key.Path == cameraId);
            camera.Value.Remove(cameraId + userId);
            if (camera.Value.Count <= 0)
            {
                camera.Key.ImageCapturedEvent -= GetImageFromCamera;
                camera.Key.Stop(CancellationToken.None);
            }

            return true;
        }

        private void GetImageFromCamera(ICamera camera, Bitmap image)
        {
            if (_cameras.TryGetValue(camera, out var stream))
            {
                foreach (var s in stream.Select(n => n.Value))
                {
                    if (s.Count > 5)
                    {
                        while (s.TryDequeue(out var img))
                        {
                            img?.Dispose();
                        }
                    }

                    s.Enqueue((Bitmap)image.Clone());
                }
            }
        }
    }
}
