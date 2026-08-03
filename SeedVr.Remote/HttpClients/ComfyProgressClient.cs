using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Estimators.Jobs;
using SeedVr.Estimators.Live;
using SeedVr.Estimators.Tracing;
using SeedVr.Logger;
using SeedVr.Remote.Models.ComfyUi;

namespace SeedVr.Remote.HttpClients
{
    /// <summary>Tracks a submitted job to completion: live progress over the ComfyUI WebSocket, with /history as the
    /// authoritative completion source when the socket signals done or drops.</summary>
    public class ComfyProgressClient
    {
        private readonly ComfyUiClient _comfyUiClient;
        private readonly PhaseLinePoller _phaseLinePoller;
        private readonly AppSettings _appSettings;
        private readonly TimeSpan _historyPollInterval;

        public ComfyProgressClient(ComfyUiClient comfyUiClient, PhaseLinePoller phaseLinePoller, IOptions<AppSettings> appSettingsOptions)
        {
            _comfyUiClient = comfyUiClient;
            _phaseLinePoller = phaseLinePoller;
            _appSettings = appSettingsOptions.Value;
            _historyPollInterval = TimeSpan.FromSeconds(Constants.ComfyUi.HistoryPollSeconds);
        }

        /// <summary>Waits for the job to finish, reporting progress and running the ETA estimators along the way.
        /// Returns the completed /history entry, carrying the outputs to download, or null when the job did not succeed.</summary>
        public async Task<ComfyUiHistoryEntry> TrackRawJobCompletion(string comfyUiAddress, string clientId, string promptId, JobProgressContext progressContext, CancellationToken cancellationToken)
        {
            var tracker = ProgressTracker.CreateStandard(progressContext);

            await TrackLiveProgress(comfyUiAddress, clientId, promptId, tracker, cancellationToken);

            var completedEntry = await PollHistoryUntilComplete(comfyUiAddress, promptId, cancellationToken);
            var trace = tracker.Complete(completedEntry != null);
            EstimatorTraceStore.SaveForPrompt(promptId, trace);
            return completedEntry;
        }

        /// <summary>Feeds the tracker from the progress socket and the phase-line poll at once, stopping the poll once the socket loop ends.</summary>
        private async Task TrackLiveProgress(string comfyUiAddress, string clientId, string promptId, ProgressTracker tracker, CancellationToken cancellationToken)
        {
            // SeedVR2's phase/batch lines drive the phase-batch estimator; poll them alongside the socket that feeds the percent-based ones.
            using var logCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var logPolling = _phaseLinePoller.PollPhaseLines(comfyUiAddress, tracker, logCancellation.Token);

            try
            {
                // Best-effort live progress; the socket reports done or drops, then /history decides the outcome.
                await ReportJobProgressFromSocket(comfyUiAddress, clientId, promptId, tracker, cancellationToken);
            }
            finally
            {
                await _phaseLinePoller.StopPhaseLinePolling(logCancellation, logPolling);
            }
        }

        /// <summary>Reports the job's progress until the socket signals the run is over or the connection drops.</summary>
        private async Task ReportJobProgressFromSocket(string comfyUiAddress, string clientId, string promptId, ProgressTracker tracker, CancellationToken cancellationToken)
        {
            using var socket = new ClientWebSocket();
            if (!string.IsNullOrWhiteSpace(_appSettings.AuthToken))
            {
                socket.Options.SetRequestHeader("Authorization", $"Bearer {_appSettings.AuthToken}");
            }

            var socketUri = GetWebSocketUri(comfyUiAddress, clientId);
            try
            {
                await socket.ConnectAsync(socketUri, cancellationToken);
                await ReceiveMessagesUntilJobComplete(socket, promptId, tracker, cancellationToken);
            }
            catch (WebSocketException ex)
            {
                // The socket is best-effort; /history still tracks the job, so log and fall back to polling.
                Log.Warning(ex, "The ComfyUI progress socket dropped; falling back to /history polling.");
            }
            finally
            {
                if (socket.State == WebSocketState.Open)
                {
                    try
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                    }
                    catch (WebSocketException)
                    {
                        // The job is already tracked by /history, so a failed courtesy close does not matter.
                    }
                }
            }
        }

        /// <summary>Reads messages until an "executing" (null node), "execution_success" or "execution_error" for this prompt, or the socket closes.</summary>
        private async Task ReceiveMessagesUntilJobComplete(ClientWebSocket socket, string promptId, ProgressTracker tracker, CancellationToken cancellationToken)
        {
            var buffer = new byte[Constants.ComfyUi.MessageBufferSize];
            while (socket.State == WebSocketState.Open)
            {
                var messageRaw = await ReceiveNextMessage(socket, buffer, cancellationToken);
                if (messageRaw == null)
                {
                    // The socket closed; /history takes over.
                    return;
                }

                if (messageRaw.Length == 0)
                {
                    // A binary preview frame carries no progress, so skip it.
                    continue;
                }

                ComfyUiSocketMessage message;
                try
                {
                    message = JsonSerializer.Deserialize<ComfyUiSocketMessage>(messageRaw);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException)
                {
                    // The socket is best-effort and /history is authoritative, so a frame we cannot parse is skipped, not fatal.
                    Log.Warning(ex, "The ComfyUI progress socket sent a frame that could not be parsed; skipping it.");
                    continue;
                }

                var jobDone = ProcessNextMessage(message, promptId, tracker);
                if (jobDone)
                {
                    return;
                }
            }
        }

