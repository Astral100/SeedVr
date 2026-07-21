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

            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromSeconds(appSettings.HttpTimeoutSeconds);

            if (!string.IsNullOrWhiteSpace(appSettings.AuthToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appSettings.AuthToken);
            }
        }

        public Task<string> GetSystemStats(string baseUrl, CancellationToken cancellationToken = default)
        {
            return _httpClient.GetStringAsync($"{baseUrl}system_stats", cancellationToken);
        }

        public async Task<IReadOnlyList<string>> GetInstalledModels(string baseUrl, string folder, CancellationToken cancellationToken = default)
        {
            return await _httpClient.GetFromJsonAsync<List<string>>($"{baseUrl}models/{folder}", cancellationToken) ?? [];
        }
    }
}
