using GrayScope.Core.Formatting;
using GrayScope.Core.Querying;

namespace GrayScope.Core.Tests.Querying;

public class LuceneTermsAndFormatterTests
{
    [Fact]
    public void FieldEquals_quotes_and_escapes_value()
    {
        Assert.Equal("msg:\"say \\\"hi\\\" C:\\\\x\"", LuceneTerms.FieldEquals("msg", "say \"hi\" C:\\x"));
    }

    [Fact]
    public void FieldEquals_with_empty_value_matches_missing_field()
    {
        Assert.Equal("NOT _exists_:city", LuceneTerms.FieldEquals("city", ""));
    }

    [Theory]
    [InlineData("a:1", "b:\"2\"", false, "a:1 AND b:\"2\"")]
    [InlineData("a:1 OR a:2", "b:\"2\"", false, "(a:1 OR a:2) AND b:\"2\"")]
    [InlineData("", "b:\"2\"", true, "* AND NOT (b:\"2\")")]
    [InlineData("a:1", "b:\"2\"", true, "a:1 AND NOT (b:\"2\")")]
    public void Append_combines_terms_safely(string query, string term, bool exclude, string expected)
    {
        Assert.Equal(expected, LuceneTerms.Append(query, term, exclude));
    }

    [Fact]
    public void Auto_format_detects_json_and_xml()
    {
        var (jsonKind, json) = ValueFormatter.Format("{\"a\":1,\"ş\":[1,2]}", ValueFormat.Auto);
        var (xmlKind, xml) = ValueFormatter.Format("<a><b>1</b></a>", ValueFormat.Auto);

        Assert.Equal(ValueFormat.Json, jsonKind);
        Assert.Contains("\"ş\"", json, StringComparison.Ordinal);
        Assert.Equal(ValueFormat.Xml, xmlKind);
        Assert.Contains("\n", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_input_falls_back_to_plain()
    {
        Assert.Equal((ValueFormat.Plain, "{broken"), ValueFormatter.Format("{broken", ValueFormat.Json));
        Assert.Equal(ValueFormat.Plain, ValueFormatter.Format("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/x\">]><x>&e;</x>", ValueFormat.Xml).Applied);
    }
}
