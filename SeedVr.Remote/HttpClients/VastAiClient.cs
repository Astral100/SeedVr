using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Remote.Models.VastAi;

namespace SeedVr.Remote.HttpClients
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

            // VastAiApiKey is [Required], so it is always present here.
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appSettings.VastAiApiKey);
        }

        /// <summary>Every instance on the account, as Vast.ai currently reports them.</summary>
        public async Task<IReadOnlyList<VastAiInstance>> GetInstances(CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.GetFromJsonAsync<VastAiResponse>(Constants.VastAi.InstancesPath, cancellationToken);
            return response?.Instances ?? [];
        }
    }
}
