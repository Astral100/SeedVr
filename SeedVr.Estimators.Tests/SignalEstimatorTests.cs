using SeedVr.Estimators.Estimators;
using SeedVr.Estimators.Jobs;
using SeedVr.Estimators.Signals;

namespace SeedVr.Estimators.Tests
{
    public class SignalEstimatorTests
    {
        [Fact]
        public void NaiveLinear_when_a_percent_is_reported_then_extrapolates_linearly()
        {
            // Arrange
            var sut = new NaiveLinearEstimator(GetWorkload(62, 33));

            // Act
            var estimate = sut.Update(new ProgressSample(TimeSpan.FromSeconds(60), 30, null));

            // Assert
            AssertSecondsCloseTo(215.19, estimate.EstimatedTotal);
            AssertSecondsCloseTo(155.19, estimate.Remaining);
        }

        [Fact]
        public void PercentDema_when_the_rate_is_constant_then_extrapolates_exactly()
        {
            // Arrange
            var sut = new PercentDemaEstimator(GetWorkload(62, 33));

            // Act
            sut.Update(new ProgressSample(TimeSpan.FromSeconds(10), 10, null));
            sut.Update(new ProgressSample(TimeSpan.FromSeconds(20), 20, null));
            var estimate = sut.Update(new ProgressSample(TimeSpan.FromSeconds(30), 30, null));

            // Assert
            // A steady 1%/sec finishes processing at 100s, followed by the reference finalization allowance.
            AssertSecondsCloseTo(115.19, estimate.EstimatedTotal);
            AssertSecondsCloseTo(85.19, estimate.Remaining);
        }

        [Fact]
        public void PercentDema_when_frames_are_duplicated_then_ignores_them()
        {
            // Arrange
            var sut = new PercentDemaEstimator(GetWorkload(62, 33));

            // Act
            sut.Update(new ProgressSample(TimeSpan.FromSeconds(10), 10, null));
            sut.Update(new ProgressSample(TimeSpan.FromSeconds(20), 20, null));
            // Keepalive frames at the same percent must not move the anchor and spike the next advance.
            sut.Update(new ProgressSample(TimeSpan.FromSeconds(21), 20, null));
            sut.Update(new ProgressSample(TimeSpan.FromSeconds(39), 20, null));
            var estimate = sut.Update(new ProgressSample(TimeSpan.FromSeconds(40), 40, null));

            // Assert
            // Rate stays ~1%/sec (20% over the 20s from t20 to t40), so the finish holds at ~100s, not a spiked-low value.
            AssertSecondsCloseTo(115.19, estimate.EstimatedTotal);
        }

        [Fact]
        public void PhaseModel_when_the_frame_count_is_missing_then_is_unavailable()
        {
            // Arrange
            var sut = new PhaseBatchEstimator(GetWorkload(0, 33));

            // Act
            var phase = sut.Update(new ProgressSample(TimeSpan.Zero, null, null));

            // Assert
            Assert.False(phase.IsAvailable);
        }

        [Fact]
        public void Finalization_when_frame_count_and_area_vary_then_uses_fixed_overhead_and_scaled_cost()
        {
            // Arrange
            var reference = new JobProgressContext(62, 33, 704, 1280, 1080);
            var shortVideo = new JobProgressContext(32, 33, 704, 1280, 1080);
            var longVideo = new JobProgressContext(300, 33, 704, 1280, 1080);
            var squareVideo = new JobProgressContext(300, 33, 1080, 1080, 1080);

            // Act
            var referenceWorkload = JobWorkloadCalculator.FromContext(reference);
            var shortWorkload = JobWorkloadCalculator.FromContext(shortVideo);
            var longWorkload = JobWorkloadCalculator.FromContext(longVideo);
            var squareWorkload = JobWorkloadCalculator.FromContext(squareVideo);

            // Assert
            Assert.True(Math.Abs(shortWorkload.FinalizationSeconds - 9.5) < 0.2, $"expected ~9.5s but got {shortWorkload.FinalizationSeconds:F1}s");
            Assert.True(Math.Abs(referenceWorkload.FinalizationSeconds - 15.2) < 0.2, $"expected ~15.2s but got {referenceWorkload.FinalizationSeconds:F1}s");
            Assert.True(Math.Abs(longWorkload.FinalizationSeconds - 60.1) < 0.2, $"expected ~60.1s but got {longWorkload.FinalizationSeconds:F1}s");
            Assert.True(squareWorkload.FinalizationSeconds < longWorkload.FinalizationSeconds);
        }

