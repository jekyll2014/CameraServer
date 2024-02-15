using System.ComponentModel.DataAnnotations;

namespace CameraServer.Auth
{
    public interface ICameraUser
    {
        [Required]
        public List<Roles> Roles { get; set; }
        [Required]
        public string Login { get; set; }
    }


    public class WebUser : ICameraUser
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
