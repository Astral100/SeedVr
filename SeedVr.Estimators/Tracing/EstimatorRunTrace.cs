using System.Text.Json.Serialization;
using SeedVr.Estimators.Jobs;
using SeedVr.Estimators.Signals;

namespace SeedVr.Estimators.Tracing
{
    /// <summary>A completed run's context, ordered signals, live predictions and observed completion time.</summary>
    public class EstimatorRunTrace
    {
        public JobProgressContext Context { get; set; }
        public List<EstimatorTraceSample> Samples { get; set; } = [];
        public double? ProcessingCompletedSeconds { get; set; }
        public double? ActualTotalSeconds { get; set; }
        public bool? Succeeded { get; set; }
    }

    public class EstimatorTraceSample
    {
        public double ElapsedSeconds { get; set; }
        public double? SignalElapsedSeconds { get; set; }
        public double? Percent { get; set; }
        public ProgressPhase Phase { get; set; }
        public int? BatchIndex { get; set; }
        public int? BatchCount { get; set; }

        [JsonConverter(typeof(LegacyPredictionConverter))]
        public double? EstimatedTotalSeconds { get; set; }

        public ProgressSample ToProgressSample()
        {
            PhaseBatchEvent phaseBatch = null;
            if (Phase != ProgressPhase.Unknown && BatchIndex != null && BatchCount != null)
            {
                phaseBatch = new PhaseBatchEvent(Phase, BatchIndex.Value, BatchCount.Value);
            }

            TimeSpan? signalElapsed = SignalElapsedSeconds == null ? null : TimeSpan.FromSeconds(SignalElapsedSeconds.Value);
            return new ProgressSample(TimeSpan.FromSeconds(ElapsedSeconds), Percent, phaseBatch, signalElapsed);
        }
    }
}
