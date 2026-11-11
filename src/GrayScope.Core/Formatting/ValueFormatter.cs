using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace GrayScope.Core.Formatting;

public enum ValueFormat
{
    Auto,
    Json,
    Xml,
    Plain,
}

/// <summary>Pretty-prints cell values for the value viewer. Never throws: unparseable input is returned as-is.</summary>
public static class ValueFormatter
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Formats <paramref name="value"/>; returns the format actually applied.</summary>
    public static (ValueFormat Applied, string Text) Format(string value, ValueFormat requested)
    {
        switch (requested)
        {
            case ValueFormat.Json:
                return TryJson(value) is { } json ? (ValueFormat.Json, json) : (ValueFormat.Plain, value);
            case ValueFormat.Xml:
                return TryXml(value) is { } xml ? (ValueFormat.Xml, xml) : (ValueFormat.Plain, value);
            case ValueFormat.Plain:
                return (ValueFormat.Plain, value);
            default:
                var trimmed = value.TrimStart();
                if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
                {
                    if (TryJson(value) is { } autoJson)
                    {
                        return (ValueFormat.Json, autoJson);
                    }
                }
                if (trimmed.StartsWith('<') && TryXml(value) is { } autoXml)
                {
                    return (ValueFormat.Xml, autoXml);
                }
                return (ValueFormat.Plain, value);
        }
    }

    public static string? TryJson(string value)
    {
        try
        {
            using var doc = JsonDocument.Parse(value);
            return JsonSerializer.Serialize(doc.RootElement, Indented);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Log values are untrusted: DTDs are prohibited outright (no XXE, no entity-expansion DoS)
    /// and no external resources are resolved.
    /// </summary>
    private static readonly XmlReaderSettings SafeXml = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        MaxCharactersInDocument = 10_000_000,
    };

    public static string? TryXml(string value)
    {
        try
        {
            using var text = new StringReader(value.Trim());
            using var reader = XmlReader.Create(text, SafeXml);
            return XDocument.Load(reader).ToString();
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
