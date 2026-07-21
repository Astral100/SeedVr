using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeedVr.Core;

namespace SeedVr.Remote
{
    public class SeedVrJobRunner
    {
        private readonly ComfyUiClient _comfyUiClient;
        private readonly AppSettings _appSettings;
        private readonly ILogger<SeedVrJobRunner> _logger;

        public SeedVrJobRunner(ComfyUiClient comfyUiClient, IOptions<AppSettings> appSettingsOptions, ILogger<SeedVrJobRunner> logger)
        {
            _comfyUiClient = comfyUiClient;
            _appSettings = appSettingsOptions.Value;
            _logger = logger;
        }

        /// <summary>Returns true when the instance is running and ready to process the configured job.</summary>
        public async Task<bool> Run(CancellationToken cancellationToken = default)
        {
            if (!await CheckInstanceIsRunning(cancellationToken))
            {
                return false;
            }

            return await CheckInstanceIsReady(cancellationToken);
        }

        private async Task<bool> CheckInstanceIsRunning(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Checking ComfyUI instance health (GET /system_stats)...");

            try
            {
                var stats = await _comfyUiClient.GetSystemStats(cancellationToken);
                _logger.LogInformation("ComfyUI is reachable. /system_stats response: {Stats}", stats);
                return true;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to reach ComfyUI instance");
                return false;
            }
        }

        private async Task<bool> CheckInstanceIsReady(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Checking downloaded models on instance (GET /models/{Folder}). DiT: {DitModel}, VAE: {VaeModel}", Constants.ComfyUi.SeedVrModelFolder, _appSettings.DitModel, _appSettings.VaeModel);

            IReadOnlyList<string> installedModels;
            try
            {
                installedModels = await _comfyUiClient.GetInstalledModels(Constants.ComfyUi.SeedVrModelFolder, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogError("The instance has no '{Folder}' models folder. Is the SeedVR2 node pack installed?", Constants.ComfyUi.SeedVrModelFolder);
                return false;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to read the installed models from the instance");
                return false;
            }

            _logger.LogInformation("Models downloaded on instance ({Count}): {Models}", installedModels.Count, string.Join(", ", installedModels));

            // Case-sensitive: the instance is Linux, and these names are submitted verbatim in the workflow.
            var ditInstalled = installedModels.Contains(_appSettings.DitModel);
            var vaeInstalled = installedModels.Contains(_appSettings.VaeModel);

            if (ditInstalled && vaeInstalled)
            {
                _logger.LogInformation("Instance is ready: the selected DiT and VAE models are both downloaded.");
                return true;
            }

            if (!ditInstalled)
            {
                _logger.LogError("DiT model {DitModel} is not downloaded on the instance.", _appSettings.DitModel);
            }

            if (!vaeInstalled)
            {
                _logger.LogError("VAE model {VaeModel} is not downloaded on the instance.", _appSettings.VaeModel);
            }

            _logger.LogError("Instance is not ready to run the job.");
            return false;
        }
    }
}
