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

            var sawRunEnd = await TrackLiveProgress(comfyUiAddress, clientId, promptId, tracker, cancellationToken);

            var completedEntry = await PollHistoryForOutcome(comfyUiAddress, promptId, sawRunEnd, cancellationToken);
            var trace = tracker.Complete(completedEntry != null);
            trace.RunId = promptId;
            EstimatorTraceStore.SaveSnapshot(trace);
            return completedEntry;
        }

        /// <summary>Feeds the tracker from the progress socket and the phase-line poll at once, stopping the poll once the socket loop ends. True when the socket saw the run end.</summary>
        private async Task<bool> TrackLiveProgress(string comfyUiAddress, string clientId, string promptId, ProgressTracker tracker, CancellationToken cancellationToken)
        {
            // SeedVR2's phase/batch lines drive the phase-batch estimator; poll them alongside the socket that feeds the percent-based ones.
            using var logCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var logPolling = _phaseLinePoller.PollPhaseLines(comfyUiAddress, tracker, logCancellation.Token);

            try
            {
                // Best-effort live progress; the socket reports done or drops, then /history decides the outcome.
                var sawRunEnd = await ReportJobProgressFromSocket(comfyUiAddress, clientId, promptId, tracker, cancellationToken);
                return sawRunEnd;
            }
            finally
            {
                await _phaseLinePoller.StopPhaseLinePolling(logCancellation, logPolling);
            }
        }

        /// <summary>Reports the job's progress until the socket signals the run is over, the connection drops, or the
        /// stall deadline runs down with no progress recorded. True when the socket saw the run end.</summary>
        private async Task<bool> ReportJobProgressFromSocket(string comfyUiAddress, string clientId, string promptId, ProgressTracker tracker, CancellationToken cancellationToken)
        {
            using var socket = new ClientWebSocket();
            if (!string.IsNullOrWhiteSpace(_appSettings.AuthToken))
            {
                socket.Options.SetRequestHeader("Authorization", $"Bearer {_appSettings.AuthToken}");
            }

            var socketUri = GetWebSocketUri(comfyUiAddress, clientId);

            // The stall deadline bounds the connect and every receive, and only a recorded progress frame re-arms it, so a silently hung job cannot hold the tracking forever.
            using var stallDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stallDeadline.CancelAfter(TimeSpan.FromSeconds(_appSettings.ProcessingStallTimeoutSeconds));
            try
            {
                await socket.ConnectAsync(socketUri, stallDeadline.Token);
                var sawRunEnd = await ReceiveMessagesUntilJobComplete(socket, promptId, tracker, stallDeadline);
                return sawRunEnd;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The job reported no progress for {_appSettings.ProcessingStallTimeoutSeconds} seconds.");
            }
            catch (WebSocketException ex)
            {
                // The socket is best-effort; /history still tracks the job, so log and fall back to polling.
                Log.Warning(ex, "The ComfyUI progress socket dropped; falling back to /history polling.");
                return false;
            }
            finally
            {
                await CloseSocket(socket);
            }
        }

        /// <summary>Courtesy close on its own short deadline: the run's outcome comes from /history either way, and a
        /// hung or already-cancelled connection must not stall the unwind, so a failed close is ignored.</summary>
        private async Task CloseSocket(ClientWebSocket socket)
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(Constants.ComfyUi.SocketCloseTimeoutSeconds));
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, closeTimeout.Token);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
            }
        }

        /// <summary>Reads messages until an "executing" (null node), "execution_success" or "execution_error" for this prompt (true),
        /// or the socket closes first (false). Each recorded progress frame re-arms the stall deadline; anything else lets it run down.</summary>
        private async Task<bool> ReceiveMessagesUntilJobComplete(ClientWebSocket socket, string promptId, ProgressTracker tracker, CancellationTokenSource stallDeadline)
        {
            var buffer = new byte[Constants.ComfyUi.MessageBufferSize];
            while (socket.State == WebSocketState.Open)
            {
                var messageRaw = await ReceiveNextMessage(socket, buffer, stallDeadline.Token);
                if (messageRaw == null)
                {
                    // The socket closed; /history takes over.
                    return false;
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

                var outcome = ProcessNextMessage(message, promptId, tracker);
                if (outcome == SocketMessageOutcome.RunComplete)
                {
                    return true;
                }

                if (outcome == SocketMessageOutcome.ProgressRecorded)
                {
                    stallDeadline.CancelAfter(TimeSpan.FromSeconds(_appSettings.ProcessingStallTimeoutSeconds));
                }
            }

            return false;
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

        /// <summary>Reports what the message carries: the end of this prompt's run, a recorded progress frame, or nothing of note.</summary>
        private SocketMessageOutcome ProcessNextMessage(ComfyUiSocketMessage message, string promptId, ProgressTracker tracker)
        {
            if (message == null)
            {
                return SocketMessageOutcome.Skipped;
            }

            var data = message.Data;

            // A shared socket carries other prompts' messages; ignore any message tagged with a different prompt.
            if (data?.PromptId != null && data.PromptId != promptId)
            {
                return SocketMessageOutcome.Skipped;
            }

            if (message.Type == Constants.ComfyUi.SocketExecutionError)
            {
                Log.Warning("ComfyUI reported an execution error; confirming the outcome via /history.");
            }

            var progressRecorded = RecordPercent(message.Type, data, tracker);

            if (IsRunComplete(message.Type, data))
            {
                return SocketMessageOutcome.RunComplete;
            }

            return progressRecorded ? SocketMessageOutcome.ProgressRecorded : SocketMessageOutcome.Skipped;
        }

        /// <summary>Feeds a progress frame's percent (value/max scaled to 0-100) to the estimator tracker. True when one was recorded.</summary>
        private bool RecordPercent(string messageType, ComfyUiSocketData data, ProgressTracker tracker)
        {
            if (messageType == Constants.ComfyUi.SocketProgress && data != null && data.Value != null && data.Max is > 0)
            {
                var percent = 100.0 * data.Value.Value / data.Max.Value;
                tracker.RecordPercent(percent);
                return true;
            }

            return false;
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

        /// <summary>Resolves the run's outcome over /history. When the socket saw the run end, the entry lands within
        /// seconds, so the poll runs under the stall deadline; when the socket dropped mid-run, the poll is the only
        /// tracker left for a possibly healthy long job with no percent signal to re-arm on, so it stays unbounded.</summary>
        private async Task<ComfyUiHistoryEntry> PollHistoryForOutcome(string comfyUiAddress, string promptId, bool sawRunEnd, CancellationToken cancellationToken)
        {
            if (!sawRunEnd)
            {
                var entry = await PollHistoryUntilComplete(comfyUiAddress, promptId, cancellationToken);
                return entry;
            }

            using var stallDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stallDeadline.CancelAfter(TimeSpan.FromSeconds(_appSettings.ProcessingStallTimeoutSeconds));
            try
            {
                var entry = await PollHistoryUntilComplete(comfyUiAddress, promptId, stallDeadline.Token);
                return entry;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"/history did not record the finished job within {_appSettings.ProcessingStallTimeoutSeconds} seconds.");
            }
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
