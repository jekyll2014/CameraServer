namespace CameraLib;

public class FrameFormatDto
{
    public int Width { get; set; } = 0;
    public int Height { get; set; } = 0;
    public string Format { get; set; } = string.Empty;
    public double Fps { get; set; } = 0.0;
}