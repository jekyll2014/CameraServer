namespace CameraLib;

public class FrameFormatBase
{
    public int Width { get; set; } = 0;
    public int Height { get; set; } = 0;
    public string Format { get; set; } = string.Empty;
    public double Fps { get; set; } = 0.0;

    public override bool Equals(object? obj)
    {
        var result = false;
        if (obj is FrameFormatBase setting)
        {
            if (setting.Width == Width
                && setting.Height == Height
                && setting.Format == Format)
                result = true;
        }

        return result;
    }

    public override string ToString()
    {
        return $"{Width}x{Height} {Format} @ {Fps} FPS";
    }

    public override int GetHashCode()
    {
        return $"{Width}{Height}{Format}{Fps}".GetHashCode();
    }
}