namespace CameraServer.Server.Models;

public class ServerSettings
{
    public string Urls { get; private set; }
    public bool AllowBasicAuthentication { get; private set; }
    public int CookieExpireTimeMinutes { get; private set; }
    public string AllowedHosts { get; private set; }
    public string ExternalHostUrl { get; private set; }

    public ServerSettings(string urls, bool allowBasicAuthentication, int cookieExpireTimeMinutes, string allowedHosts, string externalHostUrl)
    {
        Urls = urls;
        AllowBasicAuthentication = allowBasicAuthentication;
        CookieExpireTimeMinutes = cookieExpireTimeMinutes;
        AllowedHosts = allowedHosts;
        ExternalHostUrl = externalHostUrl;
    }
}