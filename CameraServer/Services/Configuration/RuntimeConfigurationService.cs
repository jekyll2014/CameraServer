using CameraServer.Server.Models;
using CameraServer.Server.Services.AntiBruteForce;
using CameraServer.Server.Services.MotionDetection;
using CameraServer.Server.Services.Telegram;
using CameraServer.Server.Services.VideoRecording;
using CameraServer.Shared.DTO;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CameraServer.Server.Services.Configuration;

/// <summary>
/// Manages runtime configuration that can be updated without server restart.
/// Supports:
/// - Telegram settings (token, timeouts, quality defaults)
/// - Brute force detection thresholds
/// - Motion detection defaults
/// - Video recording settings defaults
/// </summary>
public interface IRuntimeConfigurationService
{
    /// <summary>
    /// Gets current Telegram settings.
    /// </summary>
    TelegeramSettings GetTelegramSettings();

    /// <summary>
    /// Updates Telegram settings at runtime.
    /// </summary>
    void UpdateTelegramSettings(TelegeramSettings settings);

    /// <summary>
    /// Gets current brute force detection settings.
    /// </summary>
    BruteForceDetectionSettings GetBruteForceDetectionSettings();

    /// <summary>
    /// Updates brute force detection settings at runtime.
    /// </summary>
    void UpdateBruteForceDetectionSettings(BruteForceDetectionSettings settings);

    /// <summary>
    /// Gets current motion detection settings.
    /// </summary>
    MotionDetectionSettings GetMotionDetectionSettings();

    /// <summary>
    /// Updates motion detection settings at runtime.
    /// </summary>
    void UpdateMotionDetectionSettings(MotionDetectionSettings settings);

    /// <summary>
    /// Gets current video recording settings.
    /// </summary>
    RecorderSettings GetVideoRecordingSettings();

    /// <summary>
    /// Updates video recording settings at runtime.
    /// </summary>
    void UpdateVideoRecordingSettings(RecorderSettings settings);

    /// <summary>
    /// Gets the external host URL.
    /// </summary>
    string GetExternalHostUrl();

    /// <summary>
    /// Updates the external host URL.
    /// </summary>
    void UpdateExternalHostUrl(string url);

    /// <summary>
    /// Gets cookie expiration time in minutes.
    /// </summary>
    int GetCookieExpireTimeMinutes();

    /// <summary>
    /// Updates cookie expiration time.
    /// </summary>
    void UpdateCookieExpireTimeMinutes(int minutes);

    /// <summary>
    /// Gets whether basic authentication is allowed.
    /// </summary>
    bool GetAllowBasicAuthentication();

    /// <summary>
    /// Updates whether basic authentication is allowed.
    /// </summary>
    void UpdateAllowBasicAuthentication(bool allow);

    /// <summary>
    /// Saves all runtime configuration to persistent storage.
    /// </summary>
    void PersistConfiguration();

    /// <summary>
    /// Event raised when configuration changes.
    /// </summary>
    event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;
}

public class ConfigurationChangedEventArgs : EventArgs
{
    public string PropertyName { get; set; } = string.Empty;
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
}

public class RuntimeConfigurationService : IRuntimeConfigurationService
{
    private readonly ILogger<RuntimeConfigurationService> _logger;
    private readonly Config<CameraSettings> _cameraConfig;
    private readonly Config<TelegeramSettings> _telegramConfig;
    private readonly Config<BruteForceDetectionSettings> _bruteForceConfig;
    private readonly Config<MotionDetectionSettings> _motionDetectionConfig;
    private readonly Config<RecorderSettings> _videoRecordingConfig;
    private readonly IConfiguration _configuration;

    // Runtime settings cache
    private TelegeramSettings _telegramSettings = new();
    private BruteForceDetectionSettings _bruteForceSettings = new();
    private MotionDetectionSettings _motionDetectionSettings = new();
    private RecorderSettings _videoRecordingSettings = new();
    private string _externalHostUrl = string.Empty;
    private int _cookieExpireTimeMinutes = 60;
    private bool _allowBasicAuthentication = true;

    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

