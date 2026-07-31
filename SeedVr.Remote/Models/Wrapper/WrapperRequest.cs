using System.Text.Json.Serialization;
using SeedVr.Remote.Models.Workflow;

namespace SeedVr.Remote.Models.Wrapper
{
    /// <summary>The body of the wrapper's POST /generate.</summary>
    public class WrapperRequest
    {
        [JsonPropertyName("input")]
        public WrapperInput Input { get; set; }
    }

    public class WrapperInput
    {
        [JsonPropertyName("workflow_json")]
        public SeedVrWorkflow WorkflowJson { get; set; }
    }
}
