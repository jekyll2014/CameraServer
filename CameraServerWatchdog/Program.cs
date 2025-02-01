using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;

namespace CameraServerWatchdog;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "CameraServer Watchdog Service";
        });

        LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(builder.Services);
        WatchDogSettings watchdogSettings = new WatchDogSettings();
        builder.Configuration.Bind("WatchDogSettings", watchdogSettings);
        builder.Services.AddSingleton(watchdogSettings);
        builder.Services.AddHostedService<Worker>();

        var host = builder.Build();
        host.Run();
    }
}
