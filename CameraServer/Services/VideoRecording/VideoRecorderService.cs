using CameraLib;

using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.CameraHub;

using Emgu.CV;

using System.Collections.Concurrent;

namespace CameraServer.Services.VideoRecording
{
    public class VideoRecorderService : IHostedService, IDisposable
    {
        private const string RecorderConfigSection = "Recorder";
        private const string RecorderStreamId = "Recorder";

        private readonly IUserManager _manager;
        private readonly CameraHubService _collection;
        public readonly RecorderSettings Settings;

        public IEnumerable<string> TaskList => _recorderTasks.Select(n => n.Key);
        private readonly ConcurrentDictionary<string, Task> _recorderTasks = new();

        private bool _disposedValue;

        public VideoRecorderService(IConfiguration configuration, IUserManager manager, CameraHubService collection)
        {
            _manager = manager;
            _collection = collection;
            Settings = configuration.GetSection(RecorderConfigSection)?.Get<RecorderSettings>() ?? new RecorderSettings();
            Directory.CreateDirectory(Settings.StoragePath);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var record in Settings.RecordCameras)
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
                quality = Settings.DefaultVideoQuality;

            var taskId = GenerateTaskId(camera.Camera.Description.Path, frameFormat.Width, frameFormat.Height);
            var t = new Task(async () => await RecordingTask(camera, frameFormat, taskId, quality));
            _recorderTasks.TryAdd(taskId, t);
            t.Start();

            return taskId;
        }

        public void Stop(string taskId)
        {
            if (_recorderTasks.TryRemove(taskId, out var t))
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
            var stopTask = false;
            while (!cameraCancellationToken.IsCancellationRequested && !stopTask)
            {
                var currentTime = DateTime.Now;
                var fileName = $"{Settings.StoragePath}\\" +
                               $"{VideoRecorder.SanitizeFileName($"{camera.Camera.Description.Name}-" +
                                                                 $"{frameFormat.Width}x{frameFormat.Height}-" +
                                                                 $"{currentTime.ToString("yyyy-MM-dd")}-" +
                                                                 $"{currentTime.ToString("HH-mm-ss")}.mp4")}";
                using (var recorder = new VideoRecorder(fileName,
                           new FrameFormatDto { Width = 0, Height = 0, Format = string.Empty, Fps = camera.Camera.CurrentFps },
                           quality))
                {
                    var timeOut = DateTime.Now.AddSeconds(Settings.VideoFileLengthSeconds);
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

                            image?.Dispose();
                        }
                        else
                            await Task.Delay(10, CancellationToken.None);

                        stopTask = !_recorderTasks.TryGetValue(taskId, out _);
                    }
                }

                stopTask = !_recorderTasks.TryGetValue(taskId, out _);
            }

            await _collection.UnHookCamera(camera.Camera.Description.Path, RecorderStreamId, frameFormat);

            while (imageQueue.TryDequeue(out var image))
            {
                image?.Dispose();
            }
            imageQueue.Clear();

            _recorderTasks.TryRemove(taskId, out _);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);
        }

        public async Task<string> RecordVideoFile(ServerCamera camera,
            string streamId,
            string filePrefix,
            uint recordLengthSec,
            FrameFormatDto? frameFormat = null,
            byte quality = 90)
        {
            var currentTime = DateTime.Now;
            frameFormat ??= new FrameFormatDto();
            var tmpImageQueue = new ConcurrentQueue<Mat>();
            var tmpCameraCancellationToken = await _collection.HookCamera(camera.Camera.Description.Path,
                streamId,
                tmpImageQueue,
                frameFormat);
            if (tmpCameraCancellationToken == CancellationToken.None)
            {
                throw new ApplicationException($"Can not connect to camera#{camera.Camera.Description.Name}");
            }

            var fileName = VideoRecorder.SanitizeFileName(
                    $"{filePrefix}-" +
                    $"Cam{camera.Camera.Description.Name}-" +
                    $"{streamId}-" +
                    $"{currentTime.ToString("yyyy-MM-dd")}-" +
                    $"{currentTime.ToString("HH-mm-ss")}.mp4");

            frameFormat.Fps = camera.Camera.CurrentFps;

            using (var recorder = new VideoRecorder(fileName, frameFormat, quality))
            {
                var timeOut = DateTime.Now.AddSeconds(recordLengthSec);
                while (DateTime.Now < timeOut)
                {
                    if (tmpImageQueue.TryDequeue(out var image))
                    {
                        try
                        {
                            recorder.SaveFrame(image);
                        }
                        catch (Exception ex)
                        {
                            timeOut = DateTime.Now;
                        }
                        finally
                        {
                            image?.Dispose();
                        }
                    }
                    else
                        await Task.Delay(10, CancellationToken.None);
                }

                await _collection.UnHookCamera(
                    camera.Camera.Description.Path,
                    streamId, frameFormat);
            }

            while (tmpImageQueue.TryDequeue(out var image))
            {
                image.Dispose();
            }

            tmpImageQueue.Clear();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);

            if (!File.Exists(fileName))
                throw new ApplicationException($"Can't write file {fileName}");

            return fileName;
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
