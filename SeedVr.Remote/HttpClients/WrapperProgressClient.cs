using System.Text.Json;
using SeedVr.Core;
using SeedVr.Logger;
using SeedVr.Remote.Models.Wrapper;

namespace SeedVr.Remote.HttpClients
{
    /// <summary>Tracks a request submitted to the API wrapper to completion by polling /result for its status and progress.</summary>
    public class WrapperProgressClient
    {
        private readonly ComfyWrapperClient _comfyWrapperClient;
        private readonly TimeSpan _resultPollInterval;

        public WrapperProgressClient(ComfyWrapperClient comfyWrapperClient)
        {
            _comfyWrapperClient = comfyWrapperClient;
            _resultPollInterval = TimeSpan.FromSeconds(Constants.Wrapper.ResultPollSeconds);
        }

        /// <summary>Polls /result until the request finishes, reporting its progress along the way.
        /// Returns the completed result, carrying the output file references, or null when the request failed.</summary>
        public async Task<WrapperResult> TrackWrapperJobCompletion(string wrapperAddress, string requestId, CancellationToken cancellationToken)
        {
            string lastReportedMessage = null;
            while (true)
            {
                var result = await PollResult(wrapperAddress, requestId, cancellationToken);

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

                // Each poll repeats the same message until progress advances, so report only when it changes.
                if (result != null && result.Message != lastReportedMessage)
                {
                    Log.Information("Request {RequestId} {Status}: {Message}", [requestId, status, result.Message]);
                    lastReportedMessage = result.Message;
                }

                await Task.Delay(_resultPollInterval, cancellationToken);
            }
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
