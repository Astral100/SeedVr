using System.Text.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Estimators.Jobs;
using SeedVr.Estimators.Live;
using SeedVr.Estimators.Tracing;
using SeedVr.Logger;
using SeedVr.Remote.Models.Wrapper;

namespace SeedVr.Remote.HttpClients
{
    /// <summary>Tracks a request submitted to the API wrapper to completion by polling /result, feeding the ETA
    /// estimators from the reported percent and the instance's phase/batch log lines along the way.</summary>
    public class WrapperProgressClient
    {
        private readonly ComfyWrapperClient _comfyWrapperClient;
        private readonly PhaseLinePoller _phaseLinePoller;
        private readonly AppSettings _appSettings;
        private readonly TimeSpan _resultPollInterval;

        public WrapperProgressClient(ComfyWrapperClient comfyWrapperClient, PhaseLinePoller phaseLinePoller, IOptions<AppSettings> appSettingsOptions)
        {
            _comfyWrapperClient = comfyWrapperClient;
            _phaseLinePoller = phaseLinePoller;
            _appSettings = appSettingsOptions.Value;
            _resultPollInterval = TimeSpan.FromSeconds(Constants.Wrapper.ResultPollSeconds);
        }

        /// <summary>Waits for the request to finish, reporting progress and running the ETA estimators along the way.
        /// Returns the completed result, carrying the output file references, or null when the request failed.</summary>
        public async Task<WrapperResult> TrackWrapperJobCompletion(string comfyUiAddress, string wrapperAddress, string requestId, JobProgressContext progressContext, CancellationToken cancellationToken)
        {
            var tracker = ProgressTracker.CreateStandard(progressContext);

            var completedResult = await TrackLiveProgress(comfyUiAddress, wrapperAddress, requestId, tracker, cancellationToken);
            var trace = tracker.Complete(completedResult != null);
            EstimatorTraceStore.SaveForPrompt(requestId, trace);
            return completedResult;
        }

        /// <summary>Feeds the tracker from the /result poll and the phase-line poll at once, stopping the poll once the result loop ends.</summary>
        private async Task<WrapperResult> TrackLiveProgress(string comfyUiAddress, string wrapperAddress, string requestId, ProgressTracker tracker, CancellationToken cancellationToken)
        {
            // SeedVR2's phase/batch lines drive the phase-batch estimator; poll them alongside the /result loop that feeds the percent-based ones.
            using var logCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var logPolling = _phaseLinePoller.PollPhaseLines(comfyUiAddress, tracker, logCancellation.Token);

            try
            {
                var completedResult = await PollResultUntilComplete(wrapperAddress, requestId, tracker, cancellationToken);
                return completedResult;
            }
            finally
            {
                await _phaseLinePoller.StopPhaseLinePolling(logCancellation, logPolling);
            }
        }

        /// <summary>Polls /result until the request finishes, feeding each newly advanced percent to the tracker.
        /// Only a status transition or a changed percent re-arms the stall deadline, so a hung request - or one whose
        /// /result stays permanently failing or unparseable - ends in a stall instead of polling forever.
        /// The completed result when the request succeeded, null when it failed.</summary>
        private async Task<WrapperResult> PollResultUntilComplete(string wrapperAddress, string requestId, ProgressTracker tracker, CancellationToken cancellationToken)
        {
            using var stallDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stallDeadline.CancelAfter(TimeSpan.FromSeconds(_appSettings.ProcessingStallTimeoutSeconds));
            string lastStatus = null;
            double? lastPercent = null;
            try
            {
                while (true)
                {
                    var result = await PollResult(wrapperAddress, requestId, stallDeadline.Token);

                    var status = result?.Status;

                    if (status == Constants.Wrapper.CompletedStatus)
                    {
                        Log.Information("Request {RequestId} completed. {Message}", [requestId, result.Message]);
                        return result;
                    }

                    if (status == Constants.Wrapper.FailedStatus)
                    {
                        Log.Error("Request {RequestId} failed. {Message}", [requestId, result.Message]);
                        return null;
                    }

                    var statusAdvanced = status != null && status != lastStatus;
                    if (statusAdvanced)
                    {
                        lastStatus = status;
                    }

                    var recordedPercent = RecordChangedPercent(result, lastPercent, tracker);
                    var percentAdvanced = recordedPercent != lastPercent;
                    if (percentAdvanced)
                    {
                        lastPercent = recordedPercent;
                    }

                    if (statusAdvanced || percentAdvanced)
                    {
                        stallDeadline.CancelAfter(TimeSpan.FromSeconds(_appSettings.ProcessingStallTimeoutSeconds));
                    }

                    await Task.Delay(_resultPollInterval, stallDeadline.Token);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The wrapper request reported no progress for {_appSettings.ProcessingStallTimeoutSeconds} seconds.");
            }
        }

        /// <summary>Feeds the percent inside the /result message to the tracker only when it moved on from the last
        /// recorded value, so the poll cadence cannot replay an unchanged percent into the rate-based estimators as a
        /// slowdown. Returns the latest recorded percent.</summary>
        private double? RecordChangedPercent(WrapperResult result, double? lastPercent, ProgressTracker tracker)
        {
            var percent = WrapperMessageParser.ParsePercent(result?.Message);
            if (percent == null || percent == lastPercent)
            {
                return lastPercent;
            }

            tracker.RecordPercent(percent.Value);
            return percent;
        }

        /// <summary>Reads the request's /result, treating a transient read failure as "no update this tick" so a network blip or a
        /// timed-out poll does not abort a request that is still running. The loop retries on the next interval.</summary>
        private async Task<WrapperResult> PollResult(string wrapperAddress, string requestId, CancellationToken cancellationToken)
        {
            try
            {
                var result = await _comfyWrapperClient.GetResult(wrapperAddress, requestId, cancellationToken);
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A per-request control timeout fired, not the run's cancellation, so skip this poll and keep tracking.
                Log.Warning("The wrapper /result poll timed out; retrying on the next interval.");
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
            {
                Log.Warning(ex, "The wrapper /result poll failed; retrying on the next interval.");
                return null;
            }
        }
    }
}
