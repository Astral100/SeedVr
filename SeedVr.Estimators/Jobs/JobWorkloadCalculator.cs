namespace SeedVr.Estimators.Jobs
{
    /// <summary>Calculates estimator workload values from the supplied job context.</summary>
    public static class JobWorkloadCalculator
    {
        public static JobWorkload FromContext(JobProgressContext context)
        {
            var batchCount = context.BatchSize > 0 ? (int)Math.Ceiling((double)context.FrameCount / context.BatchSize) : 0;
            var outputPixelCount = GetOutputPixelCount(context.InputWidth, context.InputHeight, context.TargetResolution);
            var pixelScale = outputPixelCount > 0 ? (double)outputPixelCount / Constants.ReferenceOutputPixelCount : 1.0;
            var batchWorkScale = pixelScale * context.BatchSize / Constants.ReferenceBatchSize;
            var finalizationSeconds = GetFinalizationSeconds(context.FrameCount, pixelScale);
            return new JobWorkload(batchCount, batchWorkScale, finalizationSeconds);
        }

        private static int GetOutputPixelCount(int inputWidth, int inputHeight, int targetResolution)
        {
            if (inputWidth <= 0 || inputHeight <= 0 || targetResolution <= 0)
            {
                return 0;
            }

            var shortestInputEdge = Math.Min(inputWidth, inputHeight);
            var longestInputEdge = Math.Max(inputWidth, inputHeight);
            var longestOutputEdge = (int)Math.Round((double)longestInputEdge * targetResolution / shortestInputEdge);
            return targetResolution * longestOutputEdge;
        }

        private static double GetFinalizationSeconds(int frameCount, double pixelScale)
        {
            if (frameCount <= 0)
            {
                return Constants.ReferenceFinalizationSeconds;
            }

            var frameWorkSeconds = Constants.FinalizationSecondsPerFrame * frameCount * pixelScale;
            return Constants.FinalizationSetupSeconds + frameWorkSeconds;
        }
    }
}
