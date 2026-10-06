using System.Globalization;
using System.Text.Json;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from the cards the night stored to the cards a page draws in place beneath each pick. It computes
// nothing: the night wrote each line's verdict and words and the rule's record as the card read it, and this reads them
// back and hands each card to the row it belongs to.
// see: A screen reads and renders, and computes nothing
// see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
public static class CardScreen
{
    // A stored card as the page draws it.
    public static DecisionCardView View(DecisionCardRow row)
    {
        using var lines = JsonDocument.Parse(row.Lines);
        CardLineView[] drawn =
        [
            .. lines.RootElement.EnumerateArray().Select(line => new CardLineView(
                line.GetProperty("line").GetInt32(),
                line.GetProperty("name").GetString() ?? string.Empty,
                line.GetProperty("verdict").GetString() ?? string.Empty,
                line.GetProperty("words").GetString() ?? string.Empty)),
        ];

        return new DecisionCardView(row.Index, row.Night, row.Family, row.Ticker, row.Rule, row.Entry, row.Stop, row.Target, drawn, row.Record is { } stored ? Record(stored) : null);
    }

    static CardRecordView Record(string stored)
    {
        using var document = JsonDocument.Parse(stored);
        var record = document.RootElement;

        double? Number(string name) => record.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

        int? Whole(string name) => record.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

        return new CardRecordView(
            record.GetProperty("rule").GetString() ?? string.Empty,
            record.GetProperty("trades").GetInt32(),
            Number("won"),
            Number("average"),
            record.GetProperty("unit").GetString() ?? string.Empty,
            Whole("medianSessions"),
            Whole("heldSessions"),
            Number("heldShare") ?? EquityBrief.Core.Cards.CardSettings.DefaultHeldShare,
            Number("worstClose"),
            DateOnly.ParseExact(record.GetProperty("from").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(record.GetProperty("through").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            record.GetProperty("membership").GetString() ?? string.Empty);
    }

    // Each family's picks with the cards the night stored for them, matched by family and stock; a pick the night
    // stored no card for is drawn as it was.
    public static IReadOnlyList<FamilyCardView> WithCards(IReadOnlyList<FamilyCardView> cards, IReadOnlyList<DecisionCardRow> stored)
    {
        var byPick = stored
            .GroupBy(row => (row.Family, row.Ticker))
            .ToDictionary(group => group.Key, group => View(group.First()));

        return
        [
            .. cards.Select(card => card with
            {
                Picks = [.. card.Picks.Select(pick => byPick.TryGetValue((card.Family, pick.Row.Ticker), out var view) ? pick with { Card = view } : pick)],
            }),
        ];
    }

    // The sector heavyweights' holdings, each with the card of the night its book bought it on.
    public static HeavyweightCardView WithCards(HeavyweightCardView card, IReadOnlyList<DecisionCardRow> bought)
    {
        var byBuy = bought
            .GroupBy(row => (row.Ticker, row.Night))
            .ToDictionary(group => group.Key, group => View(group.First()));

        return card with
        {
            Holdings = [.. card.Holdings.Select(holding => byBuy.TryGetValue((holding.Ticker, holding.HeldSince), out var view) ? holding with { Card = view } : holding)],
        };
    }
}