    public RuntimeConfigurationService(
        IConfiguration configuration,
        ILogger<RuntimeConfigurationService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        // Initialize configuration managers
        _cameraConfig = new Config<CameraSettings>("appsettings.json", autoCreateConfig: true);
        _telegramConfig = new Config<TelegeramSettings>("appsettings.json", autoCreateConfig: true);
        _bruteForceConfig = new Config<BruteForceDetectionSettings>("appsettings.json", autoCreateConfig: true);
        _motionDetectionConfig = new Config<MotionDetectionSettings>("appsettings.json", autoCreateConfig: true);
        _videoRecordingConfig = new Config<RecorderSettings>("appsettings.json", autoCreateConfig: true);

        // Load initial settings from configuration
        LoadConfigurationFromSource();

        _logger.LogInformation("RuntimeConfigurationService initialized");
    }

    #region Telegram Settings

    public TelegeramSettings GetTelegramSettings()
    {
        return new TelegeramSettings
        {
            Token = _telegramSettings.Token,
            ReconnectTimeout = _telegramSettings.ReconnectTimeout,
            DefaultVideoTime = _telegramSettings.DefaultVideoTime,
            DefaultVideoQuality = _telegramSettings.DefaultVideoQuality,
            DefaultImageQuality = _telegramSettings.DefaultImageQuality
        };
    }

    public void UpdateTelegramSettings(TelegeramSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Validate settings
        if (settings.ReconnectTimeout < 1)
            throw new ArgumentException("ReconnectTimeout must be at least 1 second");

        if (settings.DefaultVideoQuality < 1 || settings.DefaultVideoQuality > 100)
            throw new ArgumentException("DefaultVideoQuality must be between 1 and 100");

        if (settings.DefaultImageQuality < 1 || settings.DefaultImageQuality > 100)
            throw new ArgumentException("DefaultImageQuality must be between 1 and 100");

        // Log changes
        if (_telegramSettings.Token != settings.Token)
        {
            _logger.LogInformation("Telegram token updated");
            OnConfigurationChanged(nameof(TelegeramSettings.Token), _telegramSettings.Token, settings.Token);
        }

        if (_telegramSettings.ReconnectTimeout != settings.ReconnectTimeout)
        {
            _logger.LogInformation($"Telegram reconnect timeout updated: {_telegramSettings.ReconnectTimeout}s ? {settings.ReconnectTimeout}s");
            OnConfigurationChanged(nameof(TelegeramSettings.ReconnectTimeout), _telegramSettings.ReconnectTimeout, settings.ReconnectTimeout);
        }

        if (_telegramSettings.DefaultVideoQuality != settings.DefaultVideoQuality)
        {
            _logger.LogInformation($"Telegram default video quality updated: {_telegramSettings.DefaultVideoQuality} ? {settings.DefaultVideoQuality}");
            OnConfigurationChanged(nameof(TelegeramSettings.DefaultVideoQuality), _telegramSettings.DefaultVideoQuality, settings.DefaultVideoQuality);
        }

        if (_telegramSettings.DefaultImageQuality != settings.DefaultImageQuality)
        {
            _logger.LogInformation($"Telegram default image quality updated: {_telegramSettings.DefaultImageQuality} ? {settings.DefaultImageQuality}");
            OnConfigurationChanged(nameof(TelegeramSettings.DefaultImageQuality), _telegramSettings.DefaultImageQuality, settings.DefaultImageQuality);
        }

        _telegramSettings = settings;
    }

    #endregion

    #region Brute Force Detection Settings

    public BruteForceDetectionSettings GetBruteForceDetectionSettings()
    {
        return new BruteForceDetectionSettings
        {
            RetriesPerMinute = _bruteForceSettings.RetriesPerMinute,
            RetriesPerHour = _bruteForceSettings.RetriesPerHour
        };
    }

