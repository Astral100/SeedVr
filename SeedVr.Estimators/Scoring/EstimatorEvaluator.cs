using SeedVr.Estimators.Estimators;
using SeedVr.Estimators.Tracing;

namespace SeedVr.Estimators.Scoring
{
    /// <summary>Replays a completed trace through adaptive-hybrid and measures completion-time accuracy without a live GPU run.</summary>
    public static class EstimatorEvaluator
    {
        public static EstimatorScore Score(EstimatorRunTrace trace)
        {
            if (trace.ActualTotalSeconds == null || trace.ActualTotalSeconds <= 0)
            {
                throw new ArgumentException("A completed trace with a positive ActualTotalSeconds is required.", nameof(trace));
            }

            var estimator = AdaptiveHybridEstimator.FromContext(trace.Context);
            var accumulator = new ScoreAccumulator(estimator.Name);
            var scoredPercents = new HashSet<double>();

            foreach (var traceSample in trace.Samples.OrderBy(sample => sample.ElapsedSeconds))
            {
                var sample = traceSample.ToProgressSample();
                var estimate = estimator.Update(sample);
                if (estimate.IsAvailable && IsScorePoint(traceSample, scoredPercents))
                {
                    accumulator.AddScore(estimate.EstimatedTotal.TotalSeconds, trace.ActualTotalSeconds.Value);
                }
            }

            return accumulator.Build();
        }

        private static bool IsScorePoint(EstimatorTraceSample sample, HashSet<double> scoredPercents)
        {
            if (sample.Percent == null || sample.Percent >= 100)
            {
                return false;
            }

            return scoredPercents.Add(sample.Percent.Value);
        }

        private class ScoreAccumulator
        {
            private readonly string _name;
            private readonly List<double> _errors = [];

            public ScoreAccumulator(string name)
            {
                _name = name;
            }

            public void AddScore(double prediction, double actual)
            {
                _errors.Add(prediction - actual);
            }

            public EstimatorScore Build()
            {
                // A run whose socket never delivered a percent frame yields no checkpoints, which is a valid (if uninformative)
                // outcome, not an error: scoring is diagnostics and must never fail an otherwise completed job.
                var meanAbsoluteError = _errors.Count > 0 ? _errors.Average(error => Math.Abs(error)) : 0;
                var worstAbsoluteError = _errors.Count > 0 ? _errors.Max(error => Math.Abs(error)) : 0;
                return new EstimatorScore
                {
                    Name = _name,
                    PredictionCount = _errors.Count,
                    MeanAbsoluteErrorSeconds = meanAbsoluteError,
                    WorstAbsoluteErrorSeconds = worstAbsoluteError
                };
            }
        }
    }
}
