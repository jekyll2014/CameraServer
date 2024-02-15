using CameraLib;

using CameraServer.Auth;

namespace CameraServer.Models
{
    public interface IServerCamera
    {
        public ICamera Camera { get; }
        public bool Custom { get; }
        public List<Roles> AllowedRoles { get; }
    }
}