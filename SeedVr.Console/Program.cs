using SeedVr.Remote;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SeedVr.Core;

namespace SeedVr.Console
{
    public class Program
    {
        static async Task<int> Main(string[] args)
        {
            WebApplication app = null;

            try
            {
                app = CreateHostApp();
                var runner = app.Services.GetRequiredService<SeedVrJobRunner>();
                var isInstanceReady = await runner.Run();
                return isInstanceReady ? 0 : 1;
            }
            catch (Exception ex)
            {
                // The host may have failed to build, in which case there is no logger yet.
                var logger = app?.Services.GetService<ILogger<Program>>();

                if (logger != null)
                {
                    logger.LogError(ex, "Startup failed");
                }
                else
                {
                    System.Console.Error.WriteLine($"Startup failed: {ex.Message}");
                }

                return 1;
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
            builder.Services.AddHttpClient<VastAiClient>();
            builder.Services.AddTransient<SeedVrJobRunner>();

            return builder.Build();
        }
    }
}
