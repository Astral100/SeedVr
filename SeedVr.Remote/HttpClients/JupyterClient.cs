using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Logger;
using SeedVr.Remote.Models.Jupyter;

namespace SeedVr.Remote.HttpClients
{
    /// <summary>Talks to the instance's Jupyter server, the one instance service that can delete files: neither ComfyUI
    /// nor the wrapper exposes deletion, and Vast.ai's remote-execute API only works on stopped instances.</summary>
    public class JupyterClient
    {
        private readonly HttpClient _httpClient;

        public JupyterClient(HttpClient httpClient, IOptions<AppSettings> appSettingsOptions)
        {
            var appSettings = appSettingsOptions.Value;

            _httpClient = httpClient;

            // Every Jupyter call is a control call, so the client-wide timeout fits.
            _httpClient.Timeout = TimeSpan.FromSeconds(appSettings.HttpTimeoutSeconds);
        }

        /// <summary>Deletes the folder, contents included, through Jupyter's contents API. The token is per-instance,
        /// reported by the Vast.ai account API. A folder that is already gone counts as deleted.</summary>
        public async Task DeleteFolder(string jupyterAddress, string jupyterToken, string remoteFolder, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{jupyterAddress}{Constants.Jupyter.ContentsPath}/{remoteFolder}");
            request.Headers.Authorization = new AuthenticationHeaderValue(Constants.Jupyter.TokenScheme, jupyterToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            response.EnsureSuccessStatusCode();
        }

        /// <summary>Restarts the ComfyUI process through a Jupyter terminal running supervisorctl, releasing GPU memory
        /// a cancelled job left latched. The instance itself keeps running and ComfyUI is back in well under a minute.</summary>
        public async Task RestartComfyUi(string jupyterAddress, string jupyterToken, CancellationToken cancellationToken = default)
        {
            var terminal = await CreateTerminal(jupyterAddress, jupyterToken, cancellationToken);
            try
            {
                await SendTerminalCommand(jupyterAddress, jupyterToken, terminal.Name, Constants.Jupyter.RestartComfyUiCommand, cancellationToken);

                // Give the shell a moment to run the command before the terminal is torn down with it.
                await Task.Delay(TimeSpan.FromSeconds(Constants.Jupyter.TerminalCommandGraceSeconds), cancellationToken);
            }
            finally
            {
                await DeleteTerminal(jupyterAddress, jupyterToken, terminal.Name);
            }
        }

        /// <summary>Opens a terminal session on the Jupyter server and returns its name.</summary>
        private async Task<JupyterTerminal> CreateTerminal(string jupyterAddress, string jupyterToken, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{jupyterAddress}{Constants.Jupyter.TerminalsPath}");
            request.Headers.Authorization = new AuthenticationHeaderValue(Constants.Jupyter.TokenScheme, jupyterToken);
            request.Content = JsonContent.Create(new JupyterTerminalRequest());

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var terminal = await response.Content.ReadFromJsonAsync<JupyterTerminal>(cancellationToken);
            return terminal;
        }

        /// <summary>Sends one shell command to the terminal over its WebSocket; the shell runs it on its own once delivered.</summary>
        private async Task SendTerminalCommand(string jupyterAddress, string jupyterToken, string terminalName, string command, CancellationToken cancellationToken)
        {
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", $"{Constants.Jupyter.TokenScheme} {jupyterToken}");

            // The same self-signed certificate as the HTTP side, so this socket skips validation the same way.
            socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

            var socketUri = GetTerminalSocketUri(jupyterAddress, terminalName);
            await socket.ConnectAsync(socketUri, cancellationToken);

            var frame = JsonSerializer.SerializeToUtf8Bytes(new[] { Constants.Jupyter.StdinMessageType, command + "\n" });
            await socket.SendAsync(frame, WebSocketMessageType.Text, true, cancellationToken);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
        }

        /// <summary>The wss:// address of the terminal's stdin/stdout socket.</summary>
        private Uri GetTerminalSocketUri(string jupyterAddress, string terminalName)
        {
            var httpUri = new Uri(jupyterAddress);
            var scheme = httpUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
            var builder = new UriBuilder(httpUri)
            {
                Scheme = scheme,
                Path = $"{Constants.Jupyter.TerminalWebSocketPath}/{terminalName}"
            };

            return builder.Uri;
        }

        /// <summary>Closes the terminal session. Best-effort: a leaked session is harmless, the restart outcome is what matters.</summary>
        private async Task DeleteTerminal(string jupyterAddress, string jupyterToken, string terminalName)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"{jupyterAddress}{Constants.Jupyter.TerminalsPath}/{terminalName}");
                request.Headers.Authorization = new AuthenticationHeaderValue(Constants.Jupyter.TokenScheme, jupyterToken);
                using var response = await _httpClient.SendAsync(request, CancellationToken.None);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                Log.Warning(ex, "Failed to close the Jupyter terminal '{TerminalName}' after the restart command.", [terminalName]);
            }
        }
    }
}
