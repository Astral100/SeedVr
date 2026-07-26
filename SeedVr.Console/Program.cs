using SeedVr.Remote;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Logger;

namespace SeedVr.Console
{
    public class Program
    {
        static async Task<int> Main(string[] args)
        {
            LogRegister.CreateLogger();

            try
            {
                var app = CreateHostApp();
                var runner = app.Services.GetRequiredService<SeedVrJobRunner>();
                var isInstanceReady = await runner.Run();
                return isInstanceReady ? 0 : 1;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected failure");
                return 1;
            }
            finally
            {
                // The file sink buffers, so the tail of the run is lost without this.
                LogRegister.DisposeLogger();
            }
        }

        private static WebApplication CreateHostApp()
        {
            var builder = WebApplication.CreateBuilder();

            // Logging goes through Serilog, so the built-in console provider would only duplicate the output.
            builder.Logging.ClearProviders();

            builder.Services.AddOptions<AppSettings>()
                .BindConfiguration(AppSettings.ConfigurationSection)
                .ValidateDataAnnotations();

            builder.Services.AddHttpClient<ComfyUiClient>();
            builder.Services.AddHttpClient<VastAiClient>();
            builder.Services.AddTransient<SeedVrJobRunner>();

            var app = builder.Build();

            // Settings are validated when they are first read, so read them here rather than
            // letting an invalid value surface partway through a job.
            _ = app.Services.GetRequiredService<IOptions<AppSettings>>().Value;

            return app;
        }
    }
}
