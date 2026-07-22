using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;

namespace SeedVr.Remote
{
    public class ComfyUiClient
    {
        private readonly HttpClient _httpClient;
        private readonly TimeSpan _controlTimeout;

        public ComfyUiClient(HttpClient httpClient, IOptions<AppSettings> appSettingsOptions)
        {
            var appSettings = appSettingsOptions.Value;

            _httpClient = httpClient;
            _controlTimeout = TimeSpan.FromSeconds(appSettings.HttpTimeoutSeconds);

            // Uploads and downloads run far longer than a control call, and HttpClient.Timeout is a
            // ceiling no caller can raise. Deadlines are set per request instead.
            _httpClient.Timeout = Timeout.InfiniteTimeSpan;

            if (!string.IsNullOrWhiteSpace(appSettings.AuthToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appSettings.AuthToken);
            }
        }

        public async Task<string> GetSystemStats(string baseUrl, CancellationToken cancellationToken = default)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_controlTimeout);

            return await _httpClient.GetStringAsync($"{baseUrl}system_stats", timeoutSource.Token);
        }

        public async Task<IReadOnlyList<string>> GetInstalledModels(string baseUrl, string folder, CancellationToken cancellationToken = default)
        {
            return await _httpClient.GetFromJsonAsync<List<string>>($"{baseUrl}models/{folder}", cancellationToken) ?? [];
        }
    }
}
