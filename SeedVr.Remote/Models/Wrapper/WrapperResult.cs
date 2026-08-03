using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.Wrapper
{
    /// <summary>The wrapper's Result for POST /generate and GET /result: the request's identity, status and,
    /// once completed, its output file references. comfyui_response and timings stay unmodelled.</summary>
    public class WrapperResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        /// <summary>File references for the finished job; without an s3 config the wrapper returns these, not the bytes.</summary>
        [JsonPropertyName("output")]
        public List<WrapperOutputFile> Output { get; set; }
    }

    public class WrapperOutputFile
    {
        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        [JsonPropertyName("subfolder")]
        public string Subfolder { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("node_id")]
        public string NodeId { get; set; }

        [JsonPropertyName("output_type")]
        public string OutputType { get; set; }

        [JsonPropertyName("local_path")]
        public string LocalPath { get; set; }
    }
}
