# CameraServer

CameraServer helps keep your home cameras in one bag. You can see snapshots and video remotely for any of your USB or ONVIF cameras available to the server.

This is a Windows stand-alone software. It only needs .NET 8 runtime to be installed (https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

Just unpack, adjust the configuration and run.

## Features:
 - USB cameras autodetect
 - IP (ONVIF) cameras autodetect
 - MJPEG camera source support (plain and basic authentication)
 - Role-based authorisation (Admin, User, Guest) and camera access control for both Web and Telegram access
 - Basic authorisation option to enable integrating the streams into 3rd party systems
 - full control via the REST API
 - Web-based UI to access video streams
 - Telegram bot integration to see camera snapshots and video clips
 - Video streams record
 - Motion detection with notifications to Telegram
 - Flexible configuration parameters via appsettings.json file

## Configuration:

### System settings
The last is the system settings:

"Urls": "http://0.0.0.0:808" - change the IP port to the one you like. Don't change the IP address unless you are sure you know exactly what you are doing.

"CookieExpireTimeMinutes": 60 - authentication cookie expiry time. Just note that a very long cookie can compromise your system in case your cookies are stolen.

"AllowBasicAuthentication": true - allow "Basic" authentication for video streams. Note that it is only safe to use "Basic" authentication via HTTPS connection.

"ExternalHostUrl": "http://127.0.0.1:808" - needed to generate the correct video stream link for Telegram users.

### WebUI
Create users to access the system.

There are 3 roles hard-coded into the system:
- Admin - the only one who can run the camera list rescan process. Otherwise, it could be set up to have the same privileges as any other role.
- User - intended for authenticated users to allow them to see public and private cameras.
- Guest - intended for non-password-protected users to allow them to see some public cameras.
Just put your users into the "Users" section:
```json
"Users": [
  {
    "Login": "Admin",
    "Password": "adminpass",
    "TelegramId": 0,
    "Roles": [ "Admin", "User" ] // Admin, User, Guest
  }
]
```

If you want to allow anonymous/unknown users to be able to log in you can also set it up:
```json
  "DefaultUser": {
    "Login": "Anonymous",
    "Password": "",
    "TelegramId": 0,
    "Roles": [ "Guest" ] // Admin, User, Guest
  },
```

### Telegram access
Set up your Telegram access if you plan to use it.
Put your token into the "Token" field of the "Telegram" section:
```json
"Token": "0000000000:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
```
Add trusted user's Telegram IDs to the "Users -> TelegramId" list.

You can find Telegram IDs in the console log by trying to message your bot with software running or using common Telegram @userinfobot.

"DefaultVideoTime" parameter sets the default video clip duration. Max duration is limited to 120 seconds.

The default setting is:
```json
"DefaultVideoTime": 30
```

"DefaultVideoQuality" parameter sets the default video clip quality [0%..100%].

The default setting is:
```json
"DefaultVideoQuality": 90
```

"DefaultImageQuality" parameter sets the default image quality [0%..100%].

The default setting is:
```json
"DefaultImageQuality": 90
```

### Camera definitions
The basic idea is to find all the available cameras automatically. Just allow it with the following parameters in the "CameraSettings" section:
```json
"AutoSearchIp": true,
"AutoSearchUsb": true,
"AutoSearchUsbFC": true,
```

You also need to set the default role to allow access to the cameras found:
```json
"DefaultAllowedRoles": [ "Admin" ]
```

You may want to define cameras manually instead of using automatic discovery. This is more comfortable because you can set proper camera names.
Every camera can be set up to allow certain roles to see the image. This can be done in the "CustomCameras" section:
```json
"CustomCameras": [
  {
    "Type": "IP",
    "Name": "*VStarCam01",
    "Path": "rtsp://{0}:{1}@192.168.1.50:10554/tcp/av0_0",
    "AuthenicationType": "Plain",
    "Login": "admin",
    "Password": "",
    "AllowedRoles": [ "Admin", "User", "Guest" ]
  },
  {
    "Type": "MJPEG",
    "Name": "*WebCamera-Local",
    "Path": "http://localhost:808/Camera/GetVideoContent?cameraNumber=2&xResolution=1920&yResolution=1080",
    "AuthenicationType": "Basic",
    "Login": "Admin",
    "Password": "",
    "AllowedRoles": [ "User", "Admin", "Guest" ]
  },
]
```

There are 3 basic camera types now:
- USB
- USB_FC
- IP
- MJPEG
USB_FC and USB use the same USB cameras so it's not recommended to use them simultaneously. Try them one by one and leave the one you prefer (due to memory management, image quality, etc.).

You can see the path to the USB camera in the console log of the program at the start-up. Look for the:
```
USB-Camera: USB Camera - [@device:pnp:\\?\usb#vid_05a3&pid_9422&mi_00#8&4573432&0&0000#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global]
```

For the IP cameras, you can also look at the console log:
```
IP-Camera: 192.168.102.126 - [rtsp://192.168.102.126:554/user=admin_password=_channel=1_stream=0.sdp?real_stream]
```
...or scan your network for ONVIF cameras using an external tool. Try [ONVIF Device Manager](https://sourceforge.net/projects/onvifdm/files/), which is much more capable.

Just find your cameras and put the correct "rtsp://***" link into the appsettings.json

For the MJPEG cameras, you should know exactly the path, authentication type and login/password. There is no reasonable way to detect it automatically.

Implemented authorisation types:
- None - login/password ignored even if there is anything.
- Plain - login and password are inserted into the URL string. {0} stands for login, {1} stands for password.
- Basic - login and password are passed in the request body.

Be careful setting the "AllowedRoles" for the cameras to not compromise your system.

The "MaxFrameBuffer" setting sets the maximum number of frames to keep for each client. In case the client connection or the image processing is too slow (peak load?) it'll help to smoothen the load. The default setting is:
```json
"MaxFrameBuffer": 10
```

The "DiscoveryTimeOut" settings define the IP camera query delay in ms.
```json
"DiscoveryTimeOut": 1000,
```

### Camera recorder settings
Set up basic parameters for the video recorder in the "Recorder" section:

"StoragePath": ".\\Videos" - where to store files

"VideoFileLengthSeconds": 300 - how long each file should be (seconds)

"DefaultVideoQuality": 90 - video encoder quality

and then define the recorded streams themselves in the "RecordCameras" sub-section:
```json
    "RecordCameras": [
      {
        "CameraId": "rtsp://admin:@192.168.102.50:10554/tcp/av0_0",
        "Width": 0,
        "Height": 0,
        "User": "Admin",
        "CameraFrameFormat": "",
        "Quality": 95,
        "Fps": 7.5
      },
    ]
}
```
"Width" and "Height" can be set to 0 to use the original stream resolution.

"User" - which user will be responsible for this recorder (mostly related to Telegram users).

"CameraFrameFormat": "" - which format to use for selection of the camera stream (only makes sense if more than one is available). Can be left empty by default.

"Quality": 95 - video encoder quality [0%..100%].

"Fps": 7.5 - just the initial FPS value if you like. Since the IP streams have variable FPS it will be recalculated for every file saved.

### Motion detection settings
Set up basic parameters for the motion detector in the "MotionDetector" section:

"StoragePath": ".\\MotionRecords"  - where to store notifications if any are enabled.

The default settings for the detector are mostly used for Telegram clients as it will overload the Telegram menu to introduce them all:
```json
    "DefaultMotionDetectParameters": {
      "Width": 640,
      "Height": 480,
      "DetectorDelayMs": 1000,
      "NoiseThreshold": 80,
      "ChangeLimit": 0.003,
      "NotificationDelay": 30,
      "KeepImageBuffer": 10
    }
```
"Width": 640, "Height": 480 - image size for actual processing. This is to off-load the processor and increasing it won't  make the detector much better.

"DetectorDelayMs": 1000 - the detector compares images once per certain time to evaluate actual image changes.

"NoiseThreshold": 80 - every grayscale pixel has a value range [0..255] and every camera sensor has some noise. This is to reduce false positive detection due to the noise.

"ChangeLimit": 0.003 - the amount of pixels changed on the image to react.

"NotificationDelay": 30 - minimum delay between notifications. You don't want the notifications to come every second while something is moving behind the camera.

"KeepImageBuffer": 10 - keep the latest images in the buffer to add them to a video notification. If it's 0 then the video file received only starts from the point the movement is detected.

and then define the notifications in the "Notifications" sub-section:
```json
        "Notifications": [
          {
            "Transport": "Telegram", // Telegram
            "MessageType": "Video", //Text, Image, Video
            "Destination": "000000",
            "Message": "Camera movement video",
            "VideoLengthSec": 10,
            "SaveNotificationContent": false
          },
        ]
```
The parameters are self-explanatory except for:

"VideoLengthSec": 10 - length of the video file to send. Only valid for video notification.

"SaveNotificationContent": false - you can save the notifications (pictures, video or text) in the file system.

## Planned features:
- Serilog logging
- Web-based configuration interface
