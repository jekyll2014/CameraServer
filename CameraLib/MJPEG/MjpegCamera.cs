using Microsoft.Extensions.Logging;

using OpenCvSharp;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

using IPAddress = System.Net.IPAddress;

namespace CameraLib.MJPEG
{
    public class MjpegCamera : ICamera
    {
        public AuthType AuthenticationType { get; private set; }
        public string Login { get; private set; }
        public string Password { get; private set; }
        public CameraDescription Description { get; set; }
        public bool IsRunning { get; private set; } = false;
        public FrameFormat? CurrentFrameFormat { get; private set; }
        public double CurrentFps { get; private set; }
        public int FrameTimeout { get; set; } = 30000;
        public event ICamera.ImageCapturedEventHandler? ImageCapturedEvent;
        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        private readonly ILogger? _logger;

        // JPEG delimiters
        private const byte PicMarker = 0xFF;
        private const byte PicStart = 0xD8;
        private const byte PicEnd = 0xD9;
        private CancellationTokenSource? _cancellationTokenSource;
        private CancellationTokenSource? _cancellationTokenSourceCameraGrabber;
        private Task? _imageGrabber;
        private readonly Stopwatch _fpsTimer = new();
        private byte _frameCount;
        private readonly System.Timers.Timer _keepAliveTimer = new();
        private int _width = 0;
        private int _height = 0;
        private string _format = string.Empty;
        private bool _disposedValue;

        public MjpegCamera(string path,
            string name = "",
            AuthType authenticationType = AuthType.None,
            string login = "",
            string password = "",
            int discoveryTimeout = 1000,
            bool forceCameraConnect = false,
            ILogger? logger = null)
        {
            _logger = logger;
            AuthenticationType = authenticationType;
            Login = login;
            Password = password;

            if (authenticationType == AuthType.Plain)
                path = string.Format(path, login, password);

            var cameraUri = new Uri(path);
            name = string.IsNullOrEmpty(name)
                ? cameraUri.Host
                : name;

            List<FrameFormat> frameFormats = [];
            Description = new CameraDescription(CameraType.IP, path, name, frameFormats);

            if (forceCameraConnect)
            {
                if (PingAddress(cameraUri.Host, discoveryTimeout).Result)
                {
                    try
                    {
                        var image = GrabFrameAsync(CancellationToken.None).Result;
                        if (image != null)
                        {
                            frameFormats.Add(new FrameFormat(image.Width, image.Height, "MJPG"));
                            image.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"MJPEG camera initialization failed: {ex}");
                    }
                }
            }

            Description = new CameraDescription(CameraType.IP, path, name, frameFormats);
            CurrentFps = Description.FrameFormats.FirstOrDefault()?.Fps ?? 10;

            _keepAliveTimer.Elapsed += CheckCameraDisconnected;
        }

        public async Task<bool> GetImageDataAsync(int discoveryTimeout = 5000)
        {
            var cameraUri = new Uri(Description.Path);
            if (await PingAddress(cameraUri.Host, discoveryTimeout))
            {
                try
                {
                    var image = await GrabFrameAsync(CancellationToken.None);
                    if (image != null)
                    {
                        Description.FrameFormats = new[] { new FrameFormat(image.Width, image.Height, "MJPG") };
                        image.Dispose();
                    }
                    else
                        return false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"MJPEG camera initialization failed: {ex}");

                    return false;
                }
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
                    _logger?.LogDebug($"{DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()} Camera connection restarted ({_fpsTimer.ElapsedMilliseconds} timeout)");
                    Stop(false);
                    await StartAsync(_width, _height, _format, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in camera disconnect handler");
            }
        }

        // can not be implemented
        public List<CameraDescription> DiscoverCameras(int discoveryTimeout)
        {
            return [];
        }

        private static async Task<bool> PingAddress(string host, int pingTimeout = 5000)
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

        public async Task<bool> StartAsync(int width, int height, string format, CancellationToken token)
        {
            if (IsRunning)
                return true;

            _width = width;
            _height = height;
            _format = format;
            CurrentFrameFormat = new FrameFormat(_width, _height, "MJPEG");

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationTokenSourceCameraGrabber?.Dispose();
            _cancellationTokenSourceCameraGrabber = new CancellationTokenSource();

            _fpsTimer.Reset();
            _frameCount = 0;
            _keepAliveTimer.Interval = FrameTimeout;
            _keepAliveTimer.Start();

            try
            {
                _imageGrabber?.Dispose();
                _imageGrabber = StartAsync(Description.Path, AuthenticationType, _cancellationTokenSourceCameraGrabber.Token, Login, Password).WaitAsync(TimeSpan.FromMilliseconds(FrameTimeout), token);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Camera stopped due to exception: {ex.Message}");
                Stop();

                return false;
            }

            IsRunning = true;

            return true;
        }

        public void Stop()
        {
            Stop(true);
        }

        private void Stop(bool cancellation)
        {
            if (!IsRunning)
                return;

            _keepAliveTimer.Stop();
            _cancellationTokenSourceCameraGrabber?.Cancel();
            if (cancellation)
            {
                _cancellationTokenSourceCameraGrabber?.Cancel();
                _cancellationTokenSource?.Cancel();
            }

            CurrentFrameFormat = null;
            _fpsTimer.Reset();
            IsRunning = false;
        }

