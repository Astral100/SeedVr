namespace SeedVr.Remote
{
    /// <summary>Orchestrates one run: select a ready instance, then submit the job to it.</summary>
    public class JobOrchestrator
    {
        private readonly InstanceSelector _instanceSelector;
        private readonly JobRunner _jobRunner;

        public JobOrchestrator(InstanceSelector instanceSelector, JobRunner jobRunner)
        {
            _instanceSelector = instanceSelector;
            _jobRunner = jobRunner;
        }

        public async Task<bool> StartJob(CancellationToken cancellationToken = default)
        {
            var instance = await _instanceSelector.GetFirstAvailableInstance(cancellationToken);
            if (instance == null)
            {
                return false;
            }

            var comfyUiAddress = instance.GetComfyUiAddress();
            var jupyterAddress = instance.GetJupyterAddress();
            var hostProfile = instance.GetHostProfile();

            // Raw and wrapper submit paths are both available; comment one and uncomment the other to switch.
            var success = await _jobRunner.StartRawJob(comfyUiAddress, jupyterAddress, instance.JupyterToken, hostProfile, cancellationToken);
            // var success = await _jobRunner.StartWrapperJob(comfyUiAddress, instance.GetWrapperAddress(), jupyterAddress, instance.JupyterToken, hostProfile, cancellationToken);

            return success;
        }
    }
}
