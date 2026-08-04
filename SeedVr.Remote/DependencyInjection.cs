using Microsoft.Extensions.DependencyInjection;
using SeedVr.Remote.HttpClients;

namespace SeedVr.Remote
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddSeedVrRemote(this IServiceCollection services)
        {
            services.AddHttpClient<ComfyUiClient>();
            services.AddHttpClient<ComfyWrapperClient>();
            services.AddHttpClient<VastAiClient>();

            // Vast.ai serves the instance's Jupyter over HTTPS with a self-signed certificate, so this one client skips validation.
            services.AddHttpClient<JupyterClient>()
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    });

            services.AddTransient<GpuRecovery>();
            services.AddTransient<PhaseLinePoller>();
            services.AddTransient<ComfyProgressClient>();
            services.AddTransient<WrapperProgressClient>();
            services.AddTransient<WorkflowBuilder>();
            services.AddTransient<VideoProbe>();
            services.AddTransient<InstanceSelector>();
            services.AddTransient<JobRunner>();
            services.AddTransient<JobOrchestrator>();

            return services;
        }
    }
}
