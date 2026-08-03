using SeedVr.Estimators.Jobs;
using SeedVr.Estimators.Scoring;
using SeedVr.Estimators.Signals;
using SeedVr.Estimators.Tracing;

namespace SeedVr.Estimators.Tests
{
    public class EstimatorEvaluatorTests
    {
        [Fact]
        public void Score_when_the_run_is_captured_then_scores_the_adaptive_hybrid()
        {
            // Arrange
            var trace = GetCapturedRun();

            // Act
            var score = EstimatorEvaluator.Score(trace);

            // Assert
            Assert.Equal("adaptive-hybrid", score.Name);
            Assert.True(double.IsFinite(score.MeanAbsoluteErrorSeconds));
        }

        [Fact]
        public void Score_when_the_run_is_captured_then_scores_every_percent_checkpoint()
        {
            // Arrange
            var trace = GetCapturedRun();

            // Act
            var score = EstimatorEvaluator.Score(trace);

            // Assert
            Assert.Equal(7, score.PredictionCount);
        }

        [Fact]
        public void Score_when_the_run_has_no_percent_checkpoints_then_does_not_fail()
        {
            // Arrange
            var trace = new EstimatorRunTrace
            {
                Context = new JobProgressContext(62, 33, 704, 1280, 1080),
                ActualTotalSeconds = 199.95,
                Succeeded = true,
                Samples =
                [
                    GetPhaseSample(0, ProgressPhase.Encoding),
                    GetBatchSample(2.6, ProgressPhase.Encoding, 1),
                    GetPhaseSample(41.2, ProgressPhase.DiTUpscaling)
                ]
            };

            // Act
            var score = EstimatorEvaluator.Score(trace);

            // Assert
            Assert.Equal(0, score.PredictionCount);
            Assert.Equal(0, score.MeanAbsoluteErrorSeconds);
        }

        [Fact]
        public void SaveAndLoad_when_the_trace_is_replayable_then_round_trips()
        {
            // Arrange
            var path = Path.Combine(Path.GetTempPath(), $"seedvr-estimator-{Guid.NewGuid():N}.json");
            try
            {
                var expected = GetCapturedRun();

                // Act
                EstimatorTraceStore.Save(expected, path);
                var actual = EstimatorTraceStore.Load(path);

                // Assert
                Assert.Equal(expected.ActualTotalSeconds, actual.ActualTotalSeconds);
                Assert.Equal(expected.Context.FrameCount, actual.Context.FrameCount);
                Assert.Equal(expected.Samples.Count, actual.Samples.Count);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private EstimatorRunTrace GetCapturedRun()
        {
            return new EstimatorRunTrace
            {
                Context = new JobProgressContext(62, 33, 704, 1280, 1080),
                ActualTotalSeconds = 199.95,
                Succeeded = true,
                Samples =
                [
                    GetPhaseSample(0, ProgressPhase.Encoding),
                    GetBatchSample(2.6, ProgressPhase.Encoding, 1),
                    GetPercentSample(20.4, 10),
                    GetBatchSample(23.1, ProgressPhase.Encoding, 2),
                    GetPercentSample(39.9, 20),
                    GetPhaseSample(41.2, ProgressPhase.DiTUpscaling),
                    GetBatchSample(43.8, ProgressPhase.DiTUpscaling, 1),
                    GetPercentSample(64.4, 32),
                    GetBatchSample(65.6, ProgressPhase.DiTUpscaling, 2),
                    GetPercentSample(83.0, 45),
                    GetPhaseSample(83.6, ProgressPhase.VaeDecoding),
                    GetBatchSample(83.7, ProgressPhase.VaeDecoding, 1),
                    GetPercentSample(128.8, 70),
                    GetBatchSample(130.3, ProgressPhase.VaeDecoding, 2),
                    GetPercentSample(174.9, 95),
                    GetPhaseSample(176.7, ProgressPhase.PostProcessing),
                    GetBatchSample(176.7, ProgressPhase.PostProcessing, 1),
                    GetPercentSample(180.4, 97),
                    GetBatchSample(181.9, ProgressPhase.PostProcessing, 2),
                    GetPercentSample(185.2, 100)
                ]
            };
        }

        private EstimatorTraceSample GetPercentSample(double elapsedSeconds, double percent)
        {
            return new EstimatorTraceSample { ElapsedSeconds = elapsedSeconds, Percent = percent };
        }

        private EstimatorTraceSample GetPhaseSample(double elapsedSeconds, ProgressPhase phase)
        {
            return new EstimatorTraceSample { ElapsedSeconds = elapsedSeconds, Phase = phase, BatchIndex = 1, BatchCount = 0 };
        }

        private EstimatorTraceSample GetBatchSample(double elapsedSeconds, ProgressPhase phase, int batchIndex)
        {
            return new EstimatorTraceSample { ElapsedSeconds = elapsedSeconds, Phase = phase, BatchIndex = batchIndex, BatchCount = 2 };
        }
    }
}
