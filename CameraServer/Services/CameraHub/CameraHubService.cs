using CameraLib;
using CameraLib.FlashCap;
using CameraLib.IP;
using CameraLib.MJPEG;

using CameraServer.Server.Models;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using OpenCvSharp;

using System.Collections.Concurrent;
using System.Threading.Channels;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CameraServer.Server.Services.CameraHub;

/// <summary>
/// Represents the state of a single camera including its Channel-based subscribers.
/// This is an internal implementation detail of CameraHubService.
/// </summary>
internal sealed class CameraState
{
    public ServerCamera Camera { get; init; }
    public ConcurrentDictionary<CameraQueueItem, Channel<Mat>> Subscribers { get; init; }

    public CameraState(ServerCamera camera)
    {
        Camera = camera ?? throw new ArgumentNullException(nameof(camera));
        Subscribers = new ConcurrentDictionary<CameraQueueItem, Channel<Mat>>();
    }

    public int SubscriberCount => Subscribers.Count;
    public bool HasSubscribers => Subscribers.Count > 0;
}

public class CameraHubService : IDisposable
{
    private const string CameraSettingsSection = "CameraSettings";
    private readonly ILogger<CameraHubService> _logger;
    private readonly CameraSettings _settings;
    private readonly int _maxBuffer;
    private readonly MatPoolManager _matPoolManager;
    private bool _disposedValue;

    public IEnumerable<ServerCamera> Cameras => _cameraRegistry.Values.Select(cs => cs.Camera);

    // Channel-only architecture: Single unified camera registry
    private readonly ConcurrentDictionary<int, CameraState> _cameraRegistry = new();

    public CameraHubService(IConfiguration configuration, ILogger<CameraHubService> logger)
    {
        _logger = logger;
        _settings = configuration.GetSection(CameraSettingsSection).Get<CameraSettings>() ?? new CameraSettings();
        _maxBuffer = _settings.MaxFrameBuffer;

        // Create Mat pool manager for frame cloning operations
        _matPoolManager = new MatPoolManager(maxPoolSize: 100, logger);

        _logger.LogInformation(
            "CameraHubService initialized with Channel-only architecture, Mat pooling enabled, max pool size: 100");

        RefreshCameraCollection(CancellationToken.None);
    }

    #region Channel-Based API (Modern, High-Performance)

    /// <summary>
    /// Hook camera with Channel-based async frame delivery.
    /// This is the only supported API for subscribing to camera frames.
    /// Provides zero-polling async performance with configurable backpressure.
    /// </summary>
    /// <param name="cameraItem">Camera subscription details</param>
    /// <param name="token">Cancellation token</param>
    /// <param name="fullMode">Backpressure handling mode when channel is full</param>
    /// <returns>Tuple of (CameraCancellationToken, ChannelReader) or (None, null) on failure</returns>
    public async Task<(CancellationToken CameraToken, ChannelReader<Mat>? Reader)> HookCamera(
        CameraQueueItem cameraItem,
        CancellationToken token,
        BoundedChannelFullMode fullMode = BoundedChannelFullMode.DropOldest)
    {
        ArgumentNullException.ThrowIfNull(cameraItem);

        if (string.IsNullOrWhiteSpace(cameraItem.CameraId))
            throw new ArgumentException("Camera ID cannot be null or empty", nameof(cameraItem));

        if (string.IsNullOrWhiteSpace(cameraItem.QueueId))
            throw new ArgumentException("Queue ID cannot be null or empty", nameof(cameraItem));

        // Find camera in registry
        var cameraState = _cameraRegistry.Values
            .FirstOrDefault(cs => cs.Camera.CameraStream.Description.Path == cameraItem.CameraId);

        if (cameraState == null)
        {
            _logger.LogError($"Camera not found: {cameraItem.CameraId}");
            return (CancellationToken.None, null);
        }

        // Create bounded channel with configurable backpressure
        var channelOptions = new BoundedChannelOptions(_maxBuffer)
        {
            FullMode = fullMode,
            SingleWriter = false,  // Multiple camera frame events can fire
            SingleReader = true    // One consumer per channel
        };
        var channel = Channel.CreateBounded<Mat>(channelOptions);

        // Add subscriber
        if (!cameraState.Subscribers.TryAdd(cameraItem, channel))
        {
            _logger.LogError($"Failed to attach client {cameraItem.QueueId} to camera {cameraItem.CameraId} (already subscribed)");
            return (CancellationToken.None, null);
        }

        // Start camera if first subscriber
        if (cameraState.SubscriberCount == 1)
        {
            cameraState.Camera.CameraStream.ImageCapturedEvent += GetImageFromCameraStreamAsync;

            if (!await cameraState.Camera.CameraStream.StartAsync(
                cameraItem.FrameFormat.Width,
                cameraItem.FrameFormat.Height,
                cameraItem.FrameFormat.Format,
                token))
            {
                cameraState.Subscribers.TryRemove(cameraItem, out _);
                cameraState.Camera.CameraStream.ImageCapturedEvent -= GetImageFromCameraStreamAsync;
                _logger.LogError($"Failed to connect to camera {cameraItem.CameraId}");
                return (CancellationToken.None, null);
            }

            _logger.LogInformation($"Camera {cameraItem.CameraId} started");
        }

        _logger.LogInformation($"Client {cameraItem.QueueId} subscribed to camera {cameraItem.CameraId} (Channel, {fullMode}, {cameraState.SubscriberCount} total)");

        return (cameraState.Camera.CameraStream.CancellationToken, channel.Reader);
    }

