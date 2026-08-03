using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Remote.Models.ComfyUi;
using SeedVr.Remote.Models.Workflow;

namespace SeedVr.Remote.HttpClients
{
    public class ComfyUiClient
    {
        private readonly HttpClient _httpClient;
        private readonly TimeSpan _controlTimeout;
        private readonly TimeSpan _logPollTimeout;
        private readonly TimeSpan _transferIdleTimeout;

        public ComfyUiClient(HttpClient httpClient, IOptions<AppSettings> appSettingsOptions)
        {
            var appSettings = appSettingsOptions.Value;

            _httpClient = httpClient;
            _controlTimeout = TimeSpan.FromSeconds(appSettings.HttpTimeoutSeconds);
            _logPollTimeout = TimeSpan.FromSeconds(Constants.ComfyUi.LogPollTimeoutSeconds);
            _transferIdleTimeout = TimeSpan.FromSeconds(appSettings.TransferIdleTimeoutSeconds);

            // Uploads and downloads run far longer than a control call, and HttpClient.Timeout is a
            // ceiling no caller can raise. Deadlines are set per request instead.
            _httpClient.Timeout = Timeout.InfiniteTimeSpan;

            if (!string.IsNullOrWhiteSpace(appSettings.AuthToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appSettings.AuthToken);
            }
        }

        public async Task<ComfyUiSystemStats> GetSystemStats(string baseUrl, CancellationToken cancellationToken = default)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_controlTimeout);

            var systemStats = await _httpClient.GetFromJsonAsync<ComfyUiSystemStats>($"{baseUrl}{Constants.ComfyUi.SystemStatsPath}", timeoutSource.Token);
            return systemStats;
        }

        public async Task<IReadOnlyList<string>> GetInstalledModels(string baseUrl, string folder, CancellationToken cancellationToken = default)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_controlTimeout);

            var installedModels = await _httpClient.GetFromJsonAsync<List<string>>($"{baseUrl}{Constants.ComfyUi.ModelsPath}/{folder}", timeoutSource.Token);
            return installedModels ?? [];
        }

        /// <summary>Jobs queued plus the one running, or null when ComfyUI did not report it. Zero means the instance is free.</summary>
        public async Task<int?> GetComfyUiQueueLength(string baseUrl, CancellationToken cancellationToken = default)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_controlTimeout);

            var status = await _httpClient.GetFromJsonAsync<ComfyUiPromptStatus>($"{baseUrl}{Constants.ComfyUi.PromptPath}", timeoutSource.Token);
            return status?.ExecInfo?.QueueRemaining;
        }

        /// <summary>The job's /history entry once ComfyUI has recorded it, or null while it is still queued or running.</summary>
        public async Task<ComfyUiHistoryEntry> GetJobHistory(string baseUrl, string promptId, CancellationToken cancellationToken = default)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_controlTimeout);

            // /history/<id> returns an object keyed by the prompt id, empty until the job is recorded.
            var history = await _httpClient.GetFromJsonAsync<Dictionary<string, ComfyUiHistoryEntry>>($"{baseUrl}{Constants.ComfyUi.HistoryPath}/{promptId}", timeoutSource.Token);
            if (history != null && history.TryGetValue(promptId, out var entry))
            {
                return entry;
            }

            return null;
        }

        /// <summary>The recent ComfyUI console buffer (GET /internal/logs/raw), carrying SeedVR2's phase/batch prints.</summary>
        public async Task<ComfyUiLogs> GetConsoleLogs(string baseUrl, CancellationToken cancellationToken = default)
        {
            // A short deadline, not the control timeout: this poll fires every couple of seconds, so a stuck request must skip fast rather than block the feed.
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_logPollTimeout);

            var logs = await _httpClient.GetFromJsonAsync<ComfyUiLogs>($"{baseUrl}{Constants.ComfyUi.LogsRawPath}", timeoutSource.Token);
            return logs;
        }

        /// <summary>Uploads the local video into the instance's input folder under the given subfolder and returns where ComfyUI stored it.</summary>
        public async Task<ComfyUiUploadResult> UploadVideo(string baseUrl, string localVideoPath, string subfolder, CancellationToken cancellationToken = default)
        {
            // No total timeout: each successfully written chunk re-arms the idle deadline, so long active uploads can finish.
            using var content = new MultipartFormDataContent();
            using var fileContent = new ProgressStreamContent(localVideoPath, _transferIdleTimeout, cancellationToken);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var fileName = Path.GetFileName(localVideoPath);
            content.Add(fileContent, "image", fileName);

            // ComfyUI files the upload under input/<subfolder>/, which the caller uses to namespace and later clean up the job.
            if (!string.IsNullOrEmpty(subfolder))
            {
                content.Add(new StringContent(subfolder), "subfolder");
            }

            using var response = await _httpClient.PostAsync($"{baseUrl}{Constants.ComfyUi.UploadImagePath}", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ComfyUiUploadResult>(cancellationToken);
            return result;
        }

        /// <summary>Submits the workflow to ComfyUI, tagged with the client id so a WebSocket can attach to its progress.</summary>
        public async Task<ComfyUiSubmitResult> SubmitPrompt(string baseUrl, SeedVrWorkflow workflow, string clientId, CancellationToken cancellationToken = default)
        {
            var request = new ComfyUiPromptRequest
            {
                Prompt = workflow, 
                ClientId = clientId
            };

            using var response = await _httpClient.PostAsJsonAsync($"{baseUrl}{Constants.ComfyUi.PromptPath}", request, cancellationToken);

            // ComfyUI rejects an invalid workflow with 400 and a JSON body carrying node_errors; read that
            // body instead of throwing, so the caller can report which node was refused. A 400 from the proxy
            // (bad auth) is not JSON, and any other non-success status has no such body, so surface those.
            var isNodeRejection = response.StatusCode == HttpStatusCode.BadRequest && response.Content.Headers.ContentType?.MediaType == "application/json";
            if (!response.IsSuccessStatusCode && !isNodeRejection)
            {
                response.EnsureSuccessStatusCode();
            }

            var result = await response.Content.ReadFromJsonAsync<ComfyUiSubmitResult>(cancellationToken);
            return result;
        }
    }
}
