namespace CameraServer.Shared.DTO;

[Serializable]
public class CameraDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}