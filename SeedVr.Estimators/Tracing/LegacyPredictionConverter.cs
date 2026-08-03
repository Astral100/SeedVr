using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeedVr.Estimators.Tracing
{
    /// <summary>Reads a scalar ETA prediction, tolerating the legacy per-estimator object form saved before the single-model
    /// flatten. Those historical values are diagnostic only, so an object is skipped and the sample loads without a prediction.</summary>
    public class LegacyPredictionConverter : JsonConverter<double?>
    {
        public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                reader.Skip();
                return null;
            }

            return reader.GetDouble();
        }

        public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteNumberValue(value.Value);
        }
    }
}
