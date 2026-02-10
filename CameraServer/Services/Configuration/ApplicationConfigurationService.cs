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
public interface IApplicationConfigurationService
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
    SystemSettingsDto GetServerSettings();

    /// <summary>
    /// Gets all users from settings.json.
    /// </summary>
    List<User> GetUsers();

    /// <summary>
    /// Gets default user from settings.json.
    /// </summary>
    User? GetDefaultUser();

    /// <summary>
    /// Gets camera settings from settings.json.
    /// </summary>
    CameraSettings GetCameraSettings();

    /// <summary>
    /// Updates camera settings in settings.json (requires restart).
    /// </summary>
    void UpdateCameraSettings(CameraSettings settings);

    /// <summary>
    /// Updates a user in settings.json (requires restart).
    /// </summary>
    void UpdateUser(User user);

    /// <summary>
    /// Adds a new user to settings.json (requires restart).
    /// </summary>
    void AddUser(User user);

    /// <summary>
    /// Deletes a user from settings.json (requires restart).
    /// </summary>
    void DeleteUser(string login);

    /// <summary>
    /// Saves all configuration changes to settings.json.
    /// </summary>
    void SaveConfiguration();

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

public class ApplicationConfigurationService : IApplicationConfigurationService
{
    private const string ConfigFileName = "settings.json";

    private readonly ILogger<ApplicationConfigurationService> _logger;
    private readonly IConfiguration _serverConfiguration;
    private readonly Config<AppSettings> _settingsConfig;

    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

    public ApplicationConfigurationService(
        IConfiguration serverConfiguration,
        ILogger<ApplicationConfigurationService> logger)
    {
        _serverConfiguration = serverConfiguration;
        _logger = logger;

        // Initialize settings file manager
        // Load settings from settings.json
        _settingsConfig = new Config<AppSettings>(ConfigFileName, autoCreateConfig: true);
        LoadConfigurationFromSystem();
        _logger.LogInformation("ApplicationConfigurationService initialized");
    }

    #region Telegram Settings