        public async Task<Mat?> GrabFrameAsync(CancellationToken token, int width = 0, int height = 0, string format = "")
        {
            if (IsRunning)
                return null;

            Mat? frame = null;
            await Task.Run(async () =>
            {
                if (await StartAsync(0, 0, string.Empty, token))
                    frame = await GrabFrameAsync(token);

                Stop();
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

        #region MJPEG processing

        /// <summary>
        /// Start a MJPEG on a http stream
        /// </summary>
        /// <param name="action">Delegate to run at each frame</param>
        /// <param name="url">url of the http stream (only basic auth is implemented)</param>
        /// <param name="authenticationType"></param>
        /// <param name="login">optional login</param>
        /// <param name="password">optional password (only basic auth is implemented)</param>
        /// <param name="token">cancellation token used to cancel the stream parsing</param>
        /// <param name="chunkMaxSize">Max chunk byte size when reading stream</param>
        /// <param name="frameBufferSize">Maximum frame byte size</param>
        /// <returns></returns>
        private async Task StartAsync(string url, AuthType authenticationType, CancellationToken token, string login = "", string password = "", int chunkMaxSize = 1024, int frameBufferSize = 1024 * 1024)
        {
            using (var httpClient = new HttpClient())
            {
                if (authenticationType == AuthType.Basic)
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                        "Basic",
                        Convert.ToBase64String(Encoding.ASCII.GetBytes($"{login}:{password}")));

                IsRunning = true;
                try
                {
                    await using (var stream = await httpClient.GetStreamAsync(url, token).ConfigureAwait(false))
                    {
                        var streamBuffer = new byte[chunkMaxSize];      // Stream chunk read
                        var frameBuffer = new byte[frameBufferSize];    // Frame buffer

                        var frameIdx = 0;       // Last written byte location in the frame buffer
                        var inPicture = false;  // Are we currently parsing a picture ?
                        byte current = 0x00;    // The last byte read
                        byte previous = 0x00;   // The byte before

                        // Continuously pump the stream. The cancellation token is used to get out of there
                        while (!token.IsCancellationRequested)
                        {
                            var streamLength = await stream.ReadAsync(streamBuffer.AsMemory(0, chunkMaxSize), token)
                                .ConfigureAwait(false);

                            ParseStreamBuffer(frameBuffer, ref frameIdx, streamLength, streamBuffer, ref inPicture,
                                ref previous, ref current, token);
                        }

                        IsRunning = false;
                    }
                }
                catch (Exception ex)
                {
                    Stop();
                    _logger?.LogError(ex.Message);
                }
            }
        }

        // Parse the stream buffer
        private void ParseStreamBuffer(byte[] frameBuffer, ref int frameIdx, int streamLength, byte[] streamBuffer, ref bool inPicture, ref byte previous, ref byte current, CancellationToken token)
        {
            var idx = 0;
            while (idx < streamLength && !token.IsCancellationRequested)
            {
                if (inPicture)
                {
                    ParsePicture(frameBuffer, ref frameIdx, ref streamLength, streamBuffer, ref idx, ref inPicture, ref previous, ref current, token);
                }
                else
                {
                    SearchPicture(frameBuffer, ref frameIdx, ref streamLength, streamBuffer, ref idx, ref inPicture, ref previous, ref current, token);
                }
            }
        }

        // While we are looking for a picture, look for a FFD8 (end of JPEG) sequence.
        private static void SearchPicture(byte[] frameBuffer, ref int frameIdx, ref int streamLength, byte[] streamBuffer, ref int idx, ref bool inPicture, ref byte previous, ref byte current, CancellationToken token)
        {
            do
            {
                previous = current;
                current = streamBuffer[idx++];

                // JPEG picture start ?
                if (previous == PicMarker && current == PicStart)
                {
                    frameIdx = 2;
                    frameBuffer[0] = PicMarker;
                    frameBuffer[1] = PicStart;
                    inPicture = true;
                    return;
                }
            } while (idx < streamLength && !token.IsCancellationRequested);
        }

        // While we are parsing a picture, fill the frame buffer until a FFD9 is reach.
        private void ParsePicture(byte[] frameBuffer, ref int frameIdx, ref int streamLength, byte[] streamBuffer, ref int idx, ref bool inPicture, ref byte previous, ref byte current, CancellationToken token)
        {
            do
            {
                previous = current;
                current = streamBuffer[idx++];
                frameBuffer[frameIdx++] = current;

                if (previous == PicMarker && current == PicEnd)
                {
                    Mat? frame = null;
                    try
                    {
                        frame = Cv2.ImDecode(frameBuffer, ImreadModes.Color);
                        if (frame != null && !frame.Empty())
                        {
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
                        else
                        {
                            frame?.Dispose();
                        }
                    }
                    catch
                    {
                        frame?.Dispose();
                    }

                    inPicture = false;
                    return;
                }
            } while (idx < streamLength && !token.IsCancellationRequested);
        }

        #endregion

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

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Stop();
                    _imageGrabber?.Dispose();
                    _keepAliveTimer.Elapsed -= CheckCameraDisconnected;
                    _keepAliveTimer.Close();
                    _keepAliveTimer.Dispose();
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
