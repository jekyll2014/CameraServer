using CameraLib;
using CameraLib.FlashCap;
using CameraLib.IP;
using CameraLib.MJPEG;
using CameraLib.USB;

using CameraServer.Auth;
using CameraServer.Models;

using Emgu.CV;

using System.Collections.Concurrent;

namespace CameraServer.Services.CameraHub
{
    public class CameraHubService
    {
        private const string CameraSettingsSection = "CameraSettings";
        private readonly CameraSettings _cameraSettings;
        private readonly int _maxBuffer;
        public IEnumerable<ServerCamera> Cameras => _cameras.Keys;

        private readonly ConcurrentDictionary<ServerCamera, ConcurrentDictionary<string, ConcurrentQueue<Mat>>> _cameras = new();

        public CameraHubService(IConfiguration configuration)
        {
            _cameraSettings = configuration.GetSection(CameraSettingsSection).Get<CameraSettings>() ?? new CameraSettings();
            _maxBuffer = _cameraSettings.MaxFrameBuffer;
            RefreshCameraCollection(CancellationToken.None).Wait();
        }

        public async Task RefreshCameraCollection(CancellationToken cancellationToken)
        {
            // remove predefined cameras from collection
            Console.WriteLine("Adding predefined cameras...");
            var cameras = _cameras.AsQueryable().ToArray();
            for (var i = 0; i < cameras.Length; i++)
            {
                if (cameras[i].Key.Custom)
                {
                    _cameras.TryRemove(cameras[i].Key, out _);
                }
            }

            List<CameraDescription> ipCameras = new();
            if (_cameraSettings.AutoSearchIp)
                ipCameras = await IpCamera.DiscoverOnvifCamerasAsync(_cameraSettings.DiscoveryTimeOut, cancellationToken);

            // add custom cameras again
            foreach (var c in _cameraSettings.CustomCameras)
            {
                ServerCamera serverCamera;
                if (c.Type == CameraType.IP)
                    serverCamera = new ServerCamera(new IpCamera(
                            path: c.Path,
                            name: c.Name,
                            authenicationType: c.AuthenicationType,
                            login: c.Login,
                            password: c.Password,
                            discoveryTimeout: _cameraSettings.DiscoveryTimeOut,
                            forceCameraConnect: _cameraSettings.ForceCameraConnect),
                        c.AllowedRoles, true);
                else if (c.Type == CameraType.MJPEG)
                    serverCamera = new ServerCamera(new MjpegCamera(
                            path: c.Path,
                            name: c.Name,
                            authenicationType: c.AuthenicationType,
                            login: c.Login,
                            password: c.Password,
                            discoveryTimeout: _cameraSettings.DiscoveryTimeOut,
                            forceCameraConnect: _cameraSettings.ForceCameraConnect),
                        c.AllowedRoles, true);
                else if (c.Type == CameraType.USB)
                {
                    try
                    {
                        serverCamera = new ServerCamera(new UsbCamera(c.Path, c.Name), c.AllowedRoles, true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        continue;
                    }
                }
                else if (c.Type == CameraType.USB_FC)
                {
                    try
                    {
                        serverCamera = new ServerCamera(new UsbCameraFc(c.Path, c.Name), c.AllowedRoles, true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        continue;
                    }
                }
                else
                    continue;

                _cameras.TryAdd(serverCamera, new());
            }

            if (_cameraSettings.AutoSearchUsb)
            {
                Console.WriteLine("Autodetecting USB cameras...");
                var usbCameras = UsbCamera.DiscoverUsbCameras();
                foreach (var c in usbCameras)
                    Console.WriteLine($"USB-Camera: {c.Name} - [{c.Path}]");

                // add newly discovered cameras
                foreach (var c in usbCameras
                             .Where(c => _cameras
                                 .All(n => n.Key.Camera.Description.Path != c.Path)))
                {
                    var serverCamera = new ServerCamera(new UsbCamera(c.Path), _cameraSettings.DefaultAllowedRoles);
                    _cameras.TryAdd(serverCamera, new());
                }

                // remove cameras not found by search (to not lose connection if any clients are connected)
                foreach (var c in _cameras
                             .Where(n => n.Key.Camera is UsbCamera && !n.Key.Custom)
                             .Where(c => !usbCameras
                                 .Exists(n => n.Path == c.Key.Camera.Description.Path)))
                {
                    _cameras.TryRemove(c.Key, out _);
                }
            }

            if (_cameraSettings.AutoSearchUsbFC)
            {
                Console.WriteLine("Autodetecting USB_FC cameras...");
                var usbFcCameras = UsbCameraFc.DiscoverUsbCameras();
                foreach (var c in usbFcCameras)
                    Console.WriteLine($"USB_FC-Camera: {c.Name} - [{c.Path}]");

                // add newly discovered cameras
                foreach (var c in usbFcCameras
                             .Where(c => _cameras
                                 .All(n => n.Key.Camera.Description.Path != c.Path)))
                {
                    var serverCamera = new ServerCamera(new UsbCameraFc(c.Path), _cameraSettings.DefaultAllowedRoles);
                    _cameras.TryAdd(serverCamera, new());
                }

                // remove cameras not found by search (to not lose connection if any clients are connected)
                foreach (var c in _cameras
                             .Where(n => n.Key.Camera is UsbCameraFc && !n.Key.Custom)
                             .Where(c => !usbFcCameras
                                 .Exists(n => n.Path == c.Key.Camera.Description.Path)))
                {
                    _cameras.TryRemove(c.Key, out _);
                }
            }

            if (_cameraSettings.AutoSearchIp)
            {
                Console.WriteLine("Autodetecting IP cameras...");
                foreach (var c in ipCameras)
                    Console.WriteLine($"IP-Camera: {c.Name} - [{c.Path}]");

                // add newly discovered cameras
                foreach (var c in ipCameras
                             .Where(c => _cameras
                                 .All(n => n.Key.Camera.Description.Path != c.Path)))
                {
                    var serverCamera = new ServerCamera(new IpCamera(c.Path), _cameraSettings.DefaultAllowedRoles);
                    _cameras.TryAdd(serverCamera, new());
                }

                // remove cameras not found by search (to not lose connection if any clients are connected)
                foreach (var c in _cameras
                             .Where(c => c.Key.Camera is IpCamera && !c.Key.Custom)
                             .Where(c => !ipCameras
                                 .Exists(n => n.Path == c.Key.Camera.Description.Path)))
                {
                    _cameras.TryRemove(c.Key, out _);
                }
            }

            Console.WriteLine("Done.");
        }

        public async Task<CancellationToken> HookCamera(
            string cameraId,
            string queueId,
            ConcurrentQueue<Mat> srcImageQueue,
            FrameFormatDto frameFormat)
        {
            if (_cameras.All(n => n.Key.Camera.Description.Path != cameraId))
                return CancellationToken.None;

            var camera = _cameras
                .FirstOrDefault(n => n.Key.Camera.Description.Path == cameraId);

            if (!camera.Value.TryAdd(GenerateImageQueueId(cameraId, queueId, frameFormat.Width, frameFormat.Height), srcImageQueue))
                return CancellationToken.None;

            if (camera.Value.Count == 1)
            {
                camera.Key.Camera.ImageCapturedEvent += GetImageFromCamera;
                if (!await camera.Key.Camera.Start(0, 0, frameFormat.Format, CancellationToken.None))
                    return CancellationToken.None;
            }

            return camera.Key.Camera.CancellationToken;
        }

        public async Task<bool> UnHookCamera(string cameraId, string queueId, FrameFormatDto frameFormat)
        {
            if (_cameras.All(n => n.Key.Camera.Description.Path != cameraId))
                return false;

            var camera = _cameras.FirstOrDefault(n => n.Key.Camera.Description.Path == cameraId);

            camera.Value.TryRemove(GenerateImageQueueId(cameraId, queueId, frameFormat.Width, frameFormat.Height), out _);
            if (camera.Value.Count <= 0)
            {
                camera.Key.Camera.ImageCapturedEvent -= GetImageFromCamera;
                camera.Key.Camera.Stop();
            }

            return true;
        }

        public ServerCamera GetCamera(int cameraNumber, ICameraUser currentUser)
        {
            if (cameraNumber < 0 || cameraNumber >= Cameras.Count())
                throw new ArgumentOutOfRangeException($"No camera available: \"{cameraNumber}\"");

            var camera = Cameras.ToArray()[cameraNumber];
            if (!camera.AllowedRoles.Intersect(currentUser.Roles).Any())
                throw new ArgumentOutOfRangeException($"No camera available: \"{cameraNumber}\"");

            return camera;
        }

        public ServerCamera GetCamera(string cameraId, ICameraUser currentUser)
        {
            var camera = Cameras.FirstOrDefault(n => n.Camera.Description.Path == cameraId);
            if (camera == null || !camera.AllowedRoles.Intersect(currentUser.Roles).Any())
                throw new ArgumentOutOfRangeException($"No camera available: \"{cameraId}\"");

            return camera;
        }

        private void GetImageFromCamera(ICamera camera, Mat image)
        {
            var clientStreams = _cameras.FirstOrDefault(n => n.Key.Camera == camera).Value;
            if (clientStreams != null)
            {
                foreach (var clientStream in clientStreams)
                {
                    if (clientStream.Value.Count > _maxBuffer)
                    {
                        clientStream.Value.TryDequeue(out var frame);
                        frame?.Dispose();
                    }

                    /*if (clientStream.Value.Count > _maxBuffer)
                    {
                        foreach (var frame in clientStream.Value)
                            frame.Dispose();

                        clientStream.Value.Clear();

                        // stop streaming if consumer can't cosume fast enough
                        //clientStreams.TryRemove(clientStream.Key);
                        //if (clientStreams.Count <= 0)
                        //{
                        //    camera.ImageCapturedEvent -= GetImageFromCamera;
                        //    camera.Stop(CancellationToken.None);
                        //}
                    }*/

                    clientStream.Value.Enqueue(image.Clone());
                }
            }

            image.Dispose();
        }

        public static string GenerateImageQueueId(string cameraId, string queueId, int width, int height)
        {
            return cameraId + queueId + width + height;
        }
    }
}
