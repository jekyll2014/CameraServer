using CameraServer.Server.Services.AntiBruteForce;
using CameraServer.Server.Services.MotionDetection;
using CameraServer.Server.Services.Telegram;
using CameraServer.Server.Services.VideoRecording;

namespace CameraServer.Server.Models;

/// <summary>
/// Represents the complete settings.json configuration structure.
/// This is the single source of truth for all application configuration.
/// </summary>
public class AppSettings
{
    public List<User> Users { get; set; } = new();
    public BruteForceDetectionSettings BruteForceDetection { get; set; } = new();
    public TelegeramSettings Telegram { get; set; } = new();
    public CameraSettings CameraSettings { get; set; } = new();
    public RecorderSettings Recorder { get; set; } = new();
    public MotionDetectionSettings MotionDetector { get; set; } = new();
}