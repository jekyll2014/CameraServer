namespace CameraServer.Server.Exceptions;

[Serializable]
public class DuplicateKeyException : Exception
{
    public DuplicateKeyException()
        : this("Duplicate key")
    {
    }

    public DuplicateKeyException(string message) : base(message)
    {
    }

    public DuplicateKeyException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
