using CameraServer.Shared;

using System.Net.Http.Json;

namespace CameraServer.Client.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        public event IAuthenticationService.LogInHandler? OnLogIn;
        public event IAuthenticationService.LogOutHandler? OnLogOut;

        public UserInfoModel? UserInfo
        {
            get => _userInfo;
            private set
            {
                _userInfo = value;
                if (_userInfo != null)
                    OnLogIn?.Invoke(UserInfo);
                else
                    OnLogOut?.Invoke();
            }
        }
        public bool IsAuthenticated => UserInfo != null;

        private UserInfoModel? _userInfo = null;
        private readonly HttpClient _httpClient;

        public AuthenticationService(HttpClient httpClient)
        {
            Console.WriteLine("AuthenticationService: started");

            _httpClient = httpClient;
            IsLoggedIn();

            Console.WriteLine($"AuthenticationService: UserInfo = {UserInfo?.Login}");
        }

        public async Task<bool> IsLoggedIn()
        {
            Console.WriteLine("AuthenticationService: IsLoggedIn() started");
            try
            {
                UserInfo = await _httpClient.GetFromJsonAsync<UserInfoModel>($"/Authenticate/IsLoggedIn/");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AuthenticationService: IsLoggedIn() exception: {ex}");
            }

            return UserInfo != null;
        }

        public async Task<UserInfoModel?> Login(LoginModel loginModel)
        {
            Console.WriteLine("AuthenticationService: Login() started");

            try
            {
                var result = await _httpClient.PostAsJsonAsync<LoginModel>($"/Authenticate/Login/", loginModel);
                if (result.IsSuccessStatusCode)
                {
                    UserInfo = await result.Content.ReadFromJsonAsync<UserInfoModel>();
                    //OnLogIn?.Invoke(UserInfo);
                }
                else
                    UserInfo = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AuthenticationService: Login() exception: {ex}");
            }

            return UserInfo;
        }

        public async Task<bool> Logout()
        {
            Console.WriteLine("AuthenticationService: Logout() started");

            try
            {
                var result = await _httpClient.PostAsync($"/Authenticate/Logout/", null);
                if (result.IsSuccessStatusCode)
                {
                    UserInfo = null;
                    //OnLogOut?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AuthenticationService: Logout() exception: {ex}");
            }

            return IsAuthenticated;
        }
    }
}
