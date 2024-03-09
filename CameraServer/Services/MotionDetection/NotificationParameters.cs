namespace CameraServer.Services.MotionDetection;

public class NotificationParameters
{
    public NotificationTransport Transport { get; set; } = NotificationTransport.Telegram;
    public MessageType MessageType { get; set; } = MessageType.Image;
    public string Destination { get; set; } = string.Empty; // ChatID for Telegram, E-mail address for Email
    public string Message { get; set; } = string.Empty;
    public uint VideoLengthSec { get; set; } = 10;
}