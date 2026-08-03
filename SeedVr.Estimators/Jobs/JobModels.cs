namespace SeedVr.Estimators.Jobs
{
    /// <summary>Video workload metadata, from a fast ffprobe read up front or from SeedVR2's startup log during the run.</summary>
    public record VideoMetadata(int FrameCount, int Width, int Height);

    /// <summary>What is known before the job runs. SeedVR2 pads every partial batch and scales work with output area, so the
    /// estimators charge padded frames at the derived output resolution. The video metadata may be unknown up front (frame count
    /// and dimensions left at zero) and arrive from SeedVR2's startup log instead.</summary>
    public record JobProgressContext(int FrameCount, int BatchSize, int InputWidth = 0, int InputHeight = 0, int TargetResolution = 0);

    /// <summary>Calculated workload values consumed by the ETA estimators.</summary>
    public record JobWorkload(int BatchCount, double BatchWorkScale, double FinalizationSeconds);
}
