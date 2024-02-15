﻿using CameraLib;
using CameraLib.IP;
using CameraLib.USB;

using CameraServer.Models;
using CameraServer.Settings;

using System.Collections.Concurrent;
using System.Drawing;

using CameraType = CameraServer.Settings.CameraType;

namespace CameraServer.Services.CameraHub
{
    public class CameraHubService
    {
        private const string CameraSettingsSection = "CameraSettings";
        private readonly CameraSettings _cameraSettings;
        private readonly int _maxBuffer;
        public IEnumerable<ServerCamera> Cameras => _cameras.Keys;

        private readonly Dictionary<ServerCamera, Dictionary<string, ConcurrentQueue<Bitmap>>> _cameras = [];

        public CameraHubService(IConfiguration configuration)
        {
            _cameraSettings = configuration.GetSection(CameraSettingsSection).Get<CameraSettings>() ?? new CameraSettings();
            _maxBuffer = _cameraSettings.MaxFrameBuffer;
            RefreshCameraCollection().Wait();
        }

        public async Task RefreshCameraCollection()
        {
            var cameras = _cameras.AsQueryable().ToArray();
            for (var i = 0; i < cameras.Length; i++)
            {
                if (cameras[i].Key.Custom)
                {
                    _cameras.Remove(cameras[i].Key);
                }
            }

            foreach (var c in _cameraSettings.CustomCameras)
            {
                ServerCamera serverCamera;
                if (c.Type == CameraType.IP)
                    serverCamera = new ServerCamera(new IpCamera(c.Path, c.Name), c.AllowedRoles, true);
                else if (c.Type == CameraType.USB)
                    serverCamera = new ServerCamera(new UsbCamera(c.Path, c.Name), c.AllowedRoles, true);
                else
                    continue;

                _cameras.Add(serverCamera, []);
            }

            if (_cameraSettings.AutoSearchUsb)
            {
                var usbCameras = UsbCamera.DiscoverUsbCameras();
                foreach (var c in usbCameras)
                    Console.WriteLine($"USB-Camera: {c.Name} - [{c.Path}]");

                foreach (var c in usbCameras
                             .Where(c => _cameras
                                 .All(n => n.Key.Camera.Path != c.Path)))
                {
                    var serverCamera = new ServerCamera(new UsbCamera(c.Path), _cameraSettings.DefaultAllowedRoles);
                    _cameras.Add(serverCamera, []);
                }

                foreach (var c in _cameras
                             .Where(n => n.Key.Camera is UsbCamera && !n.Key.Custom)
                             .Where(c => !usbCameras
                                 .Exists(n => n.Path == c.Key.Camera.Path)))
                {
                    _cameras.Remove(c.Key);
                }
            }

            if (_cameraSettings.AutoSearchIp)
            {
                var ipCameras = await IpCamera.DiscoverOnvifCamerasAsync(1000, CancellationToken.None);
                foreach (var c in ipCameras)
                    Console.WriteLine($"IP-Camera: {c.Name} - [{c.Path}]");

                foreach (var c in ipCameras
                             .Where(c => _cameras
                                 .All(n => n.Key.Camera.Path != c.Path)))
                {
                    var serverCamera = new ServerCamera(new IpCamera(c.Path), _cameraSettings.DefaultAllowedRoles);
                    _cameras.Add(serverCamera, []);
                }

                foreach (var c in _cameras
                             .Where(c => c.Key.Camera is IpCamera && !c.Key.Custom)
                             .Where(c => !ipCameras
                                 .Exists(n => n.Path == c.Key.Camera.Path)))
                {
                    _cameras.Remove(c.Key);
                }
            }
        }

        public CancellationToken HookCamera(string cameraId, string userId, ConcurrentQueue<Bitmap> srcImageQueue, int xResolution = 0, int yResolution = 0, string format = "")
        {
            if (!_cameras.Any(n => n.Key.Camera.Path == cameraId))
                return CancellationToken.None;

            var camera = _cameras.FirstOrDefault(n => n.Key.Camera.Path == cameraId);
            if (!camera.Value.TryAdd(cameraId + userId, srcImageQueue))
                return CancellationToken.None;

            if (camera.Value.Count == 1)
            {
                camera.Key.Camera.ImageCapturedEvent += GetImageFromCamera;
                camera.Key.Camera.Start(xResolution, yResolution, format, CancellationToken.None);
            }

            return camera.Key.Camera.CancellationToken;
        }

        public bool UnHookCamera(string cameraId, string userId)
        {
            if (!_cameras.Any(n => n.Key.Camera.Path == cameraId))
                return false;

            var camera = _cameras.FirstOrDefault(n => n.Key.Camera.Path == cameraId);
            camera.Value.Remove(cameraId + userId);
            if (camera.Value.Count <= 0)
            {
                camera.Key.Camera.ImageCapturedEvent -= GetImageFromCamera;
                camera.Key.Camera.Stop(CancellationToken.None);
            }

            return true;
        }

        private void GetImageFromCamera(ICamera camera, Bitmap image)
        {
            var clientStreams = _cameras.FirstOrDefault(n => n.Key.Camera == camera).Value;
            if (clientStreams != null)
            {
                foreach (var clientStream in clientStreams)
                {
                    if (clientStream.Value.Count > _maxBuffer)
                    {
                        clientStream.Value.Clear();
                        clientStreams.Remove(clientStream.Key);

                        if (clientStreams.Count <= 0)
                        {
                            camera.ImageCapturedEvent -= GetImageFromCamera;
                            camera.Stop(CancellationToken.None);
                        }
                    }

                    clientStream.Value.Enqueue((Bitmap)image.Clone());
                }
            }
        }
    }
}