using Microsoft.Extensions.Logging;

using OpenCvSharp;

using QuickNV.Onvif;
using QuickNV.Onvif.Discovery;
using QuickNV.Onvif.Media;
using QuickNV.Onvif.PTZ;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

using IPAddress = System.Net.IPAddress;
using PTZSpeed = QuickNV.Onvif.PTZ.PTZSpeed;
using Vector1D = QuickNV.Onvif.PTZ.Vector1D;
using Vector2D = QuickNV.Onvif.PTZ.Vector2D;

namespace CameraLib.IP
{
    public class IpCamera : ICamera
    {
        public AuthType AuthenticationType { get; private set; }
        public string Login { get; private set; }
        public string Password { get; private set; }
        public CameraDescription Description { get; set; }
        public bool IsRunning { get; private set; } = false;
        public FrameFormat? CurrentFrameFormat { get; private set; }
        public double CurrentFps { get; private set; }
        public int FrameTimeout { get; set; } = 30000;
        public bool IsPtz => _onvifClient != null && _ptzClient != null && _ptzProfile != null;

        public event ICamera.ImageCapturedEventHandler? ImageCapturedEvent;
        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        private readonly ILogger? _logger;
        private CancellationTokenSource? _cancellationTokenSource;
        private CancellationTokenSource? _cancellationTokenSourceCameraGrabber;
        private static List<CameraDescription> _lastCamerasFound = [];
        private VideoCapture? _captureDevice;
        private Task? _captureTask;
        private readonly Stopwatch _fpsTimer = new();
        private byte _frameCount;
        private readonly System.Timers.Timer _keepAliveTimer = new();
        private int _width = 0;
        private int _height = 0;
        private string _format = string.Empty;
        private OnvifClient? _onvifClient = null;
        private PTZClient? _ptzClient = null;
        private Profile? _ptzProfile = null;
        private bool _disposedValue;

