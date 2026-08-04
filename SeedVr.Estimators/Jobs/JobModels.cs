namespace SeedVr.Estimators.Jobs
{
    /// <summary>Video workload metadata, from a fast ffprobe read up front or from SeedVR2's startup log during the run.</summary>
    public record VideoMetadata(int FrameCount, int Width, int Height);

    /// <summary>What is known before the job runs. SeedVR2 pads every partial batch and scales work with output area, so the
    /// estimators charge padded frames at the derived output resolution. The video metadata may be unknown up front (frame count
    /// and dimensions left at zero) and arrive from SeedVR2's startup log instead.</summary>
    public record JobProgressContext(int FrameCount, int BatchSize, int InputWidth = 0, int InputHeight = 0, int TargetResolution = 0, HostProfile Host = null);

    /// <summary>The rented machine behind a run, as Vast.ai reports it. Runs on the same GPU model differ materially between
    /// hosts (CPU and disk drive finalization, dlperf tracks the GPU phases), so traces carry the fingerprint for correlation
    /// and MachineId keys any learned per-host priors.</summary>
    public record HostProfile(int MachineId, string GpuName, string CpuName, double CpuCoresEffective, int CpuCoresTotal, double CpuRamGb, double DiskBandwidthMbps, double PcieBandwidthGbps, double Dlperf);

    /// <summary>Calculated workload values consumed by the ETA estimators.</summary>
    public record JobWorkload(int BatchCount, double BatchWorkScale, double FinalizationSeconds);
}
