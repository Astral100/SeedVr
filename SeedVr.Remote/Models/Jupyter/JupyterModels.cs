using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.Jupyter
{
    /// <summary>The empty body of a Jupyter create-terminal call.</summary>
    public class JupyterTerminalRequest
    {
    }

    /// <summary>A terminal session on the instance's Jupyter server, addressed by its name.</summary>
    public class JupyterTerminal
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
    }
}
