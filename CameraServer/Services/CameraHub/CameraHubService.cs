using CameraLib;
using CameraLib.FlashCap;
using CameraLib.IP;
using CameraLib.MJPEG;
using CameraLib.USB;

using CameraServer.Auth;
using CameraServer.Models;

using System.Collections.Concurrent;
using OpenCvSharp;

namespace CameraServer.Services.CameraHub
{
    public class CameraHubService
    {
        private const string CameraSettingsSection = "CameraSettings";
        private readonly CameraSettings _cameraSettings;
        private readonly int _maxBuffer;
        public IEnumerable<ServerCamera> Cameras => _cameras.Keys;

        private readonly ConcurrentDictionary<ServerCamera, ConcurrentDictionary<CameraQueueItem, ConcurrentQueue<Mat>>> _cameras = new();

        public CameraHubService(IConfiguration configuration)
        {
            _cameraSettings = configuration.GetSection(CameraSettingsSection).Get<CameraSettings>() ?? new CameraSettings();
            _maxBuffer = _cameraSettings.MaxFrameBuffer;
            RefreshCameraCollection(CancellationToken.None).Wait();
        }

        public async Task RefreshCameraCollection(CancellationToken cancellationToken)
        {
            // remove predefined cameras from collection
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
            {
                Console.WriteLine("Detecting IP cameras...");
                ipCameras = await IpCamera.DiscoverOnvifCamerasAsync(_cameraSettings.DiscoveryTimeOut);
            }

            Console.WriteLine("Adding predefined cameras...");

            // add custom cameras again
            Parallel.ForEach(_cameraSettings.CustomCameras, (c) =>
            {
                Console.WriteLine($"\t{c.Name}");

                ServerCamera serverCamera;
                if (c.Type == CameraType.IP)
                {
                    serverCamera = new ServerCamera(
                        new IpCamera(
                            path: c.Path,
                            name: c.Name,
                            authenicationType: c.AuthenicationType,
                            login: c.Login,
                            password: c.Password,
                            discoveryTimeout: _cameraSettings.DiscoveryTimeOut,
                            forceCameraConnect: _cameraSettings.ForceCameraConnect),
                        c.AllowedRoles, 
                        true);
                }
                else if (c.Type == CameraType.MJPEG)
                {
                    serverCamera = new ServerCamera(
                        new MjpegCamera(
                            path: c.Path,
                            name: c.Name,
                            authenicationType: c.AuthenicationType,
                            login: c.Login,
                            password: c.Password,
                            discoveryTimeout: _cameraSettings.DiscoveryTimeOut,
                            forceCameraConnect: _cameraSettings.ForceCameraConnect),
                        c.AllowedRoles, 
                        true);
                }
                else if (c.Type == CameraType.USB)
                {
                    try
                    {
                        serverCamera = new ServerCamera(
                            new UsbCamera(c.Path, c.Name),
                            c.AllowedRoles, 
                            true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        return;
                    }
                }
                else if (c.Type == CameraType.USB_FC)
                {
                    try
                    {
                        serverCamera = new ServerCamera(
                            new UsbCameraFc(c.Path, c.Name),
                            c.AllowedRoles, 
                            true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        return;
                    }
                }
                else
                    return;

                serverCamera.CameraStream.FrameTimeout = _cameraSettings.FrameTimeout;
                _cameras.TryAdd(serverCamera,
                    new ConcurrentDictionary<CameraQueueItem, ConcurrentQueue<Mat>>());
            });

            if (_cameraSettings.AutoSearchUsb)
            {
                Console.WriteLine("Autodetecting USB cameras...");
                var usbCameras = UsbCamera.DiscoverUsbCameras();
                foreach (var c in usbCameras)
                    Console.WriteLine($"USB-CameraStream: {c.Name} - [{c.Path}]");

                // add newly discovered cameras
                foreach (var c in usbCameras
                             .Where(c => _cameras
                                 .All(n => n.Key.CameraStream.Description.Path != c.Path)))
                {
                    var serverCamera = new ServerCamera(new UsbCamera(c.Path), _cameraSettings.DefaultAllowedRoles);
                    serverCamera.CameraStream.FrameTimeout = _cameraSettings.FrameTimeout;
                    _cameras.TryAdd(serverCamera, new ConcurrentDictionary<CameraQueueItem, ConcurrentQueue<Mat>>());
                }

                // remove cameras not found by search (to not lose connection if any clients are connected)
                foreach (var c in _cameras
                             .Where(n => n.Key.CameraStream is UsbCamera && !n.Key.Custom)
                             .Where(c => !usbCameras
                                 .Exists(n => n.Path == c.Key.CameraStream.Description.Path)))
                {
                    _cameras.TryRemove(c.Key, out _);
                }
            }

            if (_cameraSettings.AutoSearchUsbFC)
            {
                Console.WriteLine("Autodetecting USB_FC cameras...");
                var usbFcCameras = UsbCameraFc.DiscoverUsbCameras();
                foreach (var c in usbFcCameras)
                    Console.WriteLine($"USB_FC-CameraStream: {c.Name} - [{c.Path}]");

                // add newly discovered cameras
                foreach (var c in usbFcCameras
                             .Where(c => _cameras
                                 .All(n => n.Key.CameraStream.Description.Path != c.Path)))
                {
                    var serverCamera = new ServerCamera(new UsbCameraFc(c.Path), _cameraSettings.DefaultAllowedRoles);
                    serverCamera.CameraStream.FrameTimeout = _cameraSettings.FrameTimeout;
                    _cameras.TryAdd(serverCamera, new ConcurrentDictionary<CameraQueueItem, ConcurrentQueue<Mat>>());
                }

                // remove cameras not found by search (to not lose connection if any clients are connected)
                foreach (var c in _cameras
                             .Where(n => n.Key.CameraStream is UsbCameraFc && !n.Key.Custom)
                             .Where(c => !usbFcCameras
                                 .Exists(n => n.Path == c.Key.CameraStream.Description.Path)))
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
                                 .All(n => n.Key.CameraStream.Description.Path != c.Path)))
                {
                    Console.WriteLine($"Adding IP-Camera: {c.Name} - [{c.Path}]");
                    var serverCamera = new ServerCamera(new IpCamera(c.Path), _cameraSettings.DefaultAllowedRoles);
                    serverCamera.CameraStream.FrameTimeout = _cameraSettings.FrameTimeout;
                    _cameras.TryAdd(serverCamera, new ConcurrentDictionary<CameraQueueItem, ConcurrentQueue<Mat>>());
                }

                // remove cameras not found by search (to not lose connection if any clients are connected)
                foreach (var c in _cameras
                             .Where(c => c.Key.CameraStream is IpCamera && !c.Key.Custom)
                             .Where(c => !ipCameras
                                 .Exists(n => n.Path == c.Key.CameraStream.Description.Path)))
                {
                    _cameras.TryRemove(c.Key, out _);
                }
            }

            Console.WriteLine("Done.");
        }

