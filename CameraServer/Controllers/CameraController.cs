
using CameraServer.StreamHelpers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System.Collections.Concurrent;
using System.Drawing;
using System.Net;

using HttpGetAttribute = Microsoft.AspNetCore.Mvc.HttpGetAttribute;

namespace CameraServer.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class CameraController : ControllerBase
    {
        //private string BOUNDARY = "frame"; //"--boundary"
        private const string Boundary = "--boundary";
        private readonly CamerasCollection _collection;

        public CameraController(CamerasCollection collection)
        {
            _collection = collection;
        }

        [HttpPost]
        [Route("RefreshCamerasList")]
        public async Task RefreshCamerasList()
        {
            await _collection.RefreshCamerasCollection();
        }

        [HttpGet]
        [Route("GetCameras")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(Dictionary<int, string>))]
        public IActionResult GetCameras()
        {
            var i = 0;
            return Ok(_collection.Cameras.Select(n => new Dictionary<int, string>() { { i++, n.Name } }));
        }

        [HttpGet]
        [Route("GetVideoContent")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(MemoryStream))]
        public async Task<IActionResult> GetVideoContent(int cameraNumber, int xResolution = 0, int yResolution = 0, string format = "")
        {
            var imageQueue = new ConcurrentQueue<Bitmap>();
            var id = _collection.Cameras.ToArray()[cameraNumber].Id;
            if (string.IsNullOrEmpty(id))
                return Problem("Can not find camera#", cameraNumber.ToString(), StatusCodes.Status204NoContent);

            if (!_collection.HookCamera(id, Request.HttpContext.TraceIdentifier, imageQueue, xResolution, yResolution, format))
                return Problem("Can not connect to camera#", cameraNumber.ToString(), StatusCodes.Status204NoContent);

            Response.ContentType = "multipart/x-mixed-replace; boundary=" + Boundary;
            using (var wr = new MjpegWriter(Response.Body))
            {
                while (!Response.HttpContext.RequestAborted.IsCancellationRequested)
                {
                    if (imageQueue.TryDequeue(out var image))
                    {
                        await wr.Write(image);
                        image.Dispose();
                    }

                    await Task.Delay(10);
                }
            }

            _collection.UnHookCamera(id, Request.HttpContext.TraceIdentifier, imageQueue);
            while (imageQueue.TryDequeue(out var image))
            {
                image.Dispose();
            }

            imageQueue.Clear();

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);

            return Ok();
        }
    }
}
