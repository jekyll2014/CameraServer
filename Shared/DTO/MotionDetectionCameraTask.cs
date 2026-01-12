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
        // Deep-copy incoming DTO fields to avoid shared-reference side effects
        CameraId = dto.CameraId;
        User = dto.User;

        // Deep-copy FrameFormat
        var ff = dto.FrameFormat ?? new FrameFormatDto();
        FrameFormat = new FrameFormatDto
        {
            Width = ff.Width,
            Height = ff.Height,
            Format = ff.Format ?? string.Empty,
            Fps = ff.Fps
        };

        // Deep-copy MotionDetectParameters
        if (dto.MotionDetectParameters != null)
        {
            var p = dto.MotionDetectParameters;
            MotionDetectParameters = new MotionDetectorParametersDto
            {
                DetectMethod = p.DetectMethod,
                Width = p.Width,
                Height = p.Height,
                DetectorDelayMs = p.DetectorDelayMs,
                ChangeLimit = p.ChangeLimit,
                TextNotificationDelay = p.TextNotificationDelay,
                ImageNotificationDelay = p.ImageNotificationDelay,
                VideoNotificationDelay = p.VideoNotificationDelay,
                KeepImageBuffer = p.KeepImageBuffer
            };
        }
        else
        {
            MotionDetectParameters = null;
        }

        // Deep-copy Notifications
        Notifications = dto.Notifications != null
            ? dto.Notifications.Select(n => new NotificationParametersDto
            {
                Transport = n.Transport,
                MessageType = n.MessageType,
                Destination = n.Destination,
                Message = n.Message,
                VideoLengthSec = n.VideoLengthSec,
                SaveNotificationContent = n.SaveNotificationContent
            }).ToList()
            : new List<NotificationParametersDto>();
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