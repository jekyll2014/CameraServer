# CameraServer

CameraServer helps keep your home cameras in one bag. You can see snapshots and video remotely for any of your USB or ONVIF cameras available to the server.
This is a Windows stand-alone software. It only needs .NET 8 runtime to be installed (https://dotnet.microsoft.com/en-us/download/dotnet/8.0).
Just unpack, adjust the configuration and run.

## Features:
 - USB cameras autodetect
 - IP (ONVIF) cameras basic autodetect
 - Role-based authorisation (Admin. User, Guest) and camera access limitation
 - Web-based UI to access video streams
 - Telegram bot integration to see camera snapshots
 - Flexible configuration parameters via appsettings.json file

## Configuration:

### WebUI
First, you'll need to create users to access the system.
There are 3 roles hard-coded into the system:
- Admin - the only one who can run the camera list rescan process. Otherwise, it could be set up to have the same privileges as any other role.
- User - intended for authenticated users to allow them to see public and private cameras.
- Guest - intended for non-password-protected users to allow them to see some public cameras.
Just put your users into the "WebUsers" section:
```json
"WebUsers": [
  {
    "Login": "Admin",
    "Password": "adminpass",
    "Roles": [ "Admin", "User" ]
  }
]
```

### Telegram access
Second, you'll need to set up your Telegram access if you plan to use it.
Put your token into the "Token" field of the "Telegram" section:
```json
"Token": "0000000000:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
```

Add trusted users to the "AutorizedUsers" list:
```json
"AutorizedUsers": [
  {
    "UserId": 00000000,
    "Roles": [ "Admin", "User", "Guest" ]
  }
]
```
You can find Telegram IDs in the console log by trying to message your bot with software running or simply use common Telegram @userinfobot.

Add certain roles for the user to give him camera access.

"DefaultRoles" parameter sets the role for the unlisted/unauthorised users to give access to certain cameras for every user.

The default setting is:
```json
"DefaultRoles": [ "Guest" ]
```
But you may want to empty it to restrict unauthorized access to all your cameras.

### Camera definitions
The basic idea is to find all the available cameras automatically. Just allow it with the following parameters in the "CameraSettings" section:
```json
"AutoSearchIp": true
"AutoSearchUsb": true
```

You also need to set the default role to allow access to the cameras found:
```json
"DefaultAllowedRoles": [ "Admin" ]
```

You may want to define cameras manually instead of using automatic discovery. This is more flexible in terms of access settings
Every camera can be set up to allow certain roles to see the image. This can be done in the "CustomCameras" section:
```json
"CustomCameras": [
  {
    "Type": "IP",
    "Name": "*VStarCam01",
    "Path": "rtsp://admin:@192.168.1.50:10554/tcp/av0_0",
    "AllowedRoles": [ "Admin", "User", "Guest" ]
  },
]
```
There are 2 basic camera types now:
- USB
- IP

You can see the path to the USB camera in the console log of the program at the start-up. Look for the:
```
USB-Camera: USB Camera - [@device:pnp:\\?\usb#vid_05a3&pid_9422&mi_00#8&4573432&0&0000#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global]
IP-Camera: 192.168.102.126 - [rtsp://192.168.102.126:554/user=admin_password=_channel=1_stream=0.sdp?real_stream]
```

For the IP cameras you can also look at the console log or scan your network for ONVIF cameras, you can use [ONVIF Device Manager](https://sourceforge.net/projects/onvifdm/files/) which is much more capable.
Just find your cameras and put the correct rtsp:// link into the appsettings.json

Be careful setting the "AllowedRoles" for the cameras to not compromise your system.

The "MaxFrameBuffer" setting sets the maximum number of frames to keep for each client. In case the client connection is too slow (or disconnected) the buffer will be full and the connection reset. The default setting is:
```json
"MaxFrameBuffer": 10
```

### System settings
The last is the system settings:

"Urls": "http://0.0.0.0:808" - change the IP port to the one you like. Don't change the IP address unless you are sure you know exactly what you are doing.

"CookieExpireTimeMinutes": 60 - authentication cookie expiry time. Just note that a very long cookie can compromise your system in case your cookies are stolen.

## Planned features:
- security improvements (limit unsuccessful authentication retries)
- Serilog logging
- Web-based configuration interface
- Telegram video calls integration
