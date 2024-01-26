﻿using Emgu.CV;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace CameraLib.IP
{
    public class IpCamera : ICamera
    {
        public bool IsRunning { get; private set; } = false;

        public string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(_ipCameraName))
                    return _ipCameraName;

                return Id;
            }
            set => _ipCameraName = value;
        }
        public string Id { get; }

        public List<FrameFormat> Capabilities { get; }

        public event ICamera.ImageCapturedEventHandler? ImageCapturedEvent;

        private readonly object _getPictureThreadLock = new object();
        private VideoCapture? _captureDevice; //create a usbCamera capture
        private string _ipCameraName;
        private readonly Mat _frame = new Mat();
        private bool _disposedValue;

        public IpCamera(string ipCameraId, string name = "")
        {
            Id = ipCameraId;
            _ipCameraName = string.IsNullOrEmpty(name) ? Dns.GetHostAddresses(new Uri(Id).Host).FirstOrDefault()?.ToString() ?? Id : name;

            Capabilities = new List<FrameFormat>();
        }

        public static async Task<List<CameraDescription>> DiscoverOnvifCamerasAsync(int discoveryTimeout, CancellationToken token)
        {
            var result = new ConcurrentBag<CameraDescription>();

            var entry = await Dns.GetHostEntryAsync(Dns.GetHostName());

            var hostIps = new List<IPAddress>();
            foreach (var ip in entry.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    var bytes = ip.GetAddressBytes();
                    bytes[3] = byte.MaxValue;
                    hostIps.Add(new IPAddress(bytes));
                }
            }

            using (var client = new UdpClient())
            {
                foreach (var ip in hostIps)
                {
                    var ipEndpoint = new IPEndPoint(ip, 3702);
                    client.EnableBroadcast = true;
                    try
                    {
                        var soapMessage = Encoding.ASCII.GetBytes(CreateSoapRequest());
                        var timeout = DateTime.Now.AddMilliseconds(discoveryTimeout);
                        await client.SendAsync(soapMessage, soapMessage.Length, ipEndpoint);

                        while (timeout > DateTime.Now)
                        {
                            if (client.Available > 0)
                            {
                                var receiveResult = await client.ReceiveAsync();
                                var text = Encoding.ASCII.GetString(receiveResult.Buffer, 0,
                                receiveResult.Buffer.Length);
                                var cameraPath = GetAddress(text);
                                var ipAddress = await Dns.GetHostAddressesAsync(new Uri(cameraPath).Host);

                                if (ipAddress.Any())
                                    result.Add(new CameraDescription(CameraType.IP,
                                    $"rtsp://{ipAddress.FirstOrDefault()}:554/user=admin_password=_channel=1_stream=0.sdp?real_stream",
                                    ipAddress.FirstOrDefault()?.ToString() ?? ""));
                            }
                            else
                            {
                                await Task.Delay(10, token);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine(exception.Message);
                    }
                }
            }

            return result.ToList();
        }

        public List<CameraDescription> DiscoverCamerasAsync(int discoveryTimeout, CancellationToken token)
        {
            return DiscoverOnvifCamerasAsync(discoveryTimeout, token).Result;
        }

        public async Task<bool> Start(int x, int y, string format, CancellationToken token)
        {
            if (!IsRunning)
            {
                _captureDevice = await GetCaptureDevice(token);
                if (_captureDevice == null)
                {
                    IsRunning = false;

                    return false;
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

        public void Stop(CancellationToken _)
        {
            Stop();
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            _captureDevice?.Stop();
            _captureDevice?.Dispose();
            IsRunning = false;
        }

        public async Task<Image?> GrabFrame(CancellationToken token)
        {
            Image image = null;
            await Task.Run(() =>
            {
                if (_captureDevice?.Grab() ?? false)
                    if (_captureDevice.Retrieve(_frame))
                    {
                        image = _frame.ToBitmap();
                    }
            }, token);

            return image;
        }

        public async IAsyncEnumerable<Image> GrabFrames([EnumeratorCancellation] CancellationToken token)
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

        private static string CreateSoapRequest()
        {
            Guid messageId = Guid.NewGuid();
            const string soap = @"
            <?xml version=""1.0"" encoding=""UTF-8""?>
            <e:Envelope xmlns:e=""http://www.w3.org/2003/05/soap-envelope""
                xmlns:w=""http://schemas.xmlsoap.org/ws/2004/08/addressing""
                xmlns:d=""http://schemas.xmlsoap.org/ws/2005/04/discovery""
                xmlns:dn=""http://www.onvif.org/ver10/device/wsdl"">
                <e:Header>
                    <w:MessageID>uuid:{0}</w:MessageID>
                    <w:To e:mustUnderstand=""true"">urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>
                    <w:Action a:mustUnderstand=""true"">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>
                </e:Header>
                <e:Body>
                    <d:Probe>
                        <d:Types>dn:Device</d:Types>
                    </d:Probe>
                </e:Body>
            </e:Envelope>
            ";

            var result = string.Format(soap, messageId);
            return result;
        }

        private static string GetAddress(string soapMessage)
        {
            var xmlNamespaceManager = new XmlNamespaceManager(new NameTable());
            xmlNamespaceManager.AddNamespace("g", "http://schemas.xmlsoap.org/ws/2005/04/discovery");

            var element = XElement.Parse(soapMessage).XPathSelectElement("//g:XAddrs[1]", xmlNamespaceManager);
            return element?.Value ?? string.Empty;
        }

        private async Task<VideoCapture?> GetCaptureDevice(CancellationToken token)
        {
            return await Task.Run(() => new VideoCapture(Id), token);
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
