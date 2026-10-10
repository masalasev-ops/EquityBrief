using System.Globalization;
using System.Text.Json;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// What a page hands its cards beyond what the night stored: the operator's account where it is set, their open taken
// trades, the trades taken from the cards drawn, and whether the page draws the card's presses. An export hands none.
public sealed record CardContext(EquityBrief.Core.Cards.AccountSettings? Account, IReadOnlyList<TakenTradeRow> Open, IReadOnlyList<TakenTradeRow> Taken, bool Pressable, IReadOnlyList<TakenRecordRow>? Records = null)
{
    public static CardContext None { get; } = new(null, [], [], false);
}

// The projection from the cards the night stored to the cards a page draws in place beneath each pick. It reads back
// each line's verdict and words and the rule's record as the night wrote them, words the rule's management from the
// stored plan, and works out two things alone where the card is drawn, since the store holds neither: the plan in the
// operator's money from their account, and the sixth line from their open trades.
// see: A screen reads and renders, and each figure it works out has one function in the core
// see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
public static class CardScreen
{
    // A stored card as the page draws it.
    public static DecisionCardView View(DecisionCardRow row, CardContext? context = null)
    {
        var given = context ?? CardContext.None;

        using var lines = JsonDocument.Parse(row.Lines);
        List<CardLineView> drawn =
        [
            .. lines.RootElement.EnumerateArray().Select(line => new CardLineView(
                line.GetProperty("line").GetInt32(),
                line.GetProperty("name").GetString() ?? string.Empty,
                line.GetProperty("verdict").GetString() ?? string.Empty,
                line.GetProperty("words").GetString() ?? string.Empty)),
        ];

        // The sixth line reads the operator's own trades, so a page drawing no presses, an export among them, draws none.
        if (given.Pressable)
        {
            var concentration = EquityBrief.Core.Cards.CardLines.Concentration(row.Ticker, row.Sector, [.. given.Open.Select(trade => (trade.Ticker, trade.Sector))]);

            drawn.Add(new CardLineView(concentration.Number, concentration.Name, concentration.Mark, concentration.Words));
        }

        var heavyweights = string.Equals(row.Family, EquityBrief.Core.Families.HeavyweightRule.Name, StringComparison.Ordinal);
        var money = given.Account is { } account
            ? EquityBrief.Core.Cards.PositionSize.Of(account, row.Entry, row.Stop, heavyweights ? row.BookHoldings : null, row.RoundTrip)
            : null;

        return new DecisionCardView(
            row.Index,
            row.Night,
            row.Family,
            row.Ticker,
            row.Rule,
            row.Entry,
            row.Stop,
            row.Target,
            drawn,
            row.Record is { } stored ? Record(stored) : null,
            EquityBrief.Core.Cards.Management.For(row.Stop, row.Target, row.Trail, row.Cap, heavyweights),
            money is { } plan && given.Account is { } held
                ? new CardMoneyView(plan.Shares, plan.AtRisk, plan.Value, plan.ShareOfAccount, plan.RoundTrip, plan.Capped, plan.WholeAtRisk, held.RiskPercent)
                : null,
            given.Pressable && given.Account is null,
            [
                .. given.Taken
                    .Where(trade => trade.Ticker == row.Ticker && trade.Index == row.Index && trade.Family == row.Family && trade.Night == row.Night)
                    .Select(trade => new CardTakenView(trade.TakenAt, trade.Fill, trade.FillDate, trade.Provisional, trade.ExitPrice, trade.ExitDate, trade.FollowedThrough is not null, trade.EndedOn, trade.EndReason, trade.EndPrice)),
            ],
            given.Pressable,
            row.Hits is { } hitsStored ? Hits(hitsStored) : null,
            given.Pressable && (given.Records ?? []).FirstOrDefault(mine => mine.Index == row.Index && mine.Family == row.Family) is { } kept
                ? new CardOperatorRecordView(kept.Unit, kept.Won, kept.Lost, kept.Ended, kept.Open, kept.Average, EquityBrief.Core.Cards.TakenWalk.RecordMinimum, heavyweights || EquityBrief.Core.Families.SetupFamilies.Named(row.Family) is { Trails: true },
                    new CardSameNightsView(kept.SameNights, kept.RuleListed, kept.RuleWon, kept.RuleLost, kept.RuleEnded, kept.RuleAverage))
                : null,
            row.Similar is { } similar ? new CardScoreView(row.ScoreRank, EquityBrief.Core.Cards.CardSimilar.Read(similar)) : null,
            row.Approved);
    }

    // What could hit the trade, as the night stored it on the card.
    static CardHitsView Hits(string stored)
    {
        using var document = JsonDocument.Parse(stored);
        var hits = document.RootElement;
        var reactions = hits.GetProperty("reactions");
        var dividend = hits.TryGetProperty("dividend", out var paid) && paid.ValueKind == JsonValueKind.Object ? paid : (JsonElement?)null;

        double? Number(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

        DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new CardHitsView(
            Day(hits.GetProperty("through").GetString()!),
            reactions.GetProperty("count").GetInt32(),
            Number(reactions, "medianTypical"),
            Number(reactions, "medianRisks"),
            reactions.GetProperty("pastTheStop").GetInt32(),
            dividend is { } on ? Day(on.GetProperty("date").GetString()!) : null,
            dividend is { } declared && declared.GetProperty("declared").GetBoolean(),
            dividend is { } amount && amount.GetProperty("amount").GetString() is { } text ? decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture) : null,
            dividend is { } risks ? Number(risks, "inRisks") : null,
            dividend is { } percent ? Number(percent, "inPercent") : null,
            [.. hits.GetProperty("events").EnumerateArray().Select(one => (Day(one.GetProperty("date").GetString()!), one.GetProperty("name").GetString()!))],
            [.. hits.GetProperty("pastTheTable").EnumerateArray().Select(kind => kind.GetString()!)],
            hits.TryGetProperty("dividendUnread", out var unread) && unread.ValueKind == JsonValueKind.True);
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
    public static IReadOnlyList<FamilyCardView> WithCards(IReadOnlyList<FamilyCardView> cards, IReadOnlyList<DecisionCardRow> stored, CardContext? context = null)
    {
        var byPick = stored
            .GroupBy(row => (row.Family, row.Ticker))
            .ToDictionary(group => group.Key, group => View(group.First(), context));

        return
        [
            .. cards.Select(card => card with
            {
                Picks = [.. card.Picks.Select(pick => byPick.TryGetValue((card.Family, pick.Row.Ticker), out var view) ? pick with { Card = view } : pick)],
            }),
        ];
    }

    // The sector heavyweights' holdings, each with the card of the night its book bought it on.
    public static HeavyweightCardView WithCards(HeavyweightCardView card, IReadOnlyList<DecisionCardRow> bought, CardContext? context = null)
    {
        var byBuy = bought
            .GroupBy(row => (row.Ticker, row.Night))
            .ToDictionary(group => group.Key, group => View(group.First(), context));

        return card with
        {
            Holdings = [.. card.Holdings.Select(holding => byBuy.TryGetValue((holding.Ticker, holding.HeldSince), out var view) ? holding with { Card = view } : holding)],
        };
    }
}
