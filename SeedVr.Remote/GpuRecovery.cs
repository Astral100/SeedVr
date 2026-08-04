using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Logger;
using SeedVr.Remote.HttpClients;

namespace SeedVr.Remote
{
    /// <summary>Brings a cancelled job's instance back to a usable state: waits out the wind-down, detects latched GPU memory and restarts the ComfyUI process when it stays held.</summary>
    public class GpuRecovery
    {
        private readonly ComfyUiClient _comfyUiClient;
        private readonly JupyterClient _jupyterClient;
        private readonly AppSettings _appSettings;

        public GpuRecovery(ComfyUiClient comfyUiClient, JupyterClient jupyterClient, IOptions<AppSettings> appSettingsOptions)
        {
            _comfyUiClient = comfyUiClient;
            _jupyterClient = jupyterClient;
            _appSettings = appSettingsOptions.Value;
        }

        /// <summary>After a cancellation, waits for the interrupted job to wind down and checks the GPU released its
        /// memory; when it stays latched, restarts the ComfyUI process through Jupyter so the next job cannot OOM on it.</summary>
        public async Task RecoverLatchedGpuMemory(string comfyUiAddress, string jupyterAddress, string jupyterToken)
        {
            // The run token is already cancelled, so the recovery runs on its own overall deadline instead.
            using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(Constants.Recovery.TimeoutSeconds));
            try
            {
                var drained = await WaitForQueueDrain(comfyUiAddress, recoveryTimeout.Token);
                if (!drained)
                {
                    return;
                }

                var latched = await IsGpuMemoryLatched(comfyUiAddress, recoveryTimeout.Token);
                if (latched == null)
                {
                    Log.Warning("Could not read the GPU memory state after the cancellation; leaving the instance as is - the readiness check will refuse it if the memory stayed latched.");
                    return;
                }

                if (latched == false)
                {
                    Log.Information("The GPU released its memory after the cancellation.");
                    return;
                }

                Log.Warning("The cancelled job left GPU memory latched; restarting the ComfyUI process to release it...");
                await _jupyterClient.RestartComfyUi(jupyterAddress, jupyterToken, recoveryTimeout.Token);
                await WaitForGpuMemoryRecovery(comfyUiAddress, recoveryTimeout.Token);
            }
            catch (OperationCanceledException) when (recoveryTimeout.IsCancellationRequested)
            {
                Log.Warning("Gave up on the GPU memory recovery after {Seconds}s; the readiness check will refuse the instance until the memory is released.", [Constants.Recovery.TimeoutSeconds]);
            }
            catch (Exception ex) when (ex is HttpRequestException or WebSocketException or JsonException or NotSupportedException)
            {
                Log.Warning(ex, "The GPU memory recovery failed; the readiness check will refuse the instance until the memory is released.");
            }
        }

        /// <summary>Waits until the queue is empty: the interrupt lands at a phase boundary, so the cancelled job can
        /// hold the instance for a while. Each poll doubles as a health ping - true when the queue drained, false when
        /// the instance stopped answering, so the recovery gives up early instead of waiting out the deadline.</summary>
        private async Task<bool> WaitForQueueDrain(string comfyUiAddress, CancellationToken cancellationToken)
        {
            var consecutiveFailures = 0;
            var secondsSinceProgressPing = 0;
            while (true)
            {
                int? queueLength;
                try
                {
                    queueLength = await _comfyUiClient.GetComfyUiQueueLength(comfyUiAddress, cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // A per-request control timeout, not the recovery deadline; an unanswered ping like any other.
                    queueLength = null;
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
                {
                    queueLength = null;
                }

                if (queueLength == null)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= Constants.Recovery.MaxConsecutiveReadFailures)
                    {
                        Log.Warning("The instance stopped answering the queue poll ({Count} failed pings in a row); skipping the GPU memory check - the readiness check will refuse the instance if its memory stayed latched.", [consecutiveFailures]);
                        return false;
                    }

                    if (consecutiveFailures == 1)
                    {
                        Log.Warning("The queue poll went unanswered while waiting for the cancelled job to wind down; pinging again...");
                    }
                }
                else
                {
                    consecutiveFailures = 0;

                    if (queueLength == 0)
                    {
                        Log.Information("The cancelled job has wound down on the instance.");
                        return true;
                    }

                    secondsSinceProgressPing += Constants.Recovery.QueueDrainPollSeconds;
                    if (secondsSinceProgressPing >= Constants.Recovery.ProgressPingSeconds)
                    {
                        Log.Information("The cancelled job is still winding down ({QueueLength} in the queue)...", [queueLength]);
                        secondsSinceProgressPing = 0;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(Constants.Recovery.QueueDrainPollSeconds), cancellationToken);
            }
        }

        /// <summary>Reads the GPU a few times before declaring a latch, because the node releases its memory a moment
        /// after the interrupt. True only on an actually observed low reading - a restart needs evidence, so reads that
        /// all failed return null rather than declaring either outcome.</summary>
        private async Task<bool?> IsGpuMemoryLatched(string comfyUiAddress, CancellationToken cancellationToken)
        {
            var lowReadingSeen = false;
            for (var attempt = 0; attempt < Constants.Recovery.VramSettleAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Constants.Recovery.VramPollSeconds), cancellationToken);
                }

                var freeFraction = await GetFreeVramFraction(comfyUiAddress, cancellationToken);
                if (freeFraction == null)
                {
                    continue;
                }

                if (freeFraction >= _appSettings.MinimumFreeVramFraction)
                {
                    return false;
                }

                lowReadingSeen = true;
            }

            if (!lowReadingSeen)
            {
                return null;
            }

            return true;
        }

        /// <summary>Polls the GPU until the restarted ComfyUI answers with the memory released.</summary>
        private async Task WaitForGpuMemoryRecovery(string comfyUiAddress, CancellationToken cancellationToken)
        {
            var secondsSinceProgressPing = 0;
            while (true)
            {
                var freeFraction = await GetFreeVramFraction(comfyUiAddress, cancellationToken);
                if (freeFraction >= _appSettings.MinimumFreeVramFraction)
                {
                    Log.Information("ComfyUI restarted and the GPU memory is released ({FreeFraction:P0} free).", [freeFraction.Value]);
                    return;
                }

                secondsSinceProgressPing += Constants.Recovery.VramPollSeconds;
                if (secondsSinceProgressPing >= Constants.Recovery.ProgressPingSeconds)
                {
                    Log.Information("ComfyUI is not back yet after the restart; still waiting...");
                    secondsSinceProgressPing = 0;
                }

                await Task.Delay(TimeSpan.FromSeconds(Constants.Recovery.VramPollSeconds), cancellationToken);
            }
        }

        /// <summary>The first GPU's free-VRAM fraction, or null when this attempt could not read it - expected
        /// mid-restart, when ComfyUI refuses connections, so the callers just poll again.</summary>
        private async Task<double?> GetFreeVramFraction(string comfyUiAddress, CancellationToken cancellationToken)
        {
            try
            {
                var stats = await _comfyUiClient.GetSystemStats(comfyUiAddress, cancellationToken);
                var device = stats?.Devices.FirstOrDefault();
                if (device == null || device.VramTotal <= 0)
                {
                    return null;
                }

                return (double)device.VramFree / device.VramTotal;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
            {
                return null;
            }
        }
    }
}
