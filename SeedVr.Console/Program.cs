using SeedVr.Remote;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
                var runner = app.Services.GetRequiredService<JobRunner>();
                var submitted = await runner.Run();
                return submitted ? 0 : 1;
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

        private static IHost CreateHostApp()
        {
            var builder = Host.CreateApplicationBuilder();

            // Logging goes through Serilog, so the built-in console provider would only duplicate the output.
            builder.Logging.ClearProviders();

            builder.Services.AddOptions<AppSettings>()
                .BindConfiguration(AppSettings.ConfigurationSection)
                .ValidateDataAnnotations();

            builder.Services.AddHttpClient<ComfyUiClient>();
            builder.Services.AddHttpClient<ComfyWrapperClient>();
            builder.Services.AddHttpClient<VastAiClient>();
            builder.Services.AddTransient<WorkflowBuilder>();
            builder.Services.AddTransient<InstanceSelector>();
            builder.Services.AddTransient<JobSubmitter>();
            builder.Services.AddTransient<JobRunner>();

            var app = builder.Build();

            // Settings are validated when they are first read, so read them here rather than
            // letting an invalid value surface partway through a job.
            _ = app.Services.GetRequiredService<IOptions<AppSettings>>().Value;

            return app;
        }
    }
}
