using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Remote.Models;

namespace SeedVr.Remote
{
    public class SeedVrJobRunner
    {
        private readonly ComfyUiClient _comfyUiClient;
        private readonly VastAiClient _vastAiClient;
        private readonly AppSettings _appSettings;
        private readonly ILogger<SeedVrJobRunner> _logger;

        public SeedVrJobRunner(ComfyUiClient comfyUiClient, VastAiClient vastAiClient, IOptions<AppSettings> appSettingsOptions, ILogger<SeedVrJobRunner> logger)
        {
            _comfyUiClient = comfyUiClient;
            _vastAiClient = vastAiClient;
            _appSettings = appSettingsOptions.Value;
            _logger = logger;
        }

        public async Task<bool> Run(CancellationToken cancellationToken = default)
        {
            var vastInstanceAddress = await GetVastInstanceAddress(cancellationToken);
            if (vastInstanceAddress == null)
            {
                return false;
            }

            var isComfyUiRunning = await IsComfyUiRunning(vastInstanceAddress, cancellationToken);
            if (!isComfyUiRunning)
            {
                return false;
            }

            var isComfyUiReady = await IsComfyUiReady(vastInstanceAddress, cancellationToken);
            return isComfyUiReady;
        }

        /// <summary>The instance's current ComfyUI address, or null when it could not be resolved.</summary>
        private async Task<string> GetVastInstanceAddress(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Resolving address of Vast.ai instance {InstanceId}...", _appSettings.VastAiInstanceId);

            VastAiInstance instance;
            try
            {
                instance = await _vastAiClient.GetInstance(_appSettings.VastAiInstanceId, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to read instance details from the Vast.ai API");
                return null;
            }

            if (instance == null)
            {
                _logger.LogError("Vast.ai reports no instance {InstanceId} on this account.", _appSettings.VastAiInstanceId);
                return null;
            }

            if (instance.ActualStatus != Constants.VastAi.RunningStatus)
            {
                _logger.LogError("Vast.ai instance {InstanceId} is '{Status}', but 'running' status is expected.", instance.Id, instance.ActualStatus);
                return null;
            }

            var hostPort = instance.Ports?.ComfyUi?.FirstOrDefault()?.HostPort;

            if (string.IsNullOrWhiteSpace(instance.PublicIpAddress) || string.IsNullOrWhiteSpace(hostPort))
            {
                _logger.LogError("Vast.ai instance {InstanceId} does not expose {Port}. Is ComfyUI running on it?", instance.Id, Constants.VastAi.ComfyUiContainerPort);
                return null;
            }

            var vastInstanceAddress = $"http://{instance.PublicIpAddress}:{hostPort}/";
            _logger.LogInformation("Vast.ai instance {InstanceId} is running at {Address}", instance.Id, vastInstanceAddress);

            return vastInstanceAddress;
        }

        private async Task<bool> IsComfyUiRunning(string vastInstanceAddress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Checking ComfyUI instance health (GET /system_stats)...");

            try
            {
                var stats = await _comfyUiClient.GetSystemStats(vastInstanceAddress, cancellationToken);
                _logger.LogInformation("ComfyUI is reachable. /system_stats response: {Stats}", stats);
                return true;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to reach ComfyUI instance");
                return false;
            }
        }

        private async Task<bool> IsComfyUiReady(string vastInstanceAddress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Checking downloaded models on instance (GET /models/{Folder}). DiT: {DitModel}, VAE: {VaeModel}", Constants.ComfyUi.SeedVrModelFolder, _appSettings.DitModel, _appSettings.VaeModel);

            IReadOnlyList<string> installedModels;
            try
            {
                installedModels = await _comfyUiClient.GetInstalledModels(vastInstanceAddress, Constants.ComfyUi.SeedVrModelFolder, cancellationToken);
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

            var ditInstalled = installedModels.Contains(_appSettings.DitModel);
            var vaeInstalled = installedModels.Contains(_appSettings.VaeModel);

            if (ditInstalled && vaeInstalled)
            {
                _logger.LogInformation("Instance is ready: selected DiT and VAE models are both downloaded.");
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
