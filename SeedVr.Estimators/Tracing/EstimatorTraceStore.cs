using System.Text.Json;
using System.Text.Json.Serialization;
using SeedVr.Logger;

namespace SeedVr.Estimators.Tracing
{
    /// <summary>Persists portable JSON traces so estimator changes can be replayed against real completed runs.</summary>
    public static class EstimatorTraceStore
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Saves a completed run as a timestamped snapshot under the logs directory ("RunSnapshot 2026.08.04 12-54-02.json"),
        /// overwriting a same-second file, warning rather than throwing so a diagnostics write cannot fail an otherwise finished job.
        /// The trace's run id keeps the snapshot correlatable with log lines and /history.</summary>
        public static void SaveSnapshot(EstimatorRunTrace trace)
        {
            var fileName = $"{Constants.SnapshotFilePrefix} {DateTime.Now.ToString(Constants.SnapshotTimestampFormat)}.json";
            var path = Path.Combine(Directory.GetCurrentDirectory(), Constants.TraceDirectory, fileName);
            try
            {
                Save(trace, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, $"The estimator run snapshot could not be saved to {path}.");
            }
        }

        public static void Save(EstimatorRunTrace trace, string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(trace, Options);
            File.WriteAllText(path, json);
        }

        public static EstimatorRunTrace Load(string path)
        {
            var json = File.ReadAllText(path);
            var trace = JsonSerializer.Deserialize<EstimatorRunTrace>(json, Options);
            if (trace == null)
            {
                throw new JsonException($"Estimator trace '{path}' contained no usable data.");
            }

            return trace;
        }
    }
}
