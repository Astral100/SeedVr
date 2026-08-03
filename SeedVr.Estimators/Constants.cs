namespace SeedVr.Estimators
{
    public static class Constants
    {
        public const int ReferenceBatchSize = 33;
        public const int ReferenceOutputPixelCount = 2118960;
        public const double ReferenceFinalizationSeconds = 15.5;
        public const double FinalizationSetupSeconds = 3.5;
        public const double FinalizationSecondsPerFrame = 0.1885;

        public const double EncodingSetupSeconds = 3.2;
        public const double EncodingPerBatchSeconds = 20.5;
        public const double DitSetupSeconds = 1.0;
        public const double DitPerBatchSeconds = 23.75;
        public const double VaeSetupSeconds = 0.3;
        public const double VaePerBatchSeconds = 48.0;
        public const double PostSetupSeconds = 0.0;
        public const double PostPerBatchSeconds = 5.0;

        public const double MinimumRunSpeedFactor = 0.75;
        public const double MaximumRunSpeedFactor = 1.35;
        public const double RunSpeedLearningRate = 0.2;
        public const double PhaseBatchRefinementAlpha = 0.25;

        public const double DemaAlpha = 0.3;
        public const double DemaMinimumRateFactor = 0.5;
        public const double DemaMaximumRateFactor = 2.0;

        public const double HybridMinimumLiveWeight = 0.15;
        public const double HybridMaximumLiveWeight = 0.8;
        public const double HybridMaximumDeviationSeconds = 15.0;
        public const int ProgressLogPercentInterval = 10;

        public const string TraceDirectory = "logs";
    }
}
