using System.ComponentModel.DataAnnotations;

namespace CameraServer.Auth;

public interface ICameraUser
{
    [Required]
    public List<Roles> Roles { get; set; }
    [Required]
    public string Login { get; set; }
}