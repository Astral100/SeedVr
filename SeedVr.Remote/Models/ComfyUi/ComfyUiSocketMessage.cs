using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.ComfyUi
{
    /// <summary>A frame from the ComfyUI progress WebSocket. Only the fields the progress monitor reads are modelled.</summary>
    public class ComfyUiSocketMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("data")]
        public ComfyUiSocketData Data { get; set; }
    }

    public class ComfyUiSocketData
    {
        /// <summary>Tags the frame with the job it belongs to, so a shared socket's other jobs are ignored.</summary>
        [JsonPropertyName("prompt_id")]
        public string PromptId { get; set; }

        /// <summary>The node being executed; null on an "executing" frame marks the end of the run.</summary>
        [JsonPropertyName("node")]
        public string Node { get; set; }

        [JsonPropertyName("value")]
        public int? Value { get; set; }

        [JsonPropertyName("max")]
        public int? Max { get; set; }
    }
}
