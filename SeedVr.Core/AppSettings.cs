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

        // The instance's WEB_PASSWORD, set as a template env var when the instance is created; sent as
        // "Authorization: Bearer <token>" to the Caddy proxy that fronts ComfyUI. Unlike OPEN_BUTTON_TOKEN
        // it is a value you choose and stays stable across restarts. Keep the real value in
        // appsettings.Development.json, not the tracked appsettings.json.
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
