using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.ComfyUi
{
    /// <summary>The response of POST /prompt, identifying the queued job.</summary>
    public class ComfyUiSubmitResult
    {
        [JsonPropertyName("prompt_id")]
        public string PromptId { get; set; }

        [JsonPropertyName("number")]
        public int Number { get; set; }

        // Keyed by node id, and populated only when ComfyUI rejected a node in the submitted workflow.
        [JsonPropertyName("node_errors")]
        public Dictionary<string, NodeError> NodeErrors { get; set; }
    }

    public class NodeError
    {
        [JsonPropertyName("class_type")]
        public string ClassType { get; set; }

        [JsonPropertyName("errors")]
        public List<NodeErrorDetail> Errors { get; set; }
    }

    public class NodeErrorDetail
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("details")]
        public string Details { get; set; }
    }
}
