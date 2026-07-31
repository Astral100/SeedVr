using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SeedVr.Core;

namespace SeedVr.Remote
{
    /// <summary>Clones the SeedVR2 API workflow and patches the per-job values onto the clone.</summary>
    public class SeedVrWorkflowBuilder
    {
        private readonly AppSettings _appSettings;

        public SeedVrWorkflowBuilder(IOptions<AppSettings> appSettingsOptions)
        {
            _appSettings = appSettingsOptions.Value;
        }

        /// <summary>A patched copy of the workflow for one job. The template on disk is left untouched.</summary>
        public JsonObject Build(string uploadedVideoFileName, string outputFilenamePrefix)
        {
            var templatePath = Path.Combine(AppContext.BaseDirectory, Constants.Paths.WorkflowTemplate);
            var templateJson = File.ReadAllText(templatePath);

            var parsed = JsonNode.Parse(templateJson);
            if (parsed == null)
            {
                throw new InvalidOperationException($"The workflow template at '{templatePath}' parsed to null.");
            }

            var workflow = parsed.AsObject();

            SetNodeInput(workflow, "21", "file", uploadedVideoFileName);
            SetNodeInput(workflow, "23", "filename_prefix", outputFilenamePrefix);
            SetNodeInput(workflow, "14", "model", _appSettings.DitModel);
            SetNodeInput(workflow, "13", "model", _appSettings.VaeModel);

            return workflow;
        }

        /// <summary>Sets one input on one node, failing loudly when the template no longer has it.</summary>
        private static void SetNodeInput(JsonObject workflow, string nodeId, string input, string value)
        {
            var inputs = workflow[nodeId]?["inputs"]?.AsObject();
            if (inputs == null)
            {
                throw new InvalidOperationException($"The workflow template has no inputs on node {nodeId}. Did the node IDs change?");
            }

            inputs[input] = value;
        }
    }
}
