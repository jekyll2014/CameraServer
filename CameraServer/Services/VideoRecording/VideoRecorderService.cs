using CameraLib;

using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.CameraHub;

using Emgu.CV;

using System.Collections.Concurrent;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CameraServer.Services.VideoRecording
{
    public class VideoRecorderService : IHostedService, IDisposable
    {
        private const string VideoRecorderTempConfig = "appsettings-recorder.json";
        private const string RecorderConfigSection = "Recorder";
        private const string RecorderStreamId = "Recorder";
        private const string DefaultVideoFileExtencion = "mp4";

        private readonly IUserManager _manager;
        private readonly CameraHubService _collection;
        private readonly ILogger<VideoRecorderService> _logger;
        public readonly RecorderSettings _settings;
        public readonly Config<List<RecordCameraSettingDto>> TaskConfig = new Config<List<RecordCameraSettingDto>>(VideoRecorderTempConfig);

        public IEnumerable<string> TaskList => _recorderTasks.Select(n => n.Key.TaskId);
        private readonly ConcurrentDictionary<RecordCameraTask, Task> _recorderTasks = new();

        private bool _disposedValue;

        public VideoRecorderService(
            IConfiguration configuration,
            IUserManager manager,
            CameraHubService collection,
            ILogger<VideoRecorderService> logger)
        {
            _logger = logger;
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
                    _logger.Log(Microsoft.Extensions.Logging.LogLevel.Information, $"Starting recording for: {record.CameraId}");
                    Start(record);
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, $"Can't start recording: {ex}");
                }
            }

            var startUpTasks = TaskConfig.ConfigStorage.ToArray();
            TaskConfig.ConfigStorage.Clear();
            foreach (var record in startUpTasks)
            {
                try
                {
                    _logger.Log(LogLevel.Information, $"Restoring recording for: {record.CameraId}");

                    if (string.IsNullOrEmpty(Start(record)))
                    {
                        throw new Exception("Recording not restored");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, $"Can't restore recording: {ex}");
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
                recordTask.Quality = _settings.DefaultVideoQuality;

            ServerCamera camera;
            try
            {
                camera = _collection.GetCamera(recordTask.CameraId, userDto);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error finding camera: {ex.Message}");
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
            if (userDto == null)
                throw new Exception($"User {newTask.User} info not found");

            ServerCamera camera;
            try
            {
                camera = _collection.GetCamera(newTask.CameraId, userDto);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error finding camera: {ex.Message}");
                throw new ApplicationException($"User [{newTask.User}] not authorised to start recording.");
            }

            var newCameraItem = new CameraQueueItem(camera.CameraStream.Description.Path,
                RecorderStreamId,
                newTask.FrameFormat);

            var imageQueue = new ConcurrentQueue<Mat>();
            try
            {
                var cameraCancellationToken = await _collection.HookCamera(newCameraItem, imageQueue);

                if (cameraCancellationToken == CancellationToken.None)
                {
                    _logger.Log(LogLevel.Error, $"Can not connect to camera [{camera.CameraStream.Description.Path}]");

                    return;
                }

                var stopTask = false;
                while (!cameraCancellationToken.IsCancellationRequested && !stopTask)
                {
                    var currentTime = DateTime.Now;
                    var fileName = $"{_settings.StoragePath.TrimEnd('\\')}\\" +
                                   $"{VideoRecorder.SanitizeFileName($"{camera.CameraStream.Description.Name}" +
                                                                     $"-{newTask.FrameFormat.Width}x{newTask.FrameFormat.Height}" +
                                                                     $"-{currentTime.ToString("yyyy-MM-dd")}" +
                                                                     $"-{currentTime.ToString("HH-mm-ss")}" +
                                                                     $".{DefaultVideoFileExtencion}")}";
                    using (var recorder = new VideoRecorder(fileName,
                               new FrameFormatDto
                               {
                                   Width = 0,
                                   Height = 0,
                                   Format = string.Empty,
                                   Fps = camera.CameraStream.CurrentFps
                               },
                               newTask.Quality,
                               _logger)
                    {
                        Codec = newTask.Codec
                    })
                    {
                        var timeOut = DateTime.Now.AddSeconds(_settings.VideoFileLengthSeconds);
                        while (DateTime.Now < timeOut && !cameraCancellationToken.IsCancellationRequested &&
                               !stopTask)
                        {
                            if (imageQueue.TryDequeue(out var image))
                            {
                                try
                                {
                                    recorder.SaveFrame(image);
                                    image?.Dispose();
                                }
                                catch (Exception ex)
                                {
                                    _logger.Log(LogLevel.Error, $"Exception while video file recording: {ex}");
                                    image?.Dispose();
                                }
                            }
                            else
                                await Task.Delay(1);

                            stopTask = !_recorderTasks.TryGetValue(newTask, out _);
                        }
                    }

                    stopTask = !_recorderTasks.TryGetValue(newTask, out _);
                }

            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Exception in VideoRecorder task: {ex}");
            }

            _collection.UnHookCamera(newCameraItem);
            while (imageQueue.TryDequeue(out var image))
            {
                image?.Dispose();
            }

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
            string codec = "",
            Mat?[]? imageBuffer = null)
        {
            var currentTime = DateTime.Now;
            imageBuffer ??= Array.Empty<Mat?>();
            frameFormat ??= new FrameFormatDto();
            var newCameraItem = new CameraQueueItem(camera.CameraStream.Description.Path,
                streamId,
                frameFormat);

            var fileName = VideoRecorder.SanitizeFileName(
                $"{fileStoragePath.TrimEnd('\\')}\\" +
                $"{filePrefix}-" +
                $"Cam{camera.CameraStream.Description.Name}-" +
                $"{streamId}-" +
                $"{currentTime.ToString("yyyy-MM-dd")}_" +
                $"{currentTime.ToString("HH-mm-ss")}.{DefaultVideoFileExtencion}");

            var tmpImageQueue = new ConcurrentQueue<Mat>();
            try
            {
                var tmpCameraCancellationToken = await _collection.HookCamera(newCameraItem, tmpImageQueue);
                if (tmpCameraCancellationToken == CancellationToken.None)
                    throw new ApplicationException($"Can not connect to camera#{camera.CameraStream.Description.Name}");

                if (frameFormat.Fps <= 0)
                    frameFormat.Fps = camera.CameraStream.CurrentFps;

                using (var recorder = new VideoRecorder(fileName, frameFormat, quality, _logger))
                {
                    recorder.Codec = codec;
                    if (imageBuffer.Length > 0)
                    {
                        foreach (var image in imageBuffer)
                        {
                            try
                            {
                                if (image != null)
                                    recorder.SaveFrame(image);
                            }
                            catch (Exception ex)
                            {
                                _logger.Log(LogLevel.Error, $"Exception while video file recording: {ex}");
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
                                image?.Dispose();
                            }
                            catch (Exception ex)
                            {
                                _logger.Log(LogLevel.Error, $"Exception while video file recording: {ex}");
                                timeOut = DateTime.Now;
                                image?.Dispose();
                            }
                        }
                        else
                            await Task.Delay(1, CancellationToken.None);
                    }

                    _collection.UnHookCamera(newCameraItem);
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Exception in video file recorder: {ex}");
            }

            while (tmpImageQueue.TryDequeue(out var image))
                image.Dispose();

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
                    _logger.Log(LogLevel.Information, "Disposing VideoRecorderService");
                    /*foreach (var k in _recorderTasks.Select(n => n.Key).ToArray())
                        Stop(k);*/
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
