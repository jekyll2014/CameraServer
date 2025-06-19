using CameraServer.Shared.Enum;

namespace CameraServer.Shared.DTO;

public class NotificationParametersDto
{
    public NotificationTransport Transport { get; set; } = NotificationTransport.Telegram;
    public MessageType MessageType { get; set; } = MessageType.Image;

    // ChatID for Telegram, E-mail address for Email
    public string Destination { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public uint VideoLengthSec { get; set; } = 10;
    public bool SaveNotificationContent { get; set; } = false;

    public override bool Equals(object? obj)
    {
        if (obj is NotificationParametersDto notification)
        {
            return Transport == notification.Transport
                && MessageType == notification.MessageType
                && Destination == notification.Destination
                && Message == notification.Message
                && VideoLengthSec == notification.VideoLengthSec;
        }
        else
            return false;
    }

    public override int GetHashCode()
    {
        return $"{Transport}{MessageType}{Destination}{Message}{VideoLengthSec}{SaveNotificationContent}".GetHashCode();
    }
}