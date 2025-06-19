using CameraServer.Shared.DTO;

namespace CameraServer.Server.Services.MotionDetection;

public class MotionDetectionCameraTask : MotionDetectionCameraSettingDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreationDateTime { get; set; } = DateTime.Now;

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
            if (setting.Id == Id
                && setting.CameraId == CameraId
                && setting.User == User
                && setting.FrameFormat.Equals(FrameFormat)
                && (setting.MotionDetectParameters == null && MotionDetectParameters == null
                    || setting.MotionDetectParameters.Equals(MotionDetectParameters)))
                result = true;
        }

        return result;
    }
}