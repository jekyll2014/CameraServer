using CameraServer.Auth;
using CameraServer.Auth.BasicAuth;
using CameraServer.Services.AntiBruteForce;
using CameraServer.Services.CameraHub;
using CameraServer.Services.MotionDetection;
using CameraServer.Services.Telegram;
using CameraServer.Services.VideoRecording;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace CameraServer
{
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
                //.WriteTo.Console()
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
                    .Filter.ByIncludingOnly(n => n.Level == LogEventLevel.Debug
                                                 && n.Level != LogEventLevel.Verbose)
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

            var serverUrl = builder.WebHost.GetSetting("Urls");
            try
            {
                int serverPort = new Uri(serverUrl ?? "").Port;
                if (PortInUse(serverPort))
                {
                    _logger?.Error($"Port in use. Trying to release...");
                    ExecuteShellCommand("net", "stop winnat");
                    Task.Delay(1000).RunSynchronously();
                    ExecuteShellCommand("net", "start winnat");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Host clean-up failed: {ex}");
            }

            builder.Host.UseSerilog(_logger);

            var expireTime = builder.Configuration.GetValue<int>(ExpireTimeSection, 60);
            // Add services to the container.
            builder.Services.AddSingleton<IBruteForceDetectionService, BruteForceDetectionDetectionService>();
            builder.Services.AddTransient<IUserManager, UserManager>();
            builder.Services.AddSingleton<CameraHubService, CameraHubService>();
            builder.Services.AddSingleton<VideoRecorderService>();
            builder.Services.AddHostedService<VideoRecorderService>(provider => provider.GetService<VideoRecorderService>());
            builder.Services.AddSingleton<TelegramService>();
            builder.Services.AddHostedService<TelegramService>(provider => provider.GetService<TelegramService>());
            builder.Services.AddSingleton<MotionDetectionService>();
            builder.Services.AddHostedService<MotionDetectionService>(provider => provider.GetService<MotionDetectionService>());
            builder.Services.AddControllersWithViews().AddControllersAsServices();

            builder.Services.AddAuthentication(BasicAuthenticationSchemeName)
                .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(BasicAuthenticationSchemeName, null);

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

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                /*options.SwaggerDoc("v1", new OpenApiInfo { Title = "BasicAuth", Version = "v1" });
                options.AddSecurityDefinition("basic", new OpenApiSecurityScheme
                {
                    Login = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "basic",
                    In = ParameterLocation.Header,
                    Description = "Basic Authorization header using the Bearer scheme."
                });
                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "basic"
                            }
                        },
                        new string[] {}
                    }
                });*/
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            //app.UseHttpsRedirection();

            app.UseStaticFiles();

            app.UseSerilogRequestLogging();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();

            //if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}"
                );

            app.MapHealthChecks("/healthcheck");

            app.Run();
        }

        private static void TryKillOldProcess()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var oldProcess = Process.GetProcessesByName(currentProcess.ProcessName).Where(n => n.Id != currentProcess.Id);
                if (oldProcess != null && oldProcess.Any())
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

        public static bool PortInUse(int port)
        {
            IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] ipEndPoints = ipProperties.GetActiveTcpListeners();

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

        public static Func<LogEvent, bool> WithProperty(string propertyName, object scalarValue)
        {
            ArgumentNullException.ThrowIfNull(propertyName);

            var scalar = new ScalarValue(scalarValue);
            return e =>
            {
                if (e.Properties.TryGetValue(propertyName, out var propertyValue))
                {
                    if (propertyValue is StructureValue stValue)
                    {
                        var value = stValue.Properties.FirstOrDefault(cc => cc.Name == "Id");

                        return scalar.Equals(value?.Value);
                    }
                }

                return false;
            };
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
                _logger?.Error($"Unhandled exception: {exception.Message}");
        }
    }
}
