namespace CameraServer.Server.Models;

public class User : UserDto
{
    public string Password { get; set; } = string.Empty;
    public bool DefaultUser { get; set; } = false;
}
