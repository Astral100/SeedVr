using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.ComfyUi
{
    /// <summary>Reads ComfyUI's zone-less log timestamp as UTC, since the server stamps entries in UTC.
    /// A value that is not a valid timestamp is an error, not a line to quietly skip.</summary>
    public class ComfyUiLogTimestampConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
            {
                return timestamp;
            }

            throw new JsonException($"The ComfyUI log timestamp '{value}' is not a valid date.");
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
