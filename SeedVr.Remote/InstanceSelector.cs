using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Logger;
using SeedVr.Remote.HttpClients;
using SeedVr.Remote.Models;
using SeedVr.Remote.Models.VastAi;

namespace SeedVr.Remote
{
    /// <summary>Picks the first running Vast.ai instance that is reachable, has the models and is free, and returns its ComfyUI address.</summary>
    public class InstanceSelector
    {
        private readonly ComfyUiClient _comfyUiClient;
        private readonly VastAiClient _vastAiClient;
        private readonly AppSettings _appSettings;

        public InstanceSelector(ComfyUiClient comfyUiClient, VastAiClient vastAiClient, IOptions<AppSettings> appSettingsOptions)
        {
            _comfyUiClient = comfyUiClient;
            _vastAiClient = vastAiClient;
            _appSettings = appSettingsOptions.Value;
        }

        /// <summary>The first instance that can take the job, or null when none can.</summary>
        public async Task<VastAiInstance> GetFirstAvailableInstance(CancellationToken cancellationToken = default)
        {
            var runningInstances = await GetRunningInstances(cancellationToken);
            if (runningInstances == null)
            {
                return null;
            }

            if (runningInstances.Count == 0)
            {
                Log.Error("The Vast.ai account has no running instance to run the job on.");
                return null;
            }

            var availableInstance = await FindFirstAvailableInstance(runningInstances, cancellationToken);
            if (availableInstance == null)
            {
                return null;
            }

            Log.Information("Vast.ai instance {InstanceId} is ready to run the job.", [availableInstance.Id]);
            return availableInstance;
        }

