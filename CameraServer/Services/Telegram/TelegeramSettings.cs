using CameraServer.Auth;

namespace CameraServer.Services.Telegram;

public class TelegeramSettings
{
    public string Token { get; set; } = string.Empty;
    public List<TelegeramUser> AutorizedUsers { get; set; } = [];
    public List<Roles> DefaultRoles { get; set; } = [Roles.Guest];
}