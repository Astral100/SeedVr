using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Remote.Models.ComfyUi;
using SeedVr.Remote.Models.Workflow;

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

            var systemStats = await _httpClient.GetStringAsync($"{baseUrl}{Constants.ComfyUi.SystemStatsPath}", timeoutSource.Token);
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

        /// <summary>Uploads the local video into the instance's input folder under the given subfolder and returns where ComfyUI stored it.</summary>
        public async Task<ComfyUiUploadResult> UploadVideo(string baseUrl, string localVideoPath, string subfolder, CancellationToken cancellationToken = default)
        {
            // No control timeout: an upload runs far longer than a control call, so it uses the caller's token only.
            await using var fileStream = File.OpenRead(localVideoPath);
            using var content = new MultipartFormDataContent();
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var fileName = Path.GetFileName(localVideoPath);
            content.Add(fileContent, "image", fileName);

            // ComfyUI files the upload under input/<subfolder>/, which the caller uses to namespace and later clean up the job.
            if (!string.IsNullOrEmpty(subfolder))
            {
                content.Add(new StringContent(subfolder), "subfolder");
            }

            var response = await _httpClient.PostAsync($"{baseUrl}{Constants.ComfyUi.UploadImagePath}", content, cancellationToken);
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

            var response = await _httpClient.PostAsJsonAsync($"{baseUrl}{Constants.ComfyUi.PromptPath}", request, cancellationToken);

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
