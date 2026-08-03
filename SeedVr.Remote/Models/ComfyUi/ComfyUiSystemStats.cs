using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.ComfyUi
{
    public class ComfyUiSystemStats
    {
        [JsonPropertyName("system")]
        public ComfyUiSystemInfo System { get; set; } = new ComfyUiSystemInfo();

        [JsonPropertyName("devices")]
        public List<ComfyUiDeviceInfo> Devices { get; set; } = [];
    }

    public class ComfyUiSystemInfo
    {
        [JsonPropertyName("os")]
        public string Os { get; set; } = "";

        [JsonPropertyName("ram_total")]
        public long RamTotal { get; set; }

        [JsonPropertyName("ram_free")]
        public long RamFree { get; set; }

        [JsonPropertyName("comfyui_version")]
        public string ComfyUiVersion { get; set; } = "";

        [JsonPropertyName("python_version")]
        public string PythonVersion { get; set; } = "";

        [JsonPropertyName("pytorch_version")]
        public string PytorchVersion { get; set; } = "";
    }

    public class ComfyUiDeviceInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("vram_total")]
        public long VramTotal { get; set; }

        [JsonPropertyName("vram_free")]
        public long VramFree { get; set; }
    }
}
