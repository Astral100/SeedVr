using SeedVr.Estimators.Jobs;
using SeedVr.Estimators.Signals;

namespace SeedVr.Estimators.Estimators
{
    /// <summary>Extrapolates from a double-exponentially-smoothed rate of percent-per-second, so the ETA tracks the current speed
    /// rather than the whole-run average. Needs only the percent stream; a good fallback when frame count and stdout are absent.
    /// Double smoothing (DEMA = 2*EMA - EMA-of-EMA) cuts the lag a single EMA carries into each phase-rate change.</summary>
    public class PercentDemaEstimator : IProgressEstimator
    {
        private readonly double _finalizationSeconds;
        private double? _previousPercent;
        private TimeSpan _previousElapsed;
        private double _emaRate;
        private double _doubleEmaRate;
        private bool _seeded;
        private EtaEstimate _lastEstimate = EtaEstimate.Empty;

        public PercentDemaEstimator(JobWorkload workload)
        {
            _finalizationSeconds = workload.FinalizationSeconds;
        }

        public string Name { get; } = "percent-dema";

        public EtaEstimate Update(ProgressSample sample)
        {
            if (sample.Percent == null)
            {
                return _lastEstimate;
            }

            var percent = sample.Percent.Value;
            if (_previousPercent == null)
            {
                _previousPercent = percent;
                _previousElapsed = sample.Elapsed;
                return _lastEstimate;
            }

            var deltaPercent = percent - _previousPercent.Value;
            var deltaSeconds = (sample.Elapsed - _previousElapsed).TotalSeconds;

            // Only a real advance carries a fresh rate. Duplicate or keepalive frames leave the anchor where it is, so the next
            // advance measures over the true interval rather than spiking off a fractional gap.
            if (deltaPercent <= 0 || deltaSeconds <= 0)
            {
                return _lastEstimate;
            }

            _previousPercent = percent;
            _previousElapsed = sample.Elapsed;
            FoldInRate(deltaPercent / deltaSeconds);

            return BuildEstimate(percent, sample.Elapsed);
        }

        private void FoldInRate(double instantRate)
        {
            if (!_seeded)
            {
                _emaRate = instantRate;
                _doubleEmaRate = instantRate;
                _seeded = true;
                return;
            }

            var minimumRate = _emaRate * Constants.DemaMinimumRateFactor;
            var maximumRate = _emaRate * Constants.DemaMaximumRateFactor;
            var clippedRate = Math.Clamp(instantRate, minimumRate, maximumRate);
            var alpha = Constants.DemaAlpha;
            _emaRate = alpha * clippedRate + (1 - alpha) * _emaRate;
            _doubleEmaRate = alpha * _emaRate + (1 - alpha) * _doubleEmaRate;
        }

        private EtaEstimate BuildEstimate(double percent, TimeSpan elapsed)
        {
            if (!_seeded)
            {
                return _lastEstimate;
            }

            var demaRate = 2 * _emaRate - _doubleEmaRate;
            if (demaRate <= 0)
            {
                return _lastEstimate;
            }

            var processingRemainingSeconds = (100.0 - percent) / demaRate;
            var remainingSeconds = processingRemainingSeconds + _finalizationSeconds;
            var total = elapsed + TimeSpan.FromSeconds(remainingSeconds);
            _lastEstimate = EtaEstimate.FromTotal(total, elapsed);
            return _lastEstimate;
        }
    }
}
