using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.ComfyUi
{
    /// <summary>One prompt's entry in GET /history for its prompt id.</summary>
    public class ComfyUiHistoryEntry
    {
        [JsonPropertyName("status")]
        public ComfyUiHistoryStatus Status { get; set; }

        /// <summary>Finished files keyed by node id; the SaveVideo node carries the upscaled video under images.</summary>
        [JsonPropertyName("outputs")]
        public Dictionary<string, ComfyUiNodeOutput> Outputs { get; set; }
    }

    public class ComfyUiNodeOutput
    {
        [JsonPropertyName("images")]
        public List<ComfyUiOutputFile> Images { get; set; }
    }

    /// <summary>One finished file reference. ComfyUI appends its own counter and extension to the filename, so it is used verbatim.</summary>
    public class ComfyUiOutputFile
    {
        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        [JsonPropertyName("subfolder")]
        public string Subfolder { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }
    }

    public class ComfyUiHistoryStatus
    {
        [JsonPropertyName("status_str")]
        public string StatusStr { get; set; }

        /// <summary>True once the job has finished, whether it succeeded or errored.</summary>
        [JsonPropertyName("completed")]
        public bool Completed { get; set; }
    }
}
