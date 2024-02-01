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
    public class UsbCameraFC : ICamera
    {
        public bool IsRunning { get; private set; }
        public string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(_usbCameraName))
                    return _usbCameraName;

                return Path;
            }
            set => _usbCameraName = value;
        }
        public string Path { get; }
        public CameraDescription Description { get; set; }

        public event ICamera.ImageCapturedEventHandler? ImageCapturedEvent;

        private readonly CaptureDeviceDescriptor _cameraDescriptor;
        private VideoCharacteristics? _cameraCharacteristics;
        private CaptureDevice? _captureDevice;
        private string _usbCameraName = string.Empty;
        private Image? _image = null;
        private volatile bool imageGrabbed = false;
        private readonly object _getPictureThreadLock = new object();

        public UsbCameraFC(string path)
        {
            Path = path;

            var devices = new CaptureDevices();
            var descriptors = devices
                .EnumerateDescriptors()
                .Where(d => d.Characteristics.Length >= 1);             // One or more valid video characteristics.

            _cameraDescriptor = descriptors.FirstOrDefault(n => n.Identity?.ToString() == path);

            if (_cameraDescriptor == null)
                throw new ArgumentException("Can not find camera", nameof(path));

            Description = new CameraDescription(CameraType.USB_FlashCap, Path, Name, new List<FrameFormat>());
        }

        public List<CameraDescription> DiscoverCamerasAsync(int discoveryTimeout, CancellationToken token)
        {
            return DiscoverUsbCamerasAsync(discoveryTimeout, token);
        }

        public static List<CameraDescription> DiscoverUsbCamerasAsync(int discoveryTimeout = 0, CancellationToken? token = null)
        {
            var devices = new CaptureDevices();
            var descriptors = devices
                .EnumerateDescriptors()
                .Where(d => d.Characteristics.Length > 0);             // One or more valid video characteristics.

            var result = new List<CameraDescription>();
            foreach (var camera in descriptors)
            {
                var formats = new List<FrameFormat>();
                foreach (var cameraCharacteristic in camera.Characteristics)
                {
                    formats.Add(new FrameFormat(cameraCharacteristic.Width,
                        cameraCharacteristic.Height,
                        cameraCharacteristic.PixelFormat.ToString(),
                        (double)cameraCharacteristic.FramesPerSecond.Numerator / cameraCharacteristic.FramesPerSecond.Denominator));
                }

                result.Add(new CameraDescription(CameraType.USB, camera.Identity.ToString(), camera.Name, formats));
            }

            return result;
        }

        public async Task<bool> Start(int x, int y, string format, CancellationToken token)
        {
            var characteristics = _cameraDescriptor.Characteristics.Where(n => n.PixelFormat != PixelFormats.Unknown).ToList();

            if (!string.IsNullOrEmpty(format))
                characteristics = _cameraDescriptor.Characteristics.Where(n => n.PixelFormat.ToString() == format).ToList();

            if (x > 0 && y > 0)
            {
                characteristics = characteristics.Where(n => n.Width == x && n.Height == y).ToList();
            }

            _cameraCharacteristics = characteristics.FirstOrDefault();
            if (_cameraCharacteristics == null)
                return false;

            _captureDevice = await _cameraDescriptor.OpenAsync(_cameraCharacteristics, OnPixelBufferArrived, token);
            await _captureDevice.StartAsync(token);
            IsRunning = true;

            return true;
        }

        public void Stop()
        {
            Stop(CancellationToken.None);
        }

        public void Stop(CancellationToken token)
        {
            _captureDevice?.StopAsync(token);
            _captureDevice?.Dispose();
            _captureDevice = null;
            IsRunning = false;
        }

        private void OnPixelBufferArrived(PixelBufferScope bufferScope)
        {
            var image = bufferScope.Buffer.CopyImage();
            bufferScope.ReleaseNow();

            // Convert to Stream (using FlashCap.Utilities)
            lock (_getPictureThreadLock)
            {
                // Decode image data to a bitmap:
                _image?.Dispose();
                var ms = new MemoryStream(image);
                _image = Image.FromStream(ms);
                if (_image != null)
                    imageGrabbed = true;
            }
        }

        public async Task<Image?> GrabFrame(CancellationToken token)
        {
            while (_image == null && !token.IsCancellationRequested)
                await Task.Delay(100, token);

            return _image;

            /*if (_cameraCharacteristics == null)
                return null;

            var imageData = await _cameraDescriptor.TakeOneShotAsync(
                _cameraCharacteristics, token);

            AnyBitmap image;
            using (var ms = new MemoryStream(imageData))
            {
                image = new AnyBitmap(ms);
            }

            return image;*/
        }

        public async IAsyncEnumerable<Image> GrabFrames([EnumeratorCancellation] CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                /*var image = await GrabFrame(token);
                if (image == null)
                    yield break;*/

                while (!imageGrabbed && !token.IsCancellationRequested)
                    await Task.Delay(100, token);

                imageGrabbed = false;
                lock (_getPictureThreadLock)
                {
                    yield return _image;
                }
            }
        }
    }
}
