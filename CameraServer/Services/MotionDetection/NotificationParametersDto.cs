namespace CameraServer.Services.MotionDetection;

public class NotificationParametersDto
{
    public NotificationTransport Transport { get; set; } = NotificationTransport.Telegram;
    public MessageType MessageType { get; set; } = MessageType.Image;

    // ChatID for Telegram, E-mail address for Email
    public string Destination { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public uint VideoLengthSec { get; set; } = 10;
    public bool SaveNotificationContent { get; set; } = false;
}