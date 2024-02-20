using FlashCap;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CameraLib.FlashCap
{
    public class UsbCameraFc : ICamera, IDisposable
    {
        public string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(_usbCameraName))
                    return _usbCameraName;

                return Path;
            }
            set
            {
                _usbCameraName = value;
                Description.Name = value;
            }
        }
        public string Path { get; }
        public CameraDescription Description { get; set; }
        public bool IsRunning { get; private set; }

        public event ICamera.ImageCapturedEventHandler? ImageCapturedEvent;
        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        private CancellationTokenSource? _cancellationTokenSource;

        private readonly CaptureDeviceDescriptor _usbCamera;
        private VideoCharacteristics? _cameraCharacteristics;
        private CaptureDevice? _captureDevice;
        private string _usbCameraName;
        private Bitmap? _image = null;
        private readonly object _getPictureThreadLock = new object();

        private bool _disposedValue;

        public UsbCameraFc(string path, string name = "")
        {
            Path = path;
            _usbCameraName = name;

            var devices = new CaptureDevices();
            var descriptors = devices
                .EnumerateDescriptors()
                .Where(d => d.Characteristics.Length >= 1);             // One or more valid video characteristics.

            _usbCamera = descriptors.FirstOrDefault(n => n.Identity?.ToString() == path)
                         ?? throw new ArgumentException("Can not find camera", nameof(path));

            if (string.IsNullOrEmpty(_usbCameraName))
                _usbCameraName = _usbCamera.Name;
            if (string.IsNullOrEmpty(_usbCameraName))
                _usbCameraName = path;

            Description = new CameraDescription(CameraType.USB_FC, Path, Name, GetAllAvailableResolution(_usbCamera));
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

                result.Add(new CameraDescription(CameraType.USB, camera.Identity.ToString(), camera.Name, formats));
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

        public async Task<bool> Start(int x, int y, string format, CancellationToken token)
        {
            if (!IsRunning)
            {
                _cameraCharacteristics = GetCaptureDevice(x, y, format);
                if (_cameraCharacteristics == null)
                    return false;

                _captureDevice = await _usbCamera.OpenAsync(_cameraCharacteristics, OnPixelBufferArrived, token);
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
                _image?.Dispose();
                try
                {
                    var image = bufferScope.Buffer.CopyImage();
                    bufferScope.ReleaseNow();

                    // Decode image data to a bitmap:
                    using (var ms = new MemoryStream(image))
                    {
                        _image = new Bitmap(Image.FromStream(ms));
                    }

                    if (_image != null)
                    {
                        ImageCapturedEvent?.Invoke(this, _image);
                    }
                }
                catch
                {
                    Stop();
                }
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
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

            _image?.Dispose();
            IsRunning = false;
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
                _cameraCharacteristics = GetCaptureDevice(0, 0, string.Empty);

                if (_cameraCharacteristics == null)
                    return;

                var imageData = await _usbCamera.TakeOneShotAsync(_cameraCharacteristics, token);

                using (var ms = new MemoryStream(imageData))
                {
                    image = new Bitmap(Image.FromStream(ms));
                }

            }, token);

            return image;
        }

        public async IAsyncEnumerable<Bitmap> GrabFrames([EnumeratorCancellation] CancellationToken token)
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
                    yield return image;
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
