using System.ComponentModel.DataAnnotations;

namespace CameraServer.Views.Account
{
    public class LoginModel
    {
        [Required(ErrorMessage = "Login is required")]
        public string? Login { get; set; }

        //[Required(ErrorMessage = "Password is required")]
        public string? Password { get; set; } = string.Empty;
        public bool RememberLogin { get; set; } = false;
        public string ReturnUrl { get; set; } = "/";
    }
}
