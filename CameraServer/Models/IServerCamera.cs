using CameraLib;

namespace CameraServer.Server.Models
{
    public interface IServerCamera
    {
        public int Id { get; set; }
        public ICamera CameraStream { get; }
        public bool Custom { get; }
        public List<Roles> AllowedRoles { get; }
    }
}