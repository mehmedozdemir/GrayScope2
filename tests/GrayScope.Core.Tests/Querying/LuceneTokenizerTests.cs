using GrayScope.Core.Querying;

namespace GrayScope.Core.Tests.Querying;

public class LuceneTokenizerTests
{
    private static List<(LuceneTokenKind Kind, string Text)> Tokens(string text) =>
        LuceneTokenizer.Tokenize(text).Select(t => (t.Kind, text.Substring(t.Start, t.Length))).ToList();

    [Fact]
    public void Tokenize_classifies_common_constructs()
    {
        var tokens = Tokens("NetworkId:{NetworkId} AND NOT status:500 OR msg:\"time out\" host:web* range:[1 TO 5]");

        Assert.Equal(
            [
                (LuceneTokenKind.Field, "NetworkId:"),
                (LuceneTokenKind.Parameter, "{NetworkId}"),
                (LuceneTokenKind.Operator, "AND"),
                (LuceneTokenKind.Operator, "NOT"),
                (LuceneTokenKind.Field, "status:"),
                (LuceneTokenKind.Number, "500"),
                (LuceneTokenKind.Operator, "OR"),
                (LuceneTokenKind.Field, "msg:"),
                (LuceneTokenKind.Quoted, "\"time out\""),
                (LuceneTokenKind.Field, "host:"),
                (LuceneTokenKind.Wildcard, "web*"),
                (LuceneTokenKind.Field, "range:"),
                (LuceneTokenKind.Range, "[1 TO 5]"),
            ],
            tokens);
    }

    [Theory]
    [InlineData("\"unterminated")]
    [InlineData("a:[1 TO")]
    [InlineData("{")]
    [InlineData("\\")]
    public void Tokenize_never_throws_on_incomplete_input(string text)
    {
        var tokens = LuceneTokenizer.Tokenize(text);
        Assert.All(tokens, t => Assert.True(t.Start + t.Length <= text.Length));
    }

    [Fact]
    public void WordBefore_returns_identifier_prefix()
    {
        Assert.Equal("http_sta", LuceneTokenizer.WordBefore("a AND http_sta", 14));
    }
}
