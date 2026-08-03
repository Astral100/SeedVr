using SeedVr.Remote;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Estimators.Live;
using SeedVr.Estimators.Scoring;
using SeedVr.Estimators.Tracing;
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
                if (args.Length == 2 && args[0] == "--score-estimator-trace")
                {
                    return ScoreEstimatorTrace(args[1]);
                }

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

        /// <summary>Replays a saved run through adaptive-hybrid without contacting Vast.ai.</summary>
        private static int ScoreEstimatorTrace(string path)
        {
            var trace = EstimatorTraceStore.Load(Path.GetFullPath(path));
            var score = EstimatorEvaluator.Score(trace);
            var reporter = new ProgressReporter();
            reporter.ReportCompletion(trace, score);

            return 0;
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

            builder.Services.AddSeedVrRemote();

            var app = builder.Build();

            // Settings are validated when they are first read, so read them here rather than
            // letting an invalid value surface partway through a job.
            _ = app.Services.GetRequiredService<IOptions<AppSettings>>().Value;

            return app;
        }
    }
}
