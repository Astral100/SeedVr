using SeedVr.Estimators.Jobs;
using SeedVr.Estimators.Signals;

namespace SeedVr.Estimators.Estimators
{
    /// <summary>Starts from the phase/batch prior, then progressively blends in the live percent-implied completion time. The
    /// phase model supplies workload structure and self-correction; the percent model adapts to this host's actual speed.</summary>
    public class AdaptiveHybridEstimator : IProgressEstimator
    {
        private readonly PhaseBatchEstimator _phaseEstimator;
        private readonly NaiveLinearEstimator _naiveEstimator;
        private readonly PercentDemaEstimator _demaEstimator;
        private double? _latestPercent;

        public AdaptiveHybridEstimator(JobWorkload workload)
        {
            _phaseEstimator = new PhaseBatchEstimator(workload);
            _naiveEstimator = new NaiveLinearEstimator(workload);
            _demaEstimator = new PercentDemaEstimator(workload);
        }

        /// <summary>Builds the estimator for a job context, deriving its workload once.</summary>
        public static AdaptiveHybridEstimator FromContext(JobProgressContext context)
        {
            var workload = JobWorkloadCalculator.FromContext(context);
            return new AdaptiveHybridEstimator(workload);
        }

        public string Name { get; } = "adaptive-hybrid";

        public EtaEstimate Update(ProgressSample sample)
        {
            var phaseEstimate = _phaseEstimator.Update(sample);
            var naiveEstimate = _naiveEstimator.Update(sample);
            var demaEstimate = _demaEstimator.Update(sample);
            if (sample.Percent != null && sample.Percent > 0)
            {
                _latestPercent = sample.Percent;
            }

            if (!phaseEstimate.IsAvailable)
            {
                return demaEstimate.IsAvailable ? demaEstimate : naiveEstimate;
            }

            if (!naiveEstimate.IsAvailable || _latestPercent == null)
            {
                return phaseEstimate;
            }

            var liveTotalSeconds = naiveEstimate.EstimatedTotal.TotalSeconds;
            if (demaEstimate.IsAvailable)
            {
                liveTotalSeconds = (liveTotalSeconds + demaEstimate.EstimatedTotal.TotalSeconds) / 2;
            }

            var phaseTotalSeconds = phaseEstimate.EstimatedTotal.TotalSeconds;
            liveTotalSeconds = Math.Clamp(liveTotalSeconds, phaseTotalSeconds - Constants.HybridMaximumDeviationSeconds, phaseTotalSeconds + Constants.HybridMaximumDeviationSeconds);
            var progress = _latestPercent.Value / 100.0;
            var liveWeight = Constants.HybridMinimumLiveWeight + progress * (Constants.HybridMaximumLiveWeight - Constants.HybridMinimumLiveWeight);
            var totalSeconds = (1 - liveWeight) * phaseTotalSeconds + liveWeight * liveTotalSeconds;
            var total = TimeSpan.FromSeconds(totalSeconds);
            return EtaEstimate.FromTotal(total, sample.Elapsed);
        }
    }
}
