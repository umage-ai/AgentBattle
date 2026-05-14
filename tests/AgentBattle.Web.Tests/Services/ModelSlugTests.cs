using AgentBattle.Web.Services;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Web.Tests.Services;

public class ModelSlugTests
{
    [Theory]
    [InlineData("gpt-4o-mini", "gpt-4o-mini")]
    [InlineData("Claude-Haiku-4.5", "claude-haiku-4-5")]
    [InlineData("llama3:8b", "llama3-8b")]
    [InlineData("  Spaces   And  STUFF ", "spaces-and-stuff")]
    [InlineData("---trim---", "trim")]
    [InlineData("multi___under", "multi-under")]
    public void For_normalises_to_slug(string input, string expected)
    {
        ModelSlug.For(input).Should().Be(expected);
    }

    [Fact]
    public void For_is_idempotent()
    {
        var once = ModelSlug.For("Claude-Haiku-4.5");
        ModelSlug.For(once).Should().Be(once);
    }

    [Fact]
    public void For_returns_empty_for_null_or_whitespace()
    {
        ModelSlug.For(null!).Should().BeEmpty();
        ModelSlug.For("   ").Should().BeEmpty();
    }
}
