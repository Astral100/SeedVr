using System.Text.Json;
using System.Text.Json.Serialization;
using SeedVr.Estimators.Jobs;
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

        /// <summary>The physical machine behind the instance; the stable key for anything learned about a host.</summary>
        [JsonPropertyName("machine_id")]
        public int MachineId { get; set; }

        [JsonPropertyName("gpu_name")]
        public string GpuName { get; set; }

        [JsonPropertyName("cpu_name")]
        public string CpuName { get; set; }

        /// <summary>The cores actually allotted to this rental; the machine's full core count is shared between tenants.</summary>
        [JsonPropertyName("cpu_cores_effective")]
        public double CpuCoresEffective { get; set; }

        [JsonPropertyName("cpu_cores")]
        public int CpuCoresTotal { get; set; }

        /// <summary>The whole machine's RAM; the rental's share is the cores-proportional slice of it.</summary>
        [JsonPropertyName("cpu_ram")]
        public double CpuRamMb { get; set; }

        [JsonPropertyName("disk_bw")]
        public double DiskBandwidthMbps { get; set; }

        [JsonPropertyName("pcie_bw")]
        public double PcieBandwidthGbps { get; set; }

        /// <summary>Vast.ai's measured deep-learning benchmark score for the machine's GPU.</summary>
        [JsonPropertyName("dlperf")]
        public double Dlperf { get; set; }

        /// <summary>The machine fingerprint the estimators record with each run. RAM is the rental's allotment, not the machine
        /// total: the API only reports the whole machine's RAM, and the instance's share is cores-proportional (matching the
        /// instance card's number).</summary>
        public HostProfile GetHostProfile()
        {
            var allottedRamMb = CpuCoresTotal > 0 ? CpuRamMb * CpuCoresEffective / CpuCoresTotal : CpuRamMb;
            return new HostProfile(MachineId, GpuName, CpuName, CpuCoresEffective, CpuCoresTotal, allottedRamMb / 1024, DiskBandwidthMbps, PcieBandwidthGbps, Dlperf);
        }

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
