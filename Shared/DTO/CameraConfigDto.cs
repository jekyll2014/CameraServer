namespace CameraServer.Shared.DTO;

/// <summary>
/// DTO for camera configuration management
/// </summary>
[Serializable]
public class CameraConfigDto
{
    public string Type { get; set; } = "Unknown"; // CameraType enum as string
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public List<string> AllowedRoles { get; set; } = new();
    public string AuthenticationType { get; set; } = "None"; // AuthType enum as string
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

