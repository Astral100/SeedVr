using System.Text.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Logger;
using SeedVr.Remote.HttpClients;
using SeedVr.Remote.Models;
using SeedVr.Remote.Models.ComfyUi;
using SeedVr.Remote.Models.Workflow;
using SeedVr.Remote.Models.Wrapper;

namespace SeedVr.Remote
{
    /// <summary>Uploads the input video and submits the patched workflow to the instance, over raw ComfyUI or the API wrapper.</summary>
    public class JobRunner
    {
        private readonly ComfyUiClient _comfyUiClient;
        private readonly ComfyProgressClient _comfyProgressClient;
        private readonly ComfyWrapperClient _comfyWrapperClient;
        private readonly WorkflowBuilder _workflowBuilder;
        private readonly AppSettings _appSettings;

        public JobRunner(ComfyUiClient comfyUiClient, ComfyProgressClient comfyProgressClient, ComfyWrapperClient comfyWrapperClient, WorkflowBuilder workflowBuilder, IOptions<AppSettings> appSettingsOptions)
        {
            _comfyUiClient = comfyUiClient;
            _comfyProgressClient = comfyProgressClient;
            _comfyWrapperClient = comfyWrapperClient;
            _workflowBuilder = workflowBuilder;
            _appSettings = appSettingsOptions.Value;
        }

        /// <summary>Uploads, builds and submits the workflow to ComfyUI over the raw protocol.</summary>
        public async Task<bool> StartRawJob(string comfyUiAddress, CancellationToken cancellationToken)
        {
            var jobRequest = GetJobRequest();
            if (jobRequest == null)
            {
                return false;
            }

            var uploadedFile = await UploadInputVideo(comfyUiAddress, jobRequest, cancellationToken);
            if (uploadedFile == null)
            {
                return false;
            }

            var workflow = _workflowBuilder.GetSeedVrWorkflow(uploadedFile, jobRequest.OutputFilenamePrefix);

            var promptId = await SubmitWorkflowToComfyUi(comfyUiAddress, workflow, jobRequest, cancellationToken);
            if (promptId == null)
            {
                return false;
            }

            var success = await _comfyProgressClient.TrackJobCompletion(comfyUiAddress, jobRequest.ClientId, promptId, cancellationToken);
            return success;
        }

        /// <summary>Uploads through raw ComfyUI, then submits the same workflow to the on-instance API wrapper instead of /prompt.</summary>
        public async Task<bool> StartWrapperJob(string comfyUiAddress, string wrapperAddress, CancellationToken cancellationToken)
        {
            var jobRequest = GetJobRequest();
            if (jobRequest == null)
            {
                return false;
            }

            var uploadedFile = await UploadInputVideo(comfyUiAddress, jobRequest, cancellationToken);
            if (uploadedFile == null)
            {
                return false;
            }

            var workflow = _workflowBuilder.GetSeedVrWorkflow(uploadedFile, jobRequest.OutputFilenamePrefix);

            var success = await SubmitWorkflowToWrapper(wrapperAddress, workflow, cancellationToken);
            return success;
        }

        /// <summary>Resolves the input video and builds the job's identity and instance-side paths, or null when the file is not found.</summary>
        private JobRequest GetJobRequest()
        {
            var localVideoPath = Path.GetFullPath(_appSettings.InputVideoPath);
            if (!File.Exists(localVideoPath))
            {
                Log.Error("The input video was not found at '{Path}'. Check AppSettings:InputVideoPath and the working directory.", [localVideoPath]);
                return null;
            }

            var jobId = Guid.NewGuid().ToString("N");
            var uploadSubfolder = $"{Constants.ComfyUi.JobRootPrefix}/{jobId}";
            var inputBaseName = Path.GetFileNameWithoutExtension(localVideoPath);
            var jobRequest = new JobRequest
            {
                JobId = jobId,
                ClientId = Guid.NewGuid().ToString(),
                LocalVideoPath = localVideoPath,
                UploadSubfolder = uploadSubfolder,
                OutputFilenamePrefix = $"{uploadSubfolder}/{inputBaseName}"
            };

            return jobRequest;
        }

        /// <summary>Uploads the input video into the job's subfolder and returns the reference ComfyUI stored it under, or null on failure.</summary>
        private async Task<string> UploadInputVideo(string comfyUiAddress, JobRequest jobRequest, CancellationToken cancellationToken)
        {
            ComfyUiUploadResult upload;
            try
            {
                Log.Information("Uploading the input video to the instance (POST /upload/image) under {Subfolder}: {Path}", [jobRequest.UploadSubfolder, jobRequest.LocalVideoPath]);
                upload = await _comfyUiClient.UploadVideo(comfyUiAddress, jobRequest.LocalVideoPath, jobRequest.UploadSubfolder, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                Log.Error(ex, "Failed to upload the input video to the instance");
                return null;
            }

            // ComfyUI addresses a subfoldered upload as "<subfolder>/<name>"; without a subfolder it is just the name.
            var uploadedFile = string.IsNullOrEmpty(upload.Subfolder) ? upload.Name : $"{upload.Subfolder}/{upload.Name}";
            Log.Information("Uploaded input video; ComfyUI stored it as {Name}.", [uploadedFile]);

            return uploadedFile;
        }

        /// <summary>Submits the workflow to ComfyUI over the raw protocol and returns its prompt id, or null when the submission fails or a node is rejected.</summary>
        private async Task<string> SubmitWorkflowToComfyUi(string comfyUiAddress, SeedVrWorkflow workflow, JobRequest jobRequest, CancellationToken cancellationToken)
        {
            ComfyUiSubmitResult submitResult;
            try
            {
                Log.Information("Submitting the workflow to ComfyUI (POST /prompt), job {JobId}, client_id {ClientId}...", [jobRequest.JobId, jobRequest.ClientId]);
                submitResult = await _comfyUiClient.SubmitPrompt(comfyUiAddress, workflow, jobRequest.ClientId, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
            {
                Log.Error(ex, "Failed to submit the workflow to ComfyUI");
                return null;
            }

            if (submitResult == null)
            {
                Log.Error("ComfyUI returned no usable response to the submission.");
                return null;
            }

            if (submitResult.NodeErrors != null && submitResult.NodeErrors.Count > 0)
            {
                LogNodeErrors(submitResult.NodeErrors);
                return null;
            }

            Log.Information("Submitted the job to ComfyUI. job {JobId}, prompt_id {PromptId}, client_id {ClientId}.", [jobRequest.JobId, submitResult.PromptId, jobRequest.ClientId]);
            return submitResult.PromptId;
        }

        /// <summary>Submits the workflow to the on-instance API wrapper (POST /generate); false when the submission fails.</summary>
        private async Task<bool> SubmitWorkflowToWrapper(string wrapperBaseUrl, SeedVrWorkflow workflow, CancellationToken cancellationToken)
        {
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
