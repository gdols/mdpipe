using System.Text.Json;
using System.Text.Json.Serialization;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Models;

namespace MdPipe.Infrastructure.Manifest;

/// <summary>Shared JSON (de)serialization for the compatibility manifest.</summary>
internal static class ManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new DateOnlyJsonConverter() }
    };

    public static CompatibilityManifest Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<ManifestDto>(json, Options)
            ?? throw new ManifestException("Manifest deserialized to null.");
        return dto.ToModel();
    }

    private sealed class ManifestDto
    {
        public int SchemaVersion { get; set; }
        public string StableVersion { get; set; } = string.Empty;
        public string MinimumVersion { get; set; } = string.Empty;
        public List<string> CompatibleVersions { get; set; } = [];
        public string UpdatedAt { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;

        public CompatibilityManifest ToModel() => new()
        {
            SchemaVersion = SchemaVersion,
            StableVersion = StableVersion,
            MinimumVersion = MinimumVersion,
            CompatibleVersions = CompatibleVersions.AsReadOnly(),
            UpdatedAt = DateOnly.TryParse(UpdatedAt, out var d) ? d : DateOnly.MinValue,
            Notes = Notes
        };
    }

    private sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
    {
        public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateOnly.Parse(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("yyyy-MM-dd"));
    }
}
