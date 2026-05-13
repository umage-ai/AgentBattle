using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentBattle.Domain.Json;

public static class BattleEventJsonOptions
{
    public static readonly JsonSerializerOptions Default = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        opts.Converters.Add(new CardJsonConverter());
        return opts;
    }
}

public sealed class CardJsonConverter : JsonConverter<AgentBattle.Domain.Cards.Card>
{
    public override AgentBattle.Domain.Cards.Card Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        => AgentBattle.Domain.Cards.Card.Parse(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, AgentBattle.Domain.Cards.Card value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
