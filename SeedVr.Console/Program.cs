using SeedVr.Remote;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SeedVr.Console.Settings;

namespace SeedVr.Console
{
    public class Program
    {
        static void Main(string[] args)
        {
            var app = CreateHostApp();
            var remoteProcessor = app.Services.GetRequiredService<RemoteProcessor>();
            remoteProcessor.ExecuteRemoteProcessing();
        }

        private static WebApplication CreateHostApp()
        {
            var builder = WebApplication.CreateBuilder();

            builder.Services.AddOptions<AppSettings>()
                .BindConfiguration(AppSettings.ConfigurationSection)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            builder.Services.AddSingleton<RemoteProcessor>();

            return builder.Build();
        }
    }
}
