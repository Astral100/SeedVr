using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models
{
    /// <summary>The response of GET /prompt, which ComfyUI uses to report its queue depth.</summary>
    public class ComfyUiPromptStatus
    {
        [JsonPropertyName("exec_info")]
        public ComfyUiExecInfo ExecInfo { get; set; }
    }

    public class ComfyUiExecInfo
    {
        /// <summary>Jobs queued plus the one running, so zero means the instance is free.</summary>
        [JsonPropertyName("queue_remaining")]
        public int? QueueRemaining { get; set; }
    }
}
