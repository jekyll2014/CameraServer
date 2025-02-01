using System.ComponentModel.DataAnnotations;

namespace CameraServer.Shared;

[Serializable]
public class LoginModel
{
    [Required(ErrorMessage = "User Name is required")]
    public string? UserName { get; set; }
    public string? Password { get; set; } = string.Empty;
    public bool RememberLogin { get; set; } = false;
    public string RedirectUri { get; set; } = string.Empty;
}

