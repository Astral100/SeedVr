using System.Text.Json;
using Microsoft.Extensions.Options;
using SeedVr.Core;
using SeedVr.Remote.Models.Workflow;

namespace SeedVr.Remote
{
    /// <summary>Loads the SeedVR2 API workflow into a typed object and patches the per-job values onto the copy.</summary>
    public class WorkflowBuilder
    {
        private readonly AppSettings _appSettings;

        public WorkflowBuilder(IOptions<AppSettings> appSettingsOptions)
        {
            _appSettings = appSettingsOptions.Value;
        }

        /// <summary>A patched copy of the workflow for one job. The template on disk is left untouched.</summary>
        public SeedVrWorkflow Build(string uploadedVideoFileName, string outputFilenamePrefix)
        {
            var templatePath = Path.Combine(AppContext.BaseDirectory, Constants.Paths.WorkflowTemplate);
            var templateJson = File.ReadAllText(templatePath);

            var workflow = JsonSerializer.Deserialize<SeedVrWorkflow>(templateJson);
            if (workflow == null)
            {
                throw new InvalidOperationException($"The workflow template at '{templatePath}' parsed to null.");
            }

            workflow.LoadVideo.Inputs.File = uploadedVideoFileName;
            workflow.SaveVideo.Inputs.FilenamePrefix = outputFilenamePrefix;
            workflow.DitLoader.Inputs.Model = _appSettings.DitModel;
            workflow.VaeLoader.Inputs.Model = _appSettings.VaeModel;

            return workflow;
        }
    }
}
