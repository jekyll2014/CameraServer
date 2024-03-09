using CameraLib;

using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.CameraHub;
using CameraServer.Services.Telegram;

using Emgu.CV;

using System.Collections.Concurrent;

using Telegram.Bot.Types;

namespace CameraServer.Services.MotionDetection
{
    public class MotionDetectionService : IHostedService, IDisposable
    {
        private const string MotionDetectionConfigSection = "MotionDetector";
        private const string MotionDetectionStreamId = "MotionDetector";
        private const string TmpVideoStreamId = "MotionDetectorTmpVideo";
        private const string FilePrefix = "motionDetector";

        private readonly IUserManager _manager;
        private readonly CameraHubService _collection;
        private readonly TelegramService _telegramService;
        public readonly MotionDetectionSettings Settings;

        public IEnumerable<string> TaskList => _detectorTasks.Select(n => n.Key);
        private readonly Dictionary<string, Task> _detectorTasks = new Dictionary<string, Task>();
        private readonly Dictionary<string, Task> _videoRecordingTasks = new Dictionary<string, Task>();

        private bool _disposedValue;

        public MotionDetectionService(IConfiguration configuration, IUserManager manager, CameraHubService collection, TelegramService telegramService)
        {
            _manager = manager;
            _collection = collection;
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
                catch (Exception e)
                {
                    Console.WriteLine($"Can't start recording: {e}");
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
            var t = new Task(async () =>
            {
                var imageQueue = new ConcurrentQueue<Mat>();
                var cameraCancellationToken = await _collection.HookCamera(camera.Camera.Description.Path,
                    MotionDetectionStreamId,
                    imageQueue,
                    frameFormat);

                if (cameraCancellationToken == CancellationToken.None)
                {
                    Console.WriteLine($"Can not connect to camera [{cameraId}]");

                    return;
                }

                //start looking for motion
                var stopTask = false;
                using (var motionDetector = new MotionDetector(detectorParams))
                {
                    while (!cameraCancellationToken.IsCancellationRequested && !stopTask)
                    {
                        if (imageQueue.TryDequeue(out var image))
                        {
                            if (motionDetector.DetectMovement(image))
                            {
                                Console.WriteLine("Motion detected!!!");
                                foreach (var notificationParam in notificationParams)
                                {
                                    if (notificationParam.Transport == NotificationTransport.Telegram)
                                    {
                                        ChatId chatId;
                                        if (long.TryParse(notificationParam.Destination, out var id))
                                            chatId = new ChatId(id);
                                        else if (notificationParam.Destination.StartsWith('@'))
                                            chatId = new ChatId(notificationParam.Destination);
                                        else
                                            continue;

                                        if (notificationParam.MessageType == MessageType.Text)
                                        {
                                            Task.Run(async () =>
                                            {
                                                await _telegramService.SendText(chatId, notificationParam.Message,
                                                    cameraCancellationToken);
                                            });
                                        }
                                        else if (notificationParam.MessageType == MessageType.Image)
                                        {
                                            var movementImage = image.Clone();
                                            Task.Run(async () =>
                                            {
                                                await _telegramService.SendImage(
                                                    chatId,
                                                    movementImage,
                                                    caption: $"{notificationParam.Message}",
                                                    cameraCancellationToken);

                                                movementImage?.Dispose();
                                            });
                                        }
                                        else if (notificationParam.MessageType == MessageType.Video)
                                        {
                                            SendMovementVideo(camera,
                                                chatId,
                                                notificationParam.Message,
                                                notificationParam.VideoLengthSec);
                                        }
                                    }
                                }
                            }
                        }
                        else
                            await Task.Delay(10, CancellationToken.None);

                        image?.Dispose();
                        stopTask = !_detectorTasks.TryGetValue(taskId, out _);
                    }
                }

                await _collection.UnHookCamera(camera.Camera.Description.Path, MotionDetectionStreamId, frameFormat);
                while (imageQueue.TryDequeue(out var image))
                {
                    image?.Dispose();
                }

                imageQueue.Clear();
                _detectorTasks.Remove(taskId);
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);
            });

            _detectorTasks.TryAdd(taskId, t);
            t.Start();

            return taskId;
        }

