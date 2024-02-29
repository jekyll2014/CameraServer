using CameraServer.Auth;

namespace CameraServer.Services.Telegram;

public class TelegeramSettings
{
    public string Token { get; set; } = string.Empty;
    public List<TelegeramUser> AutorizedUsers { get; set; } = new List<TelegeramUser>();
    public List<Roles> DefaultRoles { get; set; } = new List<Roles>() { Roles.Guest };
    public int DefaultVideoTime { get; set; } = 30;
}