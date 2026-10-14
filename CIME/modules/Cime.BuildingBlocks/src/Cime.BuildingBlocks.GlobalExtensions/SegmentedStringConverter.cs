using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cime.BuildingBlocks.GlobalExtensions
{
    /// <summary>
    /// 0070: texto grande na resposta escrito em segmentos. Para escrever uma string com escapes (quebras de linha,
    /// acentos), o System.Text.Json aluga do <c>ArrayPool</c> compartilhado um buffer de até 6× o tamanho dela — um documento
    /// da engenharia reversa de 3 milhões de caracteres alugava 64 MB, e o pool guardava um por thread (o OOM de 512 MiB).
    /// Em segmentos de <see cref="SegmentChars"/> o buffer fica pequeno; o JSON gerado é o mesmo.
    /// </summary>
    public sealed class SegmentedStringConverter : JsonConverter<string>
    {
        public const int Threshold = 16_384;
        public const int SegmentChars = 8_192;

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString();

        public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString()!;

        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WritePropertyName(value);

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value.Length <= Threshold)
            {
                writer.WriteStringValue(value);
                return;
            }
            var span = value.AsSpan();
            while (span.Length > SegmentChars)
            {
                var size = char.IsHighSurrogate(span[SegmentChars - 1]) ? SegmentChars - 1 : SegmentChars;
                writer.WriteStringValueSegment(span[..size], isFinalSegment: false);
                span = span[size..];
            }
            writer.WriteStringValueSegment(span, isFinalSegment: true);
        }
    }
}
