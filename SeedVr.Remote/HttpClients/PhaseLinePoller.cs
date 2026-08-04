using System.Text.Json;
using SeedVr.Estimators.Live;
using SeedVr.Estimators.Signals;
using SeedVr.Logger;
using SeedVr.Remote.Models.ComfyUi;

namespace SeedVr.Remote.HttpClients
{
    /// <summary>Feeds SeedVR2's stdout phase/batch lines from /internal/logs/raw to an estimator tracker, so the
    /// phase-batch model tracks the run. Shared by the raw and wrapper trackers, which submit differently but
    /// run on the same ComfyUI instance.</summary>
    public class PhaseLinePoller
    {
        private readonly ComfyUiClient _comfyUiClient;
        private readonly TimeSpan _logPollInterval;

        public PhaseLinePoller(ComfyUiClient comfyUiClient)
        {
            _comfyUiClient = comfyUiClient;
            _logPollInterval = TimeSpan.FromSeconds(Constants.ComfyUi.LogPollSeconds);
        }

        /// <summary>Polls /internal/logs/raw and feeds each new SeedVR2 phase/batch line to the estimator tracker until cancelled.</summary>
        public async Task PollPhaseLines(string comfyUiAddress, ProgressTracker tracker, CancellationToken cancellationToken)
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
                    // A per-request timeout, not the run's cancellation. Routine under heavy GPU load and lossless -
                    // the next answered poll re-reads the buffer from the cursor - so it is not worth a log line.
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

        /// <summary>Cancels the phase-line poll and awaits it, absorbing the cancellation that stopping it necessarily raises.</summary>
        public async Task StopPhaseLinePolling(CancellationTokenSource logCancellation, Task logPolling)
        {
            logCancellation.Cancel();
            try
            {
                await logPolling;
            }
            catch (OperationCanceledException)
            {
                // Expected: the log poll is cancelled once the tracking loop ends.
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
    }
}