        /// <summary>Reassembles one WebSocket message: the JSON text, null when the socket closed, or empty for a binary frame.</summary>
        private async Task<string> ReceiveNextMessage(ClientWebSocket socket, byte[] buffer, CancellationToken cancellationToken)
        {
            using var message = new MemoryStream();
            var segment = new ArraySegment<byte>(buffer);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(segment, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            // ComfyUI also pushes binary preview frames; only the JSON text frames carry progress.
            if (result.MessageType != WebSocketMessageType.Text)
            {
                return string.Empty;
            }

            var text = Encoding.UTF8.GetString(message.ToArray());
            return text;
        }

        /// <summary>Reports what the message carries and returns true when it marks the end of this prompt's run.</summary>
        private bool ProcessNextMessage(ComfyUiSocketMessage message, string promptId, ProgressTracker tracker)
        {
            if (message == null)
            {
                return false;
            }

            var data = message.Data;

            // A shared socket carries other prompts' messages; ignore any message tagged with a different prompt.
            if (data?.PromptId != null && data.PromptId != promptId)
            {
                return false;
            }

            if (message.Type == Constants.ComfyUi.SocketExecutionError)
            {
                Log.Warning("ComfyUI reported an execution error; confirming the outcome via /history.");
            }

            RecordPercent(message.Type, data, tracker);

            var isRunComplete = IsRunComplete(message.Type, data);
            return isRunComplete;
        }

        /// <summary>Feeds a progress frame's percent (value/max scaled to 0-100) to the estimator tracker.</summary>
        private void RecordPercent(string messageType, ComfyUiSocketData data, ProgressTracker tracker)
        {
            if (messageType == Constants.ComfyUi.SocketProgress && data != null && data.Value != null && data.Max is > 0)
            {
                var percent = 100.0 * data.Value.Value / data.Max.Value;
                tracker.RecordPercent(percent);
            }
        }

        /// <summary>execution_success and execution_error both end the run, as does an "executing" message with a null node.</summary>
        private bool IsRunComplete(string messageType, ComfyUiSocketData data)
        {
            return messageType switch
            {
                Constants.ComfyUi.SocketExecutionSuccess or Constants.ComfyUi.SocketExecutionError => true,
                Constants.ComfyUi.SocketExecuting => data?.Node == null,
                _ => false
            };
        }

        /// <summary>Polls /history until the job is recorded as finished; the completed entry when it succeeded, null otherwise.</summary>
        private async Task<ComfyUiHistoryEntry> PollHistoryUntilComplete(string comfyUiAddress, string promptId, CancellationToken cancellationToken)
        {
            while (true)
            {
                var entry = await GetHistoryEntry(comfyUiAddress, promptId, cancellationToken);
                var status = entry?.Status;
                if (status != null && status.Completed)
                {
                    if (status.StatusStr == Constants.ComfyUi.SuccessStatus)
                    {
                        return entry;
                    }

                    Log.Error("Job {PromptId} finished without success (status '{Status}').", [promptId, status.StatusStr]);
                    return null;
                }

                if (status != null && status.StatusStr == Constants.ComfyUi.ErrorStatus)
                {
                    Log.Error("Job {PromptId} ended with an error.", [promptId]);
                    return null;
                }

                await Task.Delay(_historyPollInterval, cancellationToken);
            }
        }

        /// <summary>Reads the job's /history entry, treating a transient read failure as "not finished yet" so a network blip or a
        /// proxy gateway error does not abort a job that is still running remotely. /history is polled again on the next interval.</summary>
        private async Task<ComfyUiHistoryEntry> GetHistoryEntry(string comfyUiAddress, string promptId, CancellationToken cancellationToken)
        {
            try
            {
                var entry = await _comfyUiClient.GetJobHistory(comfyUiAddress, promptId, cancellationToken);
                return entry;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A per-request control timeout fired, not the run's cancellation, so skip this poll and keep tracking.
                Log.Warning("The ComfyUI /history poll timed out; retrying on the next interval.");
                return null;
            }
            catch (HttpRequestException ex)
            {
                Log.Warning(ex, "The ComfyUI /history poll failed; retrying on the next interval.");
                return null;
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                Log.Warning(ex, "The ComfyUI /history endpoint returned an unexpected content type; retrying on the next interval.");
                return null;
            }
        }

        /// <summary>The ws(s):// progress URL for the run, carrying the client id ComfyUI broadcasts this job under.</summary>
        private Uri GetWebSocketUri(string comfyUiAddress, string clientId)
        {
            var httpUri = new Uri(comfyUiAddress);
            var scheme = httpUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
            var builder = new UriBuilder(httpUri)
            {
                Scheme = scheme,
                Path = Constants.ComfyUi.WebSocketPath,
                Query = $"clientId={Uri.EscapeDataString(clientId)}"
            };

            return builder.Uri;
        }
    }
}
