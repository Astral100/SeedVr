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
            var comfyUiAddress = await _instanceSelector.GetFirstAvailableInstanceAddress(cancellationToken);
            if (comfyUiAddress == null)
            {
                return false;
            }

            // Raw and wrapper submit paths are both available; comment one and uncomment the other to switch.
            var success = await _jobRunner.StartRawJob(comfyUiAddress, cancellationToken);
            // var success = await _jobRunner.StartWrapperJob(comfyUiAddress, cancellationToken);

            return success;
        }
    }
}
