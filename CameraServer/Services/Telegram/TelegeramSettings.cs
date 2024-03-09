using CameraServer.Auth;

namespace CameraServer.Services.Telegram;

public class TelegeramSettings
{
    public string Token { get; set; } = string.Empty;
    public List<Roles> DefaultRoles { get; set; } = new List<Roles>() { Roles.Guest };
    public uint DefaultVideoTime { get; set; } = 15;
}