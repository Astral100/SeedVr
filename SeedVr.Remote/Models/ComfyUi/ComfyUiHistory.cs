using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.ComfyUi
{
    /// <summary>One prompt's entry in GET /history/&lt;prompt_id&gt;. Its outputs are modelled when milestone 5 downloads them.</summary>
    public class ComfyUiHistoryEntry
    {
        [JsonPropertyName("status")]
        public ComfyUiHistoryStatus Status { get; set; }
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
