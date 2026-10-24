using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IpaDesktop;

/// <summary>Preserves the previous JSON field names and untyped dictionary/array contracts.</summary>
public static class JsonCodec
{
    static readonly JsonSerializerOptions Options = CreateOptions();
    static readonly StorageJsonContext Context = new StorageJsonContext(Options);
    static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true, MaxDepth = 128 };
        options.Converters.Add(new UntypedConverter());
        return options;
    }
    public static string Serialize(object value) => JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object), Context);
    public static T Deserialize<T>(string value)
    {
        if (value == null || value.Length > 16000000) throw new JsonException("Invalid JSON size.");
        return (T)JsonSerializer.Deserialize(value, typeof(T), Context);
    }
    public static object DeserializeObject(string value) => Deserialize<object>(value);
    sealed class UntypedConverter : JsonConverter<object>
    {
        public override object Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return Convert(document.RootElement);
        }
        static object Convert(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Object => Dictionary(element),
            JsonValueKind.Array => Array(element),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var number) ? (object)number : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
        static Dictionary<string, object> Dictionary(JsonElement element)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject()) result[property.Name] = Convert(property.Value);
            return result;
        }
        static object[] Array(JsonElement element)
        {
            var result = new List<object>();
            foreach (var item in element.EnumerateArray()) result.Add(Convert(item));
            return result.ToArray();
        }
        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value.GetType() == typeof(object)) { writer.WriteStartObject(); writer.WriteEndObject(); }
            else JsonSerializer.Serialize(writer, value, value.GetType(), Context);
        }
    }
}

[JsonSourceGenerationOptions(IncludeFields = true, PropertyNameCaseInsensitive = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(State))]
[JsonSerializable(typeof(Release))]
[JsonSerializable(typeof(Release[]))]
[JsonSerializable(typeof(List<AppSet>))]
[JsonSerializable(typeof(AppSet[]))]
[JsonSerializable(typeof(Dictionary<string,object>))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
internal partial class StorageJsonContext : JsonSerializerContext { }
