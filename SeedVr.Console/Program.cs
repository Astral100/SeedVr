using SeedVr.Remote;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SeedVr.Core;

namespace SeedVr.Console
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            var app = CreateHostApp();
            var logger = app.Services.GetRequiredService<ILogger<Program>>();

            try
            {
                var runner = app.Services.GetRequiredService<SeedVrJobRunner>();
                await runner.RunAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Startup failed");
                Environment.ExitCode = 1;
            }
        }

        private static WebApplication CreateHostApp()
        {
            var builder = WebApplication.CreateBuilder();

            builder.Services.AddOptions<AppSettings>()
                .BindConfiguration(AppSettings.ConfigurationSection)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            builder.Services.AddHttpClient<ComfyUiClient>();
            builder.Services.AddTransient<SeedVrJobRunner>();

            return builder.Build();
        }
    }
}
