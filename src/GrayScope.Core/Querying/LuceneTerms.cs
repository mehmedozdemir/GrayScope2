using System.Text;

namespace GrayScope.Core.Querying;

/// <summary>Builds safe Lucene terms from grid values ("add to query" / "exclude" actions).</summary>
public static class LuceneTerms
{
    /// <summary>Returns <c>field:"value"</c> with quotes and backslashes escaped; empty value becomes "field is missing".</summary>
    public static string FieldEquals(string field, string value)
    {
        var name = EscapeFieldName(field);
        return value.Length == 0 ? $"NOT _exists_:{name}" : $"{name}:\"{EscapeQuoted(value)}\"";
    }

    /// <summary>Appends a term to an existing query with AND / AND NOT, wrapping the original in parentheses when needed.</summary>
    public static string Append(string query, string term, bool exclude)
    {
        var trimmed = query.Trim();
        var clause = exclude ? $"NOT ({term})" : term;
        if (trimmed.Length == 0 || trimmed == "*")
        {
            return exclude ? $"* AND {clause}" : clause;
        }
        var needsParens = trimmed.Contains(" OR ", StringComparison.Ordinal) && !(trimmed.StartsWith('(') && trimmed.EndsWith(')'));
        return $"{(needsParens ? $"({trimmed})" : trimmed)} AND {clause}";
    }

    internal static string EscapeQuoted(string value)
    {
        var sb = new StringBuilder(value.Length + 4);
        foreach (var c in value)
        {
            if (c is '"' or '\\')
            {
                sb.Append('\\');
            }
            sb.Append(c is '\r' or '\n' ? ' ' : c);
        }
        return sb.ToString();
    }

    private static string EscapeFieldName(string field)
    {
        var sb = new StringBuilder(field.Length);
        foreach (var c in field)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '.' or '@' or '-'))
            {
                sb.Append('\\');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
