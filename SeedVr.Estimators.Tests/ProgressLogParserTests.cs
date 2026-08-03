using SeedVr.Estimators.Signals;

namespace SeedVr.Estimators.Tests
{
    public class ProgressLogParserTests
    {
        [Theory]
        [InlineData("Phase 1: VAE encoding", ProgressPhase.Encoding)]
        [InlineData("Phase 2: DiT upscaling", ProgressPhase.DiTUpscaling)]
        [InlineData("Phase 3: VAE decoding", ProgressPhase.VaeDecoding)]
        [InlineData("Phase 4: Post-processing", ProgressPhase.PostProcessing)]
        public void Parse_when_the_line_is_a_phase_header_then_returns_that_phase(string line, ProgressPhase expected)
        {
            // Act
            var evt = ProgressLogParser.Parse(line);

            // Assert
            Assert.NotNull(evt);
            Assert.Equal(expected, evt.Phase);
            Assert.Equal(0, evt.BatchCount);
        }

        [Theory]
        [InlineData("Encoding batch 1/2", ProgressPhase.Encoding, 1, 2)]
        [InlineData("Upscaling batch 2/2", ProgressPhase.DiTUpscaling, 2, 2)]
        [InlineData("Decoding batch 1/2", ProgressPhase.VaeDecoding, 1, 2)]
        [InlineData("Post-processing batch 2/2", ProgressPhase.PostProcessing, 2, 2)]
        public void Parse_when_the_line_carries_a_batch_count_then_returns_the_phase_and_batch(string line, ProgressPhase expected, int batchIndex, int batchCount)
        {
            // Act
            var evt = ProgressLogParser.Parse(line);

            // Assert
            Assert.NotNull(evt);
            Assert.Equal(expected, evt.Phase);
            Assert.Equal(batchIndex, evt.BatchIndex);
            Assert.Equal(batchCount, evt.BatchCount);
            Assert.True(evt.BatchCount > 0);
        }

        [Theory]
        [InlineData("Starting upscaling generation...")]
        [InlineData("Using VAE tiled encoding (Tile: (1024, 1024), Overlap: (128, 128))")]
        [InlineData("Upscaling completed successfully!")]
        [InlineData("https://github.com/numz/ComfyUI-SeedVR2_VideoUpscaler")]
        [InlineData("Padding batch: 1 frame added (32 -> 33) for uniform batches")]
        [InlineData("Sequence of 33 frames")]
        public void Parse_when_the_line_merely_mentions_a_phase_word_then_returns_null(string line)
        {
            // Act
            var evt = ProgressLogParser.Parse(line);

            // Assert
            Assert.Null(evt);
        }
    }
}