    public TelegeramSettings GetTelegramSettings()
    {
        return new TelegeramSettings
        {
            Token = _settingsConfig.ConfigStorage.Telegram.Token,
            ReconnectTimeout = _settingsConfig.ConfigStorage.Telegram.ReconnectTimeout,
            DefaultVideoTime = _settingsConfig.ConfigStorage.Telegram.DefaultVideoTime,
            DefaultVideoQuality = _settingsConfig.ConfigStorage.Telegram.DefaultVideoQuality,
            DefaultImageQuality = _settingsConfig.ConfigStorage.Telegram.DefaultImageQuality
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
        if (_settingsConfig.ConfigStorage.Telegram.Token != settings.Token)
        {
            _logger.LogInformation("Telegram token updated");
            OnConfigurationChanged(nameof(TelegeramSettings.Token), _settingsConfig.ConfigStorage.Telegram.Token, settings.Token);
        }

        if (_settingsConfig.ConfigStorage.Telegram.ReconnectTimeout != settings.ReconnectTimeout)
        {
            _logger.LogInformation($"Telegram reconnect timeout updated: {_settingsConfig.ConfigStorage.Telegram.ReconnectTimeout}s ? {settings.ReconnectTimeout}s");
            OnConfigurationChanged(nameof(TelegeramSettings.ReconnectTimeout), _settingsConfig.ConfigStorage.Telegram.ReconnectTimeout, settings.ReconnectTimeout);
        }

        if (_settingsConfig.ConfigStorage.Telegram.DefaultVideoQuality != settings.DefaultVideoQuality)
        {
            _logger.LogInformation($"Telegram default video quality updated: {_settingsConfig.ConfigStorage.Telegram.DefaultVideoQuality} ? {settings.DefaultVideoQuality}");
            OnConfigurationChanged(nameof(TelegeramSettings.DefaultVideoQuality), _settingsConfig.ConfigStorage.Telegram.DefaultVideoQuality, settings.DefaultVideoQuality);
        }

        if (_settingsConfig.ConfigStorage.Telegram.DefaultImageQuality != settings.DefaultImageQuality)
        {
            _logger.LogInformation($"Telegram default image quality updated: {_settingsConfig.ConfigStorage.Telegram.DefaultImageQuality} ? {settings.DefaultImageQuality}");
            OnConfigurationChanged(nameof(TelegeramSettings.DefaultImageQuality), _settingsConfig.ConfigStorage.Telegram.DefaultImageQuality, settings.DefaultImageQuality);
        }

        _settingsConfig.ConfigStorage.Telegram = settings;
    }

    #endregion

    #region Brute Force Detection Settings

    public BruteForceDetectionSettings GetBruteForceDetectionSettings()
    {
        return new BruteForceDetectionSettings
        {
            RetriesPerMinute = _settingsConfig.ConfigStorage.BruteForceDetection.RetriesPerMinute,
            RetriesPerHour = _settingsConfig.ConfigStorage.BruteForceDetection.RetriesPerHour
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
        OnConfigurationChanged(nameof(BruteForceDetectionSettings), _settingsConfig.ConfigStorage.BruteForceDetection, settings);

        _settingsConfig.ConfigStorage.BruteForceDetection = settings;
    }

    #endregion

    #region Motion Detection Settings

    public MotionDetectionSettings GetMotionDetectionSettings()
    {
        return new MotionDetectionSettings
        {
            StoragePath = _settingsConfig.ConfigStorage.MotionDetector.StoragePath,
            MotionDetectionCameras = new List<MotionDetectionCameraSettingDto>(_settingsConfig.ConfigStorage.MotionDetector.MotionDetectionCameras),
            DefaultMotionDetectParameters = new MotionDetectorParametersDto
            {
                Width = _settingsConfig.ConfigStorage.MotionDetector.DefaultMotionDetectParameters.Width,
                Height = _settingsConfig.ConfigStorage.MotionDetector.DefaultMotionDetectParameters.Height,
                DetectorDelayMs = _settingsConfig.ConfigStorage.MotionDetector.DefaultMotionDetectParameters.DetectorDelayMs,
                NoiseThreshold = _settingsConfig.ConfigStorage.MotionDetector.DefaultMotionDetectParameters.NoiseThreshold,
                ChangeLimit = _settingsConfig.ConfigStorage.MotionDetector.DefaultMotionDetectParameters.ChangeLimit,
                TextNotificationDelay = _settingsConfig.ConfigStorage.MotionDetector.DefaultMotionDetectParameters.TextNotificationDelay,
                ImageNotificationDelay = _settingsConfig.ConfigStorage.MotionDetector.DefaultMotionDetectParameters.ImageNotificationDelay,
                VideoNotificationDelay = _settingsConfig.ConfigStorage.MotionDetector.DefaultMotionDetectParameters.VideoNotificationDelay,
                KeepImageBuffer = _settingsConfig.ConfigStorage.MotionDetector.DefaultMotionDetectParameters.KeepImageBuffer
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
        if (_settingsConfig.ConfigStorage.MotionDetector.StoragePath != settings.StoragePath)
        {
            _logger.LogInformation($"Motion detection storage path updated: {_settingsConfig.ConfigStorage.MotionDetector.StoragePath} ? {settings.StoragePath}");
            OnConfigurationChanged(nameof(MotionDetectionSettings.StoragePath), _settingsConfig.ConfigStorage.MotionDetector.StoragePath, settings.StoragePath);
        }

        if (!_settingsConfig.ConfigStorage.MotionDetector.DefaultMotionDetectParameters.Equals(settings.DefaultMotionDetectParameters))
        {
            _logger.LogInformation($"Motion detection parameters updated: {settings.DefaultMotionDetectParameters.Width}x{settings.DefaultMotionDetectParameters.Height}, " +
                $"delay={settings.DefaultMotionDetectParameters.DetectorDelayMs}ms, " +
                $"threshold={settings.DefaultMotionDetectParameters.NoiseThreshold}, " +
                $"changeLimit={settings.DefaultMotionDetectParameters.ChangeLimit}%");
            OnConfigurationChanged(nameof(MotionDetectionSettings.DefaultMotionDetectParameters),
                _settingsConfig.ConfigStorage.MotionDetector.DefaultMotionDetectParameters,
                settings.DefaultMotionDetectParameters);
        }

        _settingsConfig.ConfigStorage.MotionDetector = settings;
    }

    #endregion

    #region Video Recording Settings

    public RecorderSettings GetVideoRecordingSettings()
    {
        return new RecorderSettings
        {
            StoragePath = _settingsConfig.ConfigStorage.Recorder.StoragePath,
            VideoFileLengthSeconds = _settingsConfig.ConfigStorage.Recorder.VideoFileLengthSeconds,
            DefaultVideoQuality = _settingsConfig.ConfigStorage.Recorder.DefaultVideoQuality,
            RecordCameras = new List<RecordCameraSettingDto>(_settingsConfig.ConfigStorage.Recorder.RecordCameras)
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
        if (_settingsConfig.ConfigStorage.Recorder.StoragePath != settings.StoragePath)
        {
            _logger.LogInformation($"Video recording storage path updated: {_settingsConfig.ConfigStorage.Recorder.StoragePath} ? {settings.StoragePath}");
            OnConfigurationChanged(nameof(RecorderSettings.StoragePath), _settingsConfig.ConfigStorage.Recorder.StoragePath, settings.StoragePath);
        }

        if (_settingsConfig.ConfigStorage.Recorder.VideoFileLengthSeconds != settings.VideoFileLengthSeconds)
        {
            _logger.LogInformation($"Video file length updated: {_settingsConfig.ConfigStorage.Recorder.VideoFileLengthSeconds}s ? {settings.VideoFileLengthSeconds}s");
            OnConfigurationChanged(nameof(RecorderSettings.VideoFileLengthSeconds), _settingsConfig.ConfigStorage.Recorder.VideoFileLengthSeconds, settings.VideoFileLengthSeconds);
        }

        if (_settingsConfig.ConfigStorage.Recorder.DefaultVideoQuality != settings.DefaultVideoQuality)
        {
            _logger.LogInformation($"Video default quality updated: {_settingsConfig.ConfigStorage.Recorder.DefaultVideoQuality} ? {settings.DefaultVideoQuality}");
            OnConfigurationChanged(nameof(RecorderSettings.DefaultVideoQuality), _settingsConfig.ConfigStorage.Recorder.DefaultVideoQuality, settings.DefaultVideoQuality);
        }

        _settingsConfig.ConfigStorage.Recorder = settings;
    }

    #endregion

    #region System Settings

    public SystemSettingsDto GetServerSettings()
    {
        return LoadConfigurationFromSystem();
    }

    #endregion

    #region Users and Camera Settings

    public List<User> GetUsers()
    {
        return _settingsConfig.ConfigStorage.Users ?? new List<User>();
    }

    public User? GetDefaultUser()
    {
        return _settingsConfig.ConfigStorage.Users.FirstOrDefault(u => u.DefaultUser);
    }

    public CameraSettings GetCameraSettings()
    {
        return _settingsConfig.ConfigStorage.CameraSettings ?? new CameraSettings();
    }

    public void UpdateCameraSettings(CameraSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _logger.LogInformation("Camera settings updated");
        OnConfigurationChanged(nameof(CameraSettings), _settingsConfig.ConfigStorage.CameraSettings, settings);

        _settingsConfig.ConfigStorage.CameraSettings = settings;
    }

    public void UpdateUser(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(user.Login))
            throw new ArgumentException("User login cannot be empty");

        var existingUser = _settingsConfig.ConfigStorage.Users.FirstOrDefault(u => u.Login == user.Login);
        if (existingUser == null)
            throw new InvalidOperationException($"User '{user.Login}' not found");

        // If setting this user as default, unset all other default users
        if (user.DefaultUser)
        {
            foreach (var u in _settingsConfig.ConfigStorage.Users.Where(u => u.DefaultUser && u.Login != user.Login))
            {
                u.DefaultUser = false;
            }
        }

        // Update the user
        var index = _settingsConfig.ConfigStorage.Users.IndexOf(existingUser);
        _settingsConfig.ConfigStorage.Users[index] = user;

        _logger.LogInformation($"User '{user.Login}' updated");
        OnConfigurationChanged($"User.{user.Login}", existingUser, user);
    }

    public void AddUser(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(user.Login))
            throw new ArgumentException("User login cannot be empty");

        if (_settingsConfig.ConfigStorage.Users.Any(u => u.Login == user.Login))
            throw new InvalidOperationException($"User '{user.Login}' already exists");

        // If setting this user as default, unset all other default users
        if (user.DefaultUser)
        {
            foreach (var u in _settingsConfig.ConfigStorage.Users.Where(u => u.DefaultUser))
            {
                u.DefaultUser = false;
            }
        }

        _settingsConfig.ConfigStorage.Users.Add(user);

        _logger.LogInformation($"User '{user.Login}' added");
        OnConfigurationChanged("Users", null, user);
    }

    public void DeleteUser(string login)
    {
        if (string.IsNullOrWhiteSpace(login))
            throw new ArgumentException("User login cannot be empty");

        var user = _settingsConfig.ConfigStorage.Users.FirstOrDefault(u => u.Login == login);
        if (user == null)
            throw new InvalidOperationException($"User '{login}' not found");

        _settingsConfig.ConfigStorage.Users.Remove(user);

        _logger.LogInformation($"User '{login}' deleted");
        OnConfigurationChanged("Users", user, null);
    }

    public void SaveConfiguration()
    {
        try
        {
            if (!_settingsConfig.SaveConfig())
            {
                throw new InvalidOperationException($"Failed to save configuration to {ConfigFileName}");
            }

            _logger.LogInformation($"Configuration saved to {ConfigFileName}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            throw;
        }
    }

    #endregion

    #region Private Methods

    private SystemSettingsDto LoadConfigurationFromSystem()
    {
        try
        {
            // System settings (runtime modifiable) - not stored in settings.json structure
            return new SystemSettingsDto
            {
                ServerUrls = _serverConfiguration.GetValue<string>("Urls") ?? "",
                ExternalHostUrl = _serverConfiguration.GetValue<string>("ExternalHostUrl") ?? "",
                CookieExpireTimeMinutes = _serverConfiguration.GetValue<int?>("CookieExpireTimeMinutes") ?? 60,
                AllowBasicAuthentication = _serverConfiguration.GetValue<bool>("AllowBasicAuthentication"),
                AllowedHosts = _serverConfiguration.GetValue<string>("AllowedHosts") ?? "",
                ValidAudience = _serverConfiguration.GetValue<string>("ValidAudience") ?? ""
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to load configuration from system, using defaults");
            return new SystemSettingsDto();
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
