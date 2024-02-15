using CameraServer.Auth;

namespace CameraServer.Services.Telegram;

public class TelegeramUser : ICameraUser
{
    public string Login { get; set; } = string.Empty;
    public long UserId { get; }
    public string Name { get; set; } = string.Empty;
    public List<Roles> Roles { get; set; } = [];

    public TelegeramUser(long userId)
    {
        UserId = userId;
    }
}