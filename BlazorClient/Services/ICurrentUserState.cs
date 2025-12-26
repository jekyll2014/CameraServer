using CameraServer.Server.Services.MotionDetection;
using CameraServer.Shared.DTO;

namespace CameraServer.Client.Services
{
    public interface ICurrentUserState
    {
        List<CameraDto>? Cameras { get; set; }
        CameraDto? SelectedCamera { get; set; }
        List<MotionDetectionCameraTask>? Detectors { get; set; }
        MotionDetectionCameraTask? SelectedDetector { get; set; }
        List<RecordTaskDto>? RecordingTasks { get; set; }
        RecordTaskDto? SelectedRecordingTask { get; set; }
    }
}