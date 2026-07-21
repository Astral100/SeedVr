using System.ComponentModel.DataAnnotations;

namespace SeedVr.Core
{
    public class AppSettings
    {
        public const string ConfigurationSection = "AppSettings";

        // Vast.ai account API key (console.vast.ai/manage-keys). The ComfyUI address is resolved from
        // the instance's current port mapping, because Vast.ai reassigns the external port on every start.
        [Required]
        public string VastAiApiKey { get; set; }

        [Range(1, int.MaxValue)]
        public int VastAiInstanceId { get; set; }

        // Vast.ai instance portal token (OPEN_BUTTON_TOKEN); sent as "Authorization: Bearer <token>".
        // Leave empty for an unauthenticated instance. Keep real values out of the tracked appsettings.json.
        public string AuthToken { get; set; }

        // The video to upscale.
        public string InputVideoPath { get; set; }

        // Model files the job should run with; verified against the instance before submitting.
        [Required]
        public string DitModel { get; set; }

        [Required]
        public string VaeModel { get; set; }

        [Range(1, 3600)]
        public int HttpTimeoutSeconds { get; set; }
    }
}
