using CameraServer.Shared;

namespace CameraServer.Client.Services
{
    public interface IAuthenticationService
    {
        delegate void LogInHandler(UserInfoModel? userInfoModel);
        event LogInHandler? OnLogIn;

        delegate void LogOutHandler();
        event LogOutHandler? OnLogOut;

        UserInfoModel? UserInfo { get; }
        bool IsAuthenticated { get; }

        Task<bool> IsLoggedIn();
        Task<UserInfoModel?> Login(LoginModel loginModel);
        Task<bool> Logout();
    }
}