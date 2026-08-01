namespace SeedVr.Core
{
    public static class Constants
    {
        public static class ComfyUi
        {
            public const string SystemStatsPath = "system_stats";
            public const string ModelsPath = "models";

            // Reports the queue depth on GET, and takes a workflow on POST.
            public const string PromptPath = "prompt";

            // Takes a multipart file upload; despite the name it accepts videos too.
            public const string UploadImagePath = "upload/image";

            // Records a finished job keyed by its prompt id; the authoritative completion source.
            public const string HistoryPath = "history";

            // The progress WebSocket; a clientId query ties the stream to this run's submission.
            public const string WebSocketPath = "ws";

            public const string SeedVrModelFolder = "seedvr2";

            // Namespaces each job's uploads and outputs on the instance.
            public const string JobRootPrefix = "jobs";

            // history status_str values marking a finished job.
            public const string SuccessStatus = "success";
            public const string ErrorStatus = "error";

            // WebSocket message types the progress monitor acts on.
            public const string SocketProgress = "progress";
            public const string SocketExecuting = "executing";
            public const string SocketExecutionSuccess = "execution_success";
            public const string SocketExecutionError = "execution_error";

            // How often /history is polled once the progress socket has dropped.
            public const int HistoryPollSeconds = 3;
        }

        public static class Wrapper
        {
            // Submits a job to the on-instance ComfyUI API wrapper and returns a request id.
            public const string GeneratePath = "generate";
        }

        public static class Paths
        {
            public const string SeedVrWorkflow = "workflows/SeedVR2_HD_video_upscale_api.json";
            public const string OutputDirectory = "videos/output";
        }

        public static class VastAi
        {
            public const string ApiBaseUrl = "https://console.vast.ai/";
            public const string InstancesPath = "api/v1/instances/";

            // ComfyUI's port inside the container; Vast.ai maps it to a different external port on each start.
            public const string ComfyUiContainerPort = "8188/tcp";

            // The API wrapper's port inside the container, mapped to a different external port on each start.
            public const string WrapperContainerPort = "8288/tcp";

            public const string RunningStatus = "running";
        }
    }
}
