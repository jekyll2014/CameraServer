﻿using CameraLib.FlashCap;
using CameraLib.IP;
using CameraLib.USB;

namespace CameraTest
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            var usbCameraFcList = UsbCameraFc.DiscoverUsbCameras();
            var usbCameraFc = new UsbCameraFc(usbCameraFcList.FirstOrDefault()?.Path ?? string.Empty);
            await usbCameraFc.Start(0, 0, string.Empty, CancellationToken.None);
            var usbFcImage = await usbCameraFc.GrabFrame(CancellationToken.None);
            usbCameraFc.Stop();
            usbFcImage?.SaveImage("usbFcImage.bmp");

            var usbCameraList = UsbCamera.DiscoverUsbCameras();
            var usbCamera = new UsbCamera(usbCameraList.FirstOrDefault()?.Path ?? string.Empty);
            await usbCamera.Start(0, 0, string.Empty, CancellationToken.None);
            var usbImage = await usbCamera.GrabFrame(CancellationToken.None);
            usbCamera.Stop();
            usbImage?.SaveImage("usbImage.bmp");

            var ipCameraList = await IpCamera.DiscoverOnvifCamerasAsync(1000);
            var ipCamera = new IpCamera(ipCameraList.FirstOrDefault()?.Path ?? string.Empty);
            await ipCamera.Start(0, 0, string.Empty, CancellationToken.None);
            var ipImage = await ipCamera.GrabFrame(CancellationToken.None);
            ipCamera.Stop();
            ipImage?.SaveImage("ipImage.bmp");
        }
    }
}
