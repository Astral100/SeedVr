namespace SeedVr.Estimators.Scoring
{
    /// <summary>Formats an estimator score's accuracy lines for the live completion summary and the offline replay tool alike.</summary>
    public static class EstimatorScoreReport
    {
        public static string DescribeAccuracy(EstimatorScore score)
        {
            if (score.PredictionCount == 0)
            {
                return "  No ETA checkpoints were recorded.";
            }

            var description = $"  Average ETA error: {score.MeanAbsoluteErrorSeconds:F1}s{Environment.NewLine}" +
                $"  Worst ETA error: {score.WorstAbsoluteErrorSeconds:F1}s{Environment.NewLine}" +
                $"  Prediction checkpoints: {score.PredictionCount}";
            return description;
        }
    }
}
