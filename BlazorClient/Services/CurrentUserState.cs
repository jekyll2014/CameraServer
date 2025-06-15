using CameraServer.Shared.DTO;

namespace CameraServer.Client.Services
{
    public class CurrentUserState : ICurrentUserState
    {
        public List<CameraDto>? Cameras { get; set; }
        public int SelectedCameraId { get; set; }
        public CameraDescriptionDto? CameraDescription { get; set; }
    }
}
