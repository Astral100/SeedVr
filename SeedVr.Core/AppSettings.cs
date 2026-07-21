using System.ComponentModel.DataAnnotations;

namespace SeedVr.Core
{
    public class AppSettings
    {
        public const string ConfigurationSection = "AppSettings";

        [Required, Url]
        public string ComfyUiBaseUrl { get; set; } // should end with "/"

        public string ComfyUiWebSocketUrl { get; set; }
        public string WorkflowTemplatePath { get; set; } = "workflows/SeedVR2_HD_video_upscale_api.json";
        public string InputVideoPath { get; set; }
        public string OutputDirectory { get; set; } = "videos/output";
        public string JobRootPrefix { get; set; } = "jobs";

        [Range(1, 3600)]
        public int HttpTimeoutSeconds { get; set; } = 30;
    }
}
