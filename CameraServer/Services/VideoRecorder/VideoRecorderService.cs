using CameraServer.Auth;
using CameraServer.Models;

using Emgu.CV;

using System.Collections.Concurrent;
using System.Diagnostics;

namespace CameraServer.Services.VideoRecorder
{
    public class VideoRecorderService : IHostedService, IDisposable
    {
        private const string RecorderConfigSection = "Recorder";

        private readonly IUserManager _manager;
        private readonly CameraHub.CameraHubService _collection;
        private readonly RecorderSettings _settings;

        private readonly Dictionary<string, Task> _recorderTasks = new Dictionary<string, Task>();

        private bool _disposedValue;

        public VideoRecorderService(IConfiguration configuration, IUserManager manager, CameraHub.CameraHubService collection)
        {
            _manager = manager;
            _collection = collection;
            _settings = configuration.GetSection(RecorderConfigSection)?.Get<RecorderSettings>() ?? new RecorderSettings();
            Directory.CreateDirectory(_settings.StoragePath);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var record in _settings.RecordCameras)
            {
                Start(record.CameraId, record.User, record.Width, record.Height);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
        }

        public string Start(string cameraId, string user, int width = 0, int height = 0, string format = "")
        {
            if (!_settings.AutorizedUsers.Any(n => n.Equals(user, StringComparison.OrdinalIgnoreCase)))
                return "";

            var userDto = _manager.GetUserInfo(user);
            ServerCamera camera;
            try
            {
                camera = _collection.GetCamera(cameraId, userDto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding camera: {ex.Message}");

                return "";
            }

            var userUid = $"{user}{DateTime.Now.Ticks}";
            var taskId = camera.Camera.Description.Path + userUid + width + height;
            var t = new Task(async () =>
            {
                var imageQueue = new ConcurrentQueue<Mat>();
                var cameraCancellationToken = await _collection.HookCamera(camera.Camera.Description.Path,
                    userUid,
                    imageQueue,
                    width,
                    height,
                    format);
                if (cameraCancellationToken == CancellationToken.None)
                    throw new ApplicationException($"Can not connect to camera [{cameraId}]");

                //record video
                try
                {
                    var avgFps = camera.Camera.Description.FrameFormats.FirstOrDefault()?.Fps ?? -1;
                    var stopTask = !_recorderTasks.TryGetValue(taskId, out _);
                    while (!cameraCancellationToken.IsCancellationRequested && !stopTask)
                    {
                        var currentTime = DateTime.Now;
                        var fileName = $"{_settings.StoragePath}\\{VideoRecorder.SanitizeFileName($"{camera.Camera.Description.Name}-{width}x{height}-{currentTime.ToShortDateString()}-{currentTime.ToLongTimeString()}.mp4")}";
                        var timer = new Stopwatch();
                        using (var recorder = new VideoRecorder(fileName, 0, 0, fps: avgFps, _settings.VideoQuality))
                        {
                            var timeOut = DateTime.Now.AddSeconds(_settings.VideoFileLengthSeconds);
                            var n = 0;
                            while (DateTime.Now < timeOut && !cameraCancellationToken.IsCancellationRequested && !stopTask)
                            {
                                if (imageQueue.TryDequeue(out var image))
                                {
                                    if (image == null)
                                        continue;

                                    if (!timer.IsRunning)
                                        timer.Start();
                                    else
                                    {
                                        //Console.WriteLine(n / (timer.ElapsedMilliseconds / 1000));
                                    }
                                    n++;

                                    recorder.SaveFrame(image);
                                    image.Dispose();
                                }
                                else
                                    await Task.Delay(10, CancellationToken.None);

                                stopTask = !_recorderTasks.TryGetValue(taskId, out _);
                            }
                            timer.Stop();
                            avgFps = (double)n / ((double)timer.ElapsedMilliseconds / (double)1000);
                            Console.WriteLine($"Avg fps: {avgFps}");
                            timer.Reset();

                            recorder.Stop();

                            stopTask = !_recorderTasks.TryGetValue(taskId, out _);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception while video file recording: {ex}");
                }

                await _collection.UnHookCamera(camera.Camera.Description.Path, userUid);
                while (imageQueue.TryDequeue(out var image))
                {
                    image?.Dispose();
                }

                imageQueue.Clear();

                _recorderTasks.Remove(taskId);
            });

            _recorderTasks.TryAdd(taskId, t);
            t.Start();

            return taskId;
        }

        public void Stop(string taskId)
        {
            _recorderTasks.Remove(taskId);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    foreach (var k in _recorderTasks.Select(n => n.Key).ToArray())
                        Stop(k);
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