    public void UpdateBruteForceDetectionSettings(BruteForceDetectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Validate settings
        if (settings.RetriesPerMinute < 1)
            throw new ArgumentException("RetriesPerMinute must be at least 1");

        if (settings.RetriesPerHour < 1)
            throw new ArgumentException("RetriesPerHour must be at least 1");

        if (settings.RetriesPerHour < settings.RetriesPerMinute)
            throw new ArgumentException("RetriesPerHour must be >= RetriesPerMinute");

        _logger.LogInformation($"Brute force detection settings updated: {settings.RetriesPerMinute}/min, {settings.RetriesPerHour}/hour");
        OnConfigurationChanged(nameof(BruteForceDetectionSettings), _bruteForceSettings, settings);

        _bruteForceSettings = settings;
    }

    #endregion

    #region Motion Detection Settings

    public MotionDetectionSettings GetMotionDetectionSettings()
    {
        return new MotionDetectionSettings
        {
            StoragePath = _motionDetectionSettings.StoragePath,
            MotionDetectionCameras = new List<MotionDetectionCameraSettingDto>(_motionDetectionSettings.MotionDetectionCameras),
            DefaultMotionDetectParameters = new MotionDetectorParametersDto
            {
                Width = _motionDetectionSettings.DefaultMotionDetectParameters.Width,
                Height = _motionDetectionSettings.DefaultMotionDetectParameters.Height,
                DetectorDelayMs = _motionDetectionSettings.DefaultMotionDetectParameters.DetectorDelayMs,
                NoiseThreshold = _motionDetectionSettings.DefaultMotionDetectParameters.NoiseThreshold,
                ChangeLimit = _motionDetectionSettings.DefaultMotionDetectParameters.ChangeLimit,
                TextNotificationDelay = _motionDetectionSettings.DefaultMotionDetectParameters.TextNotificationDelay,
                ImageNotificationDelay = _motionDetectionSettings.DefaultMotionDetectParameters.ImageNotificationDelay,
                VideoNotificationDelay = _motionDetectionSettings.DefaultMotionDetectParameters.VideoNotificationDelay,
                KeepImageBuffer = _motionDetectionSettings.DefaultMotionDetectParameters.KeepImageBuffer
            }
        };
    }

    public void UpdateMotionDetectionSettings(MotionDetectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Validate settings
        if (string.IsNullOrWhiteSpace(settings.StoragePath))
            throw new ArgumentException("StoragePath cannot be empty");

        if (settings.DefaultMotionDetectParameters == null)
            throw new ArgumentException("DefaultMotionDetectParameters cannot be null");

        // Validate motion detection parameters
        if (settings.DefaultMotionDetectParameters.Width < 1)
            throw new ArgumentException("Width must be at least 1");

        if (settings.DefaultMotionDetectParameters.Height < 1)
            throw new ArgumentException("Height must be at least 1");

        if (settings.DefaultMotionDetectParameters.DetectorDelayMs < 1)
            throw new ArgumentException("DetectorDelayMs must be at least 1");

        if (settings.DefaultMotionDetectParameters.ChangeLimit < 0 || settings.DefaultMotionDetectParameters.ChangeLimit > 100)
            throw new ArgumentException("ChangeLimit must be between 0 and 100");

        // Log changes
        if (_motionDetectionSettings.StoragePath != settings.StoragePath)
        {
            _logger.LogInformation($"Motion detection storage path updated: {_motionDetectionSettings.StoragePath} ? {settings.StoragePath}");
            OnConfigurationChanged(nameof(MotionDetectionSettings.StoragePath), _motionDetectionSettings.StoragePath, settings.StoragePath);
        }

        if (!_motionDetectionSettings.DefaultMotionDetectParameters.Equals(settings.DefaultMotionDetectParameters))
        {
            _logger.LogInformation($"Motion detection parameters updated: {settings.DefaultMotionDetectParameters.Width}x{settings.DefaultMotionDetectParameters.Height}, " +
                $"delay={settings.DefaultMotionDetectParameters.DetectorDelayMs}ms, " +
                $"threshold={settings.DefaultMotionDetectParameters.NoiseThreshold}, " +
                $"changeLimit={settings.DefaultMotionDetectParameters.ChangeLimit}%");
            OnConfigurationChanged(nameof(MotionDetectionSettings.DefaultMotionDetectParameters),
                _motionDetectionSettings.DefaultMotionDetectParameters,
                settings.DefaultMotionDetectParameters);
        }

        _motionDetectionSettings = settings;
    }

