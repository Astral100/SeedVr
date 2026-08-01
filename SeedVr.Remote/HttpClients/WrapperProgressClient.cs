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

        /// <summary>Polls /result until the request finishes, reporting its progress along the way. True when it completed.</summary>
        public async Task<bool> TrackJobCompletion(string wrapperAddress, string requestId, CancellationToken cancellationToken)
        {
            string lastReportedMessage = null;
            while (true)
            {
                var result = await _comfyWrapperClient.GetResult(wrapperAddress, requestId, cancellationToken);

                var status = result?.Status;

                if (status == Constants.Wrapper.CompletedStatus)
                {
                    Log.Information("Request {RequestId} completed. {Message}", [requestId, result.Message]);
                    return true;
                }

                if (status == Constants.Wrapper.FailedStatus)
                {
                    Log.Error("Request {RequestId} failed. {Message}", [requestId, result.Message]);
                    return false;
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
    }
}
