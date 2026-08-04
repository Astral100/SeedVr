using System.Diagnostics;
using SeedVr.Estimators.Estimators;
using SeedVr.Estimators.Jobs;
using SeedVr.Estimators.Scoring;
using SeedVr.Estimators.Signals;
using SeedVr.Estimators.Tracing;
using SeedVr.Logger;

namespace SeedVr.Estimators.Live
{
    /// <summary>Runs the adaptive estimator against live signals, records its ETA trace and owns the job clock. Presentation is
    /// delegated to a <see cref="ProgressReporter"/>, so the tracker is only signal coordination and trace recording.</summary>
    public class ProgressTracker
    {
        private IProgressEstimator _estimator;
        private readonly EstimatorRunTrace _trace;
        private readonly ProgressReporter _reporter;
        private readonly Stopwatch _clock;
        private readonly object _lock = new();
        private double _lastPercent;

        public ProgressTracker(JobProgressContext context, IProgressEstimator estimator)
        {
            _estimator = estimator;
            _trace = new EstimatorRunTrace { Context = context };
            _reporter = new ProgressReporter();

            // Not started here: the clock is anchored to the first live signal so queue wait before execution is excluded.
            _clock = new Stopwatch();
        }

        /// <summary>Builds the active progress tracker for a job, deriving the workload once for the estimator to consume.</summary>
        public static ProgressTracker CreateStandard(JobProgressContext context)
        {
            var estimator = AdaptiveHybridEstimator.FromContext(context);
            return new ProgressTracker(context, estimator);
        }

        /// <summary>Feeds a WebSocket percent reading (value/max scaled to 0-100) to the estimator and logs the result.</summary>
        public void RecordPercent(double percent)
        {
            // The socket and log poll feed the tracker from separate tasks, so serialize access to the stateful estimator.
            lock (_lock)
            {
                EnsureStarted();
                WarnIfPercentRegressed(percent);
                _lastPercent = percent;
                var elapsed = _clock.Elapsed;
                // The socket pushes progress in near-real-time, so a reading's signal (event) time is its arrival time. Polled log
                // lines instead back-date their own source timestamp, so every sample carries an explicit signal time on one basis.
                var sample = new ProgressSample(elapsed, percent, null, elapsed);
                RecordSample(sample);
                if (percent >= 100 && _trace.ProcessingCompletedSeconds == null)
                {
                    _trace.ProcessingCompletedSeconds = sample.Elapsed.TotalSeconds;
                }
            }
        }

        /// <summary>Feeds a parsed SeedVR2 phase/batch line to the estimator, carrying the last known percent.</summary>
        public void RecordPhaseBatch(PhaseBatchEvent phaseBatch, DateTimeOffset occurredAt)
        {
            lock (_lock)
            {
                EnsureStarted();
                var elapsed = _clock.Elapsed;
                var signalElapsed = GetSignalElapsed(elapsed, occurredAt);
                var sample = new ProgressSample(elapsed, null, phaseBatch, signalElapsed);
                RecordSample(sample);
            }
        }

        /// <summary>Back-dates a polled log line by its age so phase timing excludes the poll delay, clamped to [0, elapsed] so
        /// host/instance clock skew cannot push the signal into the future or before the run began.</summary>
        private TimeSpan GetSignalElapsed(TimeSpan elapsed, DateTimeOffset occurredAt)
        {
            var age = DateTimeOffset.UtcNow - occurredAt;
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            var signalElapsed = elapsed - age;
            return signalElapsed < TimeSpan.Zero ? TimeSpan.Zero : signalElapsed;
        }

        /// <summary>Supplies workload metadata reported by SeedVR2 when the local video container did not provide it.</summary>
        public void RecordVideoMetadata(VideoMetadata metadata)
        {
            lock (_lock)
            {
                if (HasVideoMetadata(_trace.Context) || metadata.FrameCount <= 0 || metadata.Width <= 0 || metadata.Height <= 0)
                {
                    return;
                }

                EnsureStarted();
                var currentContext = _trace.Context;
                var context = new JobProgressContext(metadata.FrameCount, currentContext.BatchSize, metadata.Width, metadata.Height, currentContext.TargetResolution, currentContext.Host);
                _trace.Context = context;
                _estimator = AdaptiveHybridEstimator.FromContext(context);
                WarmEstimatorFromHistory();
            }
        }

        /// <summary>Closes the trace against the observed remote completion time and reports its offline score.</summary>
        public EstimatorRunTrace Complete(bool succeeded)
        {
            lock (_lock)
            {
                EnsureStarted();
                _trace.ActualTotalSeconds = _clock.Elapsed.TotalSeconds;
                _trace.Succeeded = succeeded;

                if (succeeded)
                {
                    var score = EstimatorEvaluator.Score(_trace);
                    _reporter.ReportCompletion(_trace, score);
                }

                return _trace;
            }
        }

        /// <summary>Whether the context already carries the video metadata (frame count and dimensions), so a late report should not overwrite it.</summary>
        private bool HasVideoMetadata(JobProgressContext context)
        {
            return context.FrameCount > 0 && context.InputWidth > 0 && context.InputHeight > 0;
        }

        /// <summary>Starts the clock on the first signal, so elapsed is measured from execution rather than submission.</summary>
        private void EnsureStarted()
        {
            if (!_clock.IsRunning)
            {
                _clock.Start();
            }
        }

        /// <summary>A percent that goes backwards means ComfyUI reset the progress bar (e.g. a new node), which the percent-based estimators cannot model.</summary>
        private void WarnIfPercentRegressed(double percent)
        {
            if (percent + 0.01 < _lastPercent)
            {
                Log.Warning("Progress percent went backwards ({Previous:F1}% -> {Current:F1}%); a per-node progress reset would break the percent-based estimators.", [_lastPercent, percent]);
            }
        }

        private void RecordSample(ProgressSample sample)
        {
            var estimate = _estimator.Update(sample);
            if (sample.Percent != null)
            {
                _reporter.ReportProgress(sample.Percent.Value, sample.Elapsed, estimate);
            }

            var phaseBatch = sample.PhaseBatch;
            _trace.Samples.Add(new EstimatorTraceSample
            {
                ElapsedSeconds = sample.Elapsed.TotalSeconds,
                SignalElapsedSeconds = sample.SignalElapsed?.TotalSeconds,
                Percent = sample.Percent,
                Phase = phaseBatch?.Phase ?? ProgressPhase.Unknown,
                BatchIndex = phaseBatch?.BatchIndex,
                BatchCount = phaseBatch?.BatchCount,
                EstimatedTotalSeconds = estimate.IsAvailable ? estimate.EstimatedTotal.TotalSeconds : null
            });
        }

        /// <summary>Replays the recorded history into a freshly seeded estimator to rebuild its internal state after late metadata,
        /// leaving each sample's originally reported prediction intact so the trace stays a faithful record of what was shown live.</summary>
        private void WarmEstimatorFromHistory()
        {
            foreach (var traceSample in _trace.Samples)
            {
                _estimator.Update(traceSample.ToProgressSample());
            }
        }
    }
}
