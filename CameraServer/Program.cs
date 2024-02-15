using CameraServer.Auth;
using CameraServer.Services.CameraHub;
using CameraServer.Services.Telegram;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.OpenApi.Models;

namespace CameraServer
{
    public class Program
    {
        public const string ExpireTimeSection = "CookieExpireTimeMinutes";

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var expireTime = builder.Configuration.GetValue<int>(ExpireTimeSection, 60);
            // Add services to the container.
            builder.Services.AddTransient<IUserManager, UserManager>();
            builder.Services.AddSingleton<CameraHubService, CameraHubService>();
            builder.Services.AddHostedService<TelegramService>();
            builder.Services.AddControllersWithViews().AddControllersAsServices();
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

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "BasicAuth", Version = "v1" });
                /*options.AddSecurityDefinition("basic", new OpenApiSecurityScheme
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

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}"
                );
            });

            app.Run();
        }
    }
}