        public void Stop(string taskId)
        {
            if (_detectorTasks.Remove(taskId, out var t))
            {
                t.Wait(5000);
                t.Dispose();
            }
        }

        private void SendMovementVideo(ServerCamera camera, ChatId chatId, string message, uint recordLengthSec)
        {
            var tmpRecordtaskId =
                $"{TmpVideoStreamId}-{chatId}-{camera.Camera.Description.Path}";
            if (_videoRecordingTasks.TryGetValue(tmpRecordtaskId, out var _))
                //throw new ApplicationException($"Recording task is already started [{tmpRecordtaskId}]");
                return;

            var t = new Task(async () =>
            {
                try
                {
                    var fileName = await StartVideoRecording(camera, tmpRecordtaskId, chatId, recordLengthSec);
                    await _telegramService.SendVideo(chatId, fileName, $"{message}", CancellationToken.None);
                    System.IO.File.Delete(fileName);
                }
                catch (Exception ex)
                {
                    await _telegramService.SendText(chatId, $"Can't record video: {ex}", CancellationToken.None);
                }

                _videoRecordingTasks.Remove(tmpRecordtaskId);
            });//.ContinueWith(n => _videoRecordingTasks.Remove(tmpRecordtaskId));

            _videoRecordingTasks.Add(tmpRecordtaskId, t);
            t.Start();
        }

        private async Task<string> StartVideoRecording(ServerCamera camera, string tmpRecordtaskId, ChatId chatId, uint recordLengthSec)
        {

            //record and send video
            var currentTime = DateTime.Now;
            var tmpUserId = $"{MotionDetectionStreamId}-{chatId}-{currentTime.Ticks}";
            var tmpFrameFormat = new FrameFormatDto();
            var tmpImageQueue = new ConcurrentQueue<Mat>();
            var tmpCameraCancellationToken = await _collection.HookCamera(camera.Camera.Description.Path,
                tmpUserId,
                tmpImageQueue,
                tmpFrameFormat);
            if (tmpCameraCancellationToken == CancellationToken.None)
            {
                await _telegramService.SendText(chatId, $"Can not connect to camera#{camera.Camera.Description.Name}", tmpCameraCancellationToken);

                throw new ApplicationException($"Can not connect to camera#{camera.Camera.Description.Name}");
            }

            var fileName =
                VideoRecorder.VideoRecorder.SanitizeFileName(
                    $"{FilePrefix}-" +
                    $"Cam{camera.Camera.Description.Name}-" +
                    $"{chatId}-" +
                    $"{currentTime.ToString("yyyy-MM-dd")}-" +
                    $"{currentTime.ToString("HH-mm-ss")}.mp4");
            if (tmpFrameFormat.Fps <= 0.0)
                tmpFrameFormat.Fps = camera.Camera.Description.FrameFormats
                    .FirstOrDefault()?.Fps ?? 0;

            using (var recorder =
                   new VideoRecorder.VideoRecorder(fileName, tmpFrameFormat))
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
                            await _telegramService.SendText(chatId,
                                $"Can't record video: {ex}", tmpCameraCancellationToken);
                        }
                        image.Dispose();
                    }
                    else
                        await Task.Delay(10, CancellationToken.None);
                }

                await _collection.UnHookCamera(
                    camera.Camera.Description.Path,
                    tmpUserId, tmpFrameFormat);
            }

            while (tmpImageQueue.TryDequeue(out var image))
            {
                image.Dispose();
            }

            tmpImageQueue.Clear();

            return fileName;
        }

        private void StopVideoRecording(string taskId)
        {
            if (_videoRecordingTasks.Remove(taskId, out var t))
            {
                t.Wait(5000);
                t.Dispose();
            }
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
