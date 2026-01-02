namespace CameraServer.Shared.DTO;

/// <summary>
/// DTO for creating or updating users (includes password)
/// </summary>
[Serializable]
public class UserCreateUpdateDto
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new(); // Roles enum as string list
    public long TelegramId { get; set; }
    public string TelegramName { get; set; } = string.Empty;
    public string DefaultCodec { get; set; } = "AVC";
    public bool DefaultUser { get; set; } = false;
}