    /// <summary>
    /// Unhook a channel-based camera subscription and complete the channel.
    /// Stops the camera automatically if this was the last subscriber.
    /// </summary>
    public bool UnHookCamera(CameraQueueItem cameraItem)
    {
        ArgumentNullException.ThrowIfNull(cameraItem);

        if (string.IsNullOrWhiteSpace(cameraItem.CameraId))
            throw new ArgumentException("Camera ID cannot be null or empty", nameof(cameraItem));

        var cameraState = _cameraRegistry.Values
            .FirstOrDefault(cs => cs.Camera.CameraStream.Description.Path == cameraItem.CameraId);

        if (cameraState == null)
            return false;

        if (cameraState.Subscribers.TryRemove(cameraItem, out var channel))
        {
            // Complete the channel to signal consumers no more frames
            channel.Writer.Complete();

            _logger.LogInformation($"Client {cameraItem.QueueId} unsubscribed from camera {cameraItem.CameraId} ({cameraState.SubscriberCount} remaining)");

            // Stop camera if no more subscribers
            if (!cameraState.HasSubscribers)
            {
                cameraState.Camera.CameraStream.ImageCapturedEvent -= GetImageFromCameraStreamAsync;
                cameraState.Camera.CameraStream.Stop();
                _logger.LogInformation($"Camera {cameraItem.CameraId} stopped (no subscribers)");
            }

            return true;
        }

        _logger.LogError($"Failed to detach client {cameraItem.QueueId} from camera {cameraItem.CameraId}");
        return false;
    }

    #endregion

    public async Task RefreshCameraCollection(CancellationToken cancellationToken)
    {
        // discover IP cameras in the background
        Task<List<CameraDescription>>? t = null;
        if (_settings.AutoSearchIp)
        {
            _logger.Log(LogLevel.Information, "Detect IP cameras started...");
            t = IpCamera.DiscoverOnvifCamerasAsync(_settings.DiscoveryTimeOut);
        }

        // remove idle cameras from registry
        var cameras = _cameraRegistry.Values.ToArray();
        foreach (var cameraState in cameras)
        {
            if (!cameraState.Camera.CameraStream.IsRunning && !cameraState.HasSubscribers)
            {
                _cameraRegistry.TryRemove(cameraState.Camera.Id, out _);
            }
        }

        // add custom cameras
        _logger.Log(LogLevel.Information, "Adding predefined cameras...");
        foreach (var c in _settings.CustomCameras)
        {
            _logger.Log(LogLevel.Information, $"{c.Name}");

            ServerCamera? serverCamera = null;
            if (c.Type == CameraType.IP)
            {
                serverCamera = new ServerCamera(
                    new IpCamera(
                        path: c.Path,
                        name: c.Name,
                        authenticationType: c.AuthenticationType,
                        login: c.Login,
                        password: c.Password,
                        forceCameraConnect: _settings.ForceCameraConnect,
                        logger: _logger),
                    c.AllowedRoles,
                    true);
            }
            else if (c.Type == CameraType.MJPEG)
            {
                serverCamera = new ServerCamera(
                    new MjpegCamera(
                        path: c.Path,
                        name: c.Name,
                        authenticationType: c.AuthenticationType,
                        login: c.Login,
                        password: c.Password,
                        discoveryTimeout: _settings.DiscoveryTimeOut,
                        forceCameraConnect: _settings.ForceCameraConnect),
                    c.AllowedRoles,
                    true);
            }
            else if (c.Type == CameraType.USB_FC)
            {
                try
                {
                    serverCamera = new ServerCamera(
                        new UsbCameraFc(c.Path, c.Name),
                        c.AllowedRoles,
                        true);
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, ex.ToString());
                    continue;
                }
            }
            else
                continue;

            if (serverCamera != null)
            {
                serverCamera.CameraStream.FrameTimeout = _settings.FrameTimeout;
                // Use a temporary ID that will be reassigned later
                var tempId = _cameraRegistry.Count;
                _cameraRegistry.TryAdd(tempId, new CameraState(serverCamera));
            }
        }

