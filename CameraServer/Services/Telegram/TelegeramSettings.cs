namespace CameraServer.Services.Telegram;

public class TelegeramSettings
{
    public string Token { get; set; } = string.Empty;
    public uint DefaultVideoTime { get; set; } = 15;
    public byte DefaultVideoQuality { get; set; } = 90;
    public byte DefaultImageQuality { get; set; } = 100;
}