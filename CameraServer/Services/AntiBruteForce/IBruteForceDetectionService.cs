using System.Net;

namespace CameraServer.Services.AntiBruteForce;

public interface IBruteForceDetectionService
{
    public void AddFailedAttempt(string login, IPAddress host);
    public bool CheckThreat(string login, IPAddress host);
    public void ClearFailedAttempts(string login, IPAddress host);
}