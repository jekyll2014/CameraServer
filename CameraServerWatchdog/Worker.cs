using Microsoft.Extensions.Options;

using System.Net;
using System.ServiceProcess;

namespace CameraServerWatchdog
{
    public class Worker : BackgroundService
    {
        private readonly WatchDogSettings _settings;
        private readonly ILogger<Worker> _logger;

        public Worker(WatchDogSettings settings, ILogger<Worker> logger)
        {
            _settings = settings;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"CameraServer Watchdog Service running at: {DateTimeOffset.Now}");
            var counter = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await PingServer(_settings.Url))
                {
                    _logger.LogInformation($"CameraServer not responding");
                    counter++;

                    if (counter >= _settings.MaxFailCount)
                    {
                        counter = 0;
                        _logger.LogInformation($"{_settings.Url} not responding. Restarting service [{_settings.ServiceName}]");
                        RestartService(_settings.ServiceName, 5000);
                    }
                }
                else
                {
                    counter = 0;
                }

                await Task.Delay(_settings.Timeout * 1000, stoppingToken); // wait for 2 minutes
            }
        }

        private async Task<bool> PingServer(string currencyServiceUrl)
        {
            var client = new HttpClient();
            try
            {
                var response = await client.PostAsync(currencyServiceUrl, null);
                var responseStatus = response.StatusCode;

                if (responseStatus != HttpStatusCode.OK)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation($"Service [{currencyServiceUrl}] responded error: {responseStatus}");
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Service [{currencyServiceUrl}] request exception: {ex}");

                return false;
            }

            return true;
        }

        public void RestartService(string serviceName, int timeoutMilliseconds)
        {
            var service = new ServiceController(serviceName);
            var millisec1 = Environment.TickCount;
            var timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds);
            try
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Service [{serviceName}] stop exception: {ex}");
            }

            try
            {
                // count the rest of the timeout
                var millisec2 = Environment.TickCount;
                timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds - (millisec2 - millisec1));
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, timeout);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Service [{serviceName}] start exception: {ex}");
            }
        }
    }
}
