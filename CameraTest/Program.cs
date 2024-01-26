using CameraLib.IP;
using CameraLib.USB;

namespace CameraTest
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            var usbCameraList = UsbCamera.DiscoverUsbCameras();
            var usbCamera = new UsbCamera(usbCameraList.FirstOrDefault().Id);

            await usbCamera.Start(0, 0, "", CancellationToken.None);
            var usbImage = await usbCamera.GrabFrame(CancellationToken.None);
            usbCamera.Stop();
            usbImage.Save("usbImage.bmp");

            var ipCameraList = await IpCamera.DiscoverOnvifCamerasAsync(1000, CancellationToken.None);
            var ipCamera = new IpCamera(ipCameraList.FirstOrDefault()?.Id ?? "");

            await ipCamera.Start(0, 0, "", CancellationToken.None);
            var ipImage = await ipCamera.GrabFrame(CancellationToken.None);
            ipCamera.Stop();
            ipImage.Save("ipImage.bmp");
        }
    }
}
