using CameraServer.Server.Models;

using Microsoft.Extensions.Configuration;

namespace CameraServer.Server.Services.Configuration;

/// <summary>
/// Unified configuration manager for the entire appsettings.json file.
/// This is a singleton service that manages a single ServerSettings instance,
/// </summary>
public interface IServerConfigurationManager
{
    /// <summary>
    /// Gets the entire application settings object.
    /// </summary>
    ServerSettings GetSettings();
}

public class ServerConfigurationManager : IServerConfigurationManager
{
    private readonly ServerSettings _config;
    private readonly IConfiguration _configuration;


    public ServerConfigurationManager(IConfiguration configuration)
    {
        _configuration = configuration;
        _config = new ServerSettings(_configuration.GetValue<string>("Server:Urls"),
            _configuration.GetValue<bool>("Server:AllowBasicAuthentication"),
            _configuration.GetValue<int>("Server:CookieExpireTimeMinutes"),
            _configuration.GetValue<string>("Server:ExternalHostUrl")
        );
    }

    public ServerSettings GetSettings()
    {
        return _config;
    }
}
