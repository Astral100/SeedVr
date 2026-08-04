using SeedVr.Estimators.Jobs;
using SeedVr.Estimators.Signals;

namespace SeedVr.Estimators.Estimators
{
    /// <summary>Models the run as four phases, each with setup and padded-batch costs. Starting from measured priors, it replaces
    /// phase costs with live timings at batch and phase boundaries. Driven by parsed SeedVR2 stdout; percent is not needed.</summary>
    public class PhaseBatchEstimator : IProgressEstimator
    {
        private static readonly ProgressPhase[] OrderedPhases =
        {
            ProgressPhase.Encoding,
            ProgressPhase.DiTUpscaling,
            ProgressPhase.VaeDecoding,
            ProgressPhase.PostProcessing
        };

        private readonly int _batchCount;
        private readonly double _finalizationSeconds;
        private readonly double[] _setupSeconds;
        private readonly double[] _priorPerBatchSeconds;
        private readonly double[] _perBatchSeconds;
        private readonly double?[] _phaseSpeedFactors;
        private readonly TimeSpan?[] _phaseStartElapsed;
        private readonly TimeSpan?[] _firstBatchStartElapsed;
        private ProgressPhase _currentPhase;
        private EtaEstimate _lastEstimate;

        public PhaseBatchEstimator(JobWorkload workload)
        {
            _batchCount = workload.BatchCount;
            _finalizationSeconds = workload.FinalizationSeconds;
            var phaseCount = Enum.GetValues<ProgressPhase>().Length;
            _setupSeconds = new double[phaseCount];
            _priorPerBatchSeconds = new double[phaseCount];
            _perBatchSeconds = new double[phaseCount];
            _phaseSpeedFactors = new double?[phaseCount];
            _phaseStartElapsed = new TimeSpan?[phaseCount];
            _firstBatchStartElapsed = new TimeSpan?[phaseCount];

            foreach (var phase in OrderedPhases)
            {
                var prior = GetPhasePrior(phase);
                _setupSeconds[(int)phase] = prior.SetupSeconds;
                _priorPerBatchSeconds[(int)phase] = prior.PerBatchSeconds * workload.BatchWorkScale;
                _perBatchSeconds[(int)phase] = _priorPerBatchSeconds[(int)phase];
            }

            _currentPhase = ProgressPhase.Unknown;
            _lastEstimate = BuildEstimate(TimeSpan.Zero);
        }

        public string Name { get; } = "phase-batch";

        public EtaEstimate Update(ProgressSample sample)
        {
            if (sample.PhaseBatch != null)
            {
                var signalElapsed = sample.SignalElapsed ?? sample.Elapsed;
                FoldInPhaseBatch(sample.PhaseBatch, signalElapsed);
            }

            _lastEstimate = BuildEstimate(sample.Elapsed);
            return _lastEstimate;
        }

        private void FoldInPhaseBatch(PhaseBatchEvent phaseBatch, TimeSpan elapsed)
        {
            if (phaseBatch.Phase == ProgressPhase.Unknown)
            {
                return;
            }

            if (phaseBatch.Phase != _currentPhase)
            {
                MeasureCompletedPhase(_currentPhase, elapsed);
                _phaseStartElapsed[(int)phaseBatch.Phase] = elapsed;
                _firstBatchStartElapsed[(int)phaseBatch.Phase] = null;
                _currentPhase = phaseBatch.Phase;
            }

            if (phaseBatch.BatchCount > 0)
            {
                RecordBatchStart(phaseBatch, elapsed);
            }
        }

        /// <summary>When a new phase begins, the previous one just finished, so split its observed duration into setup and batches.</summary>
        private void MeasureCompletedPhase(ProgressPhase completedPhase, TimeSpan completedAt)
        {
            if (completedPhase == ProgressPhase.Unknown || _batchCount <= 0)
            {
                return;
            }

            var start = _phaseStartElapsed[(int)completedPhase];
            if (start == null)
            {
                return;
            }

            var setupSeconds = _setupSeconds[(int)completedPhase];
            var batchStarted = _firstBatchStartElapsed[(int)completedPhase];
            var batchSeconds = (completedAt - start.Value).TotalSeconds - setupSeconds;
            if (batchStarted != null)
            {
                var measuredSetup = (batchStarted.Value - start.Value).TotalSeconds;
                if (measuredSetup >= 0)
                {
                    setupSeconds = measuredSetup;
                    batchSeconds = (completedAt - batchStarted.Value).TotalSeconds;
                }
            }

            var perBatch = batchSeconds / _batchCount;
            if (perBatch > 0)
            {
                _setupSeconds[(int)completedPhase] = setupSeconds;
                RecordPhaseSpeed(completedPhase, perBatch);
                _perBatchSeconds[(int)completedPhase] = perBatch;
            }
        }

