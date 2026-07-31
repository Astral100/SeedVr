using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeedVr.Remote.Models.Workflow
{
    /// <summary>A ComfyUI node connection, serialized as the [sourceNodeId, outputIndex] pair.</summary>
    [JsonConverter(typeof(NodeLinkConverter))]
    public class NodeLink
    {
        public string SourceNodeId { get; set; }

        public int OutputIndex { get; set; }
    }

    /// <summary>Reads and writes a NodeLink as the two-element array ComfyUI uses for wiring.</summary>
    public class NodeLinkConverter : JsonConverter<NodeLink>
    {
        public override NodeLink Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("A node link must be a [sourceNodeId, outputIndex] array.");
            }

            reader.Read();
            var sourceNodeId = reader.GetString();
            reader.Read();
            var outputIndex = reader.GetInt32();
            reader.Read();

            var link = new NodeLink { SourceNodeId = sourceNodeId, OutputIndex = outputIndex };
            return link;
        }

        public override void Write(Utf8JsonWriter writer, NodeLink value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(value.SourceNodeId);
            writer.WriteNumberValue(value.OutputIndex);
            writer.WriteEndArray();
        }
    }
}
