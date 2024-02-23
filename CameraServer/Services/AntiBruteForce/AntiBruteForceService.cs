﻿using System.Net;

namespace CameraServer.Services.AntiBruteForce
{
    public class AntiBruteForceService : IAntiBruteForceService, IHostedService, IDisposable
    {
        private const string AntiBruteForceConfigSection = "AntiBruteForce";
        private readonly AntiBruteForceSettings _settings;
        //private CancellationTokenSource? _cts;
        private readonly Dictionary<string, List<(IPAddress, DateTime)>> _userAuthRetries = [];
        private bool _disposedValue;

        public AntiBruteForceService(IConfiguration configuration)
        {
            _settings = configuration.GetSection(AntiBruteForceConfigSection)?.Get<AntiBruteForceSettings>() ?? new AntiBruteForceSettings();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            //_cts = new CancellationTokenSource();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            /*if (_cts != null)
                await _cts.CancelAsync();

            _cts?.Dispose();*/
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
                attempts = [newAttempt];
                _userAuthRetries.TryAdd(login, attempts);
            }
        }

        public bool CheckThreat(string login, IPAddress host)
        {
            var result = false;
            if (_userAuthRetries.TryGetValue(login, out var attempts))
            {
                var lastMinuteAtempts = attempts
                    .Where(n => n.Item1.Equals(host)
                                && n.Item2 > DateTime.Now.AddMinutes(-1))
                    .ToArray();

                if (lastMinuteAtempts?.Length > _settings.RetriesPerMinute)
                    return true;

                var lastHourAtempts = attempts
                    .Where(n => n.Item1.Equals(host)
                                && n.Item2 > DateTime.Now.AddHours(-1))
                    .ToArray();

                if (lastHourAtempts?.Length > _settings.RetriesPerHour)
                    return true;
            }

            return result;
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
                    /*if (!(_cts?.IsCancellationRequested ?? true))
                        _cts.Cancel();

                    _cts?.Dispose();*/
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
