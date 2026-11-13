using System.Globalization;
using System.Text;
using System.Text.Json;
using static System.FormattableString;

namespace EquityBrief.Web.App;

// The plan the test of the buy and sell points recommends for a pick's kind on its index, priced on the pick's own levels on
// the night: its combination in words, tonight's close, the price it waits to dip to and for how many trading days, none for
// a buy at the next open, where it gives up, how it sells, a target, prices it sells parts at, a give-up price rising behind
// the highest close, a bounce, or none of them, and the trading days it is held at most; and its test from 2019, how many
// fills its mean rests on and its mean against simply buying the index's other stocks beside the plan the rules trade.
// see: A watched pick on Home draws the plan the long test recommends for its kind of pick, priced on its own levels and not traded
public sealed record AlternativePlan(
    string Combination,
    decimal Close,
    decimal? Buy,
    int Window,
    decimal GiveUp,
    decimal? Target,
    IReadOnlyList<decimal> Parts,
    decimal? Trail,
    bool Bounce,
    int Cap,
    int From,
    int Through,
    int? Fills,
    double? Edge,
    double? Traded,
    string? Unplaced = null)
{
    // Why the stock's levels place none of the plan tonight: a zone it does not draw, or a plan whose prices do not stand.
    public const string NoZone = "zone";
    // The plan a card stored, none where its text does not read as one.
    public static AlternativePlan? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            static decimal? Price(JsonElement root, string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? decimal.Parse(value.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture)
                    : null;

            static double? Figure(JsonElement root, string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

            static int? Count(JsonElement root, string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

            var unplaced = root.TryGetProperty("unplaced", out var why) && why.ValueKind == JsonValueKind.String ? why.GetString() : null;

            if (Price(root, "close") is not { } close || Count(root, "cap") is not { } cap
                || (unplaced is null ? Price(root, "stop") : 0m) is not { } giveUp
                || !root.TryGetProperty("combination", out var combination) || combination.GetString() is not { } words)
            {
                return null;
            }

            decimal[] parts = root.TryGetProperty("staged", out var staged) && staged.ValueKind == JsonValueKind.Array
                ? [.. staged.EnumerateArray().Where(part => part.ValueKind == JsonValueKind.String).Select(part => decimal.Parse(part.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture))]
                : [];

            return new AlternativePlan(
                words,
                close,
                Price(root, "buy"),
                Count(root, "window") ?? 0,
                giveUp,
                Price(root, "target"),
                parts,
                Price(root, "trail"),
                root.TryGetProperty("bounce", out var bounce) && bounce.ValueKind == JsonValueKind.True,
                cap,
                Count(root, "from") ?? EquityBrief.Core.History.LongLedger.StandingFrom.Year,
                Count(root, "through") ?? EquityBrief.Core.History.LongLedger.StandingFrom.Year,
                Count(root, "fills"),
                Figure(root, "edge"),
                Figure(root, "traded"),
                unplaced);
        }
        catch (Exception failure) when (failure is JsonException or FormatException or OverflowException or InvalidOperationException)
        {
            return null;
        }
    }
}

public sealed partial class SinglePageApp
{
    // How far a kind of pick came out for each dollar put at risk, in cents below a dollar, with the word saying which way.
    public static string Cents(double edge, string way) =>
        (int)Math.Round(Math.Abs(edge) * 100, MidpointRounding.AwayFromZero) switch
        {
            0 => "less than 1 cent " + way,
            1 => "about 1 cent " + way,
            < 100 and var cents => Invariant($"about {cents} cents {way}"),
            _ => Invariant($"about ${Math.Abs(edge):0.00} {way}"),
        };

    // A watched pick's plan that its kind's test recommends: how it buys, its three prices, when it is sold at the latest, and
    // what the test found from 2019 against the plan the rules trade, said as not traded.
    // see: A watched pick on Home draws the plan the long test recommends for its kind of pick, priced on its own levels and not traded
    public static string AlternativeBlock(AlternativePlan plan, UniverseChoice reading)
    {
        if (plan.Edge is not { } edge)
        {
            return string.Empty;
        }

        // A recommendation that did no better from 2019 than the plan the rules trade is no better plan, and is said so.
        if (plan.Traded is { } now && edge <= now)
        {
            return Invariant($"<p class=\"home-alt-none\">{Escaped(NoBetterPlan)}</p>");
        }

        var better = edge > 0;
        var heading = better ? "A plan that did better:" : "A plan that lost less:";

        // One the stock's levels place none of tonight says why, with what the test found.
        if (plan.Unplaced is { } why)
        {
            var words = why == AlternativePlan.NoZone ? UnplacedZone : UnplacedPlan;

            return Invariant($"<div class=\"home-alt\" data-alt=\"unplaced\" data-combination=\"{Escaped(plan.Combination)}\"><p class=\"home-alt-h\"><b>{heading}</b> {Escaped(words)}</p><p class=\"home-alt-note\">{Escaped(AlternativeNote(plan, reading))}</p></div>");
        }

        var block = new StringBuilder();

        // From the 23.10 correction of 2026-11-13 the plan's sale as every card draws it, worked from the price it buys at.
        // see: A pick's card draws the sale its traded plan was registered with, worked from the price the plan buys at
        var sale = new EquityBrief.Web.Marks.PlanSaleView(
            EquityBrief.Web.Marks.PlanSaleView.KindOf(plan.Combination),
            plan.Buy ?? plan.Close,
            plan.GiveUp,
            plan.Target,
            [.. plan.Parts.Where(part => part > (plan.Buy ?? plan.Close)).Order()],
            plan.Trail,
            plan.Bounce ? EquityBrief.Core.Families.DipRule.RsiExit : null,
            plan.Cap);

        block.Append(Invariant($"<div class=\"home-alt\" data-alt=\"{(better ? "better" : "less")}\" data-combination=\"{Escaped(plan.Combination)}\">"));
        block.Append(Invariant($"<p class=\"home-alt-h\"><b>{heading}</b> {Escaped(AlternativeLead(plan))}</p>"));
        block.Append("<div class=\"home-prices\">");
        block.Append(plan.Buy is { } level
            ? Invariant($"<div><span>Buy if it dips to about</span><b>{Dollars(level)}</b></div>")
            : Invariant($"<div><span>Buy at about</span><b>{Dollars(plan.Close)}</b></div>"));
        block.Append(Invariant($"<div><span>Sell if it falls to</span><b class=\"stop\">{Dollars(plan.GiveUp)}</b></div>"));

        foreach (var line in EquityBrief.Web.Marks.MarkRenderer.PlanSaleLines(sale, "this plan"))
        {
            block.Append(Invariant($"<div data-sale=\"{line.Key}\"><span>{Escaped(line.Label)}</span><b class=\"aim\">{Escaped(line.Value)}</b>{(line.Note is { } note ? Invariant($"<span class=\"sale-note\">{Escaped(note)}</span>") : string.Empty)}</div>"));
        }

        block.Append("</div>");
        block.Append(Invariant($"<p class=\"home-alt-note\">{Escaped(AlternativeNote(plan, reading))}</p>"));
        block.Append("</div>");

        return block.ToString();
    }

    public const string NoBetterPlan = "The plan the test recommends for picks like this did no better from 2019 than the plan the rules use, so there is no better plan to show.";

    public const string UnplacedZone = "It waits to buy at a lower price where buyers stepped in before, and tonight this stock has no such price far enough below, so the plan gives no prices for it.";

    public const string UnplacedPlan = "Its prices cannot be set for this stock tonight, so the plan gives none for it.";

    // How the plan buys and when it is sold at the latest, in words.
    public static string AlternativeLead(AlternativePlan plan)
    {
        var buys = plan.Buy is not null
            ? Invariant($"Wait for a lower price: buy only if it dips to the price below within {plan.Window} trading days; if it never does, there is no trade.")
            : "Buy at the next open, as the rules do, and sell it differently.";
        var sells = plan.Trail is { } trail
            ? Invariant($" The give-up price rises behind its highest close by {Dollars(trail)}, and it is sold after {plan.Cap} trading days at the latest.")
            : plan.Bounce
                ? Invariant($" It is sold at the next open after a strong bounce, or after {plan.Cap} trading days at the latest.")
                : plan.Target is not null || plan.Parts.Count > 0
                    ? Invariant($" It is sold after {plan.Cap} trading days at the latest.")
                    : string.Empty;

        return buys + sells;
    }

    // What the test found for the plan from 2019 against simply buying the index's other stocks, beside the plan the rules
    // trade, and that the rules do not trade it.
    public static string AlternativeNote(AlternativePlan plan, UniverseChoice reading)
    {
        var tested = Invariant($"We tested this plan on {reading.Name} stocks from {plan.From} to {plan.Through} the same way");
        var traded = plan.Traded is { } now ? Invariant($", against {Cents(now, now > 0 ? "better" : "worse")} with the plan the rules use") : string.Empty;

        return plan.Edge is not { } edge
            ? tested + ". The rules do not trade it."
            : edge > 0
                ? Invariant($"{tested}: picks like this did {Cents(edge, "better")} for every $1 put at risk than simply buying other {reading.Name} stocks on the same days{traded}. The rules do not trade it yet.")
                : Invariant($"{tested}: it also did worse than simply buying other {reading.Name} stocks on the same days, {Cents(edge, "worse")} for every $1 put at risk{traded}. The rules do not trade it.");
    }

    static string Listed(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
}
