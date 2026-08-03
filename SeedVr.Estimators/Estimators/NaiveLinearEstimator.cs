using SeedVr.Estimators.Jobs;
using SeedVr.Estimators.Signals;

namespace SeedVr.Estimators.Estimators
{
    /// <summary>The baseline every other approach must beat: assume progress is linear in time, so total = elapsed / fractionDone.
    /// Reads only the percent, holds no model of the run.</summary>
    public class NaiveLinearEstimator : IProgressEstimator
    {
        private readonly double _finalizationSeconds;
        private EtaEstimate _lastEstimate = EtaEstimate.Empty;

        public NaiveLinearEstimator(JobWorkload workload)
        {
            _finalizationSeconds = workload.FinalizationSeconds;
        }

        public string Name { get; } = "naive-linear";

        public EtaEstimate Update(ProgressSample sample)
        {
            if (sample.Percent == null || sample.Percent <= 0)
            {
                return _lastEstimate;
            }

            var fraction = sample.Percent.Value / 100.0;
            var processingSeconds = sample.Elapsed.TotalSeconds / fraction;
            var totalSeconds = processingSeconds + _finalizationSeconds;
            var total = TimeSpan.FromSeconds(totalSeconds);
            _lastEstimate = EtaEstimate.FromTotal(total, sample.Elapsed);
            return _lastEstimate;
        }
    }
}
