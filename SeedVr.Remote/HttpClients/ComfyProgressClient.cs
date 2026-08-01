using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Logger;
using SeedVr.Remote.Models.ComfyUi;

namespace SeedVr.Remote.HttpClients
{
    /// <summary>Tracks a submitted job to completion: live progress over the ComfyUI WebSocket, with /history as the
    /// authoritative completion source when the socket signals done or drops.</summary>
    public class ComfyProgressClient
    {
        private const int ReceiveBufferSize = 8192;

        private readonly ComfyUiClient _comfyUiClient;
        private readonly AppSettings _appSettings;
        private readonly TimeSpan _historyPollInterval;

        public ComfyProgressClient(ComfyUiClient comfyUiClient, IOptions<AppSettings> appSettingsOptions)
        {
            _comfyUiClient = comfyUiClient;
            _appSettings = appSettingsOptions.Value;
            _historyPollInterval = TimeSpan.FromSeconds(Constants.ComfyUi.HistoryPollSeconds);
        }

        /// <summary>Waits for the job to finish, reporting progress along the way. True when it completed successfully.</summary>
        public async Task<bool> TrackJobCompletion(string comfyUiAddress, string clientId, string promptId, CancellationToken cancellationToken)
        {
            // Best-effort live progress; the socket reports done or drops, then /history decides the outcome.
            await ReportJobProgressFromSocket(comfyUiAddress, clientId, promptId, cancellationToken);

            var succeeded = await PollHistoryUntilComplete(comfyUiAddress, promptId, cancellationToken);
            return succeeded;
        }

        /// <summary>Reports the job's progress until the socket signals the run is over or the connection drops.</summary>
        private async Task ReportJobProgressFromSocket(string comfyUiAddress, string clientId, string promptId, CancellationToken cancellationToken)
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
                Log.Information("Attached to the ComfyUI progress socket for prompt {PromptId}.", [promptId]);
                await ReceiveMessagesUntilJobComplete(socket, promptId, cancellationToken);
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
        private async Task ReceiveMessagesUntilJobComplete(ClientWebSocket socket, string promptId, CancellationToken cancellationToken)
        {
            var buffer = new byte[ReceiveBufferSize];
            while (socket.State == WebSocketState.Open)
            {
                var text = await ReceiveTextMessage(socket, buffer, cancellationToken);
                if (text == null)
                {
                    // The socket closed; /history takes over.
                    return;
                }

                if (text.Length == 0)
                {
                    // A binary preview frame carries no progress, so skip it.
                    continue;
                }

                var message = JsonSerializer.Deserialize<ComfyUiSocketMessage>(text);
                var jobDone = ProcessNextMessage(message, promptId);
                if (jobDone)
                {
                    return;
                }
            }
        }

        /// <summary>Reassembles one WebSocket message: the JSON text, null when the socket closed, or empty for a binary frame.</summary>
        private static async Task<string> ReceiveTextMessage(ClientWebSocket socket, byte[] buffer, CancellationToken cancellationToken)
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
        private static bool ProcessNextMessage(ComfyUiSocketMessage message, string promptId)
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

            LogProgress(message.Type, data);

            return IsRunComplete(message.Type, data);
        }

        /// <summary>execution_success and execution_error both end the run, as does an "executing" message with a null node.</summary>
        private static bool IsRunComplete(string messageType, ComfyUiSocketData data)
        {
            return messageType switch
            {
                Constants.ComfyUi.SocketExecutionSuccess or Constants.ComfyUi.SocketExecutionError => true,
                Constants.ComfyUi.SocketExecuting => data?.Node == null,
                _ => false
            };
        }

        /// <summary>Writes the progress a message reports; terminal and out-of-band messages log nothing.</summary>
        private static void LogProgress(string messageType, ComfyUiSocketData data)
        {
            if (messageType == Constants.ComfyUi.SocketExecutionError)
            {
                Log.Warning("ComfyUI reported an execution error; confirming the outcome via /history.");
            }
            else if (messageType == Constants.ComfyUi.SocketExecuting && data?.Node != null)
            {
                Log.Information("Executing node {Node}.", [data.Node]);
            }
            else if (messageType == Constants.ComfyUi.SocketProgress && data != null && data.Max > 0)
            {
                Log.Information("Job progress: {Value}/{Max} (node {Node}).", [data.Value, data.Max, data.Node]);
            }
        }

        /// <summary>Polls /history until the job is recorded as finished; true when it succeeded.</summary>
        private async Task<bool> PollHistoryUntilComplete(string comfyUiAddress, string promptId, CancellationToken cancellationToken)
        {
            while (true)
            {
                var entry = await _comfyUiClient.GetJobHistory(comfyUiAddress, promptId, cancellationToken);
                var status = entry?.Status;
                if (status != null && status.Completed)
                {
                    var succeeded = status.StatusStr == Constants.ComfyUi.SuccessStatus;
                    if (succeeded)
                    {
                        Log.Information("Job {PromptId} completed successfully.", [promptId]);
                    }
                    else
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

        /// <summary>The ws(s):// progress URL for the run, carrying the client id ComfyUI broadcasts this job under.</summary>
        private static Uri GetWebSocketUri(string comfyUiAddress, string clientId)
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
