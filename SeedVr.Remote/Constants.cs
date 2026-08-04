namespace SeedVr.Remote
{
    public static class Constants
    {
        public static class ComfyUi
        {
            public const string SystemStatsPath = "system_stats";
            public const string ModelsPath = "models";
            public const string PromptPath = "prompt";
            public const string InterruptPath = "interrupt";
            public const string UploadImagePath = "upload/image";
            public const string HistoryPath = "history";
            public const string ViewPath = "view";
            public const string WebSocketPath = "ws";
            public const string SeedVrModelFolder = "seedvr2";
            public const string JobRootPrefix = "jobs";
            public const string SuccessStatus = "success";
            public const string ErrorStatus = "error";
            public const string SocketProgress = "progress";
            public const string SocketExecuting = "executing";
            public const string SocketExecutionSuccess = "execution_success";
            public const string SocketExecutionError = "execution_error";
            public const string LogsRawPath = "internal/logs/raw";

            public const int HistoryPollSeconds = 3;
            public const int LogPollSeconds = 2;
            public const int LogPollTimeoutSeconds = 5;
            public const int MessageBufferSize = 8192;
        }

        public static class Wrapper
        {
            public const string GeneratePath = "generate";
            public const string ResultPath = "result";
            public const string CancelPath = "cancel";
            public const string CompletedStatus = "completed";
            public const string FailedStatus = "failed";
            public const int ResultPollSeconds = 3;
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
            public const string ComfyUiContainerPort = "8188/tcp";
            public const string WrapperContainerPort = "8288/tcp";
            public const string JupyterContainerPort = "8080/tcp";
            public const string RunningStatus = "running";
        }

        public static class Jupyter
        {
            public const string ContentsPath = "api/contents";
            public const string TerminalsPath = "api/terminals";
            public const string TerminalWebSocketPath = "terminals/websocket";
            public const string TokenScheme = "token";
            public const string StdinMessageType = "stdin";
            public const string RestartComfyUiCommand = "supervisorctl restart comfyui";
            public const string InputJobsRoot = "workspace/ComfyUI/input/jobs";
            public const string OutputJobsRoot = "workspace/ComfyUI/output/jobs";

            public const int TerminalCommandGraceSeconds = 5;
        }

        public static class Recovery
        {
            public const int TimeoutSeconds = 120;
            public const int QueueDrainPollSeconds = 3;
            public const int VramSettleAttempts = 3;
            public const int VramPollSeconds = 5;
            public const int MaxConsecutiveReadFailures = 5;
            public const int ProgressPingSeconds = 15;
        }

        public static class Video
        {
            public const string FfprobeExecutable = "ffprobe";
        }

        public static class Transfer
        {
            public const string PartFileSuffix = ".part";
            public const int BufferSize = 81920;
            public const int ProgressPercentInterval = 10;
        }
    }
}
