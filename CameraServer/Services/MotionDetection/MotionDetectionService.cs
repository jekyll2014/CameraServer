using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.CameraHub;
using CameraServer.Services.Telegram;
using CameraServer.Services.VideoRecording;

using OpenCvSharp;

using System.Collections.Concurrent;

using Telegram.Bot.Types;

using File = System.IO.File;

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
        public readonly MotionDetectionSettings Settings;
        public readonly Config<List<MotionDetectionCameraSettingDto>> TaskConfig = new Config<List<MotionDetectionCameraSettingDto>>(MotioDetectorTempConfig);

        public IEnumerable<string> TaskList => _detectorTasks.Select(n => n.Key.TaskId);
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
            TelegramService telegramService)
        {
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
                    Console.WriteLine($"Starting motion detector for: {record.CameraId}");
                    if (!string.IsNullOrEmpty(Start(record)))
                    {
                        throw new Exception("Motion detector not started");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Can't start motion detection: {ex}");
                }
            }

            var startUpTasks = TaskConfig.ConfigStorage.ToArray();
            TaskConfig.ConfigStorage.Clear();
            foreach (var record in startUpTasks)
            {
                try
                {
                    Console.WriteLine($"Restoring motion detector for: {record.CameraId}");

                    if (string.IsNullOrEmpty(Start(record)))
                    {
                        throw new Exception("Motion detector not restored");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Can't restore motion detection: {ex}");
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

            detectTask.MotionDetectParameters ??= Settings.DefaultMotionDetectParametersDto;

            var userDto = _manager.GetUserInfo(detectTask.User);
            if (userDto == null)
                throw new ApplicationException($"User [{detectTask.User}] not authorised to start recording.");

            if (detectTask.MotionDetectParameters.Width <= 0)
                detectTask.MotionDetectParameters.Width = Settings.DefaultMotionDetectParametersDto.Width;

            if (detectTask.MotionDetectParameters.Height <= 0)
                detectTask.MotionDetectParameters.Height = Settings.DefaultMotionDetectParametersDto.Height;

            if (detectTask.MotionDetectParameters.DetectorDelayMs <= 0)
                detectTask.MotionDetectParameters.Width = Settings.DefaultMotionDetectParametersDto.Width;

            if (detectTask.MotionDetectParameters.NoiseThreshold <= 0)
                detectTask.MotionDetectParameters.NoiseThreshold = Settings.DefaultMotionDetectParametersDto.NoiseThreshold;

            if (detectTask.MotionDetectParameters.ChangeLimit <= 0)
                detectTask.MotionDetectParameters.ChangeLimit = Settings.DefaultMotionDetectParametersDto.ChangeLimit;

            ServerCamera camera;
            try
            {
                camera = _collection.GetCamera(detectTask.CameraId, userDto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding camera: {ex.Message}");
                throw new ApplicationException($"User [{detectTask.User}] not authorised to start recording.");
            }

            var taskId = GenerateTaskId(camera.CameraStream.Description.Path, detectTask.User);
            var task = new MotionDetectionCameraTask(detectTask)
            {
                TaskId = taskId,
            };

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

        private async Task MotionDetectorTask(MotionDetectionCameraTask newTask)
        {
            var userDto = _manager.GetUserInfo(newTask.User);
            if (userDto == null)
            {
                Console.WriteLine($"User [{newTask.User}] not found");
                throw new ApplicationException($"User [{newTask.User}] not found.");
            }

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
                MotionDetectionStreamId + newTask.TaskId,
                newTask.FrameFormat);

            var imageQueue = new ConcurrentQueue<Mat>();
            var cameraCancellationToken = await _collection.HookCamera(newCameraItem, imageQueue);

            if (cameraCancellationToken == CancellationToken.None)
            {
                Console.WriteLine($"Can not connect to camera [{camera.CameraStream.Description.Path}]");

                return;
            }

            //start looking for motion
            var stopTask = false;
            using (var motionDetector = new MotionDetector(newTask.MotionDetectParameters))
            {
                var lastImagesQueue = new ConcurrentQueue<Mat>();
                var maxBufferCount = Settings.DefaultMotionDetectParametersDto.KeepImageBuffer;
                while (!cameraCancellationToken.IsCancellationRequested && !stopTask)
                {
                    if (imageQueue.TryDequeue(out var image) && image != null)
                    {
                        lastImagesQueue.Enqueue(image);
                        if (motionDetector.DetectMovement(image))
                        {
                            Console.WriteLine("Motion detected!!!");
                            var imageBuffer = lastImagesQueue?.ToArray().Select(n => n?.Clone()).ToArray() ?? Array.Empty<Mat?>();

                            SendNotifications(newTask.Notifications,
                                camera,
                                userDto,
                                image,
                                imageBuffer,
                                cameraCancellationToken);
                        }

                        if (lastImagesQueue.Count > maxBufferCount)
                        {
                            lastImagesQueue.TryDequeue(out var oldImage);
                            oldImage?.Dispose();
                        }
                    }
                    else
                        await Task.Delay(10, CancellationToken.None);

                    stopTask = !_detectorTasks.TryGetValue(newTask, out _);
                }

                while (!lastImagesQueue.IsEmpty)
                {
                    lastImagesQueue.TryDequeue(out var oldImage);
                    oldImage?.Dispose();
                }
            }

            _collection.UnHookCamera(newCameraItem);
            while (imageQueue.TryDequeue(out var image))
            {
                image?.Dispose();
            }

            imageQueue.Clear();
            _detectorTasks.TryRemove(newTask, out _);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        }

        private void SendNotifications(IReadOnlyCollection<NotificationParametersDto> notificationParams,
            ServerCamera camera,
            ICameraUser user,
            Mat image,
            Mat?[]? bufferedImages,
            CancellationToken cameraCancellationToken)
        {
            var imageNotifications = notificationParams
                .Where(n => n.Transport == NotificationTransport.Telegram
                            && n.MessageType == MessageType.Image)
                .ToArray();

            if (imageNotifications.Length != 0)
                SendMovementImageMulti(camera, image.Clone(), imageNotifications);

            var videoNotifications = notificationParams
                .Where(n => n.Transport == NotificationTransport.Telegram
                            && n.MessageType == MessageType.Video)
                .ToArray();

            if (videoNotifications.Length != 0)
            {
                SendMovementVideoMulti(camera, videoNotifications, user.DefaultCodec, bufferedImages,
                    _telegramService.Settings.DefaultVideoQuality);
            }

            var textNotifications = notificationParams
                .Where(n => n.Transport == NotificationTransport.Telegram
                            && n.MessageType == MessageType.Text)
                .ToArray();

            if (textNotifications.Length != 0)
                SendMovementTextMulti(textNotifications);
        }

        private void SendMovementTextMulti(IReadOnlyCollection<NotificationParametersDto> notificationParams)
        {
            if (notificationParams.Count == 0)
                return;

            Task.Run(async () =>
            {
                var currentTime = DateTime.Now;
                foreach (var notificationParam in notificationParams)
                {
                    var dest = notificationParam.Destination;
                    if (_notificationsText.TryGetValue(dest, out var lastNotificationTime))
                    {
                        if (currentTime.Subtract(lastNotificationTime).TotalSeconds < Settings.DefaultMotionDetectParametersDto.NotificationDelay)
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
            });
        }

        private void SendMovementImageMulti(IServerCamera camera, Mat image, NotificationParametersDto[] notificationParams)
        {
            if (notificationParams == null || !notificationParams.Any())
                return;

            Task.Run(async () =>
            {
                var currentTime = DateTime.Now;
                foreach (var notificationParam in notificationParams)
                {
                    var dest = notificationParam.Destination;
                    if (_notificationsImage.TryGetValue(dest, out var lastNotificationTime))
                    {
                        if (currentTime.Subtract(lastNotificationTime).TotalSeconds < Settings.DefaultMotionDetectParametersDto.NotificationDelay)
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
                                   $"{VideoRecorder.SanitizeFileName($"{camera.CameraStream.Description.Name}-{currentTime.ToString("yyyy-MM-dd")}_{currentTime.ToString("HH-mm-ss")}.jpg")}";
                    try
                    {
                        if (image != null)
                            await File.WriteAllBytesAsync(fileName, image.ToBytes(".jpg",
                                new ImageEncodingParam[]
                                {
                                    new(ImwriteFlags.JpegOptimize, 1),
                                    new(ImwriteFlags.JpegQuality, _videoRecorderService.Settings.DefaultVideoQuality)
                                }));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error saving image file: {ex}");
                    }
                }

                image?.Dispose();
            });
        }

        private void SendMovementVideoMulti(ServerCamera camera,
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
                            if (currentTime.Subtract(lastNotificationTime).TotalSeconds < Settings.DefaultMotionDetectParametersDto.NotificationDelay)
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
                            return;

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
                    await _telegramService.SendText(destinationTotal, $"Can't record video: {ex}", CancellationToken.None);
                }

                _videoRecordingTasks.TryRemove(tmpRecordtaskId, out _);
            });

            _videoRecordingTasks.TryAdd(tmpRecordtaskId, t);
            t.Start();
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
                    foreach (var k in _detectorTasks.Select(n => n.Key).ToArray())
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
