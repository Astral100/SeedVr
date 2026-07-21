using System.ComponentModel.DataAnnotations;

namespace SeedVr.Core
{
    public class AppSettings
    {
        public const string ConfigurationSection = "AppSettings";

        [Required, Url]
        public string ComfyUiBaseUrl { get; set; } // should end with "/"

        public string ComfyUiWebSocketUrl { get; set; }

        // Vast.ai instance portal token (OPEN_BUTTON_TOKEN); sent as "Authorization: Bearer <token>".
        // Leave empty for an unauthenticated instance. Keep real values out of the tracked appsettings.json.
        public string AuthToken { get; set; }

        public string WorkflowTemplatePath { get; set; }
        public string InputVideoPath { get; set; }
        public string OutputDirectory { get; set; }
        public string JobRootPrefix { get; set; }

        // Model files the job should run with; verified against the instance before submitting.
        [Required]
        public string DitModel { get; set; }

        [Required]
        public string VaeModel { get; set; }

        [Range(1, 3600)]
        public int HttpTimeoutSeconds { get; set; }
    }
}
