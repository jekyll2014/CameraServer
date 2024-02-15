namespace CameraServer.Auth
{
    public class UserDto : ICameraUser
    {
        public List<Roles> Roles { get; set; } = [];
        public string Login { get; set; } = string.Empty;
    }
}
