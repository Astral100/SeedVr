using System.Text.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Logger;
using SeedVr.Remote.Models;
using SeedVr.Remote.Models.ComfyUi;
using SeedVr.Remote.Models.Workflow;
using SeedVr.Remote.Models.Wrapper;

namespace SeedVr.Remote
{
    /// <summary>Uploads the input video and submits the patched workflow to the instance, over raw ComfyUI or the API wrapper.</summary>
    public class JobSubmitter
    {
        private readonly ComfyUiClient _comfyUiClient;
        private readonly ComfyWrapperClient _comfyWrapperClient;
        private readonly WorkflowBuilder _workflowBuilder;
        private readonly AppSettings _appSettings;

        public JobSubmitter(ComfyUiClient comfyUiClient, ComfyWrapperClient comfyWrapperClient, WorkflowBuilder workflowBuilder, IOptions<AppSettings> appSettingsOptions)
        {
            _comfyUiClient = comfyUiClient;
            _comfyWrapperClient = comfyWrapperClient;
            _workflowBuilder = workflowBuilder;
            _appSettings = appSettingsOptions.Value;
        }

        /// <summary>Uploads, builds and submits the workflow to ComfyUI over the raw protocol.</summary>
        public async Task<bool> SubmitRawJob(string comfyUiAddress, CancellationToken cancellationToken)
        {
            var jobContext = PrepareJob();
            if (jobContext == null)
            {
                return false;
            }

            var workflow = await BuildWorkflow(comfyUiAddress, jobContext, cancellationToken);
            if (workflow == null)
            {
                return false;
            }

            ComfyUiSubmitResult submitResult;
            try
            {
                Log.Information("Submitting the workflow to ComfyUI (POST /prompt), job {JobId}, client_id {ClientId}...", [jobContext.JobId, jobContext.ClientId]);
                submitResult = await _comfyUiClient.SubmitPrompt(comfyUiAddress, workflow, jobContext.ClientId, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
            {
                Log.Error(ex, "Failed to submit the workflow to ComfyUI");
                return false;
            }

            if (submitResult == null)
            {
                Log.Error("ComfyUI returned no usable response to the submission.");
                return false;
            }

            if (submitResult.NodeErrors != null && submitResult.NodeErrors.Count > 0)
            {
                LogNodeErrors(submitResult.NodeErrors);
                return false;
            }

            Log.Information("Submitted the job to ComfyUI. job {JobId}, prompt_id {PromptId}, client_id {ClientId}.", [jobContext.JobId, submitResult.PromptId, jobContext.ClientId]);
            return true;
        }

        /// <summary>Uploads through raw ComfyUI, then submits the same workflow to the on-instance API wrapper instead of /prompt.</summary>
        public async Task<bool> SubmitWrapperJob(string comfyUiAddress, CancellationToken cancellationToken)
        {
            var wrapperBaseUrl = _appSettings.WrapperBaseUrl;
            if (string.IsNullOrWhiteSpace(wrapperBaseUrl))
            {
                Log.Error("No wrapper address is configured. Set AppSettings:WrapperBaseUrl to the on-instance wrapper URL.");
                return false;
            }

            var jobContext = PrepareJob();
            if (jobContext == null)
            {
                return false;
            }

            var workflow = await BuildWorkflow(comfyUiAddress, jobContext, cancellationToken);
            if (workflow == null)
            {
                return false;
            }

            WrapperResult result;
            try
            {
                Log.Information("Submitting the workflow to the API wrapper (POST /generate) at {WrapperBaseUrl}...", [wrapperBaseUrl]);
                result = await _comfyWrapperClient.Generate(wrapperBaseUrl, workflow, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
            {
                Log.Error(ex, "Failed to submit the workflow to the API wrapper");
                return false;
            }

            Log.Information("Submitted the job to the API wrapper. request_id {RequestId}, status {Status}.", [result.Id, result.Status]);
            return true;
        }

        /// <summary>Resolves the input video and builds the job's identity and instance-side paths, or null when no input is configured or found.</summary>
        private JobContext PrepareJob()
        {
            var configuredPath = _appSettings.InputVideoPath;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                Log.Error("No input video is configured. Set AppSettings:InputVideoPath to the video to upscale.");
                return null;
            }

            // videos/ is not copied to the output directory, so the path resolves against the working directory, not the app base.
            var localVideoPath = Path.GetFullPath(configuredPath);
            if (!File.Exists(localVideoPath))
            {
                Log.Error("The input video was not found at '{Path}'. Check AppSettings:InputVideoPath and the working directory.", [localVideoPath]);
                return null;
            }

            var jobId = Guid.NewGuid().ToString("N");
            var uploadSubfolder = $"{Constants.ComfyUi.JobRootPrefix}/{jobId}";
            var inputBaseName = Path.GetFileNameWithoutExtension(localVideoPath);
            var jobContext = new JobContext
            {
                JobId = jobId,
                ClientId = Guid.NewGuid().ToString(),
                LocalVideoPath = localVideoPath,
                UploadSubfolder = uploadSubfolder,
                OutputFilenamePrefix = $"{uploadSubfolder}/{inputBaseName}"
            };

            return jobContext;
        }

        /// <summary>Uploads the input video into the job's subfolder and builds the patched workflow, or null on failure.</summary>
        private async Task<SeedVrWorkflow> BuildWorkflow(string comfyUiAddress, JobContext jobContext, CancellationToken cancellationToken)
        {
            ComfyUiUploadResult upload;
            try
            {
                Log.Information("Uploading the input video to the instance (POST /upload/image) under {Subfolder}: {Path}", [jobContext.UploadSubfolder, jobContext.LocalVideoPath]);
                upload = await _comfyUiClient.UploadVideo(comfyUiAddress, jobContext.LocalVideoPath, jobContext.UploadSubfolder, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                Log.Error(ex, "Failed to upload the input video to the instance");
                return null;
            }

            // ComfyUI addresses a subfoldered upload as "<subfolder>/<name>"; without a subfolder it is just the name.
            var uploadedFile = string.IsNullOrEmpty(upload.Subfolder) ? upload.Name : $"{upload.Subfolder}/{upload.Name}";
            Log.Information("Uploaded the input video; ComfyUI stored it as {Name}.", [uploadedFile]);

            var workflow = _workflowBuilder.Build(uploadedFile, jobContext.OutputFilenamePrefix);
            return workflow;
        }

        /// <summary>ComfyUI rejected one or more nodes in the workflow, so report each so the workflow can be fixed.</summary>
        private void LogNodeErrors(IReadOnlyDictionary<string, NodeError> nodeErrors)
        {
            Log.Error("ComfyUI rejected the workflow: {Count} node(s) reported errors.", [nodeErrors.Count]);

            foreach (var nodeError in nodeErrors)
            {
                var messages = string.Join("; ", nodeError.Value.Errors?.Select(error => FormatNodeError(error)) ?? []);
                Log.Error("Node {NodeId} ({ClassType}): {Messages}", [nodeError.Key, nodeError.Value.ClassType, messages]);
            }
        }

        /// <summary>One node error as "message (details)", dropping the details when ComfyUI left them empty.</summary>
        private static string FormatNodeError(NodeErrorDetail error)
        {
            if (string.IsNullOrEmpty(error.Details))
            {
                return error.Message;
            }

            var formatted = $"{error.Message} ({error.Details})";
            return formatted;
        }
    }
}
