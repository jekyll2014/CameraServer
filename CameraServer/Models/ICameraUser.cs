using System.ComponentModel.DataAnnotations;

namespace CameraServer.Models;

public interface ICameraUser
{
    [Required]
    public List<Roles> Roles { get; set; }
    [Required]
    public string Login { get; set; }
    public long TelegramId { get; set; }
    public string DefaultCodec { get; set; }
}