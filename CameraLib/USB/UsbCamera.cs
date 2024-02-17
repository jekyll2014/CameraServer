﻿using DirectShowLib;

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
    public class UsbCamera : ICamera, IDisposable
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

        private const VideoCapture.API CaptureSource = VideoCapture.API.DShow;
        private readonly DsDevice? _usbCamera;

        private readonly object _getPictureThreadLock = new object();
        private VideoCapture? _captureDevice;
        private string _usbCameraName;
        private readonly Mat _frame = new Mat();
        private Bitmap? _image;

        private bool _disposedValue;

        public UsbCamera(string path, string name = "")
        {
            Path = path;
            _usbCameraName = name;
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice) ?? Array.Empty<DsDevice>();
            _usbCamera = devices.FirstOrDefault(n => n.DevicePath == path)
                         ?? throw new ArgumentException("Can not find camera", nameof(path));

            if (string.IsNullOrEmpty(_usbCameraName))
                _usbCameraName = _usbCamera.Name;
            if (string.IsNullOrEmpty(_usbCameraName))
                _usbCameraName = path;

            Description = new CameraDescription(CameraType.USB, Path, _usbCameraName, GetAllAvailableResolution(_usbCamera));
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
                            v.AvgTimePerFrame / 10000.0));
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

                if (_usbCamera == null)
                    return false;

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

            return new VideoCapture(camNumber, CaptureSource);
        }

        private void ImageCaptured(object sender, EventArgs args)
        {
            if (Monitor.IsEntered(_getPictureThreadLock))
            {
                return;
            }

            lock (_getPictureThreadLock)
            {
                _image?.Dispose();
                try
                {
                    if (!(_captureDevice?.Grab() ?? false))
                        return;

                    if (!_captureDevice.Retrieve(_frame))
                        return;

                    _image = _frame.ToBitmap();
                    if (_image != null)
                    {
                        ImageCapturedEvent?.Invoke(this, _image);
                    }
                }
                catch
                {
                    Stop();
                }
            }
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            _captureDevice?.Stop();
            _captureDevice?.Dispose();
            _captureDevice = null;
            _cancellationTokenSource?.Cancel();
            _image?.Dispose();
            IsRunning = false;
        }

        public async Task Stop(CancellationToken token)
        {
            Stop();
        }

        public async Task<Bitmap?> GrabFrame(CancellationToken token)
        {
            if (IsRunning)
            {
                while (IsRunning && _image == null && !token.IsCancellationRequested) ;
                lock (_getPictureThreadLock)
                {
                    return (Bitmap?)_image?.Clone();
                }
            }

            Bitmap? image = null;
            await Task.Run(() =>
                {
                    _captureDevice = GetCaptureDevice();
                    if (_captureDevice == null)
                        return;

                    if (_captureDevice.Grab() && _captureDevice.Retrieve(_frame))
                    {
                        image = _frame.ToBitmap();
                    }
                    _captureDevice.Stop();
                    _captureDevice.Dispose();
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
                    _usbCamera?.Dispose();
                    _captureDevice?.Dispose();
                    _cancellationTokenSource?.Dispose();
                    _frame.Dispose();
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
