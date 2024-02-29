using CameraLib.FlashCap;
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
            var usbCameraFc = new UsbCameraFc(usbCameraFcList.FirstOrDefault()?.Path ?? "");
            await usbCameraFc.Start(0, 0, "", CancellationToken.None);
            var usbFcImage = await usbCameraFc.GrabFrame(CancellationToken.None);
            usbCameraFc.Stop();
            usbFcImage?.Save("usbFcImage.bmp");

            var usbCameraList = UsbCamera.DiscoverUsbCameras();
            var usbCamera = new UsbCamera(usbCameraList.FirstOrDefault()?.Path ?? "");
            await usbCamera.Start(0, 0, "", CancellationToken.None);
            var usbImage = await usbCamera.GrabFrame(CancellationToken.None);
            usbCamera.Stop();
            usbImage?.Save("usbImage.bmp");

            var ipCameraList = await IpCamera.DiscoverOnvifCamerasAsync(1000, CancellationToken.None);
            var ipCamera = new IpCamera(ipCameraList.FirstOrDefault()?.Path ?? "");
            await ipCamera.Start(0, 0, "", CancellationToken.None);
            var ipImage = await ipCamera.GrabFrame(CancellationToken.None);
            ipCamera.Stop();
            ipImage?.Save("ipImage.bmp");
        }
    }
}
