namespace CameraServer.Shared;

[Serializable]
public class UserInfoModel
{
    public string? UserName { get; set; }
    public IEnumerable<string> Roles { get; set; } = new List<string>();
}