using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystemTools.Converters;

/// <summary>
/// 兼容 Blockly 下拉框传入的 "true"/"false"/空字符串与普通 JSON 布尔值。
/// </summary>
public sealed class NullableBooleanStringConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.String => ParseString(reader.GetString()),
            _ => throw new JsonException($"无法将 JSON {reader.TokenType} 转换为可空布尔值。")
        };
    }

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteBooleanValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    private static bool? ParseString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (bool.TryParse(value, out var result))
        {
            return result;
        }

        throw new JsonException($"无法将字符串“{value}”转换为可空布尔值。");
    }
}
