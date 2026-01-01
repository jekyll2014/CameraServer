using CameraServer.Server.Models;

using Microsoft.Extensions.Logging;

namespace CameraServer.Server.Services.Configuration;

/// <summary>
/// Unified configuration manager for the entire appsettings.json file.
/// This is a singleton service that manages a single Config<AppSettings> instance,
/// ensuring thread-safe access and preventing data loss from multiple writers.
/// </summary>
public interface IServerConfigurationManager
{
    /// <summary>
    /// Gets the entire application settings object.
    /// </summary>
    AppSettings GetSettings();

    /// <summary>
    /// Gets a specific section of the configuration.
    /// </summary>
    T GetSection<T>(Func<AppSettings, T> selector) where T : class;

    /// <summary>
    /// Saves the entire application settings to appsettings.json.
    /// Thread-safe operation that ensures atomic writes.
    /// </summary>
    bool SaveSettings();

    /// <summary>
    /// Updates a specific section and saves the entire configuration.
    /// </summary>
    bool UpdateSection<T>(Func<AppSettings, T> selector, Action<AppSettings, T> updater) where T : class;
}

public class ServerServerConfigurationManager : IServerConfigurationManager
{
    private readonly Config<AppSettings> _config;
    private readonly ILogger<ServerServerConfigurationManager> _logger;
    private readonly ReaderWriterLockSlim _configLock = new();

    public ServerServerConfigurationManager(ILogger<ServerServerConfigurationManager> logger)
    {
        _logger = logger;
        _config = new Config<AppSettings>("appsettings.json");

        if (!_config.LoadConfig())
        {
            _logger.LogWarning("Failed to load appsettings.json or file does not exist");
        }
    }

    public AppSettings GetSettings()
    {
        _configLock.EnterReadLock();
        try
        {
            return _config.ConfigStorage;
        }
        finally
        {
            _configLock.ExitReadLock();
        }
    }

    public T GetSection<T>(Func<AppSettings, T> selector) where T : class
    {
        _configLock.EnterReadLock();
        try
        {
            return selector(_config.ConfigStorage);
        }
        finally
        {
            _configLock.ExitReadLock();
        }
    }

    public bool SaveSettings()
    {
        _configLock.EnterWriteLock();
        try
        {
            var success = _config.SaveConfig();
            if (success)
            {
                _logger.LogInformation("Application settings saved to appsettings.json");
            }
            else
            {
                _logger.LogError("Failed to save application settings");
            }
            return success;
        }
        finally
        {
            _configLock.ExitWriteLock();
        }
    }

    public bool UpdateSection<T>(Func<AppSettings, T> selector, Action<AppSettings, T> updater) where T : class
    {
        _configLock.EnterWriteLock();
        try
        {
            var section = selector(_config.ConfigStorage);
            if (section != null)
            {
                updater(_config.ConfigStorage, section);
                return SaveSettings();
            }
            else
            {
                _logger.LogWarning("Section not found in configuration");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating configuration section");
            return false;
        }
        finally
        {
            _configLock.ExitWriteLock();
        }
    }
}
