using CameraServer.Shared.DTO;

namespace CameraServer.Client.Services
{
    public interface ICurrentUserState
    {
        List<CameraDto>? Cameras { get; set; }
        int SelectedCameraId { get; set; }
        CameraDescriptionDto? CameraDescription { get; set; }
    }
}