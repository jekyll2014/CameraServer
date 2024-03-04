using DirectShowLib;

using Emgu.CV;
using Emgu.CV.CvEnum;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CameraLib.USB
{
    public class UsbCamera : ICamera, IDisposable
    {
        public CameraDescription Description { get; set; }
        public bool IsRunning { get; private set; }
        public FrameFormat? CurrentFrameFormat { get; private set; }

        public event ICamera.ImageCapturedEventHandler? ImageCapturedEvent;

        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        private CancellationTokenSource? _cancellationTokenSource;

        //private const VideoCapture.API CaptureSource = VideoCapture.API.DShow;
        private readonly DsDevice? _usbCamera;

        private readonly object _getPictureThreadLock = new();
        private VideoCapture? _captureDevice;
        private Mat? _frame;

        private bool _disposedValue;

        public UsbCamera(string path, string name = "")
        {
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice) ?? Array.Empty<DsDevice>();
            _usbCamera = devices.FirstOrDefault(n => n.DevicePath == path)
                         ?? throw new ArgumentException("Can not find camera", nameof(path));

            if (string.IsNullOrEmpty(name))
                name = _usbCamera.Name;
            if (string.IsNullOrEmpty(name))
                name = path;

            Description = new CameraDescription(CameraType.USB, path, name, GetAllAvailableResolution(_usbCamera));
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
                var formats = GetAllAvailableResolution(camera);
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

                var videoInfoHeader = new VideoInfoHeader();
                pRaw2.EnumMediaTypes(out var mediaTypeEnum);

                var mediaTypes = new AMMediaType[1];
                var fetched = IntPtr.Zero;
                mediaTypeEnum.Next(1, mediaTypes, fetched);

                while (mediaTypes[0] != null)
                {
                    Marshal.PtrToStructure(mediaTypes[0].formatPtr, videoInfoHeader);
                    var header = videoInfoHeader.BmiHeader;
                    if (header.Size != 0 && header.BitCount != 0)
                    {
                        if (header.BitCount > bitCount)
                        {
                            availableResolutions.Clear();
                            bitCount = header.BitCount;
                        }

                        FrameFormat.Codecs.TryGetValue(header.Compression, out var format);
                        availableResolutions.Add(new FrameFormat(
                            header.Width,
                            header.Height,
                            format ?? "",
                            (double)10000000 / videoInfoHeader.AvgTimePerFrame));
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

        public async Task<bool> Start(int width, int height, string format, CancellationToken token)
        {
            if (!IsRunning)
            {
                _captureDevice = GetCaptureDevice();

                if (_captureDevice == null)
                {
                    return false;
                }

                if (_usbCamera == null)
                    return false;

                if (width > 0 && height > 0)
                {
                    var res = GetAllAvailableResolution(_usbCamera);
                    if (res.Exists(n => n.Width == width && n.Heigth == height))
                    {
                        _captureDevice.Set(CapProp.FrameWidth, width);
                        _captureDevice.Set(CapProp.FrameHeight, height);
                    }

                    if (!string.IsNullOrEmpty(format))
                    {
                        var codecId = FrameFormat.Codecs.FirstOrDefault(n => n.Value == format);
                        if (!string.IsNullOrEmpty(codecId.Value))
                            _captureDevice.Set(CapProp.FourCC, codecId.Key);
                    }
                }

                _frame?.Dispose();
                _frame = new Mat();
                _cancellationTokenSource = new CancellationTokenSource();
                _captureDevice.ImageGrabbed += ImageCaptured;
                _captureDevice.Start();

                IsRunning = true;
            }

            return true;
        }

        private VideoCapture? GetCaptureDevice()
        {
            var cameras = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice) ?? Array.Empty<DsDevice>();
            var camNumber = cameras.TakeWhile(cam => _usbCamera?.DevicePath != cam.DevicePath).Count();

            if (cameras.Length == 0 || camNumber >= cameras.Length)
                return null;

            return new VideoCapture(camNumber);
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

                    if (CurrentFrameFormat == null)
                    {
                        CurrentFrameFormat = new FrameFormat(_frame.Width, _frame.Height);
                    }

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
                if (_captureDevice != null)
                {
                    _captureDevice.Stop();
                    _captureDevice.ImageGrabbed -= ImageCaptured;
                    _captureDevice.Dispose();
                    _captureDevice = null;
                }

                _frame?.Dispose();
                CurrentFrameFormat = null;
                IsRunning = false;
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
            await Task.Run(() =>
                {
                    _captureDevice = GetCaptureDevice();
                    if (_captureDevice == null)
                        return;

                    try
                    {

                        if (_captureDevice.Grab())
                        {
                            if (_captureDevice.Retrieve(image))
                            {
                                CurrentFrameFormat ??= new FrameFormat(image.Width, image.Height);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }

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
                    _usbCamera?.Dispose();
                    _captureDevice?.Dispose();
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