        /// <summary>Remember the first batch start, then refine as soon as batch 2 proves one setup-free batch has completed.</summary>
        private void RecordBatchStart(PhaseBatchEvent phaseBatch, TimeSpan elapsed)
        {
            if (phaseBatch.BatchIndex == 1)
            {
                _firstBatchStartElapsed[(int)_currentPhase] = elapsed;
                return;
            }

            if (phaseBatch.BatchIndex < 2)
            {
                return;
            }

            var firstBatchStart = _firstBatchStartElapsed[(int)_currentPhase];
            if (firstBatchStart == null)
            {
                return;
            }

            var perBatch = (elapsed - firstBatchStart.Value).TotalSeconds / (phaseBatch.BatchIndex - 1);
            if (perBatch > 0)
            {
                var priorPerBatch = _priorPerBatchSeconds[(int)_currentPhase];
                var clippedPerBatch = Math.Clamp(perBatch, priorPerBatch * Constants.MinimumRunSpeedFactor, priorPerBatch * Constants.MaximumRunSpeedFactor);
                var completedBatches = phaseBatch.BatchIndex - 1;
                var confidence = 1 - Math.Pow(1 - Constants.PhaseBatchRefinementAlpha, completedBatches);
                var refinedPerBatch = confidence * clippedPerBatch + (1 - confidence) * priorPerBatch;
                _perBatchSeconds[(int)_currentPhase] = refinedPerBatch;
            }
        }

        private void RecordPhaseSpeed(ProgressPhase phase, double measuredPerBatchSeconds)
        {
            var priorPerBatchSeconds = _priorPerBatchSeconds[(int)phase];
            if (priorPerBatchSeconds <= 0)
            {
                return;
            }

            var factor = measuredPerBatchSeconds / priorPerBatchSeconds;
            _phaseSpeedFactors[(int)phase] = Math.Clamp(factor, Constants.MinimumRunSpeedFactor, Constants.MaximumRunSpeedFactor);
        }

        /// <summary>The reference-host finalization prior adapted to this host. Finalization varies across hosts well beyond the
        /// phase speeds (59.9s vs 14.7s for the same 300-frame clip, while the phases differed ~1.3x), hence the squared factor.</summary>
        public double GetAdaptedFinalizationSeconds()
        {
            var runSpeedFactor = GetRunSpeedFactor();
            return _finalizationSeconds * runSpeedFactor * runSpeedFactor;
        }

        private double GetRunSpeedFactor()
        {
            var observedFactors = _phaseSpeedFactors.Where(factor => factor != null).Select(factor => factor.Value).ToList();
            if (observedFactors.Count == 0)
            {
                return 1.0;
            }

            // Each measured phase earns the observed speed more say over the unmeasured ones; a single phase stays shrunk
            // because phases differ individually, but two or three agreeing phases describe the host, not the phase.
            var observedAverage = observedFactors.Average();
            var learningWeight = Math.Min(Constants.MaximumRunSpeedLearningWeight, Constants.RunSpeedLearningRate * observedFactors.Count);
            return 1 + learningWeight * (observedAverage - 1);
        }

        private (double SetupSeconds, double PerBatchSeconds) GetPhasePrior(ProgressPhase phase)
        {
            return phase switch
            {
                ProgressPhase.Encoding => (Constants.EncodingSetupSeconds, Constants.EncodingPerBatchSeconds),
                ProgressPhase.DiTUpscaling => (Constants.DitSetupSeconds, Constants.DitPerBatchSeconds),
                ProgressPhase.VaeDecoding => (Constants.VaeSetupSeconds, Constants.VaePerBatchSeconds),
                ProgressPhase.PostProcessing => (Constants.PostSetupSeconds, Constants.PostPerBatchSeconds),
                _ => (0, 0)
            };
        }

        private EtaEstimate BuildEstimate(TimeSpan elapsed)
        {
            if (_batchCount <= 0)
            {
                return EtaEstimate.Empty;
            }

            var finalizationSeconds = GetAdaptedFinalizationSeconds();

            // Before any phase line, the estimate is the whole-run prior from t=0.
            if (_currentPhase == ProgressPhase.Unknown)
            {
                var priorTotal = TimeSpan.FromSeconds(WholeRunCost() + finalizationSeconds);
                return EtaEstimate.FromTotal(priorTotal, elapsed);
            }

            // The current phase started at a known time; from there, count its whole cost plus every later phase. Progress within
            // the phase falls out of (total - elapsed) as time passes, and each boundary resets the anchor with corrected costs.
            var currentStart = _phaseStartElapsed[(int)_currentPhase] ?? elapsed;
            var totalSeconds = currentStart.TotalSeconds + EstimatedWholeCost(_currentPhase) + FuturePhaseCost(_currentPhase) + finalizationSeconds;
            var total = TimeSpan.FromSeconds(totalSeconds);
            return EtaEstimate.FromTotal(total, elapsed);
        }

        private double WholeRunCost()
        {
            double cost = 0;
            foreach (var phase in OrderedPhases)
            {
                cost += WholeCost(phase);
            }

            return cost;
        }

        private double WholeCost(ProgressPhase phase)
        {
            var batchCost = _perBatchSeconds[(int)phase] * _batchCount;
            return _setupSeconds[(int)phase] + batchCost;
        }

        private double EstimatedWholeCost(ProgressPhase phase)
        {
            var cost = WholeCost(phase);
            return _phaseSpeedFactors[(int)phase] == null ? cost * GetRunSpeedFactor() : cost;
        }

        private double FuturePhaseCost(ProgressPhase currentPhase)
        {
            double cost = 0;
            foreach (var phase in OrderedPhases)
            {
                if ((int)phase > (int)currentPhase)
                {
                    cost += EstimatedWholeCost(phase);
                }
            }

            return cost;
        }
    }
}
