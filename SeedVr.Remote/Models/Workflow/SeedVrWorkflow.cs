using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.Workflow
{
    /// <summary>The SeedVR2 API workflow: ComfyUI keys each node by its id, so each node is a named property.</summary>
    public class SeedVrWorkflow
    {
        [JsonPropertyName("10")]
        public WorkflowNode<UpscalerInputs> Upscaler { get; set; }

        [JsonPropertyName("13")]
        public WorkflowNode<VaeLoaderInputs> VaeLoader { get; set; }

        [JsonPropertyName("14")]
        public WorkflowNode<DitLoaderInputs> DitLoader { get; set; }

        [JsonPropertyName("21")]
        public WorkflowNode<LoadVideoInputs> LoadVideo { get; set; }

        [JsonPropertyName("22")]
        public WorkflowNode<GetVideoComponentsInputs> GetVideoComponents { get; set; }

        [JsonPropertyName("23")]
        public WorkflowNode<SaveVideoInputs> SaveVideo { get; set; }

        [JsonPropertyName("24")]
        public WorkflowNode<CreateVideoInputs> CreateVideo { get; set; }
    }

    /// <summary>One node: its typed inputs, its ComfyUI class and the editor metadata.</summary>
    public class WorkflowNode<TInputs>
    {
        [JsonPropertyName("inputs")]
        public TInputs Inputs { get; set; }

        [JsonPropertyName("class_type")]
        public string ClassType { get; set; }

        [JsonPropertyName("_meta")]
        public NodeMeta Meta { get; set; }
    }

    public class NodeMeta
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }
    }
}
