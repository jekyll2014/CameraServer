namespace CameraServer.Shared.DTO;

/// <summary>
/// DTO for user management (excludes password for security)
/// </summary>
[Serializable]
public class UserManagementDto
{
    public string Login { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public long TelegramId { get; set; }
    public string TelegramName { get; set; } = string.Empty;
    public string DefaultCodec { get; set; } = "AVC";
}
