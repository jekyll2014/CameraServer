using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.CameraHub;
using CameraServer.Services.Telegram;
using CameraServer.Services.VideoRecording;

using OpenCvSharp;

using System.Collections.Concurrent;

using Telegram.Bot.Types;

using DateTime = System.DateTime;
using File = System.IO.File;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CameraServer.Services.MotionDetection
{
    public class MotionDetectionService : IHostedService, IDisposable
    {
        private const string MotioDetectorTempConfig = "appsettings-motion.json";
        private const string MotionDetectionConfigSection = "MotionDetector";
        private const string MotionDetectionStreamId = "MotionDetector";
        private const string TmpVideoStreamId = "MotionDetectorTmpVideo";

        private readonly IUserManager _manager;
        private readonly CameraHubService _collection;
        private readonly VideoRecorderService _videoRecorderService;
        private readonly TelegramService _telegramService;
        private readonly ILogger<MotionDetectionService> _logger;
        public readonly MotionDetectionSettings Settings;
        public readonly Config<List<MotionDetectionCameraSettingDto>> TaskConfig = new(MotioDetectorTempConfig);

        public IEnumerable<string> TaskList => _detectorTasks.Select(n => n.Key.TaskId);

        public delegate void MotionDetectPrcessedEventHandler(MotionDetectionCameraTask detectorTask, Mat? image);
        public event MotionDetectPrcessedEventHandler? ImageProcessedEvent;

        private readonly ConcurrentDictionary<MotionDetectionCameraTask, Task> _detectorTasks = new();
        private readonly ConcurrentDictionary<string, Task> _videoRecordingTasks = new();
        private readonly ConcurrentDictionary<string, DateTime> _notificationsText = new();
        private readonly ConcurrentDictionary<string, DateTime> _notificationsImage = new();
        private readonly ConcurrentDictionary<string, DateTime> _notificationsVideo = new();

        private bool _disposedValue;

        public MotionDetectionService(IConfiguration configuration,
            IUserManager manager,
            CameraHubService collection,
            VideoRecorderService videoRecorderService,
            TelegramService telegramService,
            ILogger<MotionDetectionService> logger)
        {
            _logger = logger;
            _manager = manager;
            _collection = collection;
            _videoRecorderService = videoRecorderService;
            _telegramService = telegramService;
            Settings = configuration.GetSection(MotionDetectionConfigSection)?.Get<MotionDetectionSettings>()
                       ?? new MotionDetectionSettings();
            Directory.CreateDirectory(Settings.StoragePath);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var record in Settings.MotionDetectionCameras)
            {
                try
                {
                    _logger.Log(LogLevel.Information, $"Starting motion detector for: {record.CameraId}");
                    if (!string.IsNullOrEmpty(Start(record)))
                    {
                        throw new Exception("Motion detector not started");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, $"Can't start motion detection: {ex}");
                }
            }

            var startUpTasks = TaskConfig.ConfigStorage.ToArray();
            TaskConfig.ConfigStorage.Clear();
            foreach (var record in startUpTasks)
            {
                try
                {
                    _logger.Log(LogLevel.Information, $"Restoring motion detector for: {record.CameraId}");

                    if (string.IsNullOrEmpty(Start(record)))
                    {
                        throw new Exception("Motion detector not restored");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, $"Can't restore motion detection: {ex}");
                }
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
        }

        public string Start(MotionDetectionCameraSettingDto detectTask)
        {
            if (detectTask?.Notifications == null || detectTask.Notifications.Count <= 0)
                return string.Empty;

            detectTask.MotionDetectParameters ??= Settings.DefaultMotionDetectParameters;

            var userDto = _manager.GetUserInfo(detectTask.User);
            if (userDto == null)
                throw new ApplicationException($"User [{detectTask.User}] not authorised to start recording.");

            if (detectTask.MotionDetectParameters.Width <= 0)
                detectTask.MotionDetectParameters.Width = Settings.DefaultMotionDetectParameters.Width;

            if (detectTask.MotionDetectParameters.Height <= 0)
                detectTask.MotionDetectParameters.Height = Settings.DefaultMotionDetectParameters.Height;

            if (detectTask.MotionDetectParameters.DetectorDelayMs <= 0)
                detectTask.MotionDetectParameters.Width = Settings.DefaultMotionDetectParameters.Width;

            if (detectTask.MotionDetectParameters.NoiseThreshold <= 0)
                detectTask.MotionDetectParameters.NoiseThreshold = Settings.DefaultMotionDetectParameters.NoiseThreshold;

            if (detectTask.MotionDetectParameters.ChangeLimit <= 0)
                detectTask.MotionDetectParameters.ChangeLimit = Settings.DefaultMotionDetectParameters.ChangeLimit;

            ServerCamera camera;
            try
            {
                camera = _collection.GetCamera(detectTask.CameraId, userDto);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error finding camera: {ex.Message}");

                throw new ApplicationException($"User [{detectTask.User}] not authorised to start recording.");
            }

            var taskId = GenerateTaskId(camera.CameraStream.Description.Path, detectTask.User);
            var task = new MotionDetectionCameraTask(detectTask)
            {
                TaskId = taskId,
            };

            _logger.Log(LogLevel.Information, $"Starting detection task [{task.TaskId}] for user [{task.User}]");

            var t = new Task(async () => await MotionDetectorTask(task));

            if (_detectorTasks.TryAdd(task, t))
            {
                t.Start();
                var existingTask = TaskConfig.ConfigStorage.FirstOrDefault(n => n.Equals(task));
                if (existingTask == null)
                {
                    TaskConfig.ConfigStorage.Add(task);
                }
                else
                {
                    existingTask.Merge(task);
                }

                CleanEmptyTasks();
                TaskConfig.SaveConfig();
            }
            else
                taskId = string.Empty;

            return taskId;
        }

        public void Stop(string taskId)
        {
            var task = _detectorTasks.FirstOrDefault(n => n.Key.TaskId == taskId);
            if (task.Key != null)
                Stop(task.Key);
        }

        private void Stop(MotionDetectionCameraTask detectionTask)
        {
            _logger.Log(LogLevel.Information, $"Stopping detection task [{detectionTask.TaskId}] for user [{detectionTask.User}]");
            if (_detectorTasks.TryRemove(detectionTask, out var t))
            {
                var existingTask = TaskConfig.ConfigStorage.FirstOrDefault(n => n.Equals(detectionTask));
                if (existingTask != null)
                {
                    TaskConfig.ConfigStorage.Remove(existingTask);
                }

                CleanEmptyTasks();
                TaskConfig.SaveConfig();

                t.Wait(5000);
                t.Dispose();
            }
        }

        private void CleanEmptyTasks()
        {
            for (var i = 0; i < TaskConfig.ConfigStorage.Count; i++)
            {
                if (TaskConfig.ConfigStorage[i].Notifications?.Count <= 0)
                {
                    TaskConfig.ConfigStorage.RemoveAt(i);
                    i--;
                }
            }
        }

        private async Task MotionDetectorTask(MotionDetectionCameraTask motionDetectTask)
        {
            var userDto = _manager.GetUserInfo(motionDetectTask.User);
            if (userDto == null)
            {
                _logger.Log(LogLevel.Error, $"User [{motionDetectTask.User}] not found");
                throw new ApplicationException($"User [{motionDetectTask.User}] not found.");
            }

            ServerCamera camera;
            try
            {
                camera = _collection.GetCamera(motionDetectTask.CameraId, userDto);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error finding camera: {ex.Message}");
                throw new ApplicationException($"User [{motionDetectTask.User}] not authorised to start recording.");
            }

            var newCameraItem = new CameraQueueItem(camera.CameraStream.Description.Path,
                MotionDetectionStreamId + motionDetectTask.TaskId,
                motionDetectTask.FrameFormat);

            var imageQueue = new ConcurrentQueue<Mat>();
            var lastImagesQueue = new ConcurrentQueue<Mat>();
            try
            {
                var cameraCancellationToken = await _collection.HookCamera(newCameraItem, imageQueue);
                if (cameraCancellationToken == CancellationToken.None)
                {
                    _logger.Log(LogLevel.Error, $"Can not connect to camera [{camera.CameraStream.Description.Path}]");

                    return;
                }

                //start looking for motion
                var stopTask = false;
                motionDetectTask.MotionDetectParameters ??= new MotionDetectorParametersDto();
                using (var motionDetector = new MotionDetector(motionDetectTask.MotionDetectParameters))
                {
                    var maxBufferCount = Settings.DefaultMotionDetectParameters.KeepImageBuffer;
                    while (!cameraCancellationToken.IsCancellationRequested && !stopTask)
                    {
                        if (imageQueue.TryDequeue(out var image))
                        {
                            lastImagesQueue.Enqueue(image);
                            if (motionDetector.DetectMovement(image))
                            {
                                _logger.Log(LogLevel.Information, "Motion detected!!!");

                                SendNotifications(motionDetectTask.Notifications,
                                    camera,
                                    userDto,
                                    lastImagesQueue,
                                    cameraCancellationToken);

                                while (lastImagesQueue.TryDequeue(out var oldImage))
                                    oldImage?.Dispose();

                                motionDetector.ProcessedFrame?.Clone();
                                ImageProcessedEvent?.Invoke(motionDetectTask, motionDetector.ProcessedFrame?.Clone());
                            }
                            else
                            {
                                ImageProcessedEvent?.Invoke(motionDetectTask, motionDetector.ProcessedFrame?.Clone());
                            }

                            while (lastImagesQueue.Count >= maxBufferCount)
                            {
                                if (lastImagesQueue.TryDequeue(out var oldImage))
                                    oldImage?.Dispose();
                            }
                        }
                        else
                            await Task.Delay(1);

                        stopTask = !_detectorTasks.Any(n => n.Key.TaskId == motionDetectTask.TaskId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Exception in MotionDetector task: {ex}");
            }

            _collection.UnHookCamera(newCameraItem);
            while (imageQueue.TryDequeue(out var image))
                image?.Dispose();

            while (lastImagesQueue.TryDequeue(out var oldImage))
                oldImage?.Dispose();

            _detectorTasks.TryRemove(motionDetectTask, out _);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        }

        private void SendNotifications(IReadOnlyCollection<NotificationParametersDto> notificationParams,
            ServerCamera camera,
            UserDto user,
            ConcurrentQueue<Mat> bufferedImages,
            CancellationToken cameraCancellationToken)
        {
            var tasks = new List<Task>();
            var imageNotifications = notificationParams
                .Where(n => n.Transport == NotificationTransport.Telegram
                            && n.MessageType == MessageType.Image)
                .ToArray();

            if (imageNotifications.Length != 0)
            {
                _logger.Log(LogLevel.Information, $"Sending motion notification[image]");
                var t = new Task(async () =>
                {
                    var image = bufferedImages.Last()?.Clone();
                    await SendMovementImageMulti(camera, image, imageNotifications);
                    image?.Dispose();
                }, TaskCreationOptions.LongRunning);

                //t.ConfigureAwait(false);
                t.Start();
                tasks.Add(t);
            }

            var videoNotifications = notificationParams
                    .Where(n => n.Transport == NotificationTransport.Telegram
                                && n.MessageType == MessageType.Video)
                    .ToArray();

            if (videoNotifications.Length != 0)
            {
                _logger.Log(LogLevel.Information, $"Sending motion notification[video]");

                var t = new Task(async () =>
                {
                    var buffer = bufferedImages.Select(n => n?.Clone()).ToArray();
                    try
                    {
                        await SendMovementVideoMulti(camera,
                            videoNotifications,
                            user.DefaultCodec,
                            buffer,
                            _telegramService._settings.DefaultVideoQuality);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"Can't send motion video: {ex}");
                    }

                    foreach (var img in buffer)
                        img?.Dispose();
                }
                    , TaskCreationOptions.LongRunning);

                //t.ConfigureAwait(false);
                t.Start();
                tasks.Add(t);
            }

            var textNotifications = notificationParams
                .Where(n => n.Transport == NotificationTransport.Telegram
                            && n.MessageType == MessageType.Text)
                .ToArray();

            if (textNotifications.Length != 0)
            {
                _logger.Log(LogLevel.Information, $"Sending motion notification[text]");

                var t = new Task(async () =>
                        await SendMovementTextMulti(textNotifications)
                    , TaskCreationOptions.LongRunning);

                //t.ConfigureAwait(false);
                t.Start();
                tasks.Add(t);
            }

            Task.WaitAll([.. tasks], cameraCancellationToken);
        }

        private async Task SendMovementTextMulti(IReadOnlyCollection<NotificationParametersDto> notificationParams)
        {
            if (notificationParams.Count == 0)
                return;

            var currentTime = DateTime.Now;
            foreach (var notificationParam in notificationParams)
            {
                var dest = notificationParam.Destination;
                if (_notificationsText.TryGetValue(dest, out var lastNotificationTime))
                {
                    if (currentTime.Subtract(lastNotificationTime).TotalSeconds < Settings.DefaultMotionDetectParameters.NotificationDelay)
                        continue;

                    _notificationsText[dest] = currentTime;
                }
                else
                {
                    _notificationsText.TryAdd(dest, currentTime);
                }

                ChatId chatId;
                if (long.TryParse(dest, out var id))
                    chatId = new ChatId(id);
                else if (dest.StartsWith('@'))
                    chatId = new ChatId(dest);
                else
                    return;

                await _telegramService.SendText(chatId, notificationParam.Message, CancellationToken.None);
            }

            if (notificationParams.Any(n => n.SaveNotificationContent))
            {
                // ToDo: log motion event to file
            }
        }

        private async Task SendMovementImageMulti(
            IServerCamera camera,
            Mat? image,
            NotificationParametersDto[] notificationParams)
        {
            if (notificationParams.Length <= 0)
                return;

            var currentTime = DateTime.Now;
            foreach (var notificationParam in notificationParams)
            {
                var dest = notificationParam.Destination;
                if (_notificationsImage.TryGetValue(dest, out var lastNotificationTime))
                {
                    if (currentTime.Subtract(lastNotificationTime).TotalSeconds < Settings.DefaultMotionDetectParameters.NotificationDelay)
                        continue;

                    _notificationsImage[dest] = currentTime;
                }
                else
                {
                    _notificationsImage.TryAdd(dest, currentTime);
                }

                ChatId chatId;
                if (long.TryParse(dest, out var id))
                    chatId = new ChatId(id);
                else if (dest.StartsWith('@'))
                    chatId = new ChatId(dest);
                else
                    return;

                await _telegramService.SendImage(
                    chatId,
                    image,
                    caption: $"{notificationParam.Message}",
                    CancellationToken.None);
            }

            if (notificationParams.Any(n => n.SaveNotificationContent))
            {
                var fileName = $"{Settings.StoragePath.TrimEnd('\\')}\\" +
                               $"{VideoRecorder.SanitizeFileName($"{camera.CameraStream.Description.Name}-{currentTime:yyyy-MM-dd}_{currentTime:HH-mm-ss}.jpg")}";
                try
                {
                    if (image != null)
                        await File.WriteAllBytesAsync(fileName, image.ToBytes(".jpg",
                            new ImageEncodingParam[]
                            {
                                    new(ImwriteFlags.JpegOptimize, 1),
                                    new(ImwriteFlags.JpegQuality, _videoRecorderService._settings.DefaultVideoQuality)
                            }));
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, $"Error saving image file: {ex}");
                }
            }
        }

        private async Task SendMovementVideoMulti(ServerCamera camera,
            IReadOnlyCollection<NotificationParametersDto> notificationParams,
            string codec,
            Mat?[]? bufferedImages,
            byte quality)
        {
            if (notificationParams.Count == 0)
                return;

            var destinationTotal = notificationParams
                .Select(n => n.Destination)
                .Aggregate((n, m) => m += $" {n}");
            var tmpRecordtaskId =
                $"{TmpVideoStreamId}-{destinationTotal}-{camera.CameraStream.Description.Path}";
            if (_videoRecordingTasks.TryGetValue(tmpRecordtaskId, out var _))
                return;

            var t = new Task(async () =>
            {
                var currentTime = DateTime.Now;
                try
                {
                    var tmpUserId = $"{MotionDetectionStreamId}-{destinationTotal}";
                    var fileName = await _videoRecorderService.RecordVideoFile(camera,
                        tmpUserId,
                        Settings.StoragePath,
                        TmpVideoStreamId,
                        notificationParams.Max(n => n.VideoLengthSec),
                        null,
                        quality,
                        codec,
                        bufferedImages);

                    foreach (var notificationParam in notificationParams)
                    {
                        var dest = notificationParam.Destination;
                        if (_notificationsVideo.TryGetValue(dest, out var lastNotificationTime))
                        {
                            if (currentTime.Subtract(lastNotificationTime).TotalSeconds <
                                Settings.DefaultMotionDetectParameters.NotificationDelay)
                                continue;

                            _notificationsVideo[dest] = currentTime;
                        }
                        else
                        {
                            _notificationsVideo.TryAdd(dest, currentTime);
                        }

                        ChatId chatId;
                        if (long.TryParse(dest, out var localIid))
                            chatId = new ChatId(localIid);
                        else if (dest.StartsWith('@'))
                            chatId = new ChatId(dest);
                        else
                            continue;

                        await _telegramService.SendVideo(chatId,
                            fileName,
                            $"{notificationParam.Message}",
                            CancellationToken.None);
                    }

                    if (notificationParams.All(n => !n.SaveNotificationContent))
                        File.Delete(fileName);
                }
                catch (Exception ex)
                {
                    await _telegramService.SendText(destinationTotal, $"Can't record video: {ex}",
                        CancellationToken.None);
                }

                _videoRecordingTasks.TryRemove(tmpRecordtaskId, out _);
            }, TaskCreationOptions.LongRunning);

            //await t.ConfigureAwait(false);
            t.Start();
            _videoRecordingTasks.TryAdd(tmpRecordtaskId, t);
        }

        public static string GenerateTaskId(string cameraPath, string user)
        {
            return cameraPath + user;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _logger.Log(LogLevel.Information, "Disposing MotionDetectionService");
                    /*foreach (var k in _detectorTasks.Select(n => n.Key).ToArray())
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
