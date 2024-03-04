using CameraLib.IP;

using Emgu.CV;
using Emgu.CV.CvEnum;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using IPAddress = System.Net.IPAddress;

namespace CameraLib.MJPEG
{
    public class MjpegCamera : ICamera, IDisposable
    {
        // JPEG delimiters
        const byte picMarker = 0xFF;
        const byte picStart = 0xD8;
        const byte picEnd = 0xD9;

        public AuthType AuthenicationType { get; }
        public string Login { get; }
        public string Password { get; }

        public CameraDescription Description { get; set; }
        public bool IsRunning { get; set; }
        public FrameFormat? CurrentFrameFormat { get; private set; }

        public event ICamera.ImageCapturedEventHandler? ImageCapturedEvent;

        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;
        private CancellationTokenSource? _cancellationTokenSource;

        private readonly object _getPictureThreadLock = new object();
        private Mat? _frame;
        private Task? _imageGrabber;
        private volatile bool _stopCapture = false;
        private bool _disposedValue;

        public MjpegCamera(string path, string name = "", AuthType authenicationType = AuthType.None, string login = "", string password = "", int discoveryTimeout = 1000, bool forceCameraConnect = false)
        {
            AuthenicationType = authenicationType;
            Login = login;
            Password = password;

            if (authenicationType == AuthType.Plain)
                path = string.Format(path, login, password);

            var cameraUri = new Uri(path);
            name = string.IsNullOrEmpty(name)
                ? cameraUri.Host
                : name;

            List<FrameFormat> frameFormats = new();
            Description = new CameraDescription(CameraType.IP, path, name, frameFormats);

            if (forceCameraConnect)
            {
                if (PingAddress(cameraUri.Host, discoveryTimeout).Result)
                {
                    var image = GrabFrame(CancellationToken.None).Result;
                    if (image != null)
                    {
                        frameFormats.Add(new FrameFormat(image.Width, image.Height, "MJPG"));
                        image.Dispose();
                    }
                }
            }

            Description = new CameraDescription(CameraType.IP, path, name, frameFormats);
        }

        // not implemented
        public List<CameraDescription> DiscoverCamerasAsync(int discoveryTimeout, CancellationToken token)
        {
            return new List<CameraDescription>();
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
                    _stopCapture = false;
                    _imageGrabber = StartAsync(Description.Path, AuthenicationType, Login, Password, token);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);

                    return false;
                }

                if (!IsRunning)
                    return false;

