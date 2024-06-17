using CameraLib;

namespace CameraServer.Services.CameraHub;

public class CameraQueueItem
{
    public string CameraId { get; }
    public string QueueId { get; }
    public FrameFormatDto FrameFormat { get; }

    public CameraQueueItem(string cameraId, string queueId, FrameFormatDto frameFormat)
    {
        CameraId = cameraId;
        QueueId = queueId;
        FrameFormat = frameFormat;
    }

    public static string GenerateImageQueueId(string cameraId, string queueId, int width, int height)
    {
        return cameraId + queueId + width + height;
    }

    public override bool Equals(object? obj)
    {
        var result = false;
        if (obj is CameraQueueItem setting)
        {
            if (setting.CameraId == CameraId
                && setting.QueueId == QueueId
                && setting.FrameFormat.Width == FrameFormat.Width
                && setting.FrameFormat.Height == FrameFormat.Height)
                result = true;
        }

        return result;
    }
}