namespace SeedVr.Estimators.Scoring
{
    /// <summary>Accuracy and stability statistics for one estimator over a completed trace.</summary>
    public class EstimatorScore
    {
        public string Name { get; set; }
        public int PredictionCount { get; set; }
        public double MeanAbsoluteErrorSeconds { get; set; }
        public double WorstAbsoluteErrorSeconds { get; set; }
    }
}
