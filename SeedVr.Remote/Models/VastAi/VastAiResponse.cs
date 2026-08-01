using System.Text.Json;
using System.Text.Json.Serialization;
using SeedVr.Core;

namespace SeedVr.Remote.Models.VastAi
{
    public class VastAiResponse
    {
        [JsonPropertyName("instances")]
        public List<VastAiInstance> Instances { get; set; }
    }

    public class VastAiInstance
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("public_ipaddr")]
        public string PublicIpAddress { get; set; }

        [JsonPropertyName("actual_status")]
        public string ActualStatus { get; set; }

        /// <summary>Null unless the instance is running.</summary>
        [JsonPropertyName("ports")]
        public VastAiPorts Ports { get; set; }

        /// <summary>The host port ComfyUI is published on, or null when the instance has not published it yet.</summary>
        public string GetComfyUiHostPort()
        {
            return Ports?.ComfyUi?.FirstOrDefault()?.HostPort;
        }

        /// <summary>The instance's ComfyUI base address, built from its public IP and published ComfyUI port.</summary>
        public string GetComfyUiAddress()
        {
            var hostPort = GetComfyUiHostPort();
            return $"http://{PublicIpAddress}:{hostPort}/";
        }
    }

    public class VastAiPorts
    {
        [JsonPropertyName(Constants.VastAi.ComfyUiContainerPort)]
        public List<VastAiPortBinding> ComfyUi { get; set; }

        /// <summary>Everything else the instance publishes, which tells an empty mapping apart from one without ComfyUI.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement> OtherPorts { get; set; }
    }

    public class VastAiPortBinding
    {
        [JsonPropertyName("HostPort")]
        public string HostPort { get; set; }
    }
}
