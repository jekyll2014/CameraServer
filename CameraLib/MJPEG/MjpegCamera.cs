using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using IPAddress = System.Net.IPAddress;

namespace CameraLib.IP
{
    public class MjpegCamera : ICamera, IDisposable
    {
        // JPEG delimiters
        const byte picMarker = 0xFF;
        const byte picStart = 0xD8;
        const byte picEnd = 0xD9;

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
        public AuthType AuthenicationType { get; }
        public string Login { get; }
        public string Password { get; }

        public CameraDescription Description { get; set; }
        public bool IsRunning =>
            !(_imageGrabber == null
              || _imageGrabber.IsCanceled
              || _imageGrabber.IsCompleted
              || _imageGrabber.IsCompletedSuccessfully
              || _imageGrabber.IsFaulted);

        public event ICamera.ImageCapturedEventHandler? ImageCapturedEvent;

        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;
        private CancellationTokenSource? _cancellationTokenSource;

        private readonly object _getPictureThreadLock = new object();
        private string _ipCameraName;
        private Bitmap? _image;
        private Task? _imageGrabber;
        private volatile bool _stopCapture = false;
        private bool _disposedValue;

        public MjpegCamera(string path, string name = "", AuthType authenicationType = AuthType.None, string login = "", string password = "", int discoveryTimeout = 1000, bool forceCameraConnect = false)
        {
            _discoveryTimeout = discoveryTimeout;
            Path = path;
            AuthenicationType = authenicationType;
            Login = login;
            Password = password;

            if (authenicationType == AuthType.Plain)
                Path = string.Format(Path, login, password);

            var cameraUri = new Uri(Path);
            _ipCameraName = string.IsNullOrEmpty(name)
                ? cameraUri.Host
                : name;

            List<FrameFormat> frameFormats = [];
            if (frameFormats.Count == 0 || forceCameraConnect)
            {
                if (PingAddress(cameraUri.Host).Result)
                {
                    var image = GrabFrame(CancellationToken.None).Result;
                    if (image != null)
                    {
                        frameFormats.Add(new FrameFormat(image.Width, image.Height, image.RawFormat.ToString()));
                        image.Dispose();
                    }
                }
            }

            Description = new CameraDescription(CameraType.IP, Path, _ipCameraName, frameFormats);
        }

        // not implemented
        public List<CameraDescription> DiscoverCamerasAsync(int discoveryTimeout, CancellationToken token)
        {
            return [];
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

        public async Task<bool> Start(int x, int y, string format, CancellationToken token)
        {
            if (!IsRunning)
            {
                try
                {
                    _stopCapture = false;
                    _imageGrabber = StartAsync(Path, AuthenicationType, Login, Password, token);
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

            _stopCapture = true;
            while (IsRunning)
                Task.Delay(10);

            _imageGrabber?.Dispose();
            _cancellationTokenSource?.Cancel();
            _image?.Dispose();
        }

        public async Task Stop(CancellationToken token)
        {
            Stop();
        }

        public async Task<Bitmap?> GrabFrame(CancellationToken token)
        {
            if (IsRunning)
            {
                while (IsRunning && _image == null && !token.IsCancellationRequested)
                    await Task.Delay(10, token);

                lock (_getPictureThreadLock)
                {
                    return (Bitmap?)_image?.Clone();
                }
            }

            Bitmap? image = null;
            await Task.Run(async () =>
            {
                if (await Start(0, 0, "", token))
                {
                    var img = await GrabFrame(token);
                    image = (Bitmap?)img?.Clone();
                }

                await Stop(token);
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
                    while (!_stopCapture && !tok.IsCancellationRequested)
                    {
                        var streamLength = await stream.ReadAsync(streamBuffer.AsMemory(0, chunkMaxSize), tok).ConfigureAwait(false);
                        ParseStreamBuffer(frameBuffer, ref frameIdx, streamLength, streamBuffer, ref inPicture, ref previous, ref current);
                    }
                }
            }
        }

        // Parse the stream buffer
        private void ParseStreamBuffer(byte[] frameBuffer, ref int frameIdx, int streamLength, byte[] streamBuffer, ref bool inPicture, ref byte previous, ref byte current)
        {
            var idx = 0;
            while (idx < streamLength && !_stopCapture)
            {
                if (inPicture)
                {
                    ParsePicture(frameBuffer, ref frameIdx, ref streamLength, streamBuffer, ref idx, ref inPicture, ref previous, ref current);
                }
                else
                {
                    SearchPicture(frameBuffer, ref frameIdx, ref streamLength, streamBuffer, ref idx, ref inPicture, ref previous, ref current);
                }
            }
        }

        // While we are looking for a picture, look for a FFD8 (end of JPEG) sequence.
        private void SearchPicture(byte[] frameBuffer, ref int frameIdx, ref int streamLength, byte[] streamBuffer, ref int idx, ref bool inPicture, ref byte previous, ref byte current)
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
            } while (idx < streamLength);
        }

        // While we are parsing a picture, fill the frame buffer until a FFD9 is reach.
        private void ParsePicture(byte[] frameBuffer, ref int frameIdx, ref int streamLength, byte[] streamBuffer, ref int idx, ref bool inPicture, ref byte previous, ref byte current)
        {
            do
            {
                previous = current;
                current = streamBuffer[idx++];
                frameBuffer[frameIdx++] = current;

                // JPEG picture end ?
                if (previous == picMarker && current == picEnd)
                {
                    lock (_getPictureThreadLock)
                    {
                        _image?.Dispose();

                        // Using a memorystream this way prevent arrays copy and allocations
                        using (var s = new MemoryStream(frameBuffer, 0, frameIdx))
                        {
                            try
                            {
                                _image = new Bitmap(Image.FromStream(s));
                            }
                            catch
                            {
                                // We dont care about badly decoded pictures
                            }
                        }

                        // Defer the image processing to prevent slow down
                        // The image processing delegate must dispose the image eventually.
                        if (_image != null)
                            ImageCapturedEvent?.Invoke(this, _image);
                    }

                    inPicture = false;

                    return;
                }
            } while (idx < streamLength);
        }

        #endregion
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Stop();
                    _imageGrabber?.Dispose();
                    _cancellationTokenSource?.Dispose();
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
