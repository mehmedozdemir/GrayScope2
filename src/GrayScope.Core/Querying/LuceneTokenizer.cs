namespace GrayScope.Core.Querying;

public enum LuceneTokenKind
{
    Text,
    Field,
    Operator,
    Quoted,
    Parameter,
    Range,
    Wildcard,
    Number,
    Grouping,
    Comment,
}

public readonly record struct LuceneToken(LuceneTokenKind Kind, int Start, int Length);

/// <summary>
/// Lightweight, error-tolerant tokenizer used for syntax highlighting and completion context.
/// It never throws: unknown input is reported as <see cref="LuceneTokenKind.Text"/>.
/// </summary>
public static class LuceneTokenizer
{
    private static readonly HashSet<string> WordOperators = new(StringComparer.Ordinal) { "AND", "OR", "NOT", "TO" };

    public static IReadOnlyList<LuceneToken> Tokenize(string? text)
    {
        var tokens = new List<LuceneToken>();
        if (string.IsNullOrEmpty(text))
        {
            return tokens;
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var next = c switch
            {
                '"' => ReadString(text, i, tokens),
                '{' => ReadBrace(text, i, tokens),
                '[' => ReadRange(text, i, ']', tokens),
                '(' or ')' => Add(tokens, LuceneTokenKind.Grouping, i, 1),
                '&' or '|' when i + 1 < text.Length && text[i + 1] == c => Add(tokens, LuceneTokenKind.Operator, i, 2),
                '!' or '+' or '-' when IsTermStart(text, i) => Add(tokens, LuceneTokenKind.Operator, i, 1),
                _ => ReadWord(text, i, tokens),
            };
            i = Math.Max(next, i + 1);
        }
        return tokens;
    }

    /// <summary>Returns the identifier-like word that ends at <paramref name="caret"/> (used as completion prefix).</summary>
    public static string WordBefore(string text, int caret)
    {
        var start = caret;
        while (start > 0 && IsWordChar(text[start - 1]))
        {
            start--;
        }
        return text[start..caret];
    }

    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '.' or '@' or '-';

    private static int Add(List<LuceneToken> tokens, LuceneTokenKind kind, int start, int length)
    {
        tokens.Add(new LuceneToken(kind, start, length));
        return start + length;
    }

    private static bool IsTermStart(string text, int i) => i == 0 || char.IsWhiteSpace(text[i - 1]) || text[i - 1] == '(';

    private static int ReadString(string text, int start, List<LuceneToken> tokens)
    {
        var i = start + 1;
        while (i < text.Length && text[i] != '"')
        {
            i += text[i] == '\\' ? 2 : 1;
        }
        var end = Math.Min(i + 1, text.Length);
        return Add(tokens, LuceneTokenKind.Quoted, start, end - start);
    }

    private static int ReadBrace(string text, int start, List<LuceneToken> tokens)
    {
        var match = QueryTemplate.ParameterRegex().Match(text, start);
        if (match.Success && match.Index == start)
        {
            return Add(tokens, LuceneTokenKind.Parameter, start, match.Length);
        }
        return ReadRange(text, start, '}', tokens);
    }

    private static int ReadRange(string text, int start, char close, List<LuceneToken> tokens)
    {
        var end = text.IndexOfAny([close, close == ']' ? '}' : ']'], start + 1);
        var length = (end < 0 ? text.Length : end + 1) - start;
        return Add(tokens, LuceneTokenKind.Range, start, length);
    }

    private static int ReadWord(string text, int start, List<LuceneToken> tokens)
    {
        var i = start;
        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('(' or ')' or '"' or '{' or '['))
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == ':')
            {
                // "field:" — the colon belongs to the field token.
                return Add(tokens, LuceneTokenKind.Field, start, i - start + 1);
            }
            i++;
        }
        i = Math.Min(i, text.Length);

        var word = text[start..i];
        var kind = WordOperators.Contains(word) ? LuceneTokenKind.Operator
            : word.Contains('*') || word.Contains('?') ? LuceneTokenKind.Wildcard
            : double.TryParse(word, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) ? LuceneTokenKind.Number
            : LuceneTokenKind.Text;
        return Add(tokens, kind, start, i - start);
    }
}
