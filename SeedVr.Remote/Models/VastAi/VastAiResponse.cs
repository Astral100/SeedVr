using System.Text.Json;
using System.Text.Json.Serialization;
using SeedVr.Remote;

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

        /// <summary>The token the instance's Jupyter server authenticates with, reported by the account API.</summary>
        [JsonPropertyName("jupyter_token")]
        public string JupyterToken { get; set; }

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

        /// <summary>The host port the API wrapper is published on, or null when the instance does not publish it.</summary>
        public string GetWrapperHostPort()
        {
            return Ports?.Wrapper?.FirstOrDefault()?.HostPort;
        }

        /// <summary>The instance's API wrapper base address, built from its public IP and published wrapper port.</summary>
        public string GetWrapperAddress()
        {
            var hostPort = GetWrapperHostPort();
            return $"http://{PublicIpAddress}:{hostPort}/";
        }

        /// <summary>The host port Jupyter is published on, or null when the instance does not publish it.</summary>
        public string GetJupyterHostPort()
        {
            return Ports?.Jupyter?.FirstOrDefault()?.HostPort;
        }

        /// <summary>The instance's Jupyter base address; Vast.ai serves Jupyter over HTTPS with a self-signed certificate.</summary>
        public string GetJupyterAddress()
        {
            var hostPort = GetJupyterHostPort();
            return $"https://{PublicIpAddress}:{hostPort}/";
        }
    }

    public class VastAiPorts
    {
        [JsonPropertyName(Constants.VastAi.ComfyUiContainerPort)]
        public List<VastAiPortBinding> ComfyUi { get; set; }

        [JsonPropertyName(Constants.VastAi.WrapperContainerPort)]
        public List<VastAiPortBinding> Wrapper { get; set; }

        [JsonPropertyName(Constants.VastAi.JupyterContainerPort)]
        public List<VastAiPortBinding> Jupyter { get; set; }

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
