using System.Text.Json.Serialization;
using SeedVr.Remote.Models.Workflow;

namespace SeedVr.Remote.Models.ComfyUi
{
    /// <summary>The body of POST /prompt: the workflow, tagged with the client id its progress is broadcast under.</summary>
    public class ComfyUiPromptRequest
    {
        [JsonPropertyName("prompt")]
        public SeedVrWorkflow Prompt { get; set; }

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; }
    }
}
