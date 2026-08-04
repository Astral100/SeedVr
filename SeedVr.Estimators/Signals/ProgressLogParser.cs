using System.Text.RegularExpressions;
using SeedVr.Estimators.Jobs;

namespace SeedVr.Estimators.Signals
{
    /// <summary>Turns one line of SeedVR2 stdout into a phase/batch event, or null when the line is not a real marker. A line
    /// counts only if it is a "Phase N:" header or carries a "batch k/N" count; that rejects prose that merely mentions a phase
    /// word (e.g. "Starting upscaling generation...", "Upscaling completed successfully!", the ...VideoUpscaler URL).</summary>
    public static class ProgressLogParser
    {
        private static readonly Regex BatchPattern = new Regex(@"batch\s*(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PhaseHeaderPattern = new Regex(@"Phase\s*\d+\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VideoMetadataPattern = new Regex(@"Input:\s*(\d+)\s+frames,\s*(\d+)\s*[x×]\s*(\d+)\s*px", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static VideoMetadata ParseVideoMetadata(string logLine)
        {
            if (string.IsNullOrWhiteSpace(logLine))
            {
                return null;
            }

            var match = VideoMetadataPattern.Match(logLine);
            if (!match.Success ||
                !int.TryParse(match.Groups[1].Value, out var frameCount) ||
                !int.TryParse(match.Groups[2].Value, out var width) ||
                !int.TryParse(match.Groups[3].Value, out var height))
            {
                return null;
            }

            return new VideoMetadata(frameCount, width, height);
        }

        public static PhaseBatchEvent Parse(string logLine)
        {
            if (string.IsNullOrWhiteSpace(logLine))
            {
                return null;
            }

            // Only a phase header or an explicit "batch k/N" line is a marker; a line that just mentions a phase word is not.
            var batchMatch = BatchPattern.Match(logLine);
            if (!batchMatch.Success && !PhaseHeaderPattern.IsMatch(logLine))
            {
                return null;
            }

            var phase = MatchPhase(logLine);
            if (phase == ProgressPhase.Unknown)
            {
                return null;
            }

            // A phase header is treated as that phase's first batch; a "batch k/N" line carries the real index.
            var batchIndex = 1;
            var batchCount = 0;
            if (batchMatch.Success)
            {
                batchIndex = int.Parse(batchMatch.Groups[1].Value);
                batchCount = int.Parse(batchMatch.Groups[2].Value);
            }

            return new PhaseBatchEvent(phase, batchIndex, batchCount);
        }

        private static ProgressPhase MatchPhase(string line)
        {
            if (line.Contains("decod", StringComparison.OrdinalIgnoreCase))
            {
                return ProgressPhase.VaeDecoding;
            }

            if (line.Contains("upscal", StringComparison.OrdinalIgnoreCase))
            {
                return ProgressPhase.DiTUpscaling;
            }

            if (line.Contains("post", StringComparison.OrdinalIgnoreCase))
            {
                return ProgressPhase.PostProcessing;
            }

            if (line.Contains("encod", StringComparison.OrdinalIgnoreCase))
            {
                return ProgressPhase.Encoding;
            }

            return ProgressPhase.Unknown;
        }
    }
}
