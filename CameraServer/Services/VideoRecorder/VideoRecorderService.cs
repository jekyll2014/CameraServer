using CameraLib;

using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.CameraHub;

using Emgu.CV;

using System.Collections.Concurrent;
using System.Diagnostics;

namespace CameraServer.Services.VideoRecorder
{
    public class VideoRecorderService : IHostedService, IDisposable
    {
        private const string RecorderConfigSection = "Recorder";
        private const string RecorderStreamId = "Recorder";

        private readonly IUserManager _manager;
        private readonly CameraHubService _collection;
        private readonly RecorderSettings _settings;

        public IEnumerable<string> TaskList => _recorderTasks.Select(n => n.Key);
        private readonly Dictionary<string, Task> _recorderTasks = new Dictionary<string, Task>();

        private bool _disposedValue;

        public VideoRecorderService(IConfiguration configuration, IUserManager manager, CameraHubService collection)
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
                try
                {
                    Console.WriteLine($"Starting recording for: {record.CameraId}");
                    Start(record.CameraId, record.User,
                        new FrameFormatDto
                        {
                            Width = record.Width,
                            Height = record.Height,
                            Format = record.CameraFrameFormat,
                            Fps = record.Fps
                        },
                        record.Quality);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Can't start recording: {ex}");
                }
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
        }

        public string Start(string cameraId, string user, FrameFormatDto frameFormat, byte quality = 0)
        {
            var userDto = _manager.GetUserInfo(user);
            if (userDto == null)
                throw new ApplicationException($"User [{user}] not authorised to start recording.");

            ServerCamera camera;
            try
            {
                camera = _collection.GetCamera(cameraId, userDto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding camera: {ex.Message}");
                throw new ApplicationException($"User [{user}] not authorised to start recording.");
            }

            if (quality <= 0)
                quality = _settings.DefaultVideoQuality;

            var taskId = GenerateTaskId(camera.Camera.Description.Path, frameFormat.Width, frameFormat.Height);
            var t = new Task(async () => await RecordingTask(camera, frameFormat, taskId, quality));
            //.ContinueWith(n => _recorderTasks.Remove(taskId));
            _recorderTasks.TryAdd(taskId, t);
            t.Start();

            return taskId;
        }

        public void Stop(string taskId)
        {
            if (_recorderTasks.Remove(taskId, out var t))
            {
                t.Wait(5000);
                t.Dispose();
            }
        }

        public static string GenerateTaskId(string cameraPath, int width, int height)
        {
            return cameraPath + width + height;
        }

        private async Task RecordingTask(ServerCamera camera, FrameFormatDto frameFormat, string taskId, byte quality)
        {
            var avgFps = frameFormat.Fps > 0
                ? frameFormat.Fps
                : camera.Camera.Description.FrameFormats.FirstOrDefault()?.Fps ?? 0;
            var stopTask = false;
            var timer = new Stopwatch();
            var frameCount = 0;
            var imageQueue = new ConcurrentQueue<Mat>();
            var cameraCancellationToken = await _collection.HookCamera(camera.Camera.Description.Path,
                RecorderStreamId,
                imageQueue,
                frameFormat);

            if (cameraCancellationToken == CancellationToken.None)
            {
                Console.WriteLine($"Can not connect to camera [{camera.Camera.Description.Path}]");

                return;
            }

            //record video
            while (!cameraCancellationToken.IsCancellationRequested && !stopTask)
            {
                var currentTime = DateTime.Now;
                var fileName = $"{_settings.StoragePath}\\" +
                               $"{VideoRecorder.SanitizeFileName($"{camera.Camera.Description.Name}-" +
                                                                 $"{frameFormat.Width}x{frameFormat.Height}-" +
                                                                 $"{currentTime.ToString("yyyy-MM-dd")}-" +
                                                                 $"{currentTime.ToString("HH-mm-ss")}.mp4")}";
                using (var recorder = new VideoRecorder(fileName,
                           new FrameFormatDto { Width = 0, Height = 0, Format = string.Empty, Fps = avgFps },
                           quality))
                {
                    timer.Reset();
                    var timeOut = DateTime.Now.AddSeconds(_settings.VideoFileLengthSeconds);
                    while (DateTime.Now < timeOut && !cameraCancellationToken.IsCancellationRequested &&
                           !stopTask)
                    {
                        if (imageQueue.TryDequeue(out var image))
                        {
                            try
                            {
                                recorder.SaveFrame(image);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Exception while video file recording: {ex}");
                            }

                            image.Dispose();
                            if (!timer.IsRunning)
                            {
                                timer.Start();
                                frameCount = 0;
                            }
                            else
                                frameCount++;

                        }
                        else
                            await Task.Delay(10, CancellationToken.None);

                        stopTask = !_recorderTasks.TryGetValue(taskId, out _);
                    }

                    timer.Stop();
                    if (timer.ElapsedMilliseconds > 0)
                        avgFps = (double)frameCount / ((double)timer.ElapsedMilliseconds / (double)1000);
                }

                stopTask = !_recorderTasks.TryGetValue(taskId, out _);
            }

            await _collection.UnHookCamera(camera.Camera.Description.Path, RecorderStreamId, frameFormat);

            while (imageQueue.TryDequeue(out var image))
            {
                image?.Dispose();
            }
            imageQueue.Clear();

            _recorderTasks.Remove(taskId);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);
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