        public static async Task<List<CameraDescription>> DiscoverOnvifCamerasAsync(int discoveryTimeout)
        {
            var result = new List<CameraDescription>();
            var discovery = new DiscoveryController2(TimeSpan.FromMilliseconds(discoveryTimeout));
            var devices = await discovery.RunDiscovery();

            Console.WriteLine($"Found {devices.Length} cameras");

            if (devices.Length == 0)
            {
                return result;
            }

            foreach (var device in devices)
            {
                Console.WriteLine($"Detecting media size: {device.ServiceAddresses[0]}");
                var uri = new Uri(device.ServiceAddresses[0]);
                try
                {
                    var client = new OnvifClient(new OnvifClientOptions
                    {
                        Scheme = uri.Scheme,
                        Host = uri.Host,
                        Port = uri.Port
                    });

                    await client.ConnectAsync();

                    if (client.Capabilities?.PTZ != null)
                    {
                        var ptzClient = new PTZClient(client);
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
                            [
                                    new(profile.VideoEncoderConfiguration.Resolution.Width,
                                    profile.VideoEncoderConfiguration.Resolution.Height,
                                    profile.VideoEncoderConfiguration.Encoding.ToString(),
                                    profile.VideoEncoderConfiguration.RateControl.FrameRateLimit)
                            ])
                        {
                            ServiceAddress = device.ServiceAddresses[0]
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Can not connect to camera: {uri}\r\n{ex.Message}");
                }
            }

            _lastCamerasFound = result;

            return result;
        }

        private static async Task<bool> PingAddress(string host, int pingTimeout = 5000, int port = -1)
        {
            try
            {
                if (!IPAddress.TryParse(host, out var destIp))
                {
                    var h = await Dns.GetHostEntryAsync(host).ConfigureAwait(false);
                    if (h.AddressList.Length > 0)
                        host = h.AddressList[0].ToString();

                    if (!IPAddress.TryParse(host, out destIp))
                        return false;
                }

                PingReply pingResultTask;
                using (var ping = new Ping())
                {
                    pingResultTask = await ping.SendPingAsync(destIp, pingTimeout).ConfigureAwait(true);
                }

                return pingResultTask.Status == IPStatus.Success;
            }
            catch (PlatformNotSupportedException)
            {
                // Ping is not supported on this platform (e.g., Linux in Docker)
                // Try to establish tcp connection if port is specified
                if (port == -1)
                    return false;

                try
                {
                    using var tcpClient = new System.Net.Sockets.TcpClient();
                    using var connectTask = tcpClient.ConnectAsync(host, port);
                    using var timeoutTask = Task.Delay(pingTimeout);
                    using var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                    if (completedTask == timeoutTask)
                        return false; // Timeout
                }
                catch
                {
                    return false;
                }
            }
            catch (Exception)
            {
                // Any other exception, assume host is unreachable
                return false;
            }

            return true;
        }

        public IpCamera(string path,
            string name = "",
            AuthType authenticationType = AuthType.None,
            string login = "",
            string password = "",
            int discoveryTimeout = 5000,
            bool forceCameraConnect = false,
            ILogger? logger = null)
        {
            _logger = logger;
            AuthenticationType = authenticationType;
            Login = login;
            Password = password;

            if (AuthenticationType == AuthType.Plain)
                path = string.Format(path, Login, Password);

            name = string.IsNullOrEmpty(name)
                ? Dns.GetHostAddresses(new Uri(path).Host).FirstOrDefault()?.ToString() ?? path
                : name;

            var frameFormats = _lastCamerasFound.Find(n => n.Path == path)?.FrameFormats.ToList() ?? [];
            Description = new CameraDescription(CameraType.IP, path, name, frameFormats);
            if (frameFormats.Count == 0 || forceCameraConnect)
            {
                var cameraUri = new Uri(path);
                if (PingAddress(cameraUri.Host).Result)
                {
                    var image = GrabFrameAsync(CancellationToken.None).Result;
                    if (image != null)
                    {
                        frameFormats.Add(new FrameFormat(image.Width, image.Height));
                        image.Dispose();
                    }
                }
            }

            Description.FrameFormats = frameFormats;
            CurrentFps = Description.FrameFormats.FirstOrDefault()?.Fps ?? 10;
            try
            {
                if (!string.IsNullOrEmpty(Description.ServiceAddress))
                    GetPtzControllerAsync(Description.ServiceAddress);
                else
                    GetPtzControllerAsync(path, discoveryTimeout);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception getting PTZ for {path}: {ex}");
            }

            _keepAliveTimer.Elapsed += CheckCameraDisconnected;
        }

        public async Task GetPtzControllerAsync(string path, int discoveryTimeout)
        {
            var cameraUri = new Uri(path);
            if (!IPAddress.TryParse(cameraUri.Host, out var cameraIp))
            {
                var h = await Dns.GetHostEntryAsync(cameraUri.Host);
                string? host = null;
                if (h.AddressList.Length > 0)
                {
                    host = h.AddressList[0].ToString();
                    IPAddress.TryParse(host, out cameraIp);
                }
            }

            if (cameraIp == null)
                return;

            var discovery = new DiscoveryController2(TimeSpan.FromMilliseconds(discoveryTimeout));
            var devices = await discovery.RunDiscovery();
            if (devices.Length == 0)
                return;

            foreach (var device in devices)
            {
                var uri = new Uri(device.ServiceAddresses[0]);
                if (uri.Host != cameraUri.Host && uri.Host != cameraIp?.ToString())
                    continue;

                await GetPtzControllerAsync(device.ServiceAddresses[0]);
            }
        }

        public async Task GetPtzControllerAsync(string serviceAddress)
        {
            var uri = new Uri(serviceAddress);
            Console.WriteLine($"Detecting PTZ: {serviceAddress}");
            _onvifClient ??= new OnvifClient(new OnvifClientOptions
            {
                Scheme = uri.Scheme,
                Host = uri.Host,
                Port = uri.Port
            });

            try
            {
                await _onvifClient.ConnectAsync();
                if (_onvifClient.Capabilities.PTZ != null)
                {
                    _ptzClient = new PTZClient(_onvifClient);
                    var mediaClient = new MediaClient(_onvifClient);
                    var profilesResponse = await mediaClient.GetProfilesAsync();
                    _ptzProfile = profilesResponse.Profiles.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Can not connect to camera: {uri}\r\n{ex.Message}");
            }
        }

        public async Task<bool> GetImageDataAsync(int discoveryTimeout = 5000)
        {
            var cameraUri = new Uri(Description.Path);
            if (await PingAddress(cameraUri.Host, discoveryTimeout))
            {
                var image = await GrabFrameAsync(CancellationToken.None);
                if (image != null)
                {
                    Description.FrameFormats = new[] { new FrameFormat(image.Width, image.Height) };
                    image.Dispose();
                }
                else
                    return false;
            }
            else
                return false;

            return Description.FrameFormats.Any();
        }

        private async void CheckCameraDisconnected(object? sender, ElapsedEventArgs e)
        {
            try
            {
                if (_fpsTimer.ElapsedMilliseconds > FrameTimeout)
                {
                    _logger?.LogDebug($"Camera connection restarted ({_fpsTimer.ElapsedMilliseconds} timeout)");
                    Stop(false);
                    await StartAsync(_width, _height, _format, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in camera disconnect handler");
            }
        }

        public List<CameraDescription> DiscoverCameras(int discoveryTimeout)
        {
            return DiscoverOnvifCamerasAsync(discoveryTimeout).Result;
        }

        public async Task<bool> StartAsync(int width, int height, string format, CancellationToken token)
        {
            if (IsRunning)
                return true;

            try
            {
                _captureDevice?.Dispose();
                _captureDevice = await GetCaptureDevice(token);//.WaitAsync(TimeSpan.FromMilliseconds(30000), token);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex.Message);

                return false;
            }

            if (_captureDevice == null)
                return false;

            _width = width;
            _height = height;
            _format = format;
            CurrentFrameFormat = new FrameFormat(_width, _height, "MJPEG");

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationTokenSourceCameraGrabber?.Dispose();
            _cancellationTokenSourceCameraGrabber = new CancellationTokenSource();
            _captureDevice.SetExceptionMode(false);
            _fpsTimer.Reset();
            _frameCount = 0;
            _keepAliveTimer.Interval = FrameTimeout;
            _keepAliveTimer.Start();

            _captureTask?.Dispose();
            _captureTask = Task.Run(async () =>
            {
                try
                {
                    while (!_cancellationTokenSourceCameraGrabber.Token.IsCancellationRequested)
                    {
                        if (_captureDevice?.Grab() ?? false)
                            CaptureImage();
                        else
                            await Task.Delay(10, _cancellationTokenSourceCameraGrabber.Token);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Error getting image from camera: {ex.Message}");
                    Stop();
                }

                IsRunning = false;
            }, _cancellationTokenSourceCameraGrabber.Token);

            IsRunning = true;

            return true;
        }

        private async Task<VideoCapture?> GetCaptureDevice(CancellationToken token)
        {
            try
            {
                return await Task.Run(() => new VideoCapture(Description.Path), token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Can not connect to camera: {Description.Path}\r\n{ex}");
            }

            return null;
        }

        private void CaptureImage()
        {
            Mat? frame = null;
            try
            {
                frame = new Mat();

                if (!(_captureDevice?.Retrieve(frame) ?? false) || frame.Empty())
                {
                    frame?.Dispose();
                    return;
                }

                if (!Description.FrameFormats.Any()
                    || (Description.FrameFormats.Count() == 1
                        && Description.FrameFormats.First().Width == 0))
                    Description.FrameFormats = new[] { new FrameFormat(frame.Width, frame.Height) };

                ImageCapturedEvent?.Invoke(this, frame);

                if (!_fpsTimer.IsRunning)
                {
                    _fpsTimer.Start();
                    _frameCount = 0;
                }
                else
                {
                    _frameCount++;
                    if (_frameCount >= 100)
                    {
                        if (_fpsTimer.ElapsedMilliseconds > 0)
                            CurrentFps = (double)_frameCount / ((double)_fpsTimer.ElapsedMilliseconds / (double)1000);

                        _fpsTimer.Reset();
                        _frameCount = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(new EventId(0), ex, $"Error retrieving image from camera");
                frame?.Dispose();
            }
        }

        public void Stop()
        {
            Stop(true);
        }

        private void Stop(bool cancellation)
        {
            if (!IsRunning)
                return;

            IsRunning = false;
            _keepAliveTimer.Stop();

            if (cancellation)
                _cancellationTokenSourceCameraGrabber?.Cancel();
            _cancellationTokenSource?.Cancel();

            if (_captureTask != null)
            {
                try
                {
                    if (!_captureTask.IsCompleted)
                    {
                        _captureTask.Wait(TimeSpan.FromSeconds(5));
                    }
                }
                catch (AggregateException)
                {
                    // Task was cancelled, this is expected
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error waiting for capture task");
                }
            }

            if (_captureDevice != null)
            {
                try
                {
                    _captureDevice?.Release();
                    _captureDevice?.Dispose();
                    _ = ClosePtzClient();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error releasing camera");
                }
            }

            CurrentFrameFormat = null;
            _fpsTimer.Reset();
        }

        public async Task<Mat?> GrabFrameAsync(CancellationToken token, int width = 0, int height = 0, string format = "")
        {

            if (IsRunning)
            {
                return null;
            }

            var frame = new Mat();
            await Task.Run(async () =>
            {
                try
                {
                    _captureDevice = await GetCaptureDevice(token);
                    if (_captureDevice == null)
                        return;

                    if (_captureDevice.Grab())
                    {
                        if (_captureDevice.Retrieve(frame) && !frame.Empty())
                        {
                            CurrentFrameFormat ??= new FrameFormat(frame.Width, frame.Height);
                            if (!Description.FrameFormats.Any()
                                || (Description.FrameFormats.Count() == 1
                                    && Description.FrameFormats.First().Width == 0))
                                Description.FrameFormats = new[] { new FrameFormat(frame.Width, frame.Height) };
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex.Message);
                }

                _captureDevice?.Release();
                _captureDevice?.Dispose();
            }, token);

            return frame;
        }

        public async IAsyncEnumerable<Mat> GrabFrames([EnumeratorCancellation] CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var image = await GrabFrameAsync(token);
                if (image == null)
                    await Task.Delay(10, CancellationToken.None);
                else
                    yield return image;
            }
        }

        public FrameFormat GetNearestFormat(int width, int height, string format)
        {
            FrameFormat? selectedFormat;

            if (!Description.FrameFormats.Any())
                return new FrameFormat(0, 0);

            if (Description.FrameFormats.Count() == 1)
                return Description.FrameFormats.First();

            if (width > 0 && height > 0)
            {
                var mpix = width * height;
                selectedFormat = Description.FrameFormats.MinBy(n => Math.Abs(n.Width * n.Height - mpix));
            }
            else
                selectedFormat = Description.FrameFormats.MaxBy(n => n.Width * n.Height);

            var result = Description.FrameFormats
                .Where(n =>
                    n.Width == (selectedFormat?.Width ?? 0)
                    && n.Height == (selectedFormat?.Height ?? 0))
                .ToArray();

            if (result.Length != 0)
            {
                var result2 = result.Where(n => n.Format == format)
                    .ToArray();

                if (result2.Length != 0)
                    result = result2;
            }

            if (result.Length == 0)
                return new FrameFormat(0, 0);

            var result3 = result.MaxBy(n => n.Fps) ?? result[0];

            return result3;
        }

        private async Task<bool> OpenPtzClient()
        {
            if (!IsPtz)
                return false;

            if (_onvifClient.DeviceClient.State == CommunicationState.Closed)
            {
                await _onvifClient.DeviceClient.OpenAsync();
            }

            if (_ptzClient.State == CommunicationState.Closed)
            {
                await _ptzClient.OpenAsync();
            }

            return true;
        }

        private async Task<bool> ClosePtzClient()
        {
            if (IsPtz)
            {
                if (_onvifClient.DeviceClient.State == CommunicationState.Opened)
                {
                    await _onvifClient.DeviceClient.CloseAsync();
                }

                if (_ptzClient.State == CommunicationState.Opened)
                {
                    await _ptzClient.CloseAsync();
                }
            }

            return true;
        }

        public async Task PtzContinuousMove(int xSpeedPercent, int ySpeedPercent, int zoomSpeedPercent, int delay = 0)
        {
            if (!IsPtz)
                return;

            try
            {
                if (xSpeedPercent > 100)
                    xSpeedPercent = 100;
                if (xSpeedPercent < -100)
                    xSpeedPercent = -100;
                if (ySpeedPercent > 100)
                    ySpeedPercent = 100;
                if (ySpeedPercent < -100)
                    ySpeedPercent = -100;
                if (zoomSpeedPercent > 100)
                    zoomSpeedPercent = 100;
                if (zoomSpeedPercent < -100)
                    zoomSpeedPercent = -100;

                var xmax = _ptzProfile.PTZConfiguration.PanTiltLimits.Range.XRange.Max;
                var ymax = _ptzProfile.PTZConfiguration.PanTiltLimits.Range.YRange.Max;
                var zmax = _ptzProfile.PTZConfiguration.ZoomLimits.Range.XRange.Max;
                var xSpeed = ((float)xSpeedPercent / 100) * xmax;
                var ySpeed = ((float)ySpeedPercent / 100) * ymax;
                var zoomSpeed = ((float)zoomSpeedPercent / 100) * zmax;
                await _ptzClient.ContinuousMoveAsync(_ptzProfile.token, new PTZSpeed()
                {
                    PanTilt = new Vector2D()
                    {
                        x = xSpeed,
                        y = ySpeed
                    },
                    Zoom = new Vector1D()
                    {
                        x = zoomSpeed
                    }
                }, null);

                if (delay > 0)
                {
                    await Task.Delay(delay, CancellationToken.None);
                    await _ptzClient.StopAsync(_ptzProfile.token, true, true);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(new EventId(0), ex, $"Error using camera PTZ");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Stop();
                    _keepAliveTimer.Elapsed -= CheckCameraDisconnected;
                    _keepAliveTimer.Close();
                    _keepAliveTimer.Dispose();
                    _captureDevice?.Dispose();
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSourceCameraGrabber?.Dispose();
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
