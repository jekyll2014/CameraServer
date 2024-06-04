using CameraLib;

using CameraServer.Auth;

namespace CameraServer.Models;

public class ServerCamera : IServerCamera
{
    public ICamera CameraStream { get; }
    public bool Custom { get; }
    public List<Roles> AllowedRoles { get; }

    public ServerCamera(ICamera cameraStream, List<Roles> allowedRoles, bool custom = false)
    {
        CameraStream = cameraStream;
        AllowedRoles = allowedRoles;
        Custom = custom;
    }
}