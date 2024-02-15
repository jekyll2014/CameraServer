using CameraLib;

using CameraServer.Auth;

namespace CameraServer.Models;

public class ServerCamera : IServerCamera
{
    public ICamera Camera { get; }
    public bool Custom { get; }
    public List<Roles> AllowedRoles { get; }

    public ServerCamera(ICamera camera, List<Roles> allowedRoles, bool custom = false)
    {
        Camera = camera;
        AllowedRoles = allowedRoles;
        Custom = custom;
    }
}