        if (_settings.AutoSearchUsbFC)
        {
            _logger.Log(LogLevel.Information, "Autodetecting USB_FC cameras...");
            var usbFcCameras = UsbCameraFc.DiscoverUsbCameras();
            foreach (var c in usbFcCameras)
                _logger.Log(LogLevel.Information, $"USB_FC-Camera: {c.Name} - [{c.Path}]");

            // add newly discovered cameras
            foreach (var c in usbFcCameras
                         .Where(c => !_cameraRegistry.Values
                             .Any(cs => cs.Camera.CameraStream.Description.Path == c.Path)))
            {
                var serverCamera = new ServerCamera(new UsbCameraFc(c.Path), _settings.DefaultAllowedRoles);
                serverCamera.CameraStream.FrameTimeout = _settings.FrameTimeout;
                var tempId = _cameraRegistry.Count;
                _cameraRegistry.TryAdd(tempId, new CameraState(serverCamera));
            }

            // remove cameras not found by search (to not lose connection if any clients are connected)
            foreach (var cameraState in _cameraRegistry.Values
                         .Where(cs => cs.Camera.CameraStream is UsbCameraFc && !cs.Camera.Custom)
                         .Where(cs => !usbFcCameras
                             .Exists(c => c.Path == cs.Camera.CameraStream.Description.Path)))
            {
                if (!cameraState.HasSubscribers)
                    _cameraRegistry.TryRemove(cameraState.Camera.Id, out _);
            }
        }

        if (_settings.AutoSearchIp && t != null)
        {
            var ipCameras = await t;
            _logger.Log(LogLevel.Information, "Detect IP cameras stopped...");

            _logger.Log(LogLevel.Information, "Autodetecting IP cameras...");
            foreach (var c in ipCameras)
                _logger.Log(LogLevel.Information, $"IP-Camera: {c.Name} - [{c.Path}]");

            // add newly discovered cameras
            foreach (var c in ipCameras
                         .Where(c => !_cameraRegistry.Values
                             .Any(cs => cs.Camera.CameraStream.Description.Path == c.Path)))
            {
                _logger.Log(LogLevel.Information, $"Adding IP-Camera: {c.Name} - [{c.Path}]");
                var serverCamera = new ServerCamera(new IpCamera(c.Path, logger: _logger), _settings.DefaultAllowedRoles);
                serverCamera.CameraStream.FrameTimeout = _settings.FrameTimeout;
                var tempId = _cameraRegistry.Count;
                _cameraRegistry.TryAdd(tempId, new CameraState(serverCamera));
            }

            // remove cameras not found by search (to not lose connection if any clients are connected)
            foreach (var cameraState in _cameraRegistry.Values
                         .Where(cs => cs.Camera.CameraStream is IpCamera && !cs.Camera.Custom)
                         .Where(cs => !ipCameras
                             .Exists(c => c.Path == cs.Camera.CameraStream.Description.Path)))
            {
                if (!cameraState.HasSubscribers)
                    _cameraRegistry.TryRemove(cameraState.Camera.Id, out _);
            }
        }

        // Reassign sequential IDs
        var n = 0;
        var orderedCameras = _cameraRegistry.OrderBy(kvp => kvp.Key).ToList();
        _cameraRegistry.Clear();

        foreach (var (_, cameraState) in orderedCameras)
        {
            cameraState.Camera.Id = n;
            _cameraRegistry.TryAdd(n, cameraState);
            n++;
        }

