using Microsoft.Extensions.Logging;

namespace SeedVr.Remote
{
    public class SeedVrJobRunner
    {
        private readonly ComfyUiClient _comfyUiClient;
        private readonly ILogger<SeedVrJobRunner> _logger;

        public SeedVrJobRunner(ComfyUiClient comfyUiClient, ILogger<SeedVrJobRunner> logger)
        {
            _comfyUiClient = comfyUiClient;
            _logger = logger;
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Checking ComfyUI instance health (GET /system_stats)...");

            try
            {
                var stats = await _comfyUiClient.GetSystemStatsAsync(cancellationToken);
                _logger.LogInformation("ComfyUI is reachable. /system_stats response: {Stats}", stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reach ComfyUI instance");
            }
        }
    }
}
