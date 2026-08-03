using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.Ffprobe
{
    public class FfprobeOutput
    {
        [JsonPropertyName("streams")]
        public List<FfprobeStream> Streams { get; set; }
    }

    public class FfprobeStream
    {
        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("nb_frames")]
        public string FrameCount { get; set; }
    }
}
