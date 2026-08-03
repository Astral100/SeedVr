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
