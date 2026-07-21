using System.Text.Json.Serialization;
using SeedVr.Core;

namespace SeedVr.Remote.Models
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
    }

    public class VastAiPorts
    {
        [JsonPropertyName(Constants.VastAi.ComfyUiContainerPort)]
        public List<VastAiPortBinding> ComfyUi { get; set; }
    }

    public class VastAiPortBinding
    {
        [JsonPropertyName("HostPort")]
        public string HostPort { get; set; }
    }
}
