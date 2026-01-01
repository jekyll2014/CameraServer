using CameraServer.Server.Services.AntiBruteForce;
using CameraServer.Server.Services.Telegram;
using CameraServer.Server.Services.VideoRecording;
using CameraServer.Shared.DTO;

namespace CameraServer.Server.Models;

/// <summary>
/// Represents the complete appsettings.json configuration structure.
/// This is the single source of truth for all application configuration.
/// </summary>
public class AppSettings
{
    public string Urls { get; set; } = "http://0.0.0.0:8080";
    public string AllowedHosts { get; set; } = "*";
    public int CookieExpireTimeMinutes { get; set; } = 60;
    public string ExternalHostUrl { get; set; } = string.Empty;
    public List<User> Users { get; set; } = new();
    public User? DefaultUser { get; set; }
    public string ValidAudience { get; set; } = "CameraServer";
    public bool AllowBasicAuthentication { get; set; } = true;
    public BruteForceDetectionSettings BruteForceDetection { get; set; } = new();
    public TelegeramSettings Telegram { get; set; } = new();
    public CameraSettings CameraSettings { get; set; } = new();
    public AppRecorderSettings Recorder { get; set; } = new();
    public AppMotionDetectionSettings MotionDetector { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
}

/// <summary>
/// Custom name to avoid ambiguity with RecorderSettings from VideoRecording service.
/// Represents the root Recorder section in appsettings.json.
/// </summary>
public class AppRecorderSettings
{
    public string StoragePath { get; set; } = "./Videos";
    public uint VideoFileLengthSeconds { get; set; } = 300;
    public byte DefaultVideoQuality { get; set; } = 95;
    public List<RecordCameraSettingDto> RecordCameras { get; set; } = new();
}

/// <summary>
/// Custom name to avoid ambiguity with MotionDetectionSettings from MotionDetection service.
/// Represents the root MotionDetector section in appsettings.json.
/// </summary>
public class AppMotionDetectionSettings
{
    public string StoragePath { get; set; } = "";
    public List<MotionDetectionCameraSettingDto> MotionDetectionCameras { get; set; } = new();
    public MotionDetectorParametersDto DefaultMotionDetectParameters { get; set; } = new();
}

public class LoggingSettings
{
    public LogLevelSettings LogLevel { get; set; } = new();
}

public class LogLevelSettings
{
    public string Default { get; set; } = "Information";
    public string Microsoft { get; set; } = "Information";
    public string MicrosoftAspNetCore { get; set; } = "Warning";

    [System.Text.Json.Serialization.JsonPropertyName("Microsoft.AspNetCore")]
    public string MicrosoftAspNetCoreJson
    {
        get => MicrosoftAspNetCore;
        set => MicrosoftAspNetCore = value;
    }
}
