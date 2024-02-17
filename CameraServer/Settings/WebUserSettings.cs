using System.ComponentModel.DataAnnotations;
using CameraServer.Auth;

namespace CameraServer.Settings
{
    public class WebUserSettings : ICameraUser
    {
        [Required]
        public List<Roles> Roles { get; set; } = [];
        [Required]
        public string Login { get; set; } = string.Empty;
        [Required]
        public string Password { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
