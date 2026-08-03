using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Remote.Models.Workflow;
using SeedVr.Remote.Models.Wrapper;

namespace SeedVr.Remote.HttpClients
{
    /// <summary>Talks to the on-instance ComfyUI API wrapper, the alternative to driving ComfyUI's raw protocol.</summary>
    public class ComfyWrapperClient
    {
        private readonly HttpClient _httpClient;
        private readonly TimeSpan _controlTimeout;

        public ComfyWrapperClient(HttpClient httpClient, IOptions<AppSettings> appSettingsOptions)
        {
            var appSettings = appSettingsOptions.Value;

            _httpClient = httpClient;
            _controlTimeout = TimeSpan.FromSeconds(appSettings.HttpTimeoutSeconds);

            // Like the raw client, submissions run far longer than a control call; deadlines are set per request.
            _httpClient.Timeout = Timeout.InfiniteTimeSpan;

            if (!string.IsNullOrWhiteSpace(appSettings.AuthToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appSettings.AuthToken);
            }
        }

        /// <summary>Submits the workflow to the wrapper (async) and returns the request id to poll or stream against.</summary>
        public async Task<WrapperResult> Generate(string baseUrl, SeedVrWorkflow workflow, CancellationToken cancellationToken = default)
        {
            var request = new WrapperRequest
            {
                Input = new WrapperInput
                {
                    WorkflowJson = workflow
                }
            };

            var response = await _httpClient.PostAsJsonAsync($"{baseUrl}{Constants.Wrapper.GeneratePath}", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<WrapperResult>(cancellationToken);
            return result;
        }

        /// <summary>The request's current state: its status and the human-readable progress message.</summary>
        public async Task<WrapperResult> GetResult(string baseUrl, string requestId, CancellationToken cancellationToken = default)
        {
            // A control-call deadline, not the infinite client timeout: this poll fires every few seconds, so a stuck request must
            // fail fast and let the caller retry rather than block the poll loop forever.
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_controlTimeout);

            var result = await _httpClient.GetFromJsonAsync<WrapperResult>($"{baseUrl}{Constants.Wrapper.ResultPath}/{requestId}", timeoutSource.Token);
            return result;
        }
    }
}
