namespace CameraServer.Shared;

[Serializable]
public class UserInfoModel
{
    public string? Login { get; set; }
    public IEnumerable<string> Roles { get; set; } = new List<string>();
    public string Name { get; set; } = string.Empty;
    public string TelegramName { get; set; } = string.Empty;
    public long TelegramId { get; set; } = 0;
    public string DefaultCodec { get; set; } = "AVC"; //FourCC.MP4V better compression, FourCC.AVC - better compatibility
}