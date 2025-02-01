namespace CameraServer.Server.Models;

public class User : UserDto
{
    public string Password { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TelegramName { get; set; } = string.Empty;
}
