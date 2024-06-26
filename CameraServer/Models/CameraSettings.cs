﻿using CameraServer.Auth;

namespace CameraServer.Models;

public class CameraSettings
{
    public bool AutoSearchIp { get; set; } = true;
    public bool AutoSearchUsb { get; set; } = true;
    public bool AutoSearchUsbFC { get; set; } = true;
    public List<Roles> DefaultAllowedRoles { get; set; } = new();
    public int DiscoveryTimeOut { get; set; } = 1000;
    public bool ForceCameraConnect { get; set; } = false;
    public int MaxFrameBuffer { get; set; } = 10;
    public List<CustomCameraDto> CustomCameras { get; set; } = new();
    public int FrameTimeout { get; set; } = 30000;
}