        _logger.Log(LogLevel.Information, $"Camera refresh complete. Total cameras: {_cameraRegistry.Count}");
    }

    public ServerCamera GetCamera(int cameraId, ICameraUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        if (!_cameraRegistry.TryGetValue(cameraId, out var cameraState))
            throw new ArgumentOutOfRangeException(nameof(cameraId), $"No camera available: \"{cameraId}\"");

        if (!cameraState.Camera.AllowedRoles.Intersect(currentUser.Roles).Any())
            throw new ArgumentOutOfRangeException(nameof(cameraId), $"No camera available: \"{cameraId}\"");

        return cameraState.Camera;
    }

    public ServerCamera GetCamera(string cameraId, ICameraUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        if (string.IsNullOrWhiteSpace(cameraId))
            throw new ArgumentException("Camera ID cannot be null or empty", nameof(cameraId));

        var cameraState = _cameraRegistry.Values
            .FirstOrDefault(cs => cs.Camera.CameraStream.Description.Path == cameraId);

        if (cameraState == null || !cameraState.Camera.AllowedRoles.Intersect(currentUser.Roles).Any())
            throw new ArgumentOutOfRangeException(nameof(cameraId), $"No camera available: \"{cameraId}\"");

        return cameraState.Camera;
    }

    #region Frame Distribution (Channel-Only)

    /// <summary>
    /// Distributes frames to all Channel subscribers.
    /// Uses async for Channel delivery with timeout and error handling.
    /// First subscriber gets the original frame (zero-copy), others get pooled clones.
    /// </summary>
    private async void GetImageFromCameraStreamAsync(ICamera camera, Mat image)
    {
        var cameraState = _cameraRegistry.Values
            .FirstOrDefault(cs => cs.Camera.CameraStream == camera);

        // If no subscribers, dispose and return
        if (cameraState == null || !cameraState.HasSubscribers)
        {
            image?.Dispose();
            return;
        }

        var subscribers = cameraState.Subscribers.ToArray();
        var firstCopy = true;
        var writeTasks = new List<Task>(subscribers.Length);

        // Distribute to all Channel subscribers
        foreach (var (item, channel) in subscribers)
        {
            Mat frameToSend;

            if (firstCopy)
            {
                firstCopy = false;
                frameToSend = image; // First subscriber gets original (zero-copy optimization)
            }
            else
            {
                // Clone for additional subscribers using pooled Mat
                frameToSend = _matPoolManager.Rent(image.Width, image.Height, image.Type());
                image.CopyTo(frameToSend);
            }

            // Write to channel asynchronously (fire and forget with error handling)
            var writeTask = WriteToChannelAsync(channel, frameToSend, item, image);
            writeTasks.Add(writeTask);
        }

        // Don't await here to avoid blocking the camera event
        // Errors are handled in WriteToChannelAsync
        if (writeTasks.Count > 0)
            _ = Task.WhenAll(writeTasks);

        // Log statistics periodically (every 30 seconds, only at second 0 or 30)
        if (subscribers.Length > 0 && (DateTime.Now.Second == 0 || DateTime.Now.Second == 30))
        {
            _logger.LogDebug($"Frame distributed to {subscribers.Length} channel subscribers (Camera: {cameraState.Camera.CameraStream.Description.Name})");
        }
    }

    /// <summary>
    /// Writes a frame to a channel with timeout and error handling.
    /// </summary>
    private async Task WriteToChannelAsync(Channel<Mat> channel, Mat frameToSend, CameraQueueItem cameraItem, Mat originalFrame)
    {
        try
        {
            // Try to write with a short timeout to avoid blocking
            using var cts = new CancellationTokenSource(100); // 100ms timeout

            if (await channel.Writer.WaitToWriteAsync(cts.Token))
            {
                await channel.Writer.WriteAsync(frameToSend, cts.Token);
            }
            else
            {
                // Channel closed or timeout
                if (frameToSend != originalFrame)
                    ReturnOrDisposeMat(frameToSend);

                _logger.LogWarning($"Channel {cameraItem.QueueId} write timeout or closed");
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout - return frame to pool
            if (frameToSend != originalFrame)
                ReturnOrDisposeMat(frameToSend);

            _logger.LogDebug($"Channel {cameraItem.QueueId} write timeout");
        }
        catch (ChannelClosedException)
        {
            // Channel was completed - clean up
            if (frameToSend != originalFrame)
                ReturnOrDisposeMat(frameToSend);

            _logger.LogDebug($"Channel {cameraItem.QueueId} was closed");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error writing to channel {cameraItem.QueueId}: {ex.Message}");

            if (frameToSend != originalFrame)
                ReturnOrDisposeMat(frameToSend);
        }
    }

    #endregion

    /// <summary>
    /// Returns a Mat to the pool if it belongs to our pool, otherwise disposes it.
    /// This method should be called by consumers when they're done with a Mat from CameraHubService.
    /// </summary>
    public void ReturnOrDisposeMat(Mat? mat)
    {
        if (mat == null || mat.IsDisposed)
            return;

        // Try to return to pool first; if pool doesn't accept it, dispose
        _matPoolManager.Return(mat);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _logger.LogInformation("CameraHubService pooling statistics:");
                _matPoolManager?.LogStatistics();

                // Complete all channels to signal consumers
                var totalChannels = 0;
                foreach (var cameraState in _cameraRegistry.Values)
                {
                    foreach (var channel in cameraState.Subscribers.Values)
                    {
                        channel.Writer.Complete();
                        totalChannels++;
                    }
                }

                _logger.LogInformation($"Completed {totalChannels} channels across {_cameraRegistry.Count} cameras");

                _matPoolManager?.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
