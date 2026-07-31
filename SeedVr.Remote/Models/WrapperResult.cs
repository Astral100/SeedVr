using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models
{
    /// <summary>The response of the wrapper's POST /generate, GET /result/{id} and /generate/sync.</summary>
    public class WrapperResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        // The finished outputs; empty until the job completes. Shape is consumed when milestone 5 downloads.
        [JsonPropertyName("output")]
        public JsonArray Output { get; set; }

        [JsonPropertyName("comfyui_response")]
        public JsonObject ComfyUiResponse { get; set; }

        [JsonPropertyName("timings")]
        public JsonObject Timings { get; set; }
    }
}
