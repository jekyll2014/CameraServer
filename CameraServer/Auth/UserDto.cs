namespace CameraServer.Auth
{
    public class UserDto : ICameraUser
    {
        public List<Roles> Roles { get; set; } = new();
        public string Login { get; set; } = string.Empty;
        public long TelegramId { get; set; } = 0;
    }
}
