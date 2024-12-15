using System.ComponentModel.DataAnnotations;

namespace CameraServer.Models
{
    public class UserDto : ICameraUser
    {
        [Required]
        public string Login { get; set; } = string.Empty;
        [Required]
        public List<Roles> Roles { get; set; } = new();
        public long TelegramId { get; set; } = 0;
        public string DefaultCodec { get; set; } = "AVC"; //FourCC.MP4V better compression, FourCC.AVC - better compatibility
    }
}
