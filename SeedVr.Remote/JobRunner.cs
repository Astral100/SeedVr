namespace SeedVr.Remote
{
    /// <summary>Orchestrates one run: select a ready instance, then submit the job to it.</summary>
    public class JobRunner
    {
        private readonly InstanceSelector _instanceSelector;
        private readonly JobSubmitter _jobSubmitter;

        public JobRunner(InstanceSelector instanceSelector, JobSubmitter jobSubmitter)
        {
            _instanceSelector = instanceSelector;
            _jobSubmitter = jobSubmitter;
        }

        public async Task<bool> Run(CancellationToken cancellationToken = default)
        {
            var comfyUiAddress = await _instanceSelector.SelectComfyUiAddress(cancellationToken);
            if (comfyUiAddress == null)
            {
                return false;
            }

            // Raw and wrapper submit paths are both available; comment one and uncomment the other to switch.
            var submitted = await _jobSubmitter.SubmitRawJob(comfyUiAddress, cancellationToken);
            // var submitted = await _jobSubmitter.SubmitWrapperJob(comfyUiAddress, cancellationToken);

            return submitted;
        }
    }
}