    #endregion

    #region Video Recording Settings

    public RecorderSettings GetVideoRecordingSettings()
    {
        return new RecorderSettings
        {
            StoragePath = _videoRecordingSettings.StoragePath,
            VideoFileLengthSeconds = _videoRecordingSettings.VideoFileLengthSeconds,
            DefaultVideoQuality = _videoRecordingSettings.DefaultVideoQuality,
            RecordCameras = new List<RecordCameraSettingDto>(_videoRecordingSettings.RecordCameras)
        };
    }

    public void UpdateVideoRecordingSettings(RecorderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Validate settings
        if (string.IsNullOrWhiteSpace(settings.StoragePath))
            throw new ArgumentException("StoragePath cannot be empty");

        if (settings.VideoFileLengthSeconds < 10 || settings.VideoFileLengthSeconds > 86400)
            throw new ArgumentException("VideoFileLengthSeconds must be between 10 and 86400 seconds");

        if (settings.DefaultVideoQuality < 1 || settings.DefaultVideoQuality > 100)
            throw new ArgumentException("DefaultVideoQuality must be between 1 and 100");

        // Log changes
        if (_videoRecordingSettings.StoragePath != settings.StoragePath)
        {
            _logger.LogInformation($"Video recording storage path updated: {_videoRecordingSettings.StoragePath} ? {settings.StoragePath}");
            OnConfigurationChanged(nameof(RecorderSettings.StoragePath), _videoRecordingSettings.StoragePath, settings.StoragePath);
        }

        if (_videoRecordingSettings.VideoFileLengthSeconds != settings.VideoFileLengthSeconds)
        {
            _logger.LogInformation($"Video file length updated: {_videoRecordingSettings.VideoFileLengthSeconds}s ? {settings.VideoFileLengthSeconds}s");
            OnConfigurationChanged(nameof(RecorderSettings.VideoFileLengthSeconds), _videoRecordingSettings.VideoFileLengthSeconds, settings.VideoFileLengthSeconds);
        }

        if (_videoRecordingSettings.DefaultVideoQuality != settings.DefaultVideoQuality)
        {
            _logger.LogInformation($"Video default quality updated: {_videoRecordingSettings.DefaultVideoQuality} ? {settings.DefaultVideoQuality}");
            OnConfigurationChanged(nameof(RecorderSettings.DefaultVideoQuality), _videoRecordingSettings.DefaultVideoQuality, settings.DefaultVideoQuality);
        }

        _videoRecordingSettings = settings;
    }

    #endregion

    #region System Settings

    public string GetExternalHostUrl()
    {
        return _externalHostUrl;
    }

    public void UpdateExternalHostUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("External host URL cannot be empty");

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            throw new ArgumentException("Invalid URL format");

        _logger.LogInformation($"External host URL updated: {_externalHostUrl} ? {url}");
        OnConfigurationChanged(nameof(_externalHostUrl), _externalHostUrl, url);

