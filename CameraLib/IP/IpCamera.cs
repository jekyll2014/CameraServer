using Emgu.CV;

using QuickNV.Onvif;
using QuickNV.Onvif.Discovery;
using QuickNV.Onvif.Media;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using IPAddress = System.Net.IPAddress;

namespace CameraLib.IP
{
    public class IpCamera : ICamera, IDisposable
    {
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

        private static List<CameraDescription> _lastCamerasFound = new List<CameraDescription>();
        private readonly object _getPictureThreadLock = new object();
        private VideoCapture? _captureDevice; //create a usbCamera capture
        private string _ipCameraName;
        private Mat? _frame = new Mat();

        private bool _disposedValue;

        public IpCamera(string path, string name = "", AuthType authenicationType = AuthType.None, string login = "", string password = "", int discoveryTimeout = 1000, bool forceCameraConnect = false)
        {
            Path = path;

            if (authenicationType == AuthType.Plain)
                Path = string.Format(Path, login, password);

            _ipCameraName = string.IsNullOrEmpty(name)
                ? Dns.GetHostAddresses(new Uri(Path).Host).FirstOrDefault()?.ToString() ?? Path
                : name;

            if (_lastCamerasFound.Count == 0)
                _lastCamerasFound = DiscoverOnvifCamerasAsync(discoveryTimeout, CancellationToken.None).Result;

            var frameFormats = _lastCamerasFound.Find(n => n.Path == path)?.FrameFormats.ToList() ?? new List<FrameFormat>();

            if (frameFormats.Count == 0 || forceCameraConnect)
            {
                var cameraUri = new Uri(Path);
                if (PingAddress(cameraUri.Host).Result)
                {
                    var image = GrabFrame(CancellationToken.None).Result;
                    if (image != null)
                    {
                        frameFormats.Add(new FrameFormat(image.Width, image.Height));
                        image.Dispose();
                    }
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

            if (devices.Length == 0)
            {
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

                var mediaClient = new MediaClient(client);
                var profilesResponse = await mediaClient.GetProfilesAsync();

                foreach (var profile in profilesResponse.Profiles)
                {
                    var stream = await mediaClient.QuickOnvif_GetStreamUriAsync(profile.token, true);
                    result.Add(new CameraDescription(
                        CameraType.IP,
                        stream,
                        $"{client.DeviceInformation.Manufacturer} {client.DeviceInformation.Model} [{device.EndPointAddress}]",
                        new FrameFormat[]
                        {
                            new(profile.VideoEncoderConfiguration.Resolution.Width,
                                profile.VideoEncoderConfiguration.Resolution.Height,
                                profile.VideoEncoderConfiguration.Encoding.ToString())
                        }));
                }
            }

            _lastCamerasFound = result;

            return result;
        }

        public List<CameraDescription> DiscoverCamerasAsync(int discoveryTimeout, CancellationToken token)
        {
            return DiscoverOnvifCamerasAsync(discoveryTimeout, token).Result;
        }

        private async Task<bool> PingAddress(string host, int pingTimeout = 3000)
        {
            if (!IPAddress.TryParse(host, out var destIp))
                return false;

            PingReply pingResultTask;
            using (var ping = new Ping())
            {
                pingResultTask = await ping.SendPingAsync(destIp, pingTimeout).ConfigureAwait(true);
            }

            return pingResultTask.Status == IPStatus.Success;
        }

        public async Task<bool> Start(int width, int height, string format, CancellationToken token)
        {
            if (!IsRunning)
            {
                try
                {
                    _captureDevice = await GetCaptureDevice(token);

                }
                catch (Exception e)
                {
                    Console.WriteLine(e);

                    return false;
                }

                if (_captureDevice == null)
                {
                    return false;
                }

                _cancellationTokenSource = new CancellationTokenSource();
                _captureDevice.ImageGrabbed += ImageCaptured;
                _captureDevice.Start();

                IsRunning = true;
            }

            return true;
        }

        private void ImageCaptured(object? sender, EventArgs args)
        {
            if (Monitor.IsEntered(_getPictureThreadLock))
                return;

            try
            {
                lock (_getPictureThreadLock)
                {
                    _frame?.Dispose();
                    _frame = new Mat();
                    if (!(_captureDevice?.Grab() ?? false))
                        return;

                    if (!(_captureDevice?.Retrieve(_frame) ?? false))
                        return;

                    ImageCapturedEvent?.Invoke(this, _frame.Clone());
                    //_frame?.Dispose();
                    //GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
                }
            }
            catch
            {
                Stop();
            }
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            lock (_getPictureThreadLock)
            {
                _cancellationTokenSource?.Cancel();
                _captureDevice?.Stop();
                _captureDevice?.Dispose();
                _captureDevice = null;
                _frame?.Dispose();
                IsRunning = false;
            }
        }

        public async Task Stop(CancellationToken token)
        {
            Stop();
        }

        public async Task<Mat?> GrabFrame(CancellationToken token)
        {
            if (IsRunning)
            {
                while (IsRunning && _frame == null && !token.IsCancellationRequested)
                    await Task.Delay(10, token);

                lock (_getPictureThreadLock)
                {
                    return _frame?.Clone();
                }
            }

            var image = new Mat();
            await Task.Run(async () =>
            {
                _captureDevice = await GetCaptureDevice(token);
                if (_captureDevice == null)
                    return;

                try
                {
                    if (_captureDevice.Grab())
                        _captureDevice.Retrieve(image);
                }
                catch { }

                _captureDevice.Stop();
                _captureDevice.Dispose();
            }, token);

            return image;
        }

        public async IAsyncEnumerable<Mat> GrabFrames([EnumeratorCancellation] CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var image = await GrabFrame(token);
                if (image == null)
                {
                    await Task.Delay(100, token);
                }
                else
                {
                    yield return image.Clone();
                    image.Dispose();
                }
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
                    _cancellationTokenSource?.Dispose();
                    _frame?.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
