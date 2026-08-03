using System.Text.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Estimators.Jobs;
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
        private readonly WrapperProgressClient _wrapperProgressClient;
        private readonly WorkflowBuilder _workflowBuilder;
        private readonly VideoProbe _videoProbe;
        private readonly AppSettings _appSettings;

        public JobRunner(ComfyUiClient comfyUiClient, ComfyProgressClient comfyProgressClient, ComfyWrapperClient comfyWrapperClient, WrapperProgressClient wrapperProgressClient, WorkflowBuilder workflowBuilder, VideoProbe videoProbe, IOptions<AppSettings> appSettingsOptions)
        {
            _comfyUiClient = comfyUiClient;
            _comfyProgressClient = comfyProgressClient;
            _comfyWrapperClient = comfyWrapperClient;
            _wrapperProgressClient = wrapperProgressClient;
            _workflowBuilder = workflowBuilder;
            _videoProbe = videoProbe;
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

            // Read indexed container metadata before submitting, so the estimator can report immediately without scanning the video.
            var videoMetadata = await _videoProbe.GetVideoMetadata(jobRequest.LocalVideoPath, cancellationToken);

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

            var progressContext = GetProgressContext(videoMetadata, workflow.Upscaler.Inputs.BatchSize, workflow.Upscaler.Inputs.Resolution);
            var completedEntry = await _comfyProgressClient.TrackRawJobCompletion(comfyUiAddress, jobRequest.ClientId, promptId, progressContext, cancellationToken);
            if (completedEntry == null)
            {
                return false;
            }

            var outputFiles = GetRawOutputFiles(completedEntry);
            var downloaded = await DownloadJobOutputs(comfyUiAddress, outputFiles, cancellationToken);
            return downloaded;
        }

        /// <summary>Builds the estimators' job context from local metadata, or an empty context that SeedVR2 startup logs can complete.</summary>
        private JobProgressContext GetProgressContext(VideoMetadata videoMetadata, int batchSize, int targetResolution)
        {
            if (videoMetadata == null)
            {
                Log.Warning("Fast local video metadata is unavailable; ETA will initialize when SeedVR2 reports the input dimensions.");
                return new JobProgressContext(0, batchSize, 0, 0, targetResolution);
            }

            return new JobProgressContext(videoMetadata.FrameCount, batchSize, videoMetadata.Width, videoMetadata.Height, targetResolution);
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

            var requestId = await SubmitWorkflowToWrapper(wrapperAddress, workflow, cancellationToken);
            if (requestId == null)
            {
                return false;
            }

            var completedResult = await _wrapperProgressClient.TrackWrapperJobCompletion(wrapperAddress, requestId, cancellationToken);
            if (completedResult == null)
            {
                return false;
            }

            // Without an s3 config the wrapper returns file references, not bytes, so the download goes through raw /view.
            var outputFiles = GetWrapperOutputFiles(completedResult);
            var downloaded = await DownloadJobOutputs(comfyUiAddress, outputFiles, cancellationToken);
            return downloaded;
        }

        /// <summary>The raw path's downloads: the completed history entry's node outputs flattened into file references, dropping any entry without a filename.</summary>
        private IReadOnlyList<ComfyUiOutputFile> GetRawOutputFiles(ComfyUiHistoryEntry completedEntry)
        {
            var outputFiles = completedEntry.Outputs?.Values.Where(output => output?.Images != null).SelectMany(output => output.Images).Where(image => !string.IsNullOrEmpty(image?.Filename)).ToList() ?? [];
            return outputFiles;
        }

        /// <summary>The wrapper path's downloads: its output references mapped onto the raw /view coordinates, dropping any entry without a filename.</summary>
        private IReadOnlyList<ComfyUiOutputFile> GetWrapperOutputFiles(WrapperResult completedResult)
        {
            var outputFiles = completedResult.Output?.Where(output => !string.IsNullOrEmpty(output?.Filename)).Select(output => new ComfyUiOutputFile { Filename = output.Filename, Subfolder = output.Subfolder, Type = output.Type }).ToList() ?? [];
            return outputFiles;
        }

        /// <summary>Downloads every output file over raw /view into the local output directory, mirroring the remote job
        /// subfolder so runs never collide. True when every file arrived.</summary>
        private async Task<bool> DownloadJobOutputs(string comfyUiAddress, IReadOnlyList<ComfyUiOutputFile> outputFiles, CancellationToken cancellationToken)
        {
            if (outputFiles.Count == 0)
            {
                Log.Error("The job completed but reported no output files to download.");
                return false;
            }

            var outputRoot = Path.GetFullPath(Constants.Paths.OutputDirectory);
            foreach (var outputFile in outputFiles)
            {
                var saved = await SaveOutputFile(comfyUiAddress, outputRoot, outputFile, cancellationToken);
                if (!saved)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Saves one output file to its resolved local path. True when it arrived; a failure is logged and stops the job.</summary>
        private async Task<bool> SaveOutputFile(string comfyUiAddress, string outputRoot, ComfyUiOutputFile outputFile, CancellationToken cancellationToken)
        {
            var localPath = GetLocalOutputPath(outputRoot, outputFile);
            if (localPath == null)
            {
                Log.Error("The server-returned output location '{Subfolder}/{Filename}' would fall outside the local output directory; refusing to write it.", [outputFile.Subfolder, outputFile.Filename]);
                return false;
            }

            try
            {
                Log.Information("Downloading the output (GET /view): {Filename} -> {LocalPath}...", [outputFile.Filename, localPath]);
                await _comfyUiClient.DownloadOutput(comfyUiAddress, outputFile, localPath, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
            {
                Log.Error(ex, "Failed to download the output file '{Filename}'.", [outputFile.Filename]);
                return false;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Error("Downloading the output file '{Filename}' timed out before the transfer started.", [outputFile.Filename]);
                return false;
            }

            Log.Information("Downloaded the output to {LocalPath}.", [localPath]);
            return true;
        }

        /// <summary>Resolves the file's local path under the output root, or null when the server-returned subfolder or
        /// filename is rooted, traverses out of it or holds characters no local path allows, so a hostile value cannot
        /// write outside videos/output.</summary>
        private string GetLocalOutputPath(string outputRoot, ComfyUiOutputFile outputFile)
        {
            string localPath;
            try
            {
                localPath = Path.GetFullPath(Path.Combine(outputRoot, outputFile.Subfolder ?? string.Empty, outputFile.Filename));
            }
            catch (ArgumentException)
            {
                return null;
            }

            if (!localPath.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return null;
            }

            return localPath;
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
            ComfyUiUploadResult uploadResult;
            try
            {
                Log.Information("Uploading the input video to the instance (POST /upload/image) under {Subfolder}: {Path}", [jobRequest.UploadSubfolder, jobRequest.LocalVideoPath]);
                uploadResult = await _comfyUiClient.UploadVideo(comfyUiAddress, jobRequest.LocalVideoPath, jobRequest.UploadSubfolder, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                Log.Error(ex, "Failed to upload the input video to the instance");
                return null;
            }

            // ComfyUI addresses a subfoldered upload as "<subfolder>/<name>"; without a subfolder it is just the name.
            var uploadedFile = string.IsNullOrEmpty(uploadResult.Subfolder) ? uploadResult.Name : $"{uploadResult.Subfolder}/{uploadResult.Name}";
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

        /// <summary>Submits the workflow to the on-instance API wrapper (POST /generate) and returns its request id, or null when the submission fails.</summary>
        private async Task<string> SubmitWorkflowToWrapper(string wrapperAddress, SeedVrWorkflow workflow, CancellationToken cancellationToken)
        {
            WrapperResult result;
            try
            {
                Log.Information("Submitting the workflow to the API endpoint (POST /generate) at {WrapperAddress}...", [wrapperAddress]);
                result = await _comfyWrapperClient.Generate(wrapperAddress, workflow, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
            {
                Log.Error(ex, "Failed to submit the workflow to the API endpoint 'Generate'");
                return null;
            }

            if (result == null)
            {
                Log.Error("The API 'Generate' returned null instead of expected response");
                return null;
            }

            Log.Information("Submitted the job to the API endpoint 'Generate'. request_id {RequestId}, status {Status}.", [result.Id, result.Status]);
            return result.Id;
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
        private string FormatNodeError(NodeErrorDetail error)
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
