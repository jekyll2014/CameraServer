using CameraServer.Auth;
namespace CameraServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            // uncomment for JWT auth
            //ConfigurationManager configuration = builder.Configuration;

            // Add services to the container.
            builder.Services.AddTransient<IUserManager, UserManager>();
            builder.Services.AddSingleton<CamerasCollection, CamerasCollection>();
            builder.Services.AddSwaggerGenNewtonsoftSupport();

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
