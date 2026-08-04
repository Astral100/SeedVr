using SeedVr.Estimators.Estimators;
using SeedVr.Estimators.Jobs;
using SeedVr.Estimators.Signals;

namespace SeedVr.Estimators.Tests
{
    public class PhaseBatchEstimatorTests
    {
        [Fact]
        public void Update_when_no_signal_has_arrived_then_the_prior_total_matches_the_measured_model()
        {
            // Arrange
            var sut = new PhaseBatchEstimator(GetWorkload(62, 33));

            // Act
            var estimate = sut.Update(new ProgressSample(TimeSpan.Zero, null, null));

            // Assert
            // Four setup + batch priors total 199s processing, followed by 15.5s reference finalization.
            Assert.True(estimate.IsAvailable);
            AssertSecondsCloseTo(214.5, estimate.EstimatedTotal);
        }

        [Fact]
        public void Update_when_the_second_batch_starts_then_refines_the_current_phase()
        {
            // Arrange
            // 132 frames at a batch size of 33 is four batches, long enough for a phase to refine mid-way.
            var sut = new PhaseBatchEstimator(GetWorkload(132, 33));

            // Act
            sut.Update(GetPhaseStartSample(ProgressPhase.VaeDecoding, 100));
            sut.Update(GetBatchSample(ProgressPhase.VaeDecoding, 1, 101));
            var beforeSecond = sut.Update(new ProgressSample(TimeSpan.FromSeconds(149), null, null));
            var afterSecond = sut.Update(GetBatchSample(ProgressPhase.VaeDecoding, 2, 150));
            var afterThird = sut.Update(GetBatchSample(ProgressPhase.VaeDecoding, 3, 260));

            // Assert
            Assert.True(afterSecond.EstimatedTotal > beforeSecond.EstimatedTotal);
            Assert.True(afterThird.EstimatedTotal > afterSecond.EstimatedTotal);
        }

        [Fact]
        public void Update_when_elapsed_grows_within_a_phase_then_remaining_shrinks()
        {
            // Arrange
            var sut = new PhaseBatchEstimator(GetWorkload(62, 33));

            // Act
            sut.Update(GetPhaseStartSample(ProgressPhase.VaeDecoding, 74));
            var early = sut.Update(new ProgressSample(TimeSpan.FromSeconds(90), 60, null));
            var later = sut.Update(new ProgressSample(TimeSpan.FromSeconds(120), 80, null));

            // Assert
            Assert.True(later.Remaining < early.Remaining);
        }

        [Fact]
        public void Update_when_a_completed_phase_ran_slow_then_scales_the_unseen_phase_priors()
        {
            // Arrange
            var sut = new PhaseBatchEstimator(GetWorkload(62, 33));

            // Act
            sut.Update(GetPhaseStartSample(ProgressPhase.Encoding, 0));
            sut.Update(GetBatchSample(ProgressPhase.Encoding, 1, 2));
            sut.Update(GetBatchSample(ProgressPhase.Encoding, 2, 26));
            var estimate = sut.Update(GetPhaseStartSample(ProgressPhase.DiTUpscaling, 50));

            // Assert
            Assert.True(estimate.EstimatedTotal.TotalSeconds > 220, $"unseen phases were not scaled for the slow run: {estimate.EstimatedTotal.TotalSeconds:F1}s");
        }

        [Fact]
        public void Update_when_the_second_batch_is_an_outlier_then_clips_and_confidence_weights_it()
        {
            // Arrange
            var sut = new PhaseBatchEstimator(GetWorkload(62, 33));

            // Act
            sut.Update(GetPhaseStartSample(ProgressPhase.VaeDecoding, 100));
            sut.Update(GetBatchSample(ProgressPhase.VaeDecoding, 1, 101));
            var beforeSecond = sut.Update(new ProgressSample(TimeSpan.FromSeconds(179), null, null));
            var afterSecond = sut.Update(GetBatchSample(ProgressPhase.VaeDecoding, 2, 180));

            // Assert
            // The raw outlier would move the two-batch total by ~62s; clipping and confidence-weighting must shrink that hard.
            var jump = afterSecond.EstimatedTotal.TotalSeconds - beforeSecond.EstimatedTotal.TotalSeconds;
            Assert.True(jump < 20, $"one completed batch moved the phase total by {jump:F1}s");
        }

        [Fact]
        public void Update_when_a_signal_time_is_given_then_removes_the_log_polling_delay_from_phase_timing()
        {
            // Arrange
            var baseline = new PhaseBatchEstimator(GetWorkload(62, 33));
            var sut = new PhaseBatchEstimator(GetWorkload(62, 33));

            // Act
            baseline.Update(GetPhaseStartSample(ProgressPhase.VaeDecoding, 100));
            baseline.Update(GetBatchSample(ProgressPhase.VaeDecoding, 1, 110));
            var baselineEstimate = baseline.Update(GetBatchSample(ProgressPhase.VaeDecoding, 2, 150));

            sut.Update(GetPhaseSignalSample(ProgressPhase.VaeDecoding, 1, 0, 100, 80));
            sut.Update(GetPhaseSignalSample(ProgressPhase.VaeDecoding, 1, 2, 110, 90));
            var sourceEstimate = sut.Update(GetPhaseSignalSample(ProgressPhase.VaeDecoding, 2, 2, 150, 130));

            // Assert
            AssertSecondsCloseTo(baselineEstimate.EstimatedTotal.TotalSeconds - 20, sourceEstimate.EstimatedTotal);
        }

        private JobWorkload GetWorkload(int frameCount, int batchSize)
        {
            var workload = JobWorkloadCalculator.FromContext(new JobProgressContext(frameCount, batchSize));
            return workload;
        }

        private ProgressSample GetPhaseStartSample(ProgressPhase phase, double elapsedSeconds)
        {
            var phaseBatch = new PhaseBatchEvent(phase, 1, 0);
            return new ProgressSample(TimeSpan.FromSeconds(elapsedSeconds), null, phaseBatch);
        }

        private ProgressSample GetBatchSample(ProgressPhase phase, int batchIndex, double elapsedSeconds)
        {
            var phaseBatch = new PhaseBatchEvent(phase, batchIndex, 4);
            return new ProgressSample(TimeSpan.FromSeconds(elapsedSeconds), null, phaseBatch);
        }

        private ProgressSample GetPhaseSignalSample(ProgressPhase phase, int batchIndex, int batchCount, double elapsedSeconds, double signalElapsedSeconds)
        {
            var phaseBatch = new PhaseBatchEvent(phase, batchIndex, batchCount);
            return new ProgressSample(TimeSpan.FromSeconds(elapsedSeconds), null, phaseBatch, TimeSpan.FromSeconds(signalElapsedSeconds));
        }

        private void AssertSecondsCloseTo(double expectedSeconds, TimeSpan actual)
        {
            Assert.True(Math.Abs(actual.TotalSeconds - expectedSeconds) < 0.5, $"expected ~{expectedSeconds}s but got {actual.TotalSeconds:F1}s");
        }
    }
}
