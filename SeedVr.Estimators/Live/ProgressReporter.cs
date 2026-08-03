using SeedVr.Estimators.Estimators;
using SeedVr.Estimators.Scoring;
using SeedVr.Estimators.Tracing;
using SeedVr.Logger;

namespace SeedVr.Estimators.Live
{
    /// <summary>Formats and throttles the human-readable progress and completion output for a tracked job. Holds the reporting
    /// cadence state so the tracker is left with signal coordination and trace recording.</summary>
    public class ProgressReporter
    {
        private double? _lastReportedPercent;

        /// <summary>Reports progress at the configured percent interval, and always at 100%, formatting elapsed, remaining and total.</summary>
        public void ReportProgress(double percent, TimeSpan elapsed, EtaEstimate estimate)
        {
            var shouldReport = _lastReportedPercent == null || percent >= 100 || percent >= _lastReportedPercent + Constants.ProgressLogPercentInterval;
            if (!shouldReport)
            {
                return;
            }

            _lastReportedPercent = percent;
            if (estimate.IsAvailable)
            {
                Log.Information("Progress {Percent:F0}% | elapsed {Elapsed} | remaining {Remaining} | estimated total {Total}", [percent, FormatDuration(elapsed), FormatDuration(estimate.Remaining), FormatDuration(estimate.EstimatedTotal)]);
                return;
            }

            Log.Information("Progress {Percent:F0}% | elapsed {Elapsed} | ETA unavailable", [percent, FormatDuration(elapsed)]);
        }

        /// <summary>Reports the completion summary: total elapsed, its processing/finalization split and the offline accuracy score.</summary>
        public void ReportCompletion(EstimatorRunTrace trace, EstimatorScore score)
        {
            var totalSeconds = trace.ActualTotalSeconds ?? 0;
            var processingSeconds = trace.ProcessingCompletedSeconds ?? totalSeconds;
            var finalizationSeconds = Math.Max(0, totalSeconds - processingSeconds);
            var message = $"Completion metrics:{Environment.NewLine}" +
                $"  Status: completed{Environment.NewLine}" +
                $"  Total elapsed: {FormatDuration(TimeSpan.FromSeconds(totalSeconds))}{Environment.NewLine}" +
                $"  Processing: {FormatDuration(TimeSpan.FromSeconds(processingSeconds))}{Environment.NewLine}" +
                $"  Finalization: {FormatDuration(TimeSpan.FromSeconds(finalizationSeconds))}{Environment.NewLine}" +
                EstimatorScoreReport.DescribeAccuracy(score);
            Log.Information(message);
        }

        private string FormatDuration(TimeSpan value)
        {
            if (value.TotalHours >= 1)
            {
                return $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}";
            }

            return $"{(int)value.TotalMinutes:00}:{value.Seconds:00}";
        }
    }
}
