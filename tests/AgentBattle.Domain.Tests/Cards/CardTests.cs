using AgentBattle.Domain.Cards;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Domain.Tests.Cards;

public class CardTests
{
    [Theory]
    [InlineData("As", Rank.Ace, Suit.Spades)]
    [InlineData("Td", Rank.Ten, Suit.Diamonds)]
    [InlineData("2c", Rank.Two, Suit.Clubs)]
    [InlineData("Kh", Rank.King, Suit.Hearts)]
    public void Parse_returns_card_for_two_char_notation(string input, Rank rank, Suit suit)
    {
        var card = Card.Parse(input);
        card.Rank.Should().Be(rank);
        card.Suit.Should().Be(suit);
    }

    [Fact]
    public void ToString_round_trips_through_Parse()
    {
        foreach (var rank in System.Enum.GetValues<Rank>())
        foreach (var suit in System.Enum.GetValues<Suit>())
        {
            var card = new Card(rank, suit);
            Card.Parse(card.ToString()).Should().Be(card);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Asd")]
    [InlineData("1s")]
    [InlineData("Az")]
    public void Parse_throws_for_invalid_notation(string input)
    {
        var act = () => Card.Parse(input);
        act.Should().Throw<System.FormatException>();
    }
}
