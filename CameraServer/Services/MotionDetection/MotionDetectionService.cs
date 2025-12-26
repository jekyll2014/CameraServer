using CameraServer.Server.Auth;
using CameraServer.Server.Models;
using CameraServer.Server.Services.CameraHub;
using CameraServer.Server.Services.Telegram;
using CameraServer.Server.Services.VideoRecording;
using CameraServer.Shared.DTO;
using CameraServer.Shared.Enum;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenCvSharp;

using System.Collections.Concurrent;
using System.Threading.Channels;

using Telegram.Bot.Types;

using DateTime = System.DateTime;
using File = System.IO.File;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CameraServer.Server.Services.MotionDetection;

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

    public IEnumerable<Guid> TaskList => _detectorTasks.Select(n => n.Key.Id);
    public IEnumerable<MotionDetectionCameraTask> TaskDescriptions => _detectorTasks.Select(n => n.Key);

    public delegate void MotionDetectProcessedEventHandler(MotionDetectionCameraTask detectorTask, Mat? image);
    public event MotionDetectProcessedEventHandler? ImageProcessedEvent;

    private readonly ConcurrentDictionary<MotionDetectionCameraTask, Task> _detectorTasks = new();
    private readonly ConcurrentDictionary<string, Task> _videoRecordingTasks = new();
    private readonly ConcurrentDictionary<string, DateTime> _notificationsTextLast = new();
    private readonly ConcurrentDictionary<string, DateTime> _notificationsImageLast = new();
    private readonly ConcurrentDictionary<string, DateTime> _notificationsVideoLast = new();

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
                if (Start(record) == Guid.Empty)
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

                if (Start(record) == Guid.Empty)
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

    public Guid Start(MotionDetectionCameraSettingDto detectTask)
    {
        ArgumentNullException.ThrowIfNull(detectTask);

        if (string.IsNullOrWhiteSpace(detectTask.User))
            throw new ArgumentException("User cannot be null or empty", nameof(detectTask));

        if (string.IsNullOrWhiteSpace(detectTask.CameraId))
            throw new ArgumentException("CameraId cannot be null or empty", nameof(detectTask));

        if (!detectTask.Notifications.Any())
            return Guid.Empty;

        // Log incoming parameters for debugging
        _logger.LogInformation("Motion detector Start() called with parameters: " +
            "CameraId={CameraId}, User={User}, " +
            "FrameFormat={FrameFormatWidth}x{FrameFormatHeight} {FrameFormatFormat}, " +
            "MotionDetectParams: Width={ParamWidth}, Height={ParamHeight}, " +
            "DelayMs={DelayMs}, NoiseThreshold={NoiseThreshold}, ChangeLimit={ChangeLimit}%",
            detectTask.CameraId,
            detectTask.User,
            detectTask.FrameFormat?.Width ?? 0,
            detectTask.FrameFormat?.Height ?? 0,
            detectTask.FrameFormat?.Format ?? "null",
            detectTask.MotionDetectParameters?.Width ?? 0,
            detectTask.MotionDetectParameters?.Height ?? 0,
            detectTask.MotionDetectParameters?.DetectorDelayMs ?? 0,
            detectTask.MotionDetectParameters?.NoiseThreshold ?? 0,
            detectTask.MotionDetectParameters?.ChangeLimit ?? 0);

        // Ensure we don't share the default parameters instance between tasks.
        // Clone defaults into a new instance if parameters are null.
        if (detectTask.MotionDetectParameters == null)
        {
            var def = Settings.DefaultMotionDetectParameters;
            detectTask.MotionDetectParameters = new MotionDetectorParametersDto
            {
                Width = def.Width,
                Height = def.Height,
                DetectorDelayMs = def.DetectorDelayMs,
                NoiseThreshold = def.NoiseThreshold,
                ChangeLimit = def.ChangeLimit,
                TextNotificationDelay = def.TextNotificationDelay,
                ImageNotificationDelay = def.ImageNotificationDelay,
                VideoNotificationDelay = def.VideoNotificationDelay,
                KeepImageBuffer = def.KeepImageBuffer
            };
        }

        var userDto = _manager.GetUserInfo(detectTask.User);
        if (userDto == null)
            throw new ApplicationException($"User [{detectTask.User}] not authorised to start recording.");

        if (detectTask.MotionDetectParameters.Width <= 0)
            detectTask.MotionDetectParameters.Width = Settings.DefaultMotionDetectParameters.Width;

        if (detectTask.MotionDetectParameters.Height <= 0)
            detectTask.MotionDetectParameters.Height = Settings.DefaultMotionDetectParameters.Height;

        // FIXED: set DetectorDelayMs (previous code erroneously set Width here)
        if (detectTask.MotionDetectParameters.DetectorDelayMs <= 0)
            detectTask.MotionDetectParameters.DetectorDelayMs = Settings.DefaultMotionDetectParameters.DetectorDelayMs;

        if (detectTask.MotionDetectParameters.NoiseThreshold <= 0)
            detectTask.MotionDetectParameters.NoiseThreshold = Settings.DefaultMotionDetectParameters.NoiseThreshold;

        if (detectTask.MotionDetectParameters.ChangeLimit <= 0)
            detectTask.MotionDetectParameters.ChangeLimit = Settings.DefaultMotionDetectParameters.ChangeLimit;

        _logger.LogInformation("Motion detector parameters after normalization: " +
            "Width={FinalWidth}, Height={FinalHeight}, " +
            "DelayMs={FinalDelayMs}, NoiseThreshold={FinalNoiseThreshold}, ChangeLimit={FinalChangeLimit}%",
            detectTask.MotionDetectParameters.Width,
            detectTask.MotionDetectParameters.Height,
            detectTask.MotionDetectParameters.DetectorDelayMs,
            detectTask.MotionDetectParameters.NoiseThreshold,
            detectTask.MotionDetectParameters.ChangeLimit);

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

        var task = new MotionDetectionCameraTask(detectTask);
        var taskId = task.Id;

        _logger.Log(LogLevel.Information, $"Starting detection task [{taskId}] for user [{task.User}]");

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
            taskId = Guid.Empty;

        return taskId;
    }

    public async Task Stop(Guid taskId)
    {
        if (taskId == Guid.Empty)
            return;

        try
        {
            var task = _detectorTasks.FirstOrDefault(n => n.Key.Id == taskId);
            if (task.Key != null)
                await Stop(task.Key);
            else
                _logger.LogWarning($"Task {taskId} not found");
        }
        catch (Exception e)
        {
            _logger.LogError($"Error getting the detector task #{taskId}: {e}");
            throw;
        }
    }

    private async Task Stop(MotionDetectionCameraTask? detectionTask)
    {
        if (detectionTask == null)
        {
            _logger.LogWarning("Attempted to stop null detection task");
            return;
        }

        _logger.Log(LogLevel.Information, $"Stopping detection task [{detectionTask.Id}] for user [{detectionTask.User}]");
        if (_detectorTasks.TryRemove(detectionTask, out var t))
        {
            var existingTask = TaskConfig.ConfigStorage.FirstOrDefault(n => n.Equals(detectionTask));
            if (existingTask != null)
            {
                TaskConfig.ConfigStorage.Remove(existingTask);
            }

            CleanEmptyTasks();
            TaskConfig.SaveConfig();

            try
            {
                await t?.WaitAsync(CancellationToken.None);
                t?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"{this} Stop() failed: {ex}");
            }
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
            MotionDetectionStreamId + motionDetectTask.Id,
            motionDetectTask.FrameFormat);

        var lastImagesQueue = new ConcurrentQueue<Mat?>();
        ChannelReader<Mat>? channelReader = null;

        try
        {
            // Use Channel-based async frame delivery (eliminates polling)
            var (cameraCancellationToken, reader) = await _collection.HookCamera(
                newCameraItem,
                CancellationToken.None,
                BoundedChannelFullMode.DropOldest);  // Drop oldest frames if processing is slow

            if (reader == null || cameraCancellationToken == CancellationToken.None)
            {
                _logger.LogError($"Can not connect to camera [{camera.CameraStream.Description.Path}]");
                return;
            }

            channelReader = reader;

            // Start motion detection
            var stopTask = false;
            motionDetectTask.MotionDetectParameters ??= new MotionDetectorParametersDto();

            using (var motionDetector = new MotionDetector(motionDetectTask.MotionDetectParameters, _logger))
            {
                var maxBufferCount = Settings.DefaultMotionDetectParameters.KeepImageBuffer;

                _logger.LogInformation($"Motion detector started with Channel-based delivery (task: {motionDetectTask.Id})");

                // Event-driven frame processing - NO POLLING! ✅
                await foreach (var image in channelReader.ReadAllAsync(cameraCancellationToken))
                {
                    lastImagesQueue.Enqueue(image);

                    if (motionDetector.DetectMovement(image))
                    {
                        _logger.LogInformation("Motion detected!!!");

                        var buffer = lastImagesQueue.ToList();
                        lastImagesQueue = new ConcurrentQueue<Mat?>();

                        SendNotifications(motionDetectTask.Notifications,
                            camera,
                            userDto,
                            buffer,
                            cameraCancellationToken);

                        foreach (var img in buffer)
                            _collection.ReturnOrDisposeMat(img);
                    }

                    if (ImageProcessedEvent != null)
                        ImageProcessedEvent?.Invoke(motionDetectTask, motionDetector.ProcessedFrame?.Clone());

                    // Maintain buffer size
                    while (lastImagesQueue.Count >= maxBufferCount)
                    {
                        if (lastImagesQueue.TryDequeue(out var oldImage))
                            _collection.ReturnOrDisposeMat(oldImage);
                    }

                    // Check if task should stop
                    if (stopTask || !_detectorTasks.ContainsKey(motionDetectTask))
                        break;

                    stopTask = !_detectorTasks.Any(n => n.Key.Id == motionDetectTask.Id);
                }

                _logger.LogInformation($"Motion detector stopped (task: {motionDetectTask.Id})");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation($"Motion detector cancelled (task: {motionDetectTask.Id})");
        }
        catch (ChannelClosedException)
        {
            _logger.LogInformation($"Motion detector channel closed (task: {motionDetectTask.Id})");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception in MotionDetector task: {ex}");
        }
        finally
        {
            // Clean up
            _collection.UnHookCamera(newCameraItem);

            // Return any buffered frames to pool
            while (lastImagesQueue.TryDequeue(out var oldImage))
                _collection.ReturnOrDisposeMat(oldImage);

            _detectorTasks.TryRemove(motionDetectTask, out _);

            _logger.LogInformation($"Motion detector cleanup complete (task: {motionDetectTask.Id})");
        }
    }

    private void SendNotifications(IReadOnlyCollection<NotificationParametersDto> notificationParams,
        ServerCamera camera,
        UserDto user,
        List<Mat?> bufferedImages,
        CancellationToken cameraCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notificationParams);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(bufferedImages);

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
                try
                {
                    await SendMovementVideoMulti(camera,
                        videoNotifications,
                        user.DefaultCodec,
                        bufferedImages,
                        _telegramService._settings.DefaultVideoQuality);
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Can't send motion video: {ex}");
                }
            }, TaskCreationOptions.LongRunning);

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
                    await SendMovementTextMulti(textNotifications),
                    TaskCreationOptions.LongRunning);

            //t.ConfigureAwait(false);
            t.Start();
            tasks.Add(t);
        }

        Task.WaitAll([.. tasks], cameraCancellationToken);
    }

    private async Task SendMovementTextMulti(IReadOnlyCollection<NotificationParametersDto> notificationParams)
    {
        ArgumentNullException.ThrowIfNull(notificationParams);

        if (notificationParams.Count == 0)
            return;

        var currentTime = DateTime.Now;
        foreach (var notificationParam in notificationParams)
        {
            if (notificationParam == null || string.IsNullOrWhiteSpace(notificationParam.Destination))
                continue;

            var dest = notificationParam.Destination;
            if (_notificationsTextLast.TryGetValue(dest, out var lastNotificationTime))
            {
                if (currentTime.Subtract(lastNotificationTime).TotalSeconds < Settings.DefaultMotionDetectParameters.TextNotificationDelay)
                    continue;

                _notificationsTextLast[dest] = currentTime;
            }
            else
            {
                _notificationsTextLast.TryAdd(dest, currentTime);
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
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(notificationParams);

        if (notificationParams.Length <= 0)
            return;

        var currentTime = DateTime.Now;
        foreach (var notificationParam in notificationParams)
        {
            if (notificationParam == null || string.IsNullOrWhiteSpace(notificationParam.Destination))
                continue;

            var dest = notificationParam.Destination;
            if (_notificationsImageLast.TryGetValue(dest, out var lastNotificationTime))
            {
                if (currentTime.Subtract(lastNotificationTime).TotalSeconds < Settings.DefaultMotionDetectParameters.ImageNotificationDelay)
                    continue;

                _notificationsImageLast[dest] = currentTime;
            }
            else
            {
                _notificationsImageLast.TryAdd(dest, currentTime);
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
                                    new(ImwriteFlags.JpegQuality, _videoRecorderService.Settings.DefaultVideoQuality)
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
        List<Mat?>? bufferedImages,
        byte quality)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(notificationParams);

        if (notificationParams.Count == 0)
            return;

        var destinationTotal = notificationParams
            .Where(n => n != null && !string.IsNullOrWhiteSpace(n.Destination))
            .Select(n => n.Destination)
            .Aggregate((n, m) => m += $" {n}");

        if (string.IsNullOrWhiteSpace(destinationTotal))
        {
            _logger.LogWarning("No valid destinations for video notifications");
            return;
        }

        var tmpRecordtaskId =
            $"{TmpVideoStreamId}-{destinationTotal}-{camera.CameraStream.Description.Path}";
        if (_videoRecordingTasks.TryGetValue(tmpRecordtaskId, out var _))
            return;

        // Clone buffered images to avoid using disposed frames in the async task.
        // The original bufferedImages list will be disposed by the motion detector,
        // but we need the frames to remain valid during async video recording.
        var clonedImages = bufferedImages != null && bufferedImages.Count > 0
            ? new List<Mat?>(bufferedImages.Select(img => img?.Clone()))
            : new List<Mat?>();

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
                    clonedImages);

                foreach (var notificationParam in notificationParams)
                {
                    if (notificationParam == null || string.IsNullOrWhiteSpace(notificationParam.Destination))
                        continue;

                    var dest = notificationParam.Destination;
                    if (_notificationsVideoLast.TryGetValue(dest, out var lastNotificationTime))
                    {
                        if (currentTime.Subtract(lastNotificationTime).TotalSeconds <
                            Settings.DefaultMotionDetectParameters.VideoNotificationDelay)
                            continue;

                        _notificationsVideoLast[dest] = currentTime;
                    }
                    else
                    {
                        _notificationsVideoLast.TryAdd(dest, currentTime);
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
            finally
            {
                // Clean up cloned images
                foreach (var img in clonedImages)
                    img?.Dispose();
            }

            _videoRecordingTasks.TryRemove(tmpRecordtaskId, out _);
        }, TaskCreationOptions.LongRunning);

        //await t.ConfigureAwait(false);
        t.Start();
        _videoRecordingTasks.TryAdd(tmpRecordtaskId, t);
    }

    public Guid GetTaskId(string cameraPath, string user)
    {
        if (string.IsNullOrWhiteSpace(cameraPath))
            throw new ArgumentException("Camera path cannot be null or empty", nameof(cameraPath));

        if (string.IsNullOrWhiteSpace(user))
            throw new ArgumentException("User cannot be null or empty", nameof(user));

        var task = _detectorTasks.FirstOrDefault(n => n.Key.CameraId == cameraPath && n.Key.User == user);
        return task.Key?.Id ?? Guid.Empty;
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
