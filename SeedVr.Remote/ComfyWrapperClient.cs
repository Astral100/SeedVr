using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Remote.Models;

namespace SeedVr.Remote
{
    /// <summary>Talks to the on-instance ComfyUI API wrapper, the alternative to driving ComfyUI's raw protocol.</summary>
    public class ComfyWrapperClient
    {
        private readonly HttpClient _httpClient;

        public ComfyWrapperClient(HttpClient httpClient, IOptions<AppSettings> appSettingsOptions)
        {
            var appSettings = appSettingsOptions.Value;

            _httpClient = httpClient;

            // Like the raw client, submissions run far longer than a control call; deadlines are set per request.
            _httpClient.Timeout = Timeout.InfiniteTimeSpan;

            if (!string.IsNullOrWhiteSpace(appSettings.AuthToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appSettings.AuthToken);
            }
        }

        /// <summary>Submits the workflow to the wrapper (async) and returns the request id to poll or stream against.</summary>
        public async Task<WrapperResult> Generate(string baseUrl, JsonObject workflow, CancellationToken cancellationToken = default)
        {
            var request = new JsonObject
            {
                ["input"] = new JsonObject
                {
                    ["workflow_json"] = workflow
                }
            };

            var response = await _httpClient.PostAsJsonAsync($"{baseUrl}{Constants.Wrapper.GeneratePath}", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<WrapperResult>(cancellationToken);
            return result;
        }
    }
}
