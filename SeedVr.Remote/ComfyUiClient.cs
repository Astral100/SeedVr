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

        }

        public async Task<string> GetSystemStatsAsync(CancellationToken cancellationToken = default)
        {
            using var response = await _httpClient.GetAsync("system_stats", cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
    }
}