        public async Task<CancellationToken> HookCamera(
            CameraQueueItem cameraItem,
            ConcurrentQueue<Mat> srcImageQueue)
        {
            if (_cameras.All(n => n.Key.CameraStream.Description.Path != cameraItem.CameraId))
                return CancellationToken.None;

            var camera = _cameras
                .FirstOrDefault(n => n.Key.CameraStream.Description.Path == cameraItem.CameraId);

            if (camera.Key == null || !camera.Value.TryAdd(
                    cameraItem,
                    srcImageQueue))
                return CancellationToken.None;

            if (camera.Value.Count == 1)
            {
                camera.Key.CameraStream.ImageCapturedEvent += GetImageFromCameraStream;
                if (!await camera.Key.CameraStream.Start(cameraItem.FrameFormat.Width,
                        cameraItem.FrameFormat.Height,
                        cameraItem.FrameFormat.Format,
                        CancellationToken.None))
                    return CancellationToken.None;
            }

            return camera.Key.CameraStream.CancellationToken;
        }

        public bool UnHookCamera(CameraQueueItem cameraItem)
        {
            if (_cameras.All(n => n.Key.CameraStream.Description.Path != cameraItem.CameraId))
                return false;

            var camera = _cameras.FirstOrDefault(n => n.Key.CameraStream.Description.Path == cameraItem.CameraId);

            camera.Value.TryRemove(cameraItem, out _);
            if (camera.Key != null && camera.Value.Count <= 0)
            {
                camera.Key.CameraStream.ImageCapturedEvent -= GetImageFromCameraStream;
                camera.Key.CameraStream.Stop();
            }

            return true;
        }

        public ServerCamera GetCamera(int cameraNumber, ICameraUser currentUser)
        {
            if (cameraNumber < 0 || cameraNumber >= Cameras.Count())
                throw new ArgumentOutOfRangeException($"No cameraStream available: \"{cameraNumber}\"");

            var camera = Cameras.ToArray()[cameraNumber];
            if (!camera.AllowedRoles.Intersect(currentUser.Roles).Any())
                throw new ArgumentOutOfRangeException($"No cameraStream available: \"{cameraNumber}\"");

            return camera;
        }

        public ServerCamera GetCamera(string cameraId, ICameraUser currentUser)
        {
            var camera = Cameras.FirstOrDefault(n => n.CameraStream.Description.Path == cameraId);
            if (camera == null || !camera.AllowedRoles.Intersect(currentUser.Roles).Any())
                throw new ArgumentOutOfRangeException($"No cameraStream available: \"{cameraId}\"");

            return camera;
        }

        private void GetImageFromCameraStream(ICamera camera, Mat image)
        {
            var clientStreams = _cameras.FirstOrDefault(n => n.Key.CameraStream == camera).Value;
            if (clientStreams != null)
            {
                foreach (var clientStream in clientStreams)
                {
                    if (clientStream.Value.Count >= _maxBuffer)
                    {
                        while (clientStream.Value.TryDequeue(out var frame))
                            frame?.Dispose();

                        // stop streaming if consumer can't cosume fast enough
                        UnHookCamera(clientStream.Key);
                        break;
                        //clientStreams.TryRemove(clientStream);
                        //if (clientStreams.Count <= 0)
                        //{
                        //    cameraStream.ImageCapturedEvent -= GetImageFromCameraStream;
                        //    cameraStream.Stop(CancellationToken.None);
                        //}
                    }

                    clientStream.Value.Enqueue(image.Clone());
                }
            }

            image.Dispose();
        }
    }
}
