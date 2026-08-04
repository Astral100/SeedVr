using SeedVr.Estimators.Scoring;
using SeedVr.Estimators.Tracing;

namespace SeedVr.Estimators.Tests
{
    /// <summary>Replays every recorded live trace through the current adaptive-hybrid and guards its accuracy. The bounds are
    /// regression alarms set above the score at the time each trace was added, not precision targets: a deliberate retune that
    /// shifts the balance is expected to refresh them as part of the change.</summary>
    public class EstimatorTraceRegressionTests
    {
        /// <summary>Snapshot file in Traces/ and the mean-absolute-error ceiling in seconds it must stay under. Each fixture
        /// covers a distinct situation (host x clip length x speed regime x submit path); near-duplicate runs are not promoted.</summary>
        public static TheoryData<string, double> TraceBounds { get; } = new TheoryData<string, double>
        {
            { "RunSnapshot 2026.08.03 07-31-16.json", 5 },
            { "RunSnapshot 2026.08.03 07-58-47.json", 8 },
            { "RunSnapshot 2026.08.03 08-25-56.json", 9 },
            { "RunSnapshot 2026.08.03 08-52-04.json", 25 },
            { "RunSnapshot 2026.08.03 13-39-05.json", 18 },
            { "RunSnapshot 2026.08.04 11-44-32.json", 19 },
            { "RunSnapshot 2026.08.04 11-47-02.json", 19 },
            { "RunSnapshot 2026.08.04 13-06-42.json", 130 }
        };

        [Theory]
        [MemberData(nameof(TraceBounds))]
        public void Replay_stays_under_the_recorded_accuracy_bound(string traceFileName, double maximumMeanAbsoluteErrorSeconds)
        {
            // Arrange
            var trace = EstimatorTraceStore.Load(GetTracePath(traceFileName));

            // Act
            var score = EstimatorEvaluator.Score(trace);

            // Assert
            Assert.True(score.PredictionCount > 0, $"{traceFileName} produced no scored checkpoints");
            Assert.True(score.MeanAbsoluteErrorSeconds <= maximumMeanAbsoluteErrorSeconds, $"{traceFileName} scored {score.MeanAbsoluteErrorSeconds:F1}s MAE against the {maximumMeanAbsoluteErrorSeconds}s bound");
        }

        [Fact]
        public void Every_saved_trace_has_a_regression_bound()
        {
            // Arrange
            var boundedFileNames = TraceBounds.Select(row => (string)row[0]).ToHashSet();
            var savedFileNames = Directory.GetFiles(GetTracePath(""), "RunSnapshot*.json").Select(Path.GetFileName).ToList();

            // Act
            var unboundedFileNames = savedFileNames.Where(fileName => !boundedFileNames.Contains(fileName)).ToList();

            // Assert
            Assert.True(savedFileNames.Count > 0, "no trace files were copied next to the test assembly");
            Assert.True(unboundedFileNames.Count == 0, $"traces without a bound in {nameof(TraceBounds)}: {string.Join(", ", unboundedFileNames)}");
        }

        private string GetTracePath(string traceFileName)
        {
            return Path.Combine(AppContext.BaseDirectory, "Traces", traceFileName);
        }
    }
}
