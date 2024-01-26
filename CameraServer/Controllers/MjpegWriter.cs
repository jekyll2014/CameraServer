using System.Drawing;
using System.Drawing.Imaging;
using System.Text;

// -------------------------------------------------
// Developed By : Ragheed Al-Tayeb
// e-Mail       : ragheedemail@gmail.com
// Date         : April 2012
// -------------------------------------------------

namespace CameraServer.Streaming
{
    /// <summary>
    ///     Provides a stream writer that can be used to write images as MJPEG
    ///     or (Motion JPEG) to any stream.
    /// </summary>
    public class MjpegWriter : IDisposable
    {
        public string Boundary { get; }
        public Stream Stream { get; private set; }
        public const string DefaultBoundaryValue = "--boundary";

        public MjpegWriter(Stream stream, string boundary = DefaultBoundaryValue)
        {
            Stream = stream;
            Boundary = boundary;
        }

        public async Task WriteHeader()
        {
            await Write(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: multipart/x-mixed-replace; boundary=" + Boundary + "\r\n"
            );

            await Stream.FlushAsync();
        }

        public async Task Write(Bitmap image)
        {
            var ms = BytesOf(image);
            await Write(ms);
        }

        public async Task Write(MemoryStream imageStream)
        {
            var sb = new StringBuilder();

            sb.AppendLine();
            sb.AppendLine(Boundary);
            sb.AppendLine("Content-Type: image/jpeg");
            sb.AppendLine("Content-Length: " + imageStream.Length);
            sb.AppendLine();

            await Write(sb.ToString());
            imageStream.Position = 0;
            await imageStream.CopyToAsync(Stream);
            imageStream.Position = 0;
            await Write("\r\n");

            await Stream.FlushAsync();
        }

        private async Task Write(byte[] data)
        {
            await Stream.WriteAsync(data);
        }

        private async Task Write(string text)
        {
            var data = BytesOf(text);
            await Stream.WriteAsync(data);
        }

        private static byte[] BytesOf(string text)
        {
            return Encoding.ASCII.GetBytes(text);
        }

        private static MemoryStream BytesOf(Bitmap image)
        {
            var ms = new MemoryStream();
            image.Save(ms, ImageFormat.Jpeg);
            return ms;
        }

        public async Task<string> ReadRequest(int length)
        {
            var data = new byte[length];
            var count = await Stream.ReadAsync(data, 0, data.Length);

            return count != 0 ? Encoding.ASCII.GetString(data, 0, count) : null;
        }

        #region IDisposable Members

        public void Dispose()
        {
            try
            {
                if (Stream != null)
                    Stream.Dispose();
            }
            finally
            {
                Stream = null;
            }
        }

        #endregion
    }
}
