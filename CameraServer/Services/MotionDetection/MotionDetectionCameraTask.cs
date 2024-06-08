namespace CameraServer.Services.MotionDetection;

public class MotionDetectionCameraTask : MotionDetectionCameraSettingDto
{
    public string TaskId { get; set; } = string.Empty;

    public MotionDetectionCameraTask()
    { }

    public MotionDetectionCameraTask(MotionDetectionCameraSettingDto dto)
    {
        CameraId = dto.CameraId;
        User = dto.User;
        FrameFormat = dto.FrameFormat;
        MotionDetectParameters = dto.MotionDetectParameters;
        Notifications = dto.Notifications;
    }

    public override bool Equals(object? obj)
    {
        var result = false;
        if (obj is MotionDetectionCameraTask setting)
        {
            if (setting.TaskId == TaskId
                && setting.CameraId == CameraId
                && setting.User == User
                && setting.FrameFormat.Equals(FrameFormat)
                && ((setting.MotionDetectParameters == null && MotionDetectParameters == null)
                    || (setting.MotionDetectParameters?.Equals(MotionDetectParameters) ?? false)))
                result = true;
        }

        return result;
    }
}