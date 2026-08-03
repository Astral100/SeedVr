using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.ComfyUi
{
    /// <summary>GET /internal/logs/raw: the recent console buffer. Only the entry text and timestamp are modelled.</summary>
    public class ComfyUiLogs
    {
        [JsonPropertyName("entries")]
        public List<ComfyUiLogEntry> Entries { get; set; }
    }

    public class ComfyUiLogEntry
    {
        /// <summary>The entry's timestamp; used only to tell new lines from ones already seen.</summary>
        [JsonPropertyName("t")]
        [JsonConverter(typeof(ComfyUiLogTimestampConverter))]
        public DateTimeOffset Timestamp { get; set; }

        [JsonPropertyName("m")]
        public string Message { get; set; }
    }
}
