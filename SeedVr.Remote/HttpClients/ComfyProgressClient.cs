using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Estimators.Jobs;
using SeedVr.Estimators.Live;
using SeedVr.Estimators.Signals;
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
        private readonly AppSettings _appSettings;
        private readonly TimeSpan _historyPollInterval;
        private readonly TimeSpan _logPollInterval;

        public ComfyProgressClient(ComfyUiClient comfyUiClient, IOptions<AppSettings> appSettingsOptions)
        {
            _comfyUiClient = comfyUiClient;
            _appSettings = appSettingsOptions.Value;
            _historyPollInterval = TimeSpan.FromSeconds(Constants.ComfyUi.HistoryPollSeconds);
            _logPollInterval = TimeSpan.FromSeconds(Constants.ComfyUi.LogPollSeconds);
        }

        /// <summary>Waits for the job to finish, reporting progress and running the ETA estimators along the way. True when it completed successfully.</summary>
        public async Task<bool> TrackJobCompletion(string comfyUiAddress, string clientId, string promptId, JobProgressContext progressContext, CancellationToken cancellationToken)
        {
            var tracker = ProgressTracker.CreateStandard(progressContext);

            await TrackLiveProgress(comfyUiAddress, clientId, promptId, tracker, cancellationToken);

            var succeeded = await PollHistoryUntilComplete(comfyUiAddress, promptId, cancellationToken);
            var trace = tracker.Complete(succeeded);
            EstimatorTraceStore.SaveForPrompt(promptId, trace);
            return succeeded;
        }

        /// <summary>Feeds the tracker from the progress socket and the phase-line poll at once, stopping the poll once the socket loop ends.</summary>
        private async Task TrackLiveProgress(string comfyUiAddress, string clientId, string promptId, ProgressTracker tracker, CancellationToken cancellationToken)
        {
            // SeedVR2's phase/batch lines drive the phase-batch estimator; poll them alongside the socket that feeds the percent-based ones.
            using var logCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var logPolling = PollPhaseLines(comfyUiAddress, tracker, logCancellation.Token);

            try
            {
                // Best-effort live progress; the socket reports done or drops, then /history decides the outcome.
                await ReportJobProgressFromSocket(comfyUiAddress, clientId, promptId, tracker, cancellationToken);
            }
            finally
            {
                await StopPhaseLinePolling(logCancellation, logPolling);
            }
        }

        /// <summary>Cancels the phase-line poll and awaits it, absorbing the cancellation that stopping it necessarily raises.</summary>
        private async Task StopPhaseLinePolling(CancellationTokenSource logCancellation, Task logPolling)
        {
            logCancellation.Cancel();
            try
            {
                await logPolling;
            }
            catch (OperationCanceledException)
            {
                // Expected: the log poll is cancelled once the socket loop ends.
            }
        }

        /// <summary>Polls /internal/logs/raw and feeds each new SeedVR2 phase/batch line to the estimator tracker, so the phase-batch model tracks the run.</summary>
        private async Task PollPhaseLines(string comfyUiAddress, ProgressTracker tracker, CancellationToken cancellationToken)
        {
            var trackingStarted = DateTimeOffset.UtcNow;
            // The instance stamps log entries in its own clock, which may differ from this host's. The cursor is set from the
            // first batch (see GetInitialCursor) and then lives entirely in the instance's clock, so host/instance skew cannot
            // drop current-run lines or replay the pre-run buffer.
            DateTimeOffset? lastTimestamp = null;
            while (true)
            {
                await Task.Delay(_logPollInterval, cancellationToken);
                try
                {
                    var logs = await _comfyUiClient.GetConsoleLogs(comfyUiAddress, cancellationToken);
                    lastTimestamp = FeedNewLogLines(logs, tracker, lastTimestamp, trackingStarted);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // A per-request timeout fired, not the run's cancellation, so skip this poll and keep tracking.
                    Log.Warning("The ComfyUI log poll (/internal/logs/raw) timed out; retrying on the next interval.");
                }
                catch (HttpRequestException ex)
                {
                    Log.Warning(ex, "The ComfyUI log poll (/internal/logs/raw) failed; the phase-batch estimator coasts on its priors.");
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException)
                {
                    Log.Warning(ex, "The ComfyUI log endpoint (/internal/logs/raw) returned an unexpected content type.");
                }
            }
        }

        /// <summary>Feeds every log entry produced after tracking began and returns the newest timestamp, all in the instance's clock.
        /// The cursor stays null until a non-empty batch anchors it, so an empty first poll does not fall back to the host clock.</summary>
        private DateTimeOffset? FeedNewLogLines(ComfyUiLogs logs, ProgressTracker tracker, DateTimeOffset? lastTimestamp, DateTimeOffset trackingStarted)
        {
            if (logs?.Entries == null || logs.Entries.Count == 0)
            {
                return lastTimestamp;
            }

            var cursor = lastTimestamp ?? GetInitialCursor(logs.Entries, trackingStarted);
            var latestTimestamp = cursor;
            foreach (var entry in logs.Entries)
            {
                if (entry.Message == null)
                {
                    continue;
                }

                if (entry.Timestamp > cursor)
                {
                    FeedLogLine(entry.Message, entry.Timestamp, tracker);
                }

                if (entry.Timestamp > latestTimestamp)
                {
                    latestTimestamp = entry.Timestamp;
                }
            }

            return latestTimestamp;
        }

        /// <summary>The first poll's cursor, in the instance's own clock: the newest entry mapped back by how long tracking has
        /// run, so current-run startup lines are still fed while the pre-run buffer is not, whatever the host/instance clock offset.</summary>
        private DateTimeOffset GetInitialCursor(List<ComfyUiLogEntry> entries, DateTimeOffset trackingStarted)
        {
            var newestRemote = entries.Max(entry => entry.Timestamp);
            var trackedDuration = DateTimeOffset.UtcNow - trackingStarted;
            return newestRemote - trackedDuration;
        }

        /// <summary>Parses one SeedVR2 line into a phase/batch event for the tracker.</summary>
        private void FeedLogLine(string message, DateTimeOffset occurredAt, ProgressTracker tracker)
        {
            var line = message.TrimEnd('\r', '\n');
            var videoMetadata = ProgressLogParser.ParseVideoMetadata(line);
            if (videoMetadata != null)
            {
                tracker.RecordVideoMetadata(videoMetadata);
            }

            var phaseBatch = ProgressLogParser.Parse(line);
            if (phaseBatch != null)
            {
                tracker.RecordPhaseBatch(phaseBatch, occurredAt);
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

        /// <summary>Polls /history until the job is recorded as finished; true when it succeeded.</summary>
        private async Task<bool> PollHistoryUntilComplete(string comfyUiAddress, string promptId, CancellationToken cancellationToken)
        {
            while (true)
            {
                var status = await GetHistoryStatus(comfyUiAddress, promptId, cancellationToken);
                if (status != null && status.Completed)
                {
                    var succeeded = status.StatusStr == Constants.ComfyUi.SuccessStatus;
                    if (!succeeded)
                    {
                        Log.Error("Job {PromptId} finished without success (status '{Status}').", [promptId, status.StatusStr]);
                    }

                    return succeeded;
                }

                if (status != null && status.StatusStr == Constants.ComfyUi.ErrorStatus)
                {
                    Log.Error("Job {PromptId} ended with an error.", [promptId]);
                    return false;
                }

                await Task.Delay(_historyPollInterval, cancellationToken);
            }
        }

        /// <summary>Reads the job's /history status, treating a transient read failure as "not finished yet" so a network blip or a
        /// proxy gateway error does not abort a job that is still running remotely. /history is polled again on the next interval.</summary>
        private async Task<ComfyUiHistoryStatus> GetHistoryStatus(string comfyUiAddress, string promptId, CancellationToken cancellationToken)
        {
            try
            {
                var entry = await _comfyUiClient.GetJobHistory(comfyUiAddress, promptId, cancellationToken);
                return entry?.Status;
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