                _cancellationTokenSource = new CancellationTokenSource();
            }

            return true;
        }

        public void Stop()
        {
            if (!IsRunning)
                return;
            lock (_getPictureThreadLock)
            {
                _cancellationTokenSource?.Cancel();
                _stopCapture = true;
                var timeOut = DateTime.Now.AddSeconds(100);
                while (IsRunning && DateTime.Now < timeOut)
                    Task.Delay(10).Wait();

                _imageGrabber?.Dispose();
                _frame?.Dispose();
                CurrentFrameFormat = null;
            }
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
                if (await Start(0, 0, "", token))
                {
                    image = await GrabFrame(token);

                    if (image != null)
                        CurrentFrameFormat ??= new FrameFormat(image.Width, image.Height);
                }

                Stop();
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

        #region MJPEG processing

        /// <summary>
        /// Start a MJPEG on a http stream
        /// </summary>
        /// <param name="action">Delegate to run at each frame</param>
        /// <param name="url">url of the http stream (only basic auth is implemented)</param>
        /// <param name="authenicationType"></param>
        /// <param name="login">optional login</param>
        /// <param name="password">optional password (only basic auth is implemented)</param>
        /// <param name="token">cancellation token used to cancel the stream parsing</param>
        /// <param name="chunkMaxSize">Max chunk byte size when reading stream</param>
        /// <param name="frameBufferSize">Maximum frame byte size</param>
        /// <returns></returns>
        private async Task StartAsync(string url, AuthType authenicationType, string login = "", string password = "", CancellationToken? token = null, int chunkMaxSize = 1024, int frameBufferSize = 1024 * 1024)
        {
            var tok = token ?? CancellationToken.None;

            using (var httpClient = new HttpClient())
            {
                if (authenicationType == AuthType.Basic)
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                        "Basic",
                        Convert.ToBase64String(Encoding.ASCII.GetBytes($"{login}:{password}")));

                await using (var stream = await httpClient.GetStreamAsync(url, tok).ConfigureAwait(false))
                {

                    var streamBuffer = new byte[chunkMaxSize];      // Stream chunk read
                    var frameBuffer = new byte[frameBufferSize];    // Frame buffer

                    var frameIdx = 0;       // Last written byte location in the frame buffer
                    var inPicture = false;  // Are we currently parsing a picture ?
                    byte current = 0x00;    // The last byte read
                    byte previous = 0x00;   // The byte before

                    // Continuously pump the stream. The cancellationtoken is used to get out of there
                    IsRunning = true;
                    try
                    {
                        while (!_stopCapture && !tok.IsCancellationRequested)
                        {
                            var streamLength = await stream.ReadAsync(streamBuffer.AsMemory(0, chunkMaxSize), tok)
                                .ConfigureAwait(false);
                            ParseStreamBuffer(frameBuffer, ref frameIdx, streamLength, streamBuffer, ref inPicture,
                                ref previous, ref current, tok);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                    finally
                    {
                        IsRunning = false;
                    }
                }
            }
        }

        // Parse the stream buffer
        private void ParseStreamBuffer(byte[] frameBuffer, ref int frameIdx, int streamLength, byte[] streamBuffer, ref bool inPicture, ref byte previous, ref byte current, CancellationToken token)
        {
            var idx = 0;
            while (idx < streamLength && !_stopCapture && !token.IsCancellationRequested)
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
        private void SearchPicture(byte[] frameBuffer, ref int frameIdx, ref int streamLength, byte[] streamBuffer, ref int idx, ref bool inPicture, ref byte previous, ref byte current, CancellationToken token)
        {
            do
            {
                previous = current;
                current = streamBuffer[idx++];

                // JPEG picture start ?
                if (previous == picMarker && current == picStart)
                {
                    frameIdx = 2;
                    frameBuffer[0] = picMarker;
                    frameBuffer[1] = picStart;
                    inPicture = true;
                    return;
                }
            } while (idx < streamLength && !_stopCapture && !token.IsCancellationRequested);
        }

        // While we are parsing a picture, fill the frame buffer until a FFD9 is reach.
        private void ParsePicture(byte[] frameBuffer, ref int frameIdx, ref int streamLength, byte[] streamBuffer, ref int idx, ref bool inPicture, ref byte previous, ref byte current, CancellationToken token)
        {
            do
            {
                previous = current;
                current = streamBuffer[idx++];
                frameBuffer[frameIdx++] = current;

                // JPEG picture end ?
                if (previous == picMarker && current == picEnd)
                {
                    if (Monitor.IsEntered(_getPictureThreadLock))
                    {
                        inPicture = false;

                        return;
                    }

                    lock (_getPictureThreadLock)
                    {
                        _frame?.Dispose();
                        _frame = new Mat();
                        try
                        {
                            CvInvoke.Imdecode(frameBuffer, ImreadModes.Color, _frame);
                            CurrentFrameFormat ??= new FrameFormat(_frame.Width, _frame.Height);
                            ImageCapturedEvent?.Invoke(this, _frame.Clone());
                        }
                        catch
                        {
                            // We dont care about badly decoded pictures
                        }
                        finally
                        {
                            //_frame?.Dispose();
                            //GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
                        }
                    }

                    inPicture = false;

                    return;
                }
            } while (idx < streamLength && !_stopCapture && !token.IsCancellationRequested);
        }

        #endregion

        public FrameFormat GetNearestFormat(int xResolution, int yResolution, string format)
        {
            FrameFormat? selectedFormat;

            if (!Description.FrameFormats.Any())
                return new FrameFormat(0, 0);

            if (Description.FrameFormats.Count() == 1)
                return Description.FrameFormats.First();

            if (xResolution > 0 && yResolution > 0)
            {
                var mpix = xResolution * yResolution;
                selectedFormat = Description.FrameFormats.MinBy(n => Math.Abs(n.Width * n.Heigth - mpix));
            }
            else
                selectedFormat = Description.FrameFormats.MaxBy(n => n.Width * n.Heigth);

            var result = Description.FrameFormats
                .Where(n =>
                    n.Width == selectedFormat?.Width
                    && n.Heigth == selectedFormat.Heigth)
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
