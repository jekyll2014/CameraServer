namespace CameraServer.Shared.DTO;

/// <summary>
/// DTO for system settings configuration
/// </summary>
[Serializable]
public class SystemSettingsDto
{
    public string ServerUrls { get; set; } = string.Empty;
    public int CookieExpireTimeMinutes { get; set; } = 60;
    public bool AllowBasicAuthentication { get; set; } = true;
    public string ExternalHostUrl { get; set; } = string.Empty;
    public string AllowedHosts { get; set; } = string.Empty;
    public string ValidAudience { get; set; } = string.Empty;
}
