using CameraServer.Server.Auth;
using CameraServer.Server.Auth.BasicAuth;
using CameraServer.Server.Services.AntiBruteForce;
using CameraServer.Server.Services.CameraHub;
using CameraServer.Server.Services.Configuration;
using CameraServer.Server.Services.MotionDetection;
using CameraServer.Server.Services.Telegram;
using CameraServer.Server.Services.VideoRecording;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;

using MudBlazor.Services;

using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace CameraServer.Server;

public class Program
{
    public const string ExpireTimeSection = "CookieExpireTimeMinutes";
    public const string BasicAuthenticationSchemeName = "BasicAuthentication";

    private static Serilog.Core.Logger? _logger;
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        _logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console(LogEventLevel.Information)
            .WriteTo.Logger(l => l
                .Filter.ByIncludingOnly(n => n.Level == LogEventLevel.Verbose)//WithProperty("EventId", 1001))
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    "telegram_api.log.json",
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    retainedFileCountLimit: 10,
                    rollOnFileSizeLimit: true,
                    shared: false,
                    flushToDiskInterval: TimeSpan.FromSeconds(2)))
            .WriteTo.Logger(l => l
                .Filter.ByIncludingOnly(n => n.Level != LogEventLevel.Debug
                                             && n.Level != LogEventLevel.Verbose)
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    "CameraServer.log.json",
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    retainedFileCountLimit: 10,
                    rollOnFileSizeLimit: true,
                    shared: false,
                    flushToDiskInterval: TimeSpan.FromSeconds(2)))
            .WriteTo.Logger(l => l
                .Filter.ByIncludingOnly(n => n.Level == LogEventLevel.Debug)
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    path: "CameraServer_debug.log.json",
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    retainedFileCountLimit: 10,
                    rollOnFileSizeLimit: true,
                    shared: false,
                    flushToDiskInterval: TimeSpan.FromSeconds(2)))
            .CreateLogger();

        TryKillOldProcess();

        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration.SetBasePath(Directory.GetCurrentDirectory());

        var serverUrls = builder.WebHost.GetSetting("Urls") ?? "http://0.0.0.0:8080";
        try
        {
            foreach (var serverUrl in serverUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var serverPort = new Uri(serverUrl ?? "").Port;
                if (PortInUse(serverPort))
                {
                    _logger?.Error($"Port in use. Trying to release...");

                    // Only attempt Windows-specific port cleanup on Windows
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        ExecuteShellCommand("net", "stop winnat");
                        Task.Delay(1000).RunSynchronously();
                        ExecuteShellCommand("net", "start winnat");
                    }
                    else
                    {
                        _logger?.Error($"Port {serverPort} is in use on non-Windows platform. Please free it manually.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Error($"Host ports check/clean-up failed: {ex}");
        }

        builder.Host.UseSerilog(_logger);

        builder.Services.AddMudServices();

        // Add services to the container.
        builder.Services.AddSingleton<IServerConfigurationManager, ServerServerConfigurationManager>();
        builder.Services.AddSingleton<IBruteForceDetectionService, BruteForceDetectionDetectionService>();
        builder.Services.AddTransient<IUserManager, UserManager>();
        builder.Services.AddSingleton<CameraHubService, CameraHubService>();
        builder.Services.AddSingleton<IRuntimeConfigurationService, RuntimeConfigurationService>();
        builder.Services.AddSingleton<VideoRecorderService>();
        builder.Services.AddHostedService<VideoRecorderService>(provider => provider.GetService<VideoRecorderService>());
        builder.Services.AddSingleton<TelegramService>();
        builder.Services.AddHostedService<TelegramService>(provider => provider.GetService<TelegramService>());
        builder.Services.AddSingleton<MotionDetectionService>();
        builder.Services.AddHostedService<MotionDetectionService>(provider => provider.GetService<MotionDetectionService>());

        builder.Services.AddControllers().AddControllersAsServices();
        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddCors(o => o.AddPolicy("MyPolicy", builder =>
        {
            builder.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
        }));

        builder.Services.AddAuthentication(BasicAuthenticationSchemeName)
            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(BasicAuthenticationSchemeName, null);

        var expireTime = builder.Configuration.GetValue<int>(ExpireTimeSection, 60);
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                //options.LoginPath = "/Authenticate/login";
                //options.LogoutPath = "/Authenticate/logout";
                options.ExpireTimeSpan = TimeSpan.FromMinutes(expireTime);
                options.SlidingExpiration = true;
            });
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddAuthorization();

        builder.Services.AddHealthChecks();

        builder.Services.AddSwaggerGenNewtonsoftSupport();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "CameraServer API", Version = "v1" });
        });

        builder.Services.AddRazorPages();

        var app = builder.Build();

        app.UseSerilogRequestLogging();

        app.UseSwagger();
        app.UseSwaggerUI();

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseWebAssemblyDebugging();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        // Configure the HTTP request pipeline.
        //app.UseHttpsRedirection();

        app.UseRouting();
        app.UseAntiforgery();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();


        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();

        app.MapRazorPages();

        app.MapFallbackToFile("index.html");

        app.MapHealthChecks("/healthcheck");

        Console.WriteLine($"Starting at: {serverUrls}");

        app.Run();
    }

    private static void TryKillOldProcess()
    {
        try
        {
            var currentProcess = Process.GetCurrentProcess();
            var oldProcess = Process.GetProcessesByName(currentProcess.ProcessName).Where(n => n.Id != currentProcess.Id).ToArray();
            if (oldProcess.Any())
            {
                _logger?.Error($"Another application copy is running. Trying to kill...");
                foreach (var p in oldProcess)
                    p?.Kill(true);
            }
        }
        catch (Exception exception)
        {
            _logger?.Error($"Process management exception: {exception.Message}");
        }
    }

    private static bool PortInUse(int port)
    {
        var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
        var ipEndPoints = ipProperties.GetActiveTcpListeners();

        return ipEndPoints.Any(n => n.Port == port);
    }

    private static bool ExecuteShellCommand(string command, string args)
    {
        var processInfo = new ProcessStartInfo(command, args)
        {
            CreateNoWindow = true,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            var p = Process.Start(processInfo);
            return p?.WaitForExit(10000) ?? false;
        }
        catch (Exception exception)
        {
            _logger?.Error($"Shell command execution exception: {exception.Message}");
        }

        return false;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            _logger?.Error($"Unhandled exception: {exception.Message}");
    }
}
