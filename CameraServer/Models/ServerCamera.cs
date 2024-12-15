using CameraLib;

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

    public override int GetHashCode()
    {
        return CameraStream.Description.Path.GetHashCode();
    }

    public override bool Equals(object? obj)
    {
        if (obj is IServerCamera camera)
        {
            return CameraStream.Description.Path == camera.CameraStream.Description.Path;
        }

        return false;
    }
}