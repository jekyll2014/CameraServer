namespace CameraServer.Shared.DTO;

/// <summary>
/// DTO for updating Telegram settings at runtime.
/// </summary>
public class TelegramSettingsDto
{
    public string Token { get; set; } = string.Empty;
    public int ReconnectTimeout { get; set; } = 30;
    public uint DefaultVideoTime { get; set; } = 15;
    public byte DefaultVideoQuality { get; set; } = 90;
    public byte DefaultImageQuality { get; set; } = 100;
}

/// <summary>
/// DTO for updating brute force detection settings.
/// </summary>
public class BruteForceDetectionSettingsDto
{
    public int RetriesPerMinute { get; set; } = 3;
    public int RetriesPerHour { get; set; } = 10;
}

/// <summary>
/// DTO for updating motion detection parameters at runtime.
/// </summary>
public class MotionDetectionRuntimeSettingsDto
{
    /// <summary>
    /// Storage path for motion detection recordings.
    /// </summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>
    /// Default motion detection parameters (resolution, thresholds, delays).
    /// </summary>
    public MotionDetectorParametersDto DefaultMotionDetectParameters { get; set; } = new();
}

/// <summary>
/// DTO for updating video recording settings at runtime.
/// </summary>
public class VideoRecordingRuntimeSettingsDto
{
    /// <summary>
    /// Storage path for recorded videos.
    /// </summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>
    /// Video file length in seconds (10-86400).
    /// </summary>
    public uint VideoFileLengthSeconds { get; set; } = 300;

    /// <summary>
    /// Default video quality (1-100).
    /// </summary>
    public byte DefaultVideoQuality { get; set; } = 90;
}

/// <summary>
/// DTO for updating system runtime settings.
/// </summary>
public class RuntimeSettingsUpdateDto
{
    public string? ExternalHostUrl { get; set; }
    public int? CookieExpireTimeMinutes { get; set; }
    public bool? AllowBasicAuthentication { get; set; }
    public TelegramSettingsDto? TelegramSettings { get; set; }
    public BruteForceDetectionSettingsDto? BruteForceDetectionSettings { get; set; }
    public MotionDetectionRuntimeSettingsDto? MotionDetectionSettings { get; set; }
    public VideoRecordingRuntimeSettingsDto? VideoRecordingSettings { get; set; }
    public CameraSettingsDto? CameraSettings { get; set; }
}

/// <summary>
/// DTO for getting all runtime settings.
/// </summary>
public class RuntimeSettingsDto
{
    public TelegramSettingsDto TelegramSettings { get; set; } = new();
    public BruteForceDetectionSettingsDto BruteForceDetectionSettings { get; set; } = new();
    public MotionDetectionRuntimeSettingsDto MotionDetectionSettings { get; set; } = new();
    public VideoRecordingRuntimeSettingsDto VideoRecordingSettings { get; set; } = new();
    public CameraSettingsDto CameraSettings { get; set; } = new();
    public List<UserManagementDto> Users { get; set; } = new();
}
