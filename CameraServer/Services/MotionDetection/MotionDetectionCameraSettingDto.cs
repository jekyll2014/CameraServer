using CameraServer.Shared.DTO;

namespace CameraServer.Server.Services.MotionDetection;

public class MotionDetectionCameraSettingDto
{
    public string CameraId { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public FrameFormatDto FrameFormat { get; set; } = new();
    public MotionDetectorParametersDto? MotionDetectParameters { get; set; }
    public List<NotificationParametersDto> Notifications { get; set; } = new();

    public void Merge(MotionDetectionCameraSettingDto newTask)
    {
        Notifications.AddRange(newTask.Notifications
                 .Where(notification => !Notifications.Contains(notification)));
    }

    public void TryRemove(NotificationParametersDto notification)
    {
        for (var i = 0; i < Notifications.Count; i++)
        {
            if (Notifications[i].Equals(notification))
            {
                Notifications.RemoveAt(i);
                i--;
            }
        }
    }

    public override bool Equals(object? obj)
    {
        var result = false;
        if (obj is MotionDetectionCameraSettingDto setting)
        {
            if (setting.CameraId == CameraId
                && setting.User == User
                && setting.FrameFormat.Equals(FrameFormat)
                && (setting.MotionDetectParameters == null && MotionDetectParameters == null
                     || setting.MotionDetectParameters.Equals(MotionDetectParameters)))
                result = true;
        }

        return result;
    }

    public override int GetHashCode()
    {
        return $"{CameraId}{User}".GetHashCode();
    }
}