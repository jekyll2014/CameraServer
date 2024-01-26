
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;

namespace CameraServer.StreamHelpers
{
    static class StreamExtention
    {
        internal static async IAsyncEnumerable<MemoryStream> Streams(this IAsyncEnumerable<Image> source, [EnumeratorCancellation] CancellationToken token)
        {
            using (var imageStream = new MemoryStream())
            {
                await foreach (var img in source.WithCancellation(token))
                {
                    if (token.IsCancellationRequested)
                        break;

                    imageStream.SetLength(0);
                    img.Save(imageStream, ImageFormat.Jpeg);

                    yield return imageStream;
                }
            }
        }
    }
}