        _externalHostUrl = url;
    }

    public int GetCookieExpireTimeMinutes()
    {
        return _cookieExpireTimeMinutes;
    }

    public void UpdateCookieExpireTimeMinutes(int minutes)
    {
        if (minutes < 1 || minutes > 10080) // 1 minute to 7 days
            throw new ArgumentException("Cookie expiration time must be between 1 and 10080 minutes");

        _logger.LogInformation($"Cookie expiration time updated: {_cookieExpireTimeMinutes} ? {minutes} minutes");
        OnConfigurationChanged(nameof(_cookieExpireTimeMinutes), _cookieExpireTimeMinutes, minutes);

        _cookieExpireTimeMinutes = minutes;
    }

    public bool GetAllowBasicAuthentication()
    {
        return _allowBasicAuthentication;
    }

    public void UpdateAllowBasicAuthentication(bool allow)
    {
        _logger.LogInformation($"Basic authentication: {(_allowBasicAuthentication ? "Enabled" : "Disabled")} ? {(allow ? "Enabled" : "Disabled")}");
        OnConfigurationChanged(nameof(_allowBasicAuthentication), _allowBasicAuthentication, allow);

        _allowBasicAuthentication = allow;
    }

    #endregion

    #region Persistence

    public void PersistConfiguration()
    {
        try
        {
            // Create merged settings object
            var appSettings = new Dictionary<string, object>
            {
                { "ExternalHostUrl", _externalHostUrl },
                { "CookieExpireTimeMinutes", _cookieExpireTimeMinutes },
                { "AllowBasicAuthentication", _allowBasicAuthentication },
                { "Telegram", _telegramSettings },
                { "BruteForceDetection", _bruteForceSettings },
                { "MotionDetector", new { StoragePath = _motionDetectionSettings.StoragePath, DefaultMotionDetectParameters = _motionDetectionSettings.DefaultMotionDetectParameters } },
                { "Recorder", new { StoragePath = _videoRecordingSettings.StoragePath, VideoFileLengthSeconds = _videoRecordingSettings.VideoFileLengthSeconds, DefaultVideoQuality = _videoRecordingSettings.DefaultVideoQuality } }
            };

            // Load existing appsettings
            var existingJson = File.Exists("appsettings.json")
                ? File.ReadAllText("appsettings.json")
                : "{}";

            var settings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(existingJson)
                ?? new Dictionary<string, object>();

            // Merge new settings
            foreach (var kvp in appSettings)
            {
                settings[kvp.Key] = kvp.Value;
            }

            // Save back to file
            var json = System.Text.Json.JsonSerializer.Serialize(
                settings,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText("appsettings.json", json);

            _logger.LogInformation("Configuration persisted to appsettings.json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist configuration");
            throw;
        }
    }

    #endregion

    #region Private Methods

    private void LoadConfigurationFromSource()
    {
        try
        {
            // Load Telegram settings
            var telegramSettings = _configuration.GetSection("Telegram").Get<TelegeramSettings>();
            if (telegramSettings != null)
                _telegramSettings = telegramSettings;

            // Load brute force settings
            var bruteForceSettings = _configuration.GetSection("BruteForceDetection").Get<BruteForceDetectionSettings>();
            if (bruteForceSettings != null)
                _bruteForceSettings = bruteForceSettings;

            // Load motion detection settings
            var motionDetectionSettings = _configuration.GetSection("MotionDetector").Get<MotionDetectionSettings>();
            if (motionDetectionSettings != null)
                _motionDetectionSettings = motionDetectionSettings;

            // Load video recording settings
            var videoRecordingSettings = _configuration.GetSection("Recorder").Get<RecorderSettings>();
            if (videoRecordingSettings != null)
                _videoRecordingSettings = videoRecordingSettings;

            // Load system settings
            _externalHostUrl = _configuration["ExternalHostUrl"] ?? string.Empty;
            _cookieExpireTimeMinutes = _configuration.GetValue<int>("CookieExpireTimeMinutes", 60);
            _allowBasicAuthentication = _configuration.GetValue<bool>("AllowBasicAuthentication", true);

            _logger.LogInformation("Configuration loaded from source");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load configuration from source, using defaults");
        }
    }

    private void OnConfigurationChanged(string propertyName, object? oldValue, object? newValue)
    {
        ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs
        {
            PropertyName = propertyName,
            OldValue = oldValue,
            NewValue = newValue
        });
    }

    #endregion
}
