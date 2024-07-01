using System.ComponentModel.DataAnnotations;

namespace CameraServer.Models
{
    public class User : UserDto
    {
        [Required]
        public string Password { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TelegramName { get; set; } = string.Empty;
    }
}
