namespace CameraServer.Services.VideoRecorder;

public class RecorderSettings
{
    public List<string> AutorizedUsers { get; set; } = new();
    public List<RecordCameraSetting> RecordCameras { get; set; } = new List<RecordCameraSetting>();

    public string StoragePath { get; set; } = ".\\Records";

    public int VideoFileLengthSeconds
    {
        get => _videoFileLengthSeconds;
        set
        {
            if (value > 86400)
                _videoFileLengthSeconds = 86400;
            else if (value < 10)
                _videoFileLengthSeconds = 10;
            else
                _videoFileLengthSeconds = value;
        }
    }

    private int _videoFileLengthSeconds = 300;

    public byte VideoQuality
    {
        get => _videoQuality;
        set
        {
            if (value > 100)
                _videoQuality = 100;
            else if (value < 1)
                _videoQuality = 1;
            else
                _videoQuality = value;
        }
    }

    private byte _videoQuality = 90;
}