        [Fact]
        public void AdaptiveHybrid_when_the_first_percent_arrives_then_brackets_host_speed()
        {
            // Arrange
            var sut = new AdaptiveHybridEstimator(GetWorkload(62, 33));

            // Act
            sut.Update(new ProgressSample(TimeSpan.Zero, null, new PhaseBatchEvent(ProgressPhase.Encoding, 1, 0)));
            var estimate = sut.Update(new ProgressSample(TimeSpan.FromSeconds(20), 10, null));

            // Assert
            AssertSecondsCloseTo(214.4, estimate.EstimatedTotal);
        }

        [Fact]
        public void AdaptiveHybrid_when_a_phase_only_event_follows_a_percent_then_retains_the_percent_evidence()
        {
            // Arrange
            var sut = new AdaptiveHybridEstimator(GetWorkload(62, 33));

            // Act
            sut.Update(new ProgressSample(TimeSpan.Zero, null, new PhaseBatchEvent(ProgressPhase.Encoding, 1, 0)));
            sut.Update(new ProgressSample(TimeSpan.FromSeconds(2), null, new PhaseBatchEvent(ProgressPhase.Encoding, 1, 2)));
            var afterPercent = sut.Update(new ProgressSample(TimeSpan.FromSeconds(25), 10, null));
            var afterBatch = sut.Update(new ProgressSample(TimeSpan.FromSeconds(26), null, new PhaseBatchEvent(ProgressPhase.Encoding, 1, 2)));

            // Assert
            var jump = Math.Abs(afterBatch.EstimatedTotal.TotalSeconds - afterPercent.EstimatedTotal.TotalSeconds);
            Assert.True(jump < 5, $"phase-only event discarded percent evidence and moved the total by {jump:F1}s");
        }

        [Fact]
        public void AdaptiveHybrid_when_an_early_percent_rate_spikes_then_bounds_the_total()
        {
            // Arrange
            var sut = new AdaptiveHybridEstimator(GetWorkload(62, 33));

            // Act
            sut.Update(new ProgressSample(TimeSpan.Zero, null, new PhaseBatchEvent(ProgressPhase.Encoding, 1, 0)));
            sut.Update(new ProgressSample(TimeSpan.FromSeconds(20), 10, null));
            sut.Update(new ProgressSample(TimeSpan.FromSeconds(40), 20, null));
            var estimate = sut.Update(new ProgressSample(TimeSpan.FromSeconds(80), 32, null));

            // Assert
            // The spike's naive-implied total is ~265s against a ~214s prior; the blend must hold the move to a fraction of that.
            Assert.True(estimate.EstimatedTotal.TotalSeconds < 240, $"early percent slowdown moved the hybrid total to {estimate.EstimatedTotal.TotalSeconds:F1}s");
        }

        private JobWorkload GetWorkload(int frameCount, int batchSize, int inputWidth = 0, int inputHeight = 0, int targetResolution = 0)
        {
            var context = new JobProgressContext(frameCount, batchSize, inputWidth, inputHeight, targetResolution);
            var workload = JobWorkloadCalculator.FromContext(context);
            return workload;
        }

        private void AssertSecondsCloseTo(double expectedSeconds, TimeSpan actual)
        {
            Assert.True(Math.Abs(actual.TotalSeconds - expectedSeconds) < 0.5, $"expected ~{expectedSeconds}s but got {actual.TotalSeconds:F1}s");
        }
    }
}
