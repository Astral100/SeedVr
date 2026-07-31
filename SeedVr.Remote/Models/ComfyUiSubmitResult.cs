using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models
{
    /// <summary>The response of POST /prompt, identifying the queued job.</summary>
    public class ComfyUiSubmitResult
    {
        [JsonPropertyName("prompt_id")]
        public string PromptId { get; set; }

        [JsonPropertyName("number")]
        public int Number { get; set; }

        // Present and populated only when ComfyUI rejected a node in the submitted workflow.
        [JsonPropertyName("node_errors")]
        public JsonObject NodeErrors { get; set; }
    }
}
