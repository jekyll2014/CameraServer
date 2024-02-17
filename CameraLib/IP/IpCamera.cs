﻿using Emgu.CV;

using QuickNV.Onvif;
using QuickNV.Onvif.Discovery;
using QuickNV.Onvif.Media;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CameraLib.IP
{
    public class IpCamera : ICamera, IDisposable
    {
        private readonly int _discoveryTimeout;
        public string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(_ipCameraName))
                    return _ipCameraName;

                return Path;
            }
            set
            {
                _ipCameraName = value;
                Description.Name = value;
            }
        }
        public string Path { get; }
        public CameraDescription Description { get; set; }
        public bool IsRunning { get; private set; } = false;

        public event ICamera.ImageCapturedEventHandler? ImageCapturedEvent;

        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;
        private CancellationTokenSource? _cancellationTokenSource;

        private static List<CameraDescription> _lastCamerasFound = [];
        private readonly object _getPictureThreadLock = new object();
        private VideoCapture? _captureDevice; //create a usbCamera capture
        private string _ipCameraName;
        private readonly Mat _frame = new Mat();
        private Bitmap? _image;

        private bool _disposedValue;

        public IpCamera(string path, string name = "", int discoveryTimeout = 1000)
        {
            _discoveryTimeout = discoveryTimeout;
            Path = path;
            _ipCameraName = string.IsNullOrEmpty(name)
                ? Dns.GetHostAddresses(new Uri(Path).Host).FirstOrDefault()?.ToString() ?? Path
                : name;

            if (_lastCamerasFound.Count == 0)
                _lastCamerasFound = DiscoverOnvifCamerasAsync(_discoveryTimeout, CancellationToken.None).Result;

            var frameFormats = _lastCamerasFound.Find(n => n.Path == path)?.FrameFormats.ToList() ?? [];
            if (!frameFormats.Any())
            {
                var image = GrabFrame(CancellationToken.None).Result;
                if (image != null)
                {
                    frameFormats.Add(new FrameFormat(image.Width, image.Height, image.RawFormat.ToString()));
                    image.Dispose();
                }
            }
            Description = new CameraDescription(CameraType.IP, Path, _ipCameraName, frameFormats);
        }

        public static async Task<List<CameraDescription>> DiscoverOnvifCamerasAsync(int discoveryTimeout,
            CancellationToken token)
        {
            var result = new List<CameraDescription>();

            var discovery = new DiscoveryController2(TimeSpan.FromMilliseconds(discoveryTimeout));
            var devices = await discovery.RunDiscovery();

            if (!devices.Any())
            {
                Console.WriteLine("No cameras found");
                return result;
            }
            Console.WriteLine("Found {0} cameras", devices.Length);

            foreach (var device in devices)
            {
                var uri = new Uri(device.ServiceAddresses[0]);
                var client = new OnvifClient(new OnvifClientOptions
                {
                    Scheme = uri.Scheme,
                    Host = uri.Host,
                    Port = uri.Port
                });

                try
                {
                    await client.ConnectAsync();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Can not connect to camera: {uri}\r\n{e.Message}");

                    continue;
                }
                //Console.WriteLine("DeviceInformation: " + JsonConvert.SerializeObject(client.DeviceInformation, Formatting.Indented));

                var mediaClient = new MediaClient(client);
                var profilesResponse = await mediaClient.GetProfilesAsync();

                foreach (var profile in profilesResponse.Profiles)
                {
                    var stream = await mediaClient.QuickOnvif_GetStreamUriAsync(profile.token, true);
                    //var resolution = $"{profile.VideoEncoderConfiguration.Resolution.Width}x{profile.VideoEncoderConfiguration.Resolution.Height}";

                    result.Add(new CameraDescription(
                        CameraType.IP,
                        stream,
                        $"{client.DeviceInformation.Manufacturer} {client.DeviceInformation.Model} [{device.EndPointAddress}]",
                        new FrameFormat[]
                        {
                            new FrameFormat(profile.VideoEncoderConfiguration.Resolution.Width,
                                profile.VideoEncoderConfiguration.Resolution.Height,
                                profile.VideoEncoderConfiguration.Encoding.ToString())
                        }));

                    /*Console.WriteLine("Stream: {0}", new
                    {
                        profile.Name,
                        StreamUrl = stream,
                        profile.VideoEncoderConfiguration.Encoding,
                        resolution
                    });*/
                }
            }

            _lastCamerasFound = result;

            return result;
        }

        public List<CameraDescription> DiscoverCamerasAsync(int discoveryTimeout, CancellationToken token)
        {
            return DiscoverOnvifCamerasAsync(discoveryTimeout, token).Result;
        }

        public async Task<bool> Start(int x, int y, string format, CancellationToken token)
        {
            if (!IsRunning)
            {
                _captureDevice = await GetCaptureDevice(token);
                if (_captureDevice == null)
                {
                    IsRunning = false;

                    return false;
                }

                _cancellationTokenSource = new CancellationTokenSource();
                _captureDevice.ImageGrabbed += ImageCaptured;
                _captureDevice.Start();

                IsRunning = true;
            }

            return true;
        }

        private void ImageCaptured(object sender, EventArgs args)
        {
            if (Monitor.IsEntered(_getPictureThreadLock))
            {
                return;
            }

            lock (_getPictureThreadLock)
            {
                _image?.Dispose();
                try
                {
                    if (!(_captureDevice?.Grab() ?? false))
                        return;

                    if (!(_captureDevice?.Retrieve(_frame) ?? false))
                        return;

                    _image = _frame.ToBitmap();
                    if (_image != null)
                    {
                        ImageCapturedEvent?.Invoke(this, _image);
                    }
                }
                catch
                {
                    Stop();
                }
            }
        }

        public async Task Stop(CancellationToken token)
        {
            Stop();
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            _cancellationTokenSource?.Cancel();
            _captureDevice?.Stop();
            _captureDevice?.Dispose();
            _image?.Dispose();
            IsRunning = false;
        }

        public async Task<Bitmap?> GrabFrame(CancellationToken token)
        {
            if (IsRunning)
            {
                while (IsRunning && _image == null && !token.IsCancellationRequested) ;

                lock (_getPictureThreadLock)
                {
                    return (Bitmap?)_image?.Clone();
                }
            }

            Bitmap? image = null;
            await Task.Run(async () =>
            {
                _captureDevice = await GetCaptureDevice(token);
                if (_captureDevice == null)
                    return;

                if (_captureDevice.Grab() && _captureDevice.Retrieve(_frame))
                {
                    image = _frame.ToBitmap();
                }

                _captureDevice.Stop();
                _captureDevice.Dispose();
            }, token);

            return image;
        }

        public async IAsyncEnumerable<Bitmap> GrabFrames([EnumeratorCancellation] CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var image = await GrabFrame(token);
                if (image == null)
                    yield break;

                yield return image;
                image.Dispose();
            }
        }

        private async Task<VideoCapture?> GetCaptureDevice(CancellationToken token)
        {
            return await Task.Run(() => new VideoCapture(Path), token);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Stop();
                    _cancellationTokenSource.Dispose();
                    _frame.Dispose();
                    _image?.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            //GC.SuppressFinalize(this);
        }
    }
}
