using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Remote.Models;

namespace SeedVr.Remote
{
    /// <summary>
    /// Reads instance details from the Vast.ai account API, so the ComfyUI address can be
    /// discovered rather than hardcoded - Vast.ai reassigns the external port on every start.
    /// </summary>
    public class VastAiClient
    {
        private readonly HttpClient _httpClient;

        public VastAiClient(HttpClient httpClient, IOptions<AppSettings> appSettingsOptions)
        {
            var appSettings = appSettingsOptions.Value;

            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(Constants.VastAi.ApiBaseUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(appSettings.HttpTimeoutSeconds);

            if (!string.IsNullOrWhiteSpace(appSettings.VastAiApiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appSettings.VastAiApiKey);
            }
        }

        /// <summary>The instance as Vast.ai currently reports it, or null when the account has no instance with that id.</summary>
        public async Task<VastAiInstance> GetInstance(int instanceId, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.GetFromJsonAsync<VastAiResponse>(Constants.VastAi.InstancesPath, cancellationToken);
            return response?.Instances?.FirstOrDefault(instance => instance.Id == instanceId);
        }
    }
}
