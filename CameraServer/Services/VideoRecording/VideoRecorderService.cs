using CameraLib;

using CameraServer.Server.Auth;
using CameraServer.Server.Models;
using CameraServer.Server.Services.CameraHub;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenCvSharp;

using System.Collections.Concurrent;
using System.Threading.Channels;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CameraServer.Server.Services.VideoRecording;

public class VideoRecorderService : IHostedService, IDisposable
{
    private const string VideoRecorderTempConfig = "appsettings-recorder.json";
    private const string RecorderConfigSection = "Recorder";
    private const string RecorderStreamId = "Recorder";
    private const string DefaultVideoFileExtension = "mp4";

    private readonly IUserManager _manager;
    private readonly CameraHubService _collection;
    private readonly ILogger<VideoRecorderService> _logger;
    private readonly MatPoolManager _matPoolManager;
    public readonly RecorderSettings Settings;
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
        Settings = configuration.GetSection(RecorderConfigSection)?.Get<RecorderSettings>() ?? new RecorderSettings();
        Directory.CreateDirectory(Settings.StoragePath);

        // Create Mat pool manager for video recording operations
        // Pool size based on expected concurrent recordings
        _matPoolManager = new MatPoolManager(maxPoolSize: 50, logger);

        _logger.LogInformation(
            "VideoRecorderService initialized with Mat pooling, max pool size: 50");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var record in Settings.RecordCameras)
        {
            try
            {
                _logger.Log(LogLevel.Information, $"Starting recording for: {record.CameraId}");
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
        ArgumentNullException.ThrowIfNull(recordTask);

        if (string.IsNullOrWhiteSpace(recordTask.User))
            throw new ArgumentException("User cannot be null or empty", nameof(recordTask));

        if (string.IsNullOrWhiteSpace(recordTask.CameraId))
            throw new ArgumentException("CameraId cannot be null or empty", nameof(recordTask));

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

    public void Stop(Guid taskId)
    {
        try
        {
            var task = _recorderTasks.FirstOrDefault(n => n.Key.Id == taskId);
            if (task.Key != null)
                Stop(task.Key);
            else
                _logger.LogWarning($"Recording task {taskId} not found");
        }
        catch (Exception e)
        {
            _logger.LogError($"Error getting the detector task #{taskId}: {e}");
            throw;
        }
    }

    public void Stop(string taskId)
    {
        var task = _recorderTasks.FirstOrDefault(n => n.Key.TaskId == taskId);
        if (task.Key != null)
            Stop(task.Key);
    }

    public void Stop(RecordCameraTask? recordTask)
    {
        if (recordTask == null)
        {
            _logger.LogWarning("Attempted to stop null recording task");
            return;
        }

        if (_recorderTasks.TryRemove(recordTask, out var t))
        {
            var existingTask = TaskConfig.ConfigStorage.FirstOrDefault(n => n.Equals(recordTask));
            if (existingTask != null)
            {
                TaskConfig.ConfigStorage.Remove(existingTask);
            }

            TaskConfig.SaveConfig();

            try
            {
                t?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"{this} Stop() failed: {ex}");
            }
        }
    }

    public static string GenerateTaskId(string cameraPath, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(cameraPath))
            throw new ArgumentException("Camera path cannot be null or empty", nameof(cameraPath));

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

        try
        {
            // Use Channel-based async frame delivery (eliminates polling)
            var (cameraCancellationToken, channelReader) = await _collection.HookCamera(
                newCameraItem,
                CancellationToken.None,
                BoundedChannelFullMode.DropOldest);  // Drop oldest if encoder falls behind

            if (channelReader == null || cameraCancellationToken == CancellationToken.None)
            {
                _logger.LogError($"Can not connect to camera [{camera.CameraStream.Description.Path}]");
                return;
            }

            _logger.LogInformation($"Video recorder started with Channel-based delivery (task: {newTask.TaskId})");

            var stopTask = false;
            while (!cameraCancellationToken.IsCancellationRequested && !stopTask)
            {
                var currentTime = DateTime.Now;
                var fileName = $"{Settings.StoragePath.TrimEnd('\\')}\\" +
                               $"{VideoRecorder.SanitizeFileName($"{camera.CameraStream.Description.Name}" +
                                                                 $"-{newTask.FrameFormat.Width}x{newTask.FrameFormat.Height}" +
                                                                 $"-{currentTime:yyyy-MM-dd}" +
                                                                 $"-{currentTime:HH-mm-ss}" +
                                                                 $".{DefaultVideoFileExtension}")}";

                using (var recorder = new VideoRecorder(fileName,
                           new CameraServer.Shared.DTO.FrameFormatDto
                           {
                               Width = 0,
                               Height = 0,
                               Format = string.Empty,
                               Fps = camera.CameraStream.CurrentFps
                           },
                           newTask.Quality,
                           _logger,
                           _matPoolManager)
                {
                    Codec = newTask.Codec
                })
                {
                    var timeOut = DateTime.Now.AddSeconds(Settings.VideoFileLengthSeconds);

                    // Event-driven frame processing - NO POLLING! ✅
                    await foreach (var image in channelReader.ReadAllAsync(cameraCancellationToken))
                    {
                        // Check timeout
                        if (DateTime.Now >= timeOut)
                            break;

                        try
                        {
                            recorder.SaveFrame(image);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Exception while video file (Task) recording: {ex}");
                        }
                        finally
                        {
                            _collection.ReturnOrDisposeMat(image);
                        }

                        // Check if task should stop
                        stopTask = !_recorderTasks.TryGetValue(newTask, out _);
                        if (stopTask)
                            break;
                    }
                }

                stopTask = !_recorderTasks.TryGetValue(newTask, out _);
            }

            _logger.LogInformation($"Video recorder stopped (task: {newTask.TaskId})");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation($"Video recorder cancelled (task: {newTask.TaskId})");
        }
        catch (ChannelClosedException)
        {
            _logger.LogInformation($"Video recorder channel closed (task: {newTask.TaskId})");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception in VideoRecorder task: {ex}");
        }
        finally
        {
            // Clean up
            _collection.UnHookCamera(newCameraItem);
            _recorderTasks.TryRemove(newTask, out _);

            _logger.LogInformation($"Video recorder cleanup complete (task: {newTask.TaskId})");
        }
    }

    public async Task<string> RecordVideoFile(ServerCamera camera,
        string streamId,
        string fileStoragePath,
        string filePrefix,
        uint recordLengthSec,
        CameraServer.Shared.DTO.FrameFormatDto? frameFormat = null,
        byte quality = 90,
        string codec = "",
        List<Mat?>? imageBuffer = null)
    {
        ArgumentNullException.ThrowIfNull(camera);

        if (string.IsNullOrWhiteSpace(streamId))
            throw new ArgumentException("Stream ID cannot be null or empty", nameof(streamId));

        if (string.IsNullOrWhiteSpace(fileStoragePath))
            throw new ArgumentException("File storage path cannot be null or empty", nameof(fileStoragePath));

        if (string.IsNullOrWhiteSpace(filePrefix))
            throw new ArgumentException("File prefix cannot be null or empty", nameof(filePrefix));

        var currentTime = DateTime.Now;
        imageBuffer ??= [];
        frameFormat ??= new CameraServer.Shared.DTO.FrameFormatDto();
        var newCameraItem = new CameraQueueItem(camera.CameraStream.Description.Path,
            streamId,
            frameFormat);

        var fileName = VideoRecorder.SanitizeFileName(
            $"{fileStoragePath.TrimEnd('\\')}\\" +
            $"{filePrefix}-" +
            $"Cam{camera.CameraStream.Description.Name}-" +
            $"{streamId}-" +
            $"{currentTime:yyyy-MM-dd}_" +
            $"{currentTime:HH-mm-ss}.{DefaultVideoFileExtension}");

        var frameCount = 0;
        try
        {
            // Use Channel-based async frame delivery (eliminates polling)
            var (tmpCameraCancellationToken, channelReader) = await _collection.HookCamera(
                newCameraItem,
                CancellationToken.None,
                BoundedChannelFullMode.DropOldest);

            if (channelReader == null || tmpCameraCancellationToken == CancellationToken.None)
                throw new ApplicationException($"Can not connect to camera#{camera.CameraStream.Description.Name}");

            if (frameFormat.Fps <= 0)
                frameFormat.Fps = camera.CameraStream.CurrentFps;

            using (var recorder = new VideoRecorder(fileName, frameFormat, quality, _logger, _matPoolManager))
            {
                recorder.Codec = codec;

                // Encode buffered frames first (from motion detection, etc.)
                if (imageBuffer.Count > 0)
                {
                    foreach (var image in imageBuffer)
                    {
                        try
                        {
                            if (image != null)
                            {
                                recorder.SaveFrame(image);
                                frameCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Exception while video file (Buffer) recording: {ex}");
                            break;
                        }
                    }
                }

                var timeOut = DateTime.Now.AddSeconds(recordLengthSec);

                // Event-driven frame encoding - NO POLLING! ✅
                await foreach (var image in channelReader.ReadAllAsync(tmpCameraCancellationToken))
                {
                    // Check timeout
                    if (DateTime.Now >= timeOut)
                        break;

                    try
                    {
                        recorder.SaveFrame(image);
                        frameCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Exception while video file (Stream) recording: {ex}");
                        break;  // Exit on encoding error
                    }
                    finally
                    {
                        _collection.ReturnOrDisposeMat(image);
                    }
                }
            }

            _logger.LogInformation($"Video file recording complete: {fileName}, frames: {frameCount}");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation($"Video file recording cancelled: {fileName}");
        }
        catch (ChannelClosedException)
        {
            _logger.LogInformation($"Video file recording channel closed: {fileName}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception in video file recorder: {ex}");
        }
        finally
        {
            _collection.UnHookCamera(newCameraItem);
        }

        if (frameCount == 0)
            throw new ApplicationException($"No frames written to file: {fileName}");

        if (!File.Exists(fileName))
            throw new ApplicationException($"Error writing to file. File doesn't exist: {fileName}");

        return fileName;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _logger.Log(LogLevel.Information, "Disposing VideoRecorderService");

                // Log pooling statistics before disposal
                _logger.LogInformation("VideoRecorderService pooling statistics:");
                _matPoolManager?.LogStatistics();
                _matPoolManager?.Dispose();

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
