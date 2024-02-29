using Emgu.CV;
using Emgu.CV.CvEnum;

using FlashCap;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CameraLib.FlashCap
{
    public class UsbCameraFc : ICamera, IDisposable
    {
        public CameraDescription Description { get; set; }
        public bool IsRunning { get; private set; }

        public event ICamera.ImageCapturedEventHandler? ImageCapturedEvent;

        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        private CancellationTokenSource? _cancellationTokenSource;

        private readonly CaptureDeviceDescriptor _usbCamera;
        private CaptureDevice? _captureDevice;
        private Mat? _frame = null;
        private readonly object _getPictureThreadLock = new object();

        private bool _disposedValue;

        public UsbCameraFc(string path, string name = "")
        {
            var devices = new CaptureDevices();
            var descriptors = devices
                .EnumerateDescriptors()
                .Where(d => d.Characteristics.Length >= 1);             // One or more valid video characteristics.

            _usbCamera = descriptors.FirstOrDefault(n => n.Identity?.ToString() == path)
                         ?? throw new ArgumentException("Can not find camera", nameof(path));

            if (string.IsNullOrEmpty(name))
                name = _usbCamera.Name;
            if (string.IsNullOrEmpty(name))
                name = path;

            Description = new CameraDescription(CameraType.USB_FC, path, name, GetAllAvailableResolution(_usbCamera));
        }

        public List<CameraDescription> DiscoverCamerasAsync(int discoveryTimeout, CancellationToken token)
        {
            return DiscoverUsbCameras();
        }

        public static List<CameraDescription> DiscoverUsbCameras()
        {
            var devices = new CaptureDevices();
            var descriptors = devices
                .EnumerateDescriptors()
                .Where(d => d.Characteristics.Length > 0 && d.DeviceType == DeviceTypes.DirectShow);             // One or more valid video characteristics.

            var result = new List<CameraDescription>();
            foreach (var camera in descriptors)
            {
                var formats = GetAllAvailableResolution(camera);
                result.Add(new CameraDescription(CameraType.USB, camera.Identity.ToString() ?? "", camera.Name, formats));
            }

            return result;
        }

        private static IEnumerable<FrameFormat> GetAllAvailableResolution(CaptureDeviceDescriptor usbCamera)
        {
            var formats = new List<FrameFormat>();
            foreach (var cameraCharacteristic in usbCamera.Characteristics)
            {
                formats.Add(new FrameFormat(cameraCharacteristic.Width,
                    cameraCharacteristic.Height,
                    cameraCharacteristic.PixelFormat.ToString(),
                    (double)cameraCharacteristic.FramesPerSecond.Numerator / cameraCharacteristic.FramesPerSecond.Denominator));
            }

            return formats;
        }

        public async Task<bool> Start(int width, int height, string format, CancellationToken token)
        {
            if (!IsRunning)
            {
                var cameraCharacteristics = GetCaptureDevice(width, height, format);
                if (cameraCharacteristics == null)
                    return false;

                _captureDevice = await _usbCamera.OpenAsync(cameraCharacteristics, OnPixelBufferArrived, token);
                if (_captureDevice == null)
                {
                    IsRunning = false;

                    return false;
                }

                _cancellationTokenSource = new CancellationTokenSource();
                await _captureDevice.StartAsync(token);

                IsRunning = true;
            }

            return true;
        }

        private VideoCharacteristics? GetCaptureDevice(int x, int y, string format)
        {
            var characteristics = _usbCamera.Characteristics
                .Where(n => n.PixelFormat != PixelFormats.Unknown);

            if (!string.IsNullOrEmpty(format))
                characteristics = _usbCamera.Characteristics
                    .Where(n => n.PixelFormat.ToString() == format);

            if (x > 0 && y > 0)
            {
                characteristics = characteristics
                    .Where(n => n.Width == x && n.Height == y).ToList();
            }
            else
            {
                characteristics = new List<VideoCharacteristics>(){
                    characteristics.Aggregate((n, m) =>
                    {
                        if (n.Width * n.Height > m.Width * m.Height)
                            return n;
                        else
                            return m;
                    })};
            }

            return characteristics.FirstOrDefault();
        }

        private void OnPixelBufferArrived(PixelBufferScope bufferScope)
        {
            if (Monitor.IsEntered(_getPictureThreadLock))
            {
                bufferScope.ReleaseNow();

                return;
            }

            lock (_getPictureThreadLock)
            {
                _frame?.Dispose();
                _frame = new Mat();
                try
                {
                    var image = bufferScope.Buffer.CopyImage();
                    bufferScope.ReleaseNow();
                    CvInvoke.Imdecode(image, ImreadModes.Color, _frame);
                    ImageCapturedEvent?.Invoke(this, _frame.Clone());
                }
                catch
                {
                    Stop(CancellationToken.None).Wait(CancellationToken.None);
                }
                finally
                {
                    //_frame?.Dispose();
                    //GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
                }
            }
        }

        public async void Stop()
        {
            await Stop(CancellationToken.None);
        }

        public async Task Stop(CancellationToken token)
        {
            if (!IsRunning)
                return;

            if (_captureDevice != null)
            {
                await _captureDevice.StopAsync(token);
                await _captureDevice.DisposeAsync();
            }

            _captureDevice = null;
            if (_cancellationTokenSource != null)
                await _cancellationTokenSource.CancelAsync();

            _frame?.Dispose();
            IsRunning = false;
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
                var cameraCharacteristics = GetCaptureDevice(0, 0, string.Empty);

                if (cameraCharacteristics == null)
                    return;

                var imageData = await _usbCamera.TakeOneShotAsync(cameraCharacteristics, token);

                CvInvoke.Imdecode(imageData, ImreadModes.Color, image);
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

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Stop();
                    _cancellationTokenSource?.Dispose();
                    _captureDevice?.Dispose();
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
