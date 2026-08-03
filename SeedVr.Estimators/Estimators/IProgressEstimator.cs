using SeedVr.Estimators.Signals;

namespace SeedVr.Estimators.Estimators
{
    /// <summary>A single ETA approach. Stateful by design: each estimator IS a running model of the job, so Update folds the
    /// new sample into its state and returns the current estimate rather than staying pure.</summary>
    public interface IProgressEstimator
    {
        string Name { get; }
        EtaEstimate Update(ProgressSample sample);
    }

    /// <summary>An estimator's current answer: the remaining time and the implied total, or Empty before it has enough signal.</summary>
    public record EtaEstimate(bool IsAvailable, TimeSpan Remaining, TimeSpan EstimatedTotal)
    {
        public static EtaEstimate Empty { get; } = new EtaEstimate(false, TimeSpan.Zero, TimeSpan.Zero);

        /// <summary>An available estimate for a known total, deriving remaining from elapsed. A negative remaining means the run
        /// overshot the estimate, so it is reported as zero rather than a countdown past done.</summary>
        public static EtaEstimate FromTotal(TimeSpan estimatedTotal, TimeSpan elapsed)
        {
            var remaining = estimatedTotal - elapsed;
            var clampedRemaining = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
            return new EtaEstimate(true, clampedRemaining, estimatedTotal);
        }
    }
}
