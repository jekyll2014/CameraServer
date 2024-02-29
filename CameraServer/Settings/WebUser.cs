using CameraServer.Auth;

using System.ComponentModel.DataAnnotations;

namespace CameraServer.Settings
{
    public class WebUser : ICameraUser
    {
        [Required]
        public List<Roles> Roles { get; set; } = new();
        [Required]
        public string Login { get; set; } = string.Empty;
        [Required]
        public string Password { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
