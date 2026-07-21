namespace SeedVr.Core
{
    public static class Constants
    {
        public static class ComfyUi
        {
            public const string SeedVrModelFolder = "seedvr2";

            // Namespaces each job's uploads and outputs on the instance.
            public const string JobRootPrefix = "jobs";
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
