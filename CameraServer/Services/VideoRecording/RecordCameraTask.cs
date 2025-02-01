namespace CameraServer.Server.Services.VideoRecording;

public class RecordCameraTask : RecordCameraSettingDto
{
    public Guid Id { get; set; } = new Guid();
    public DateTime CreationDateTime { get; set; } = DateTime.Now;
    public string TaskId { get; set; } = string.Empty;

    public RecordCameraTask()
    { }

    public RecordCameraTask(RecordCameraSettingDto dto)
    {
        CameraId = dto.CameraId;
        User = dto.User;
        FrameFormat = dto.FrameFormat;
        Quality = dto.Quality;
        Codec = dto.Codec;
    }

    public override bool Equals(object? obj)
    {
        var result = false;
        if (obj is RecordCameraTask setting)
        {
            if (setting.TaskId == TaskId
                && setting.CameraId == CameraId
                && setting.User == User
                && setting.FrameFormat.Equals(FrameFormat)
                && setting.Quality == Quality
                && setting.Codec == Codec)
                result = true;
        }

        return result;
    }

    public override int GetHashCode()
    {
        return TaskId.GetHashCode();
    }
}