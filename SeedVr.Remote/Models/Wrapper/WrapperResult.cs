using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.Wrapper
{
    /// <summary>The wrapper's Result for POST /generate: enough to identify and poll the request.
    /// The finished outputs (output, comfyui_response, timings) are modelled when milestone 5 consumes them.</summary>
    public class WrapperResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }
    }
}