        /// <summary>The account's running instances, or null when Vast.ai could not be read.</summary>
        private async Task<IReadOnlyList<VastAiInstance>> GetRunningInstances(CancellationToken cancellationToken)
        {
            Log.Information("Reading the instances on the Vast.ai account...");

            IReadOnlyList<VastAiInstance> instances;
            try
            {
                instances = await _vastAiClient.GetInstances(cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Error("Timed out after {Seconds}s reading the instances from the Vast.ai API.", [_appSettings.HttpTimeoutSeconds]);
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
            {
                Log.Error(ex, "Failed to read the instances from the Vast.ai API");
                return null;
            }

            var runningInstances = instances.Where(instance => instance.ActualStatus == Constants.VastAi.RunningStatus).ToList();
            Log.Information("Vast.ai reports {RunningCount} running instance(s) of {TotalCount} on the account.", [runningInstances.Count, instances.Count]);

            return runningInstances;
        }

        /// <summary>The first instance that is reachable, has the models downloaded and is not busy, or null when there is none.</summary>
        private async Task<VastAiInstance> FindFirstAvailableInstance(IReadOnlyList<VastAiInstance> instances, CancellationToken cancellationToken)
        {
            var unavailableInstances = new List<InstanceState>();

            foreach (var instance in instances)
            {
                Log.Information("Evaluating if Vast.ai instance {InstanceId} is available for processing...", [instance.Id]);

                var availability = await GetInstanceState(instance, cancellationToken);
                if (availability == InstanceState.Available)
                {
                    return instance;
                }

                Log.Warning("Vast.ai instance {InstanceId} is {Availability}.", [instance.Id, availability]);
                unavailableInstances.Add(availability);
            }

            LogUnavailableInstances(unavailableInstances);
            return null;
        }

        /// <summary>Faulted instances need attention, so they are reported apart from the ones that only need time.</summary>
        private void LogUnavailableInstances(IReadOnlyList<InstanceState> rejected)
        {
            var busyCount = rejected.Count(availability => availability == InstanceState.Busy);
            var provisioningCount = rejected.Count(availability => availability == InstanceState.Provisioning);
            var faultedCount = rejected.Count(availability => availability == InstanceState.Faulted);

            if (faultedCount == 0)
            {
                Log.Warning("No instance is free yet: {BusyCount} busy, {ProvisioningCount} still provisioning. Try again shortly.", [busyCount, provisioningCount]);
                return;
            }

            Log.Error("No instance is available: {FaultedCount} faulted and need attention, {BusyCount} busy, {ProvisioningCount} still provisioning.", [faultedCount, busyCount, provisioningCount]);
        }

        /// <summary>Runs the checks in order, stopping at the first one the instance fails.</summary>
        private async Task<InstanceState> GetInstanceState(VastAiInstance instance, CancellationToken cancellationToken)
        {
            var instanceState = ValidateInstanceAddress(instance);
            if (instanceState != InstanceState.Available)
            {
                return instanceState;
            }

            var comfyUiAddress = instance.GetComfyUiAddress();
            Log.Information("Vast.ai instance {InstanceId} is running at {Address}", [instance.Id, comfyUiAddress]);

            instanceState = await ValidateComfyUiReachable(comfyUiAddress, cancellationToken);
            if (instanceState != InstanceState.Available)
            {
                return instanceState;
            }

            instanceState = await ValidateModelsDownloaded(comfyUiAddress, cancellationToken);
            if (instanceState != InstanceState.Available)
            {
                return instanceState;
            }

            instanceState = await ValidateComfyUiAvailability(comfyUiAddress, cancellationToken);

            return instanceState;
        }

        /// <summary>Whether the instance publishes an address for ComfyUI, and why not when it does not.</summary>
        private InstanceState ValidateInstanceAddress(VastAiInstance instance)
        {
            var otherPortCount = instance.Ports?.OtherPorts?.Count ?? 0;

            if (!string.IsNullOrWhiteSpace(instance.PublicIpAddress) && !string.IsNullOrWhiteSpace(instance.GetComfyUiHostPort()))
            {
                return InstanceState.Available;
            }

            // Ports are published together once the container is up, so an empty mapping is a matter of
            // time, while a mapping that skips ComfyUI's port is how the instance was created.
            if (string.IsNullOrWhiteSpace(instance.PublicIpAddress) || otherPortCount == 0)
            {
                Log.Warning("Vast.ai instance {InstanceId} has not published its address and ports yet.", [instance.Id]);
                return InstanceState.Provisioning;
            }

            Log.Warning("Vast.ai instance {InstanceId} publishes {OtherPortCount} port(s), but not {Port}. Was it created from a template that exposes ComfyUI?", [instance.Id, otherPortCount, Constants.VastAi.ComfyUiContainerPort]);
            return InstanceState.Faulted;
        }

        private async Task<InstanceState> ValidateComfyUiReachable(string comfyUiAddress, CancellationToken cancellationToken)
        {
            Log.Information("Checking ComfyUI instance health (GET /system_stats)...");

            try
            {
                var stats = await _comfyUiClient.GetSystemStats(comfyUiAddress, cancellationToken);
                if (stats == null)
                {
                    Log.Warning("ComfyUI returned an empty /system_stats response.");
                    return InstanceState.Faulted;
                }

                var system = stats.System;
                Log.Information("ComfyUI is reachable: version {Version}, OS {Os}, RAM {FreeRam:F1}/{TotalRam:F1} GiB free.", [system.ComfyUiVersion, system.Os, ToGigabytes(system.RamFree), ToGigabytes(system.RamTotal)]);

                var device = stats.Devices.FirstOrDefault();
                if (device != null)
                {
                    Log.Information("ComfyUI device: {Name} ({Type}), VRAM {FreeVram:F1}/{TotalVram:F1} GiB free.", [device.Name, device.Type, ToGigabytes(device.VramFree), ToGigabytes(device.VramTotal)]);
                }

                return InstanceState.Available;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warning("Timed out after {Seconds}s waiting for the ComfyUI instance. It may still be starting up.", [_appSettings.HttpTimeoutSeconds]);
                return InstanceState.Provisioning;
            }
            // A status code means ComfyUI answered and refused; without one the connection itself failed,
            // which is what a container that has not finished starting looks like.
            catch (HttpRequestException ex) when (ex.StatusCode == null)
            {
                Log.Warning("ComfyUI is not answering on the instance yet: {Reason}", [ex.Message]);
                return InstanceState.Provisioning;
            }
            // The proxy answers but cannot reach ComfyUI behind it yet: the container is up but the ComfyUI
            // process is still starting, so the reverse proxy returns a gateway error rather than a refusal.
            catch (HttpRequestException ex) when (IsComfyUiStillStarting(ex.StatusCode))
            {
                Log.Warning("ComfyUI is not ready behind the proxy yet (StatusCode {StatusCode}). It may still be starting up.", [ex.StatusCode]);
                return InstanceState.Provisioning;
            }
            catch (HttpRequestException ex)
            {
                Log.Warning("ComfyUI refused the health check with StatusCode {StatusCode}", [ex.StatusCode]);
                return InstanceState.Faulted;
            }
        }

        /// <summary>A gateway status comes from the reverse proxy, not ComfyUI, so the backend is still starting up rather than broken.</summary>
        private bool IsComfyUiStillStarting(HttpStatusCode? statusCode)
        {
            return statusCode == HttpStatusCode.BadGateway || statusCode == HttpStatusCode.ServiceUnavailable || statusCode == HttpStatusCode.GatewayTimeout;
        }

        private double ToGigabytes(long bytes)
        {
            return bytes / 1024.0 / 1024.0 / 1024.0;
        }

        private async Task<InstanceState> ValidateModelsDownloaded(string comfyUiAddress, CancellationToken cancellationToken)
        {
            Log.Information("Checking downloaded models on instance (GET /models/{Folder}). DiT: {DitModel}, VAE: {VaeModel}", [Constants.ComfyUi.SeedVrModelFolder, _appSettings.DitModel, _appSettings.VaeModel]);

            IReadOnlyList<string> installedModels;
            try
            {
                installedModels = await _comfyUiClient.GetInstalledModels(comfyUiAddress, Constants.ComfyUi.SeedVrModelFolder, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                Log.Warning("The instance has no '{Folder}' models folder. Is the SeedVR2 node pack installed?", [Constants.ComfyUi.SeedVrModelFolder]);
                return InstanceState.Faulted;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warning("Timed out after {Seconds}s reading the installed models from the instance.", [_appSettings.HttpTimeoutSeconds]);
                return InstanceState.Faulted;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
            {
                Log.Warning(ex, "Failed to read the installed models from the instance");
                return InstanceState.Faulted;
            }

            Log.Information("Models downloaded on instance ({Count}): {Models}", [installedModels.Count, string.Join(", ", installedModels)]);

            var ditInstalled = installedModels.Contains(_appSettings.DitModel);
            var vaeInstalled = installedModels.Contains(_appSettings.VaeModel);

            if (ditInstalled && vaeInstalled)
            {
                Log.Information("Selected DiT and VAE models are both downloaded.");
                return InstanceState.Available;
            }

            if (!ditInstalled)
            {
                Log.Warning("DiT model {DitModel} is not downloaded on the instance.", [_appSettings.DitModel]);
            }

            if (!vaeInstalled)
            {
                Log.Warning("VAE model {VaeModel} is not downloaded on the instance.", [_appSettings.VaeModel]);
            }

            // The folder is there, so the node pack is installed and the models may still be downloading.
            return InstanceState.Provisioning;
        }

        /// <summary>Whether the ComfyUI instance is free, so the job is not queued behind work already running on it.</summary>
        private async Task<InstanceState> ValidateComfyUiAvailability(string comfyUiAddress, CancellationToken cancellationToken)
        {
            Log.Information("Checking the ComfyUI job queue (GET /prompt)...");

            int? jobQueueLength;
            try
            {
                jobQueueLength = await _comfyUiClient.GetComfyUiQueueLength(comfyUiAddress, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warning("Timed out after {Seconds}s reading the job queue length from the ComfyUI instance.", [_appSettings.HttpTimeoutSeconds]);
                return InstanceState.Faulted;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
            {
                Log.Warning(ex, "Failed to read the job queue from ComfyUI instance");
                return InstanceState.Faulted;
            }

            if (jobQueueLength == null)
            {
                Log.Warning("The ComfyUI instance did not report its job queue length, so it cannot be treated as available.");
                return InstanceState.Faulted;
            }

            if (jobQueueLength > 0)
            {
                Log.Information("The instance is busy: {QueueLength} job(s) queued or running.", [jobQueueLength]);
                return InstanceState.Busy;
            }

            Log.Information("The ComfyUI job queue is empty.");
            return InstanceState.Available;
        }
    }
}
