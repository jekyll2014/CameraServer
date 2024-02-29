using System.Net;

namespace CameraServer.Services.AntiBruteForce
{
    public class BruteForceDetectionDetectionService : IBruteForceDetectionService, IDisposable//, IHostedService
    {
        private const string AntiBruteForceConfigSection = "BruteForceDetection";
        private readonly BruteForceDetectionSettings _detectionSettings;
        private readonly Dictionary<string, List<(IPAddress, DateTime)>> _userAuthRetries = new Dictionary<string, List<(IPAddress, DateTime)>>();
        private bool _disposedValue;

        public BruteForceDetectionDetectionService(IConfiguration configuration)
        {
            _detectionSettings = configuration.GetSection(AntiBruteForceConfigSection)?.Get<BruteForceDetectionSettings>() ?? new BruteForceDetectionSettings();
        }

        public void AddFailedAttempt(string login, IPAddress host)
        {
            var newAttempt = new ValueTuple<IPAddress, DateTime>()
            {
                Item1 = host,
                Item2 = DateTime.Now
            };

            if (_userAuthRetries.TryGetValue(login, out var attempts))
            {
                attempts.Add(newAttempt);
            }
            else
            {
                attempts = new() { newAttempt };
                _userAuthRetries.TryAdd(login, attempts);
            }
        }

        public bool CheckThreat(string login, IPAddress host)
        {
            if (_userAuthRetries.TryGetValue(login, out var attempts))
            {
                var lastMinuteAtempts = attempts
                    .Where(n => n.Item1.Equals(host)
                                && n.Item2 > DateTime.Now.AddMinutes(-1))
                    .ToArray();

                if (lastMinuteAtempts.Length > _detectionSettings.RetriesPerMinute)
                    return true;

                var lastHourAtempts = attempts
                    .Where(n => n.Item1.Equals(host)
                                && n.Item2 > DateTime.Now.AddHours(-1))
                    .ToArray();

                if (lastHourAtempts.Length > _detectionSettings.RetriesPerHour)
                    return true;
            }

            return false;
        }

        public void ClearFailedAttempts(string login, IPAddress host)
        {
            if (_userAuthRetries.TryGetValue(login, out var attempts))
            {
                attempts.RemoveAll(n => n.Item1.Equals(host));
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
