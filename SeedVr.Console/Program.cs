using SeedVr.Remote;
using SeedVr.Remote.HttpClients;
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

            using var cancellation = new CancellationTokenSource();
            System.Console.CancelKeyPress += (_, eventArgs) =>
            {
                // Handle the signal so the run unwinds, logs and flushes rather than the process being killed outright.
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            try
            {
                var app = CreateHostApp();
                var orchestrator = app.Services.GetRequiredService<JobOrchestrator>();
                var success = await orchestrator.StartJob(cancellation.Token);
                return success ? 0 : 1;
            }
            catch (OperationCanceledException)
            {
                Log.Warning("Run cancelled.");
                return 1;
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
            // Pin the content root to the app's own directory so appsettings load from where they are copied,
            // leaving the working directory free to point at the repo root where videos/ and workflows/ live.
            var hostSettings = new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory };
            var builder = Host.CreateApplicationBuilder(hostSettings);

            // Logging goes through Serilog, so the built-in console provider would only duplicate the output.
            builder.Logging.ClearProviders();

            builder.Services.AddOptions<AppSettings>()
                .BindConfiguration(AppSettings.ConfigurationSection)
                .ValidateDataAnnotations();

            builder.Services.AddHttpClient<ComfyUiClient>();
            builder.Services.AddHttpClient<ComfyWrapperClient>();
            builder.Services.AddHttpClient<VastAiClient>();
            builder.Services.AddTransient<ComfyProgressClient>();
            builder.Services.AddTransient<WorkflowBuilder>();
            builder.Services.AddTransient<InstanceSelector>();
            builder.Services.AddTransient<JobRunner>();
            builder.Services.AddTransient<JobOrchestrator>();

            var app = builder.Build();

            // Settings are validated when they are first read, so read them here rather than
            // letting an invalid value surface partway through a job.
            _ = app.Services.GetRequiredService<IOptions<AppSettings>>().Value;

            return app;
        }
    }
}
