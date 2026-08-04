using System.ComponentModel.DataAnnotations;

namespace SeedVr.Core
{
    public class AppSettings
    {
        public const string ConfigurationSection = "AppSettings";

        // Both secrets live in user secrets, not in any appsettings file: dotnet user-secrets set
        // "AppSettings:VastAiApiKey" "<value>". They are read automatically in the Development environment.

        // Vast.ai account API key (console.vast.ai/manage-keys). The ComfyUI address is resolved from
        // the instance's current port mapping, because Vast.ai reassigns the external port on every start.
        [Required]
        public string VastAiApiKey { get; set; }

        // The instance's WEB_PASSWORD, set as a template env var when the instance is created; sent as
        // "Authorization: Bearer <token>" to the Caddy proxy that fronts ComfyUI. Unlike OPEN_BUTTON_TOKEN
        // it is a value you choose and stays stable across restarts.
        [Required]
        public string AuthToken { get; set; }

        // The video to upscale.
        [Required]
        public string InputVideoPath { get; set; }

        // Model files the job should run with; verified against the instance before submitting.
        [Required]
        public string DitModel { get; set; }

        [Required]
        public string VaeModel { get; set; }

        [Range(1, 3600)]
        public int HttpTimeoutSeconds { get; set; }

        [Range(1, 3600)]
        public int TransferIdleTimeoutSeconds { get; set; }

        // A cancelled job can leave GPU memory latched. Below this free fraction the instance is refused as
        // not ready, and the post-cancellation recovery restarts the ComfyUI process to release the memory.
        // The minimum excludes the unset default of 0, which would silently disable both protections.
        [Range(0.05, 1.0)]
        public double MinimumFreeVramFraction { get; set; }
    }
}
