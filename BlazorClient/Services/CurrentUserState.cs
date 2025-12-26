using CameraServer.Server.Services.MotionDetection;
using CameraServer.Shared.DTO;

namespace CameraServer.Client.Services
{
    public class CurrentUserState : ICurrentUserState
    {
        public List<CameraDto>? Cameras { get; set; }
        public CameraDto? SelectedCamera { get; set; }
        public List<MotionDetectionCameraTask>? Detectors { get; set; }
        public MotionDetectionCameraTask? SelectedDetector { get; set; }
        public List<RecordTaskDto>? RecordingTasks { get; set; }
        public RecordTaskDto? SelectedRecordingTask { get; set; }
    }
}
