using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;

namespace SeedVr.Remote
{
    public class ComfyUiClient
    {
        private readonly HttpClient _httpClient;

        public ComfyUiClient(HttpClient httpClient, IOptions<AppSettings> appSettingsOptions)
        {
            var appSettings = appSettingsOptions.Value;
            var baseUrl = appSettings.ComfyUiBaseUrl;

            _httpClient = httpClient;

            _httpClient.BaseAddress = new Uri(baseUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(appSettings.HttpTimeoutSeconds);

            if (!string.IsNullOrWhiteSpace(appSettings.AuthToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appSettings.AuthToken);
            }
        }

        public Task<string> GetSystemStats(CancellationToken cancellationToken = default)
        {
            return _httpClient.GetStringAsync("system_stats", cancellationToken);
        }

        /// <summary>
        /// Model files present on disk in the given ComfyUI models folder. Unlike the node's dropdown,
        /// this lists only what is actually downloaded - a model the node offers may still need
        /// downloading on first use. Throws HttpRequestException with NotFound when the folder is not registered.
        /// </summary>
        public async Task<IReadOnlyList<string>> GetInstalledModels(string folder, CancellationToken cancellationToken = default)
        {
            return await _httpClient.GetFromJsonAsync<List<string>>($"models/{folder}", cancellationToken) ?? [];
        }
    }
}
