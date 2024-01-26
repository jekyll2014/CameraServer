using DirectShowLib;

using Emgu.CV;
using Emgu.CV.CvEnum;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CameraLib.USB
{
    public class UsbCamera : ICamera
    {
        public bool IsRunning { get; private set; }
        public string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(_usbCameraName))
                    return _usbCameraName;

                return Id;
            }
            set => _usbCameraName = value;
        }
        public string Id { get; }
        public List<FrameFormat> Capabilities { get; }

        public event ICamera.ImageCapturedEventHandler? ImageCapturedEvent;

        private const VideoCapture.API CaptureSource = VideoCapture.API.DShow;
        private readonly object _getPictureThreadLock = new object();
        private readonly DsDevice _usbCamera;
        private VideoCapture? _captureDevice;
        private readonly Mat _frame = new Mat();
        private string _usbCameraName;
        private Image? _image;
        private bool _disposedValue;

        public UsbCamera(string id, string name = "")
        {
            Id = id;
            _usbCameraName = name;
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice) ?? Array.Empty<DsDevice>();
            _usbCamera = devices.FirstOrDefault(n => n.DevicePath == id);

            if (_usbCamera == null)
                throw new ArgumentException("Can not find camera", nameof(id));

            if (string.IsNullOrEmpty(_usbCameraName))
                _usbCameraName = _usbCamera.Name;
            if (string.IsNullOrEmpty(_usbCameraName))
                _usbCameraName = id;

            Capabilities = GetAllAvailableResolution(_usbCamera);
        }

        public List<CameraDescription> DiscoverCamerasAsync(int discoveryTimeout, CancellationToken token)
        {
            return DiscoverUsbCameras();
        }

        public static List<CameraDescription> DiscoverUsbCameras()
        {
            var descriptors = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            var result = new List<CameraDescription>();
            foreach (var camera in descriptors)
            {
                var formats = new List<FrameFormat>();
                var cameraCharacteristics = GetAllAvailableResolution(camera);
                foreach (var cameraCharacteristic in cameraCharacteristics)
                {
                    formats.Add(new FrameFormat(cameraCharacteristic.Width,
                        cameraCharacteristic.Heigth,
                        cameraCharacteristic.Format,
                        0.0));
                }

                result.Add(new CameraDescription(CameraType.USB, camera.DevicePath, camera.Name, formats));
            }

            return result;
        }

        private static List<FrameFormat> GetAllAvailableResolution(DsDevice usbCamera)
        {
            try
            {
                var bitCount = 0;

                var availableResolutionsSorted = new List<FrameFormat>();

                if (!(new FilterGraph() is IFilterGraph2 mFilterGraph2))
                    return availableResolutionsSorted;

                mFilterGraph2.AddSourceFilterForMoniker(usbCamera.Mon, null, usbCamera.Name, out var sourceFilter);

                var pRaw2 = DsFindPin.ByCategory(sourceFilter, PinCategory.Capture, 0);

                var availableResolutions = new List<FrameFormat>();

                var v = new VideoInfoHeader();
                pRaw2.EnumMediaTypes(out var mediaTypeEnum);

                var mediaTypes = new AMMediaType[1];
                var fetched = IntPtr.Zero;
                mediaTypeEnum.Next(1, mediaTypes, fetched);

                while (mediaTypes[0] != null)
                {
                    Marshal.PtrToStructure(mediaTypes[0].formatPtr, v);
                    var header = v.BmiHeader;
                    if (header.Size != 0 && header.BitCount != 0)
                    {
                        if (header.BitCount > bitCount)
                        {
                            availableResolutions.Clear();
                            bitCount = header.BitCount;
                        }

                        FrameFormat.codecs.TryGetValue(header.Compression, out var format);
                        availableResolutions.Add(new FrameFormat(
                            header.Width,
                            header.Height,
                            format ?? "",
                            1.0 / v.AvgTimePerFrame));
                    }

                    mediaTypeEnum.Next(1, mediaTypes, fetched);
                }

                return availableResolutions;
            }

            catch (Exception)
            {
                return new List<FrameFormat>();
            }
        }

        public async Task<bool> Start(int x, int y, string format, CancellationToken token)
        {
            if (!IsRunning)
            {
                _captureDevice = GetCaptureDevice();

                if (_captureDevice == null)
                {
                    IsRunning = false;

                    return false;
                }

                if (x > 0 && y > 0)
                {
                    var res = GetAllAvailableResolution(_usbCamera);
                    if (res.Exists(n => n.Width == x && n.Heigth == y))
                    {
                        _captureDevice.Set(CapProp.FrameWidth, x);
                        _captureDevice.Set(CapProp.FrameHeight, y);
                    }

                    if (!string.IsNullOrEmpty(format))
                    {
                        var codecId = FrameFormat.codecs.FirstOrDefault(n => n.Value == format);
                        if (!string.IsNullOrEmpty(codecId.Value))
                            _captureDevice.Set(CapProp.FourCC, codecId.Key);
                    }
                }

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
                try
                {
                    if (!(_captureDevice?.Grab() ?? false))
                        return;

                    if (!_captureDevice.Retrieve(_frame))
                        return;

                    var bitmap = _frame.ToBitmap();
                    if (bitmap != null)
                    {
                        ImageCapturedEvent?.Invoke(this, bitmap);
                        bitmap.Dispose();
                    }
                }
                catch
                {
                    Stop();
                }
            }
        }

        private VideoCapture? GetCaptureDevice()
        {
            var camNumber = 0;
            var cameras = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice) ?? Array.Empty<DsDevice>();
            foreach (var cam in cameras)
            {
                if (_usbCamera.DevicePath == cam.DevicePath)
                    break;

                camNumber++;
            }

            if (cameras.Length == 0 || camNumber >= cameras.Length)
                return null;

            return new VideoCapture(camNumber, CaptureSource);
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            _captureDevice?.Stop();
            _captureDevice?.Dispose();
            _captureDevice = null;
            IsRunning = false;
        }

        public void Stop(CancellationToken token)
        {
            Stop();
        }

        public async Task<Image?> GrabFrame(CancellationToken token)
        {
            await Task.Run(() =>
           {
               if (_captureDevice?.Grab() ?? false)
                   if (_captureDevice.Retrieve(_frame))
                   {
                       _image?.Dispose();
                       _image = _frame.ToBitmap();
                       return _image;
                   }

               return null;
           }, token);

            return _image;
        }

        public async IAsyncEnumerable<Image> GrabFrames([EnumeratorCancellation] CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var image = await GrabFrame(token);
                if (image == null)
                {
                    await Task.Delay(100, token);
                    // yield break;
                }

                yield return image;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    Stop();
                    _frame.Dispose();
                    _usbCamera.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                _disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~CameraVideoSource()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
