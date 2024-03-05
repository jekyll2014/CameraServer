﻿namespace CameraServer.Services.VideoRecorder;

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

    public byte DefaultVideoQuality
    {
        get => _defaultVideoQuality;
        set
        {
            if (value > 100)
                _defaultVideoQuality = 100;
            else if (value < 1)
                _defaultVideoQuality = 1;
            else
                _defaultVideoQuality = value;
        }
    }

    private byte _defaultVideoQuality = 90;
}