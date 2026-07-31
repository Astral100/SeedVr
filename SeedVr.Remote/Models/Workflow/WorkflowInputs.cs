using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.Workflow
{
    /// <summary>Node 10, SeedVR2VideoUpscaler: the upscale parameters and the model/image wiring.</summary>
    public class UpscalerInputs
    {
        [JsonPropertyName("seed")]
        public long Seed { get; set; }

        [JsonPropertyName("resolution")]
        public int Resolution { get; set; }

        [JsonPropertyName("max_resolution")]
        public int MaxResolution { get; set; }

        [JsonPropertyName("batch_size")]
        public int BatchSize { get; set; }

        [JsonPropertyName("uniform_batch_size")]
        public bool UniformBatchSize { get; set; }

        [JsonPropertyName("color_correction")]
        public string ColorCorrection { get; set; }

        [JsonPropertyName("temporal_overlap")]
        public int TemporalOverlap { get; set; }

        [JsonPropertyName("prepend_frames")]
        public int PrependFrames { get; set; }

        [JsonPropertyName("input_noise_scale")]
        public double InputNoiseScale { get; set; }

        [JsonPropertyName("latent_noise_scale")]
        public double LatentNoiseScale { get; set; }

        [JsonPropertyName("offload_device")]
        public string OffloadDevice { get; set; }

        [JsonPropertyName("enable_debug")]
        public bool EnableDebug { get; set; }

        [JsonPropertyName("image")]
        public NodeLink Image { get; set; }

        [JsonPropertyName("dit")]
        public NodeLink Dit { get; set; }

        [JsonPropertyName("vae")]
        public NodeLink Vae { get; set; }
    }

    /// <summary>Node 13, SeedVR2LoadVAEModel: the VAE model and its tiling.</summary>
    public class VaeLoaderInputs
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("device")]
        public string Device { get; set; }

        [JsonPropertyName("encode_tiled")]
        public bool EncodeTiled { get; set; }

        [JsonPropertyName("encode_tile_size")]
        public int EncodeTileSize { get; set; }

        [JsonPropertyName("encode_tile_overlap")]
        public int EncodeTileOverlap { get; set; }

        [JsonPropertyName("decode_tiled")]
        public bool DecodeTiled { get; set; }

        [JsonPropertyName("decode_tile_size")]
        public int DecodeTileSize { get; set; }

        [JsonPropertyName("decode_tile_overlap")]
        public int DecodeTileOverlap { get; set; }

        // A string "false"/"true" in the workflow, not a JSON boolean.
        [JsonPropertyName("tile_debug")]
        public string TileDebug { get; set; }

        [JsonPropertyName("offload_device")]
        public string OffloadDevice { get; set; }

        [JsonPropertyName("cache_model")]
        public bool CacheModel { get; set; }
    }

    /// <summary>Node 14, SeedVR2LoadDiTModel: the DiT model and how it is swapped in.</summary>
    public class DitLoaderInputs
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("device")]
        public string Device { get; set; }

        [JsonPropertyName("blocks_to_swap")]
        public int BlocksToSwap { get; set; }

        [JsonPropertyName("swap_io_components")]
        public bool SwapIoComponents { get; set; }

        [JsonPropertyName("offload_device")]
        public string OffloadDevice { get; set; }

        [JsonPropertyName("cache_model")]
        public bool CacheModel { get; set; }

        [JsonPropertyName("attention_mode")]
        public string AttentionMode { get; set; }
    }

    /// <summary>Node 21, LoadVideo: the uploaded input file.</summary>
    public class LoadVideoInputs
    {
        [JsonPropertyName("file")]
        public string File { get; set; }

        [JsonPropertyName("video-preview")]
        public string VideoPreview { get; set; }
    }

    /// <summary>Node 22, GetVideoComponents: splits the loaded video into frames, fps and audio.</summary>
    public class GetVideoComponentsInputs
    {
        [JsonPropertyName("video")]
        public NodeLink Video { get; set; }
    }

    /// <summary>Node 23, SaveVideo: the output naming and the assembled video to write.</summary>
    public class SaveVideoInputs
    {
        [JsonPropertyName("filename_prefix")]
        public string FilenamePrefix { get; set; }

        [JsonPropertyName("format")]
        public string Format { get; set; }

        [JsonPropertyName("codec")]
        public string Codec { get; set; }

        [JsonPropertyName("video-preview")]
        public string VideoPreview { get; set; }

        [JsonPropertyName("video")]
        public NodeLink Video { get; set; }
    }

    /// <summary>Node 24, CreateVideo: recombines the upscaled frames with the original fps and audio.</summary>
    public class CreateVideoInputs
    {
        [JsonPropertyName("fps")]
        public NodeLink Fps { get; set; }

        [JsonPropertyName("bit_depth")]
        public int BitDepth { get; set; }

        [JsonPropertyName("images")]
        public NodeLink Images { get; set; }

        [JsonPropertyName("audio")]
        public NodeLink Audio { get; set; }
    }
}
