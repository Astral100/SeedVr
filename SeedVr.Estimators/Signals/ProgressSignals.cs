namespace SeedVr.Estimators.Signals
{
    /// <summary>SeedVR2's four sequential stages, in run order. Unknown covers a line that maps to no phase.</summary>
    public enum ProgressPhase
    {
        Unknown,
        Encoding,
        DiTUpscaling,
        VaeDecoding,
        PostProcessing
    }

    /// <summary>A parsed SeedVR2 progress line: which phase it belongs to, and which batch (1-based) of how many.</summary>
    public record PhaseBatchEvent(ProgressPhase Phase, int BatchIndex, int BatchCount);

    /// <summary>One live observation fed to the estimator: time since the first live signal, the WebSocket percent when present, and a
    /// parsed phase/batch line when present. Each estimator reads whichever signals it needs and ignores the rest.</summary>
    public record ProgressSample(TimeSpan Elapsed, double? Percent, PhaseBatchEvent PhaseBatch, TimeSpan? SignalElapsed = null);
}
