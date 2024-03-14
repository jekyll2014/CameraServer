using CameraLib;

using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.CameraHub;
using CameraServer.Services.Telegram;
using CameraServer.Services.VideoRecording;

using Emgu.CV;

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

        public string Start(string cameraId, string user, FrameFormatDto frameFormat, MotionDetectorParameters detectorParams, List<NotificationParameters> notificationParams)
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
            IEnumerable<NotificationParameters> notificationParams)
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
                while (!cameraCancellationToken.IsCancellationRequested && !stopTask)
                {
                    if (imageQueue.TryDequeue(out var image) && image != null)
                    {
                        if (motionDetector.DetectMovement(image))
                        {
                            Console.WriteLine("Motion detected!!!");
                            SendNotifications(notificationParams,
                                camera,
                                image,
                                cameraCancellationToken);
                        }

                        image.Dispose();
                    }
                    else
                        await Task.Delay(10, CancellationToken.None);

                    stopTask = !_detectorTasks.TryGetValue(taskId, out _);
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

        private void SendNotifications(IEnumerable<NotificationParameters> notificationParams,
            ServerCamera camera,
            Mat image,
            CancellationToken cameraCancellationToken)
        {
            var textNotifications = notificationParams
                .Where(n => n.Transport == NotificationTransport.Telegram
                            && n.MessageType == MessageType.Image);

            if (textNotifications.Any())
                SendMovementTextMulti(textNotifications);


            var imageNotifications = notificationParams
                .Where(n => n.Transport == NotificationTransport.Telegram
                            && n.MessageType == MessageType.Image);

            if (imageNotifications.Any())
                SendMovementImageMulti(image.Clone(), imageNotifications);

            var videoNotifications = notificationParams
                .Where(n => n.Transport == NotificationTransport.Telegram
                            && n.MessageType == MessageType.Video);

            if (videoNotifications.Any())
                SendMovementVideoMulti(camera, videoNotifications, _telegramService.Settings.DefaultVideoQuality);
        }

        private void SendMovementTextMulti(IEnumerable<NotificationParameters> notificationParams)
        {
            if (notificationParams == null || !notificationParams.Any())
                return;

            Task.Run(async () =>
            {
                foreach (var notificationParam in notificationParams)
                {
                    var dest = notificationParam.Destination;
                    ChatId chatId;
                    if (long.TryParse(dest, out var id))
                        chatId = new ChatId(id);
                    else if (dest.StartsWith('@'))
                        chatId = new ChatId(dest);
                    else
                        return;

                    await _telegramService.SendText(chatId, notificationParam.Message, CancellationToken.None);
                }
            });
        }

        private void SendMovementImageMulti(Mat image, IEnumerable<NotificationParameters> notificationParams)
        {
            if (notificationParams == null || !notificationParams.Any())
                return;

            Task.Run(async () =>
            {
                foreach (var notificationParam in notificationParams)
                {
                    var dest = notificationParam.Destination;
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

                image?.Dispose();
            });
        }

        private void SendMovementVideoMulti(ServerCamera camera, IEnumerable<NotificationParameters> notificationParams, byte quality)
        {
            if (notificationParams == null || !notificationParams.Any())
                return;

            var dest = notificationParams.FirstOrDefault()?.Destination ?? string.Empty;
            ChatId chatId;
            if (long.TryParse(dest, out var id))
                chatId = new ChatId(id);
            else if (dest.StartsWith('@'))
                chatId = new ChatId(dest);
            else
                return;

            var tmpRecordtaskId =
                $"{TmpVideoStreamId}-{chatId}-{camera.Camera.Description.Path}";
            if (_videoRecordingTasks.TryGetValue(tmpRecordtaskId, out var _))
                return;

            var t = new Task(async () =>
            {
                try
                {

                    var tmpUserId = $"{MotionDetectionStreamId}-{chatId}";
                    var fileName = await _videoRecorderService.RecordVideoFile(camera,
                        tmpUserId,
                        TmpVideoStreamId,
                        notificationParams.Max(n => n.VideoLengthSec),
                        null,
                        quality);

                    foreach (var notificationParam in notificationParams)
                    {
                        ChatId chatId;
                        if (long.TryParse(notificationParam.Destination, out var id))
                            chatId = new ChatId(id);
                        else if (notificationParam.Destination.StartsWith('@'))
                            chatId = new ChatId(notificationParam.Destination);
                        else
                            return;

                        await _telegramService.SendVideo(chatId, fileName, $"{notificationParam.Message}", CancellationToken.None);
                    }

                    File.Delete(fileName);
                }
                catch (Exception ex)
                {
                    await _telegramService.SendText(chatId, $"Can't record video: {ex}", CancellationToken.None);
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
