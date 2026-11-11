using System.Text.RegularExpressions;
using GrayScope.Core.Errors;

namespace GrayScope.Core.Querying;

/// <summary>A named placeholder inside a query template.</summary>
/// <param name="Name">Identifier between braces.</param>
/// <param name="DefaultValue">Value after <c>:</c>, empty when absent.</param>
/// <param name="IsOptional">True for <c>{Name?}</c>: an empty value becomes the default or <c>*</c>.</param>
public sealed record QueryParameter(string Name, string DefaultValue, bool IsOptional);

/// <summary>
/// Parameter syntax (compatible with GrayScope 1):
/// <list type="bullet">
/// <item><c>{Name}</c> required</item>
/// <item><c>{Name?}</c> optional, empty → <c>*</c></item>
/// <item><c>{Name:v}</c> required, pre-filled with v</item>
/// <item><c>{Name?:v}</c> optional, empty → v</item>
/// </list>
/// Names must be identifiers so Lucene ranges such as <c>{1 TO 5}</c> are never mistaken for parameters.
/// </summary>
public static partial class QueryTemplate
{
    /// <summary>Upper bound for batch fan-out so a typo cannot fire hundreds of searches.</summary>
    public const int MaxBatchCombinations = 50;

    /// <summary>Upper bound for a single parameter value.</summary>
    public const int MaxValueLength = 1000;

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)(\?)?(?::([^{}]*))?\}")]
    internal static partial Regex ParameterRegex();

    /// <summary>Distinct parameters in first-seen order.</summary>
    public static IReadOnlyList<QueryParameter> ExtractParameters(string? template)
    {
        var result = new List<QueryParameter>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in ParameterRegex().Matches(template ?? ""))
        {
            var name = m.Groups[1].Value;
            if (seen.Add(name))
            {
                result.Add(new QueryParameter(name, m.Groups[3].Value, m.Groups[2].Success));
            }
        }
        return result;
    }

    /// <summary>Substitutes parameter values. Throws <see cref="ValidationException"/> for missing required values.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        return ParameterRegex().Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            var isOptional = m.Groups[2].Success;
            var fallback = m.Groups[3].Value;
            var value = values.TryGetValue(name, out var v) ? v.Trim() : "";
            if (value.Length > 0)
            {
                ParameterValueValidator.Validate(name, value);
                return value;
            }
            if (isOptional)
            {
                return fallback.Length > 0 ? fallback : "*";
            }
            throw new ValidationException($"'{name}' parametresi için bir değer girin.");
        });
    }

    /// <summary>
    /// Expands comma-separated inputs into every combination (cartesian product).
    /// Parameters with a single value contribute one entry, so a non-batch input yields exactly one combination.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> ExpandBatch(IReadOnlyDictionary<string, string> rawValues)
    {
        IEnumerable<Dictionary<string, string>> combos = [new Dictionary<string, string>(StringComparer.Ordinal)];
        foreach (var (name, raw) in rawValues)
        {
            var options = SplitBatchValues(raw);
            combos = combos.SelectMany(c => options.Select(o => new Dictionary<string, string>(c, StringComparer.Ordinal) { [name] = o }));
        }

        var list = combos.Take(MaxBatchCombinations + 1).ToList();
        if (list.Count > MaxBatchCombinations)
        {
            throw new ValidationException($"Toplu çalıştırma en fazla {MaxBatchCombinations} kombinasyon üretebilir; değer sayısını azaltın.");
        }
        return list;
    }

    /// <summary>Splits <c>"34, 35 ,6"</c> into distinct trimmed values. Empty input yields a single empty value.</summary>
    public static IReadOnlyList<string> SplitBatchValues(string? raw)
    {
        var parts = (raw ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return parts.Count == 0 ? [""] : parts;
    }
}

/// <summary>
/// Defensive check on user-entered parameter values before they are spliced into a Lucene query.
/// The query is sent as data (never executed locally), so this only keeps the query well-formed.
/// </summary>
public static class ParameterValueValidator
{
    public static void Validate(string name, string value)
    {
        if (value.Length > QueryTemplate.MaxValueLength)
        {
            throw new ValidationException($"'{name}' parametresi çok uzun (en fazla {QueryTemplate.MaxValueLength} karakter).");
        }
        if (value.Any(char.IsControl))
        {
            throw new ValidationException($"'{name}' parametresi satır sonu veya kontrol karakteri içeremez.");
        }
        if (CountUnescapedQuotes(value) % 2 != 0)
        {
            throw new ValidationException($"'{name}' parametresinde kapanmamış tırnak (\") var.");
        }
    }

    private static int CountUnescapedQuotes(string value)
    {
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\')
            {
                i++;
                continue;
            }
            if (value[i] == '"')
            {
                count++;
            }
        }
        return count;
    }
}
