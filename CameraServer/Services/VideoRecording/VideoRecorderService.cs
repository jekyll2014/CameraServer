using CameraLib;

using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.CameraHub;

using System.Collections.Concurrent;
using OpenCvSharp;

namespace CameraServer.Services.VideoRecording
{
    public class VideoRecorderService : IHostedService, IDisposable
    {
        private const string VideoRecorderTempConfig = "appsettings-recorder";
        private const string RecorderConfigSection = "Recorder";
        private const string RecorderStreamId = "Recorder";
        private const string DefaultVideoFileExtencion = "mp4";

        private readonly IUserManager _manager;
        private readonly CameraHubService _collection;
        public readonly RecorderSettings Settings;
        public readonly Config<List<RecordCameraSettingDto>> TaskConfig = new Config<List<RecordCameraSettingDto>>(VideoRecorderTempConfig);

        public IEnumerable<string> TaskList => _recorderTasks.Select(n => n.Key.TaskId);
        private readonly ConcurrentDictionary<RecordCameraTask, Task> _recorderTasks = new();

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
                    Start(record);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Can't start recording: {ex}");
                }
            }

            var startUpTasks = TaskConfig.ConfigStorage.ToArray();
            TaskConfig.ConfigStorage.Clear();
            foreach (var record in startUpTasks)
            {
                try
                {
                    Console.WriteLine($"Restoring recording for: {record.CameraId}");

                    if (string.IsNullOrEmpty(Start(record)))
                    {
                        throw new Exception("Recording not restored");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Can't restore recording: {ex}");
                }
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
        }

        public string Start(RecordCameraSettingDto recordTask)
        {
            var userDto = _manager.GetUserInfo(recordTask.User);
            if (userDto == null)
                throw new ApplicationException($"User [{recordTask.User}] not authorised to start recording.");

            if (recordTask.Quality <= 0)
                recordTask.Quality = Settings.DefaultVideoQuality;

            ServerCamera camera;
            try
            {
                camera = _collection.GetCamera(recordTask.CameraId, userDto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding camera: {ex.Message}");
                throw new ApplicationException($"User [{recordTask.User}] not authorised to start recording.");
            }

            var taskId = GenerateTaskId(camera.CameraStream.Description.Path, recordTask.FrameFormat.Width, recordTask.FrameFormat.Height);
            var task = new RecordCameraTask(recordTask)
            {
                TaskId = taskId
            };

            var t = new Task(async () => await RecordingTask(task));

            if (_recorderTasks.TryAdd(task, t))
            {
                t.Start();

                var existingTask = TaskConfig.ConfigStorage.FirstOrDefault(n => n.Equals(task));
                if (existingTask == null)
                {
                    TaskConfig.ConfigStorage.Add(task);
                }

                TaskConfig.SaveConfig();
            }
            else
                taskId = string.Empty;

            return taskId;
        }

        public void Stop(string taskId)
        {
            var task = _recorderTasks.FirstOrDefault(n => n.Key.TaskId == taskId);
            if (task.Key != null)
                Stop(task.Key);
        }

        public void Stop(RecordCameraTask recordTask)
        {
            if (_recorderTasks.TryRemove(recordTask, out var t))
            {
                var existingTask = TaskConfig.ConfigStorage.FirstOrDefault(n => n.Equals(recordTask));
                if (existingTask != null)
                {
                    TaskConfig.ConfigStorage.Remove(existingTask);
                }

                TaskConfig.SaveConfig();

                t.Wait(5000);
                t.Dispose();
            }
        }

        public static string GenerateTaskId(string cameraPath, int width, int height)
        {
            return cameraPath + width + height;
        }

        private async Task RecordingTask(RecordCameraTask newTask)
        {
            var userDto = _manager.GetUserInfo(newTask.User);
            ServerCamera camera;
            try
            {
                camera = _collection.GetCamera(newTask.CameraId, userDto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding camera: {ex.Message}");
                throw new ApplicationException($"User [{newTask.User}] not authorised to start recording.");
            }

            var newCameraItem = new CameraQueueItem(camera.CameraStream.Description.Path,
                RecorderStreamId,
                newTask.FrameFormat);

            var imageQueue = new ConcurrentQueue<Mat>();
            var cameraCancellationToken = await _collection.HookCamera(newCameraItem, imageQueue);

            if (cameraCancellationToken == CancellationToken.None)
            {
                Console.WriteLine($"Can not connect to camera [{camera.CameraStream.Description.Path}]");

                return;
            }

            var stopTask = false;
            while (!cameraCancellationToken.IsCancellationRequested && !stopTask)
            {
                var currentTime = DateTime.Now;
                var fileName = $"{Settings.StoragePath.TrimEnd('\\')}\\" +
                               $"{VideoRecorder.SanitizeFileName($"{camera.CameraStream.Description.Name}-{newTask.FrameFormat.Width}x{newTask.FrameFormat.Height}-{currentTime.ToString("yyyy-MM-dd")}-{currentTime.ToString("HH-mm-ss")}.mp4")}";
                using (var recorder = new VideoRecorder(fileName,
                           new FrameFormatDto { Width = 0, Height = 0, Format = string.Empty, Fps = camera.CameraStream.CurrentFps },
                           newTask.Quality))
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

                        stopTask = !_recorderTasks.TryGetValue(newTask, out _);
                    }
                }

                stopTask = !_recorderTasks.TryGetValue(newTask, out _);
            }

            _collection.UnHookCamera(newCameraItem);

            while (imageQueue.TryDequeue(out var image))
            {
                image?.Dispose();
            }
            imageQueue.Clear();

            _recorderTasks.TryRemove(newTask, out _);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        }

        public async Task<string> RecordVideoFile(ServerCamera camera,
            string streamId,
            string fileStoragePath,
            string filePrefix,
            uint recordLengthSec,
            FrameFormatDto? frameFormat = null,
            byte quality = 90,
            Mat?[]? imageBuffer = null)
        {
            var currentTime = DateTime.Now;

            imageBuffer ??= Array.Empty<Mat?>();

            frameFormat ??= new FrameFormatDto();
            var newCameraItem = new CameraQueueItem(camera.CameraStream.Description.Path,
                streamId,
                frameFormat);

            var tmpImageQueue = new ConcurrentQueue<Mat>();
            var tmpCameraCancellationToken = await _collection.HookCamera(newCameraItem, tmpImageQueue);
            if (tmpCameraCancellationToken == CancellationToken.None)
            {
                throw new ApplicationException($"Can not connect to camera#{camera.CameraStream.Description.Name}");
            }

            var fileName = VideoRecorder.SanitizeFileName(
                    $"{fileStoragePath.TrimEnd('\\')}\\" +
                    $"{filePrefix}-" +
                    $"Cam{camera.CameraStream.Description.Name}-" +
                    $"{streamId}-" +
                    $"{currentTime.ToString("yyyy-MM-dd")}_" +
                    $"{currentTime.ToString("HH-mm-ss")}.{DefaultVideoFileExtencion}");

            if (frameFormat.Fps <= 0)
                frameFormat.Fps = camera.CameraStream.CurrentFps;

            using (var recorder = new VideoRecorder(fileName, frameFormat, quality))
            {
                if (imageBuffer.Length > 0)
                {
                    foreach (var img in imageBuffer)
                    {
                        if (img != null && !img.IsDisposed)
                        {
                            recorder.SaveFrame(img);
                            img.Dispose();
                        }
                    }
                }

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

                _collection.UnHookCamera(newCameraItem);
            }

            while (tmpImageQueue.TryDequeue(out var image))
            {
                image.Dispose();
            }

            tmpImageQueue.Clear();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);

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
