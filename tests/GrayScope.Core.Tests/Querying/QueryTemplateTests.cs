using GrayScope.Core.Errors;
using GrayScope.Core.Querying;

namespace GrayScope.Core.Tests.Querying;

public class QueryTemplateTests
{
    [Fact]
    public void ExtractParameters_returns_distinct_parameters_in_order_with_modifiers()
    {
        var parameters = QueryTemplate.ExtractParameters("NetworkId:{NetworkId} AND city:{City?} AND lvl:{Level:3} AND x:{Opt?:5} AND {NetworkId}");

        Assert.Equal(
            [
                new QueryParameter("NetworkId", "", false),
                new QueryParameter("City", "", true),
                new QueryParameter("Level", "3", false),
                new QueryParameter("Opt", "5", true),
            ],
            parameters);
    }

    [Fact]
    public void ExtractParameters_ignores_lucene_ranges()
    {
        Assert.Empty(QueryTemplate.ExtractParameters("status:{200 TO 299} AND size:[1 TO 5]"));
    }

    [Fact]
    public void Render_substitutes_values_and_optional_fallbacks()
    {
        var rendered = QueryTemplate.Render(
            "NetworkId:{NetworkId} AND city:{City?} AND x:{Opt?:5}",
            new Dictionary<string, string> { ["NetworkId"] = " 34 " });

        Assert.Equal("NetworkId:34 AND city:* AND x:5", rendered);
    }

    [Fact]
    public void Render_throws_when_required_parameter_missing()
    {
        var ex = Assert.Throws<ValidationException>(() => QueryTemplate.Render("NetworkId:{NetworkId}", new Dictionary<string, string>()));
        Assert.Contains("NetworkId", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("\"unclosed")]
    public void Render_rejects_unsafe_values(string value)
    {
        Assert.Throws<ValidationException>(() => QueryTemplate.Render("x:{P}", new Dictionary<string, string> { ["P"] = value }));
    }

    [Fact]
    public void Render_accepts_balanced_and_escaped_quotes()
    {
        var rendered = QueryTemplate.Render("x:{P}", new Dictionary<string, string> { ["P"] = "\"a b\" \\\"" });
        Assert.Equal("x:\"a b\" \\\"", rendered);
    }

    [Fact]
    public void ExpandBatch_produces_cartesian_product()
    {
        var combos = QueryTemplate.ExpandBatch(new Dictionary<string, string> { ["N"] = "34, 35,34", ["L"] = "1,2" });

        Assert.Equal(4, combos.Count);
        Assert.Contains(combos, c => c["N"] == "35" && c["L"] == "2");
    }

    [Fact]
    public void ExpandBatch_keeps_single_combination_for_plain_values()
    {
        var combos = QueryTemplate.ExpandBatch(new Dictionary<string, string> { ["N"] = "34" });
        Assert.Single(combos);
    }

    [Fact]
    public void ExpandBatch_rejects_too_many_combinations()
    {
        var many = string.Join(',', Enumerable.Range(1, 51));
        Assert.Throws<ValidationException>(() => QueryTemplate.ExpandBatch(new Dictionary<string, string> { ["N"] = many }));
    }
}
