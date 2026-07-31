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

            public const string SeedVrModelFolder = "seedvr2";

            // Namespaces each job's uploads and outputs on the instance.
            public const string JobRootPrefix = "jobs";
        }

        public static class Wrapper
        {
            // Submits a job to the on-instance ComfyUI API wrapper and returns a request id.
            public const string GeneratePath = "generate";
        }

        public static class Paths
        {
            public const string WorkflowTemplate = "workflows/SeedVR2_HD_video_upscale_api.json";
            public const string OutputDirectory = "videos/output";
        }

        public static class VastAi
        {
            public const string ApiBaseUrl = "https://console.vast.ai/";
            public const string InstancesPath = "api/v1/instances/";

            // ComfyUI's port inside the container; Vast.ai maps it to a different external port on each start.
            public const string ComfyUiContainerPort = "8188/tcp";

            public const string RunningStatus = "running";
        }
    }
}
