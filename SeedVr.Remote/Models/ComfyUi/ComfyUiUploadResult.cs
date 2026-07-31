using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.ComfyUi
{
    /// <summary>The response of POST /upload/image, naming where ComfyUI stored the file.</summary>
    public class ComfyUiUploadResult
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("subfolder")]
        public string Subfolder { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }
    }
}
