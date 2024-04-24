using CameraLib;

using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.CameraHub;
using CameraServer.Services.Telegram;
using CameraServer.Services.VideoRecording;

using Emgu.CV;
using Emgu.CV.Structure;

using System.Collections.Concurrent;

using Telegram.Bot.Types;

using File = System.IO.File;

namespace CameraServer.Services.MotionDetection
{
    public class MotionDetectionService : IHostedService, IDisposable
    {
        private const string MotionDetectionConfigSection = "MotionDetector";
        private const string MotionDetectionStreamId = "MotionDetector";
        private const string TmpVideoStreamId = "MotionDetectorTmpVideo";

        private readonly IUserManager _manager;
        private readonly CameraHubService _collection;
        private readonly VideoRecorderService _videoRecorderService;
        private readonly TelegramService _telegramService;
        public readonly MotionDetectionSettings Settings;

        public IEnumerable<string> TaskList => _detectorTasks.Select(n => n.Key);
        private readonly ConcurrentDictionary<string, Task> _detectorTasks = new();
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
            Settings = configuration.GetSection(MotionDetectionConfigSection)?.Get<MotionDetectionSettings>() ?? new MotionDetectionSettings();
            Directory.CreateDirectory(Settings.StoragePath);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var record in Settings.MotionDetectionCameras)
            {
                try
                {
                    Console.WriteLine($"Starting motion detector for: {record.CameraId}");
                    Start(record.CameraId,
                        record.User,
                        record.FrameFormat,
                        record.MotionDetectParameters ?? Settings.DefaultMotionDetectParameters,
                        record.Notifications);
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

        public string Start(string cameraId,
            string user,
            FrameFormatDto frameFormat,
            MotionDetectorParameters detectorParams,
            List<NotificationParameters> notificationParams)
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

            if (detectorParams.Width <= 0)
                detectorParams.Width = Settings.DefaultMotionDetectParameters.Width;

            if (detectorParams.Height <= 0)
                detectorParams.Height = Settings.DefaultMotionDetectParameters.Height;

            if (detectorParams.DetectorDelayMs <= 0)
                detectorParams.Width = Settings.DefaultMotionDetectParameters.Width;

            if (detectorParams.NoiseThreshold <= 0)
                detectorParams.NoiseThreshold = Settings.DefaultMotionDetectParameters.NoiseThreshold;

            if (detectorParams.ChangeLimit <= 0)
                detectorParams.ChangeLimit = Settings.DefaultMotionDetectParameters.ChangeLimit;

            var taskId = GenerateTaskId(camera.Camera.Description.Path, user);
            var t = new Task(async () => await MotionDetectorTask(camera, frameFormat,
                     taskId,
                     detectorParams,
                     notificationParams));

            _detectorTasks.TryAdd(taskId, t);
            t.Start();

            return taskId;
        }

        public void Stop(string taskId)
        {
            if (_detectorTasks.TryRemove(taskId, out var t))
            {
                //t.Wait(5000);
                //t.Dispose();
            }
        }

        private async Task MotionDetectorTask(ServerCamera camera,
            FrameFormatDto frameFormat,
            string taskId,
            MotionDetectorParameters detectorParams,
            List<NotificationParameters> notificationParams)
        {
            var imageQueue = new ConcurrentQueue<Mat>();
            var cameraCancellationToken = await _collection.HookCamera(camera.Camera.Description.Path,
                MotionDetectionStreamId + taskId,
                imageQueue,
                frameFormat);

            if (cameraCancellationToken == CancellationToken.None)
            {
                Console.WriteLine($"Can not connect to camera [{camera.Camera.Description.Path}]");

                return;
            }

            //start looking for motion
            var stopTask = false;
            using (var motionDetector = new MotionDetector(detectorParams))
            {
                var lastImagesQueue = new ConcurrentQueue<Mat>();
                var maxBufferCount = Settings.DefaultMotionDetectParameters.KeepImageBuffer;
                while (!cameraCancellationToken.IsCancellationRequested && !stopTask)
                {
                    if (imageQueue.TryDequeue(out var image) && image != null)
                    {
                        lastImagesQueue.Enqueue(image);
                        if (motionDetector.DetectMovement(image))
                        {
                            Console.WriteLine("Motion detected!!!");
                            SendNotifications(notificationParams,
                                camera,
                                image,
                                lastImagesQueue,
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

                    stopTask = !_detectorTasks.TryGetValue(taskId, out _);
                }

                while (!lastImagesQueue.IsEmpty)
                {
                    lastImagesQueue.TryDequeue(out var oldImage);
                    oldImage?.Dispose();
                }
            }

            await _collection.UnHookCamera(camera.Camera.Description.Path, MotionDetectionStreamId + taskId, frameFormat);
            while (imageQueue.TryDequeue(out var image))
            {
                image?.Dispose();
            }

            imageQueue.Clear();
            _detectorTasks.TryRemove(taskId, out _);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);
        }

        private void SendNotifications(IReadOnlyCollection<NotificationParameters> notificationParams,
            ServerCamera camera,
            Mat image,
            ConcurrentQueue<Mat> bufferedImages,
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
                SendMovementVideoMulti(camera, videoNotifications, bufferedImages,
                    _telegramService.Settings.DefaultVideoQuality);
            }

            var textNotifications = notificationParams
                .Where(n => n.Transport == NotificationTransport.Telegram
                            && n.MessageType == MessageType.Text)
                .ToArray();

            if (textNotifications.Length != 0)
                SendMovementTextMulti(textNotifications);
        }

        private void SendMovementTextMulti(NotificationParameters[] notificationParams)
        {
            if (notificationParams.Length == 0)
                return;

            Task.Run(async () =>
            {
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
            });
        }

        private void SendMovementImageMulti(IServerCamera camera, Mat image, NotificationParameters[] notificationParams)
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
                                   $"{VideoRecorder.SanitizeFileName($"{camera.Camera.Description.Name}-{currentTime.ToString("yyyy-MM-dd")}-{currentTime.ToString("HH-mm-ss")}.jpg")}";
                    try
                    {
                        await File.WriteAllBytesAsync(fileName, image.ToImage<Rgb, byte>().ToJpegData());
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
            NotificationParameters[] notificationParams,
            ConcurrentQueue<Mat> bufferedImages,
            byte quality)
        {
            if (notificationParams.Length == 0)
                return;

            var destinationTotal = notificationParams.Select(n => n.Destination).Aggregate((n, m) => m += $" {n}");
            var tmpRecordtaskId =
                $"{TmpVideoStreamId}-{destinationTotal}-{camera.Camera.Description.Path}";
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
                        bufferedImages);

                    foreach (var notificationParam in notificationParams)
                    {
                        var dest = notificationParam.Destination;
                        if (_notificationsVideo.TryGetValue(dest, out var lastNotificationTime))
                        {
                            if (currentTime.Subtract(lastNotificationTime).TotalSeconds < Settings.DefaultMotionDetectParameters.NotificationDelay)
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

                        await _telegramService.SendVideo(chatId, fileName, $"{notificationParam.Message}", CancellationToken.None);
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
