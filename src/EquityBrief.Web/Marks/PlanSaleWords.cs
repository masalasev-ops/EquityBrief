using System.Globalization;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.Marks;

// From the 23.10 correction of 2026-11-13 the sale a plan was registered with, worked from the price it buys at: the sale as the
// plan names it, the stop the plan places from that price, the target it aims at, the staged levels above the buy each selling
// an equal share with the last held to the stop or the time limit, the trail behind the highest close, the dip's bounce, and
// the trading days after which it sells what is left.
// see: A pick's card draws the sale its traded plan was registered with, worked from the price the plan buys at
public sealed record PlanSaleView(string Kind, decimal Buy, decimal? Stop, decimal? Target, IReadOnlyList<decimal> Stages, decimal? Trail, double? RsiAbove, int Cap)
{
    // From the operator's order of 2026-11-14 the price the plan takes profit at, worked from the price it buys at: its own
    // target or first band above, the price above its last staged level what is left sells at, or where its sale names none
    // above the buy the price it turned down at before or the multiple of its risk; none for a plan placed before the order.
    // see: Each rule takes profit in the shape its test chose on the early years, never at the nearest price it turned down at
    public TakeProfitView? TakeProfit { get; init; }

    // A plan selling on its time limit alone, which names no price to sell at.
    public bool OnTimeAlone => Kind == PlanPoints.TimeLimit;

    // From the second 23.10 correction of 2026-11-13 a plan naming no price above its buy to sell at, held to its stop or its
    // time limit: the time limit alone, and the staged sales or the first band above where its levels place none above it.
    // see: A plan whose levels place no price above its buy says so in one line and draws the record of picks like it beneath
    public bool HoldsToItsLimit =>
        TakeProfit is null && (OnTimeAlone || (Kind == PlanPoints.StagedSales && Stages.Count == 0) || (Kind == PlanPoints.FirstBand && Target is null));

    // What the sale aims at, which what the plan risks for what it aims at reads: its target, or the staged levels' mean; none
    // where it names no price above the buy; from the operator's order of 2026-11-14 its take-profit price where it names none
    // of its own.
    public decimal? Aim =>
        Kind != PlanPoints.StagedSales && (Target is not { } own || own <= Buy) && TakeProfit is { } taking && taking.Price > Buy ? taking.Price
        : OnTimeAlone ? null
        : Target is { } target && target > Buy ? target
        : Kind == PlanPoints.StagedSales && Stages.Count > 0 ? Stages.Average()
        : null;

    // What the plan risks for what it aims at, from the price it buys at and the stop it places from it.
    public decimal? RewardPerRisk => Aim is { } aim && Stop is { } stop && Buy > stop ? (aim - Buy) / (Buy - stop) : null;

    // The sale a plan's words name, its last part; the rule's own where the words name none.
    public static string KindOf(string words) =>
        PlanPoints.Sales.FirstOrDefault(sale => words.EndsWith(sale, StringComparison.Ordinal)) ?? PlanPoints.RuleSale;

    // The share each staged level sells and the share kept, the walk selling one part in one more than its levels at each.
    public static (string Each, string Last) SharesOf(int levels) => levels switch
    {
        1 => ("half", "the other half"),
        2 => ("a third", "the last third"),
        3 => ("a quarter", "the last quarter"),
        4 => ("a fifth", "the last fifth"),
        _ => (FormattableString.Invariant($"one part in {levels + 1}"), "the last part"),
    };

    // The staged levels above the buy, lowest first, as the walk sells at them.
    IReadOnlyList<decimal> StagesAbove => [.. Stages.Where(stage => stage > Buy).Order()];

    // On the operator's answer of 2026-11-17 on item 5 the parts a holding sells in as the price rises, each its share of the
    // whole and in the order its price is reached, as the walk sells them: each staged level its equal share or what is left
    // where less, a take-profit on half half of what is held when its price is reached, taken before a level at the same price,
    // and what is left after the last to the stop or the time limit; one part, the whole, for a sale naming no share.
    // see: A pick's card draws the sale its traded plan was registered with, worked from the price the plan buys at
    public IReadOnlyList<SalePart> Parts()
    {
        var stages = StagesAbove;

        if (Kind == PlanPoints.StagedSales && stages.Count > 0)
        {
            var of = 2 * (stages.Count + 1);
            var (held, each) = (of, 2);
            var parts = new List<SalePart>();
            var reached = stages.Select(stage => (Price: stage, Half: false)).ToList();

            if (TakeProfit is { Half: true } half)
            {
                reached.Add((half.Price, true));
            }

            foreach (var (price, halves) in reached.OrderBy(step => step.Price).ThenBy(step => step.Half ? 0 : 1))
            {
                var units = halves ? held / 2 : Math.Min(each, held);

                if (units > 0)
                {
                    parts.Add(new SalePart(halves ? SalePart.HalfOfTheRest : SalePart.Stage, price, units, of));
                    held -= units;
                }
            }

            if (held > 0)
            {
                parts.Add(new SalePart(TakeProfit is { Half: false } ? SalePart.TakeProfitOnTheRest : SalePart.Last, TakeProfit is { Half: false } rest ? rest.Price : null, held, of));
            }

            return parts;
        }

        return TakeProfit is { Half: true } onHalf
            ? [new SalePart(SalePart.HalfOfTheRest, onHalf.Price, 1, 2), new SalePart(SalePart.Last, null, 1, 2)]
            : [new SalePart(SalePart.Last, null, 1, 1)];
    }

    // Whether the plan stages its sales and takes profit on half beside them, which its rows draw part by part.
    public bool StagesAndHalves => Kind == PlanPoints.StagedSales && StagesAbove.Count > 0 && TakeProfit is { Half: true };

    // How the plan is managed in steps, as a card lists them: the stop, then each staged level's share and the share kept, or
    // the target, the trail or the bounce, and the time limit; none of the rule's own exit where the plan sells otherwise.
    public IReadOnlyList<string> Steps()
    {
        var steps = new List<string>();
        static string At(decimal price) => price.ToString("0.00", CultureInfo.InvariantCulture);

        if (Stop is { } stop)
        {
            steps.Add(Trail is { } distance && Target is null && !OnTimeAlone
                ? $"Sell at the next trading day's open after a day that closes below the stop, {At(stop)} at the buy, raised after each close to that close less {At(distance)} and never lowered."
                : $"Sell at the next trading day's open after a day that closes below {At(stop)}.");
        }

        if (StagesAndHalves)
        {
            var parts = Parts();
            var halfPart = parts.First(part => part.Kind == SalePart.HalfOfTheRest);

            foreach (var part in parts)
            {
                steps.Add(part.Kind switch
                {
                    SalePart.Stage => $"Sell {part.Words} at the next trading day's open after a day that closes at or above {At(part.Price!.Value)}.",
                    SalePart.HalfOfTheRest => $"Sell {part.Words}, half of what is left, at the next trading day's open after a day that closes at or above {At(part.Price!.Value)}, the stop on the rest then moving up to the buy, {At(Buy)}.",
                    _ => $"Keep {part.LastWords} until a day that closes below the stop, at the buy once the {halfPart.Noun} at {At(halfPart.Price!.Value)} sells, or the time limit.",
                });
            }
        }
        else if (Kind == PlanPoints.StagedSales && Stages.Count > 0)
        {
            var (each, last) = SharesOf(Stages.Count);

            steps.AddRange(Stages.Select(stage => $"Sell {each} at the next trading day's open after a day that closes at or above {At(stage)}."));
            steps.Add(TakeProfit is { } remainder
                ? $"Sell {last} at the next trading day's open after a day that closes at or above {At(remainder.Price)}."
                : $"Keep {last} until a day that closes below the stop or the time limit.");
        }
        else if (TakeProfit is { } taking)
        {
            // From the operator's order of 2026-11-14 the take-profit price, beside a trail or the dip's bounce; on half, half the
            // holding at it and the stop on the other half then moved up to the buy.
            steps.Add(taking.Half
                ? $"Sell half at the next trading day's open after a day that closes at or above {At(taking.Price)}, the stop on the other half then moving up to the buy, {At(Buy)}."
                : $"Sell at the next trading day's open after a day that closes at or above {At(taking.Price)}.");

            if (RsiAbove is { } level && !OnTimeAlone)
            {
                steps.Add(FormattableString.Invariant($"Or sell at the next trading day's open after a day its {EquityBrief.Core.Families.DipRule.RsiSessions}-day RSI closes above {level:0}."));
            }
        }
        else if (!OnTimeAlone && Target is { } target)
        {
            steps.Add($"Sell at the next trading day's open after a day that closes at or above {At(target)}.");
        }
        else if (!OnTimeAlone && RsiAbove is { } bounce)
        {
            steps.Add(FormattableString.Invariant($"Sell at the next trading day's open after a day its {EquityBrief.Core.Families.DipRule.RsiSessions}-day RSI closes above {bounce:0}."));
        }
        else if (Trail is null || OnTimeAlone)
        {
            steps.Add("No target: the trade is ended by its stop or its time limit.");
        }

        steps.Add(FormattableString.Invariant($"Otherwise sell at the close {Cap} trading days after the buy."));

        return steps;
    }
}

// One line of a plan's sale as a card draws it: its key, its label, its value, and a note beneath where it has one.
public sealed record PlanSaleLine(string Key, string Label, string Value, string? Note = null);

// One part of a holding as its plan sells it on a rise: what sells it, the price a close at or above sells it after, none for
// what is left, and its share of the whole as units of a count, with the share in words.
public sealed record SalePart(string Kind, decimal? Price, int Units, int Of)
{
    public const string Stage = "stage";

    public const string HalfOfTheRest = "half";

    public const string TakeProfitOnTheRest = "take";

    public const string Last = "last";

    static readonly string[] Ordinals = ["", "", "half", "third", "quarter", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh", "twelfth"];

    static readonly string[] Cardinals = ["", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven"];

    (int Part, int Whole) Reduced
    {
        get
        {
            var (a, b) = (Units, Of);

            while (b != 0)
            {
                (a, b) = (b, a % b);
            }

            return (Units / a, Of / a);
        }
    }

    // The share alone, as "sixth", "half" or "three eighths".
    public string Noun => Reduced switch
    {
        (var part, var whole) when part == whole => "whole",
        (1, var whole) when whole < Ordinals.Length => Ordinals[whole],
        (var part, var whole) when whole < Ordinals.Length && part < Cardinals.Length => Cardinals[part] + " " + Ordinals[whole] + "s",
        (var part, var whole) => FormattableString.Invariant($"{part} parts in {whole}"),
    };

    // The share as a row names it: "a third", "an eighth", "half" or "three eighths".
    public string Words => Reduced switch
    {
        (1, 2) => "half",
        (1, 8) or (1, 11) => "an " + Noun,
        (1, _) => "a " + Noun,
        _ => Noun,
    };

    // The share left after every other part, as "the last sixth".
    public string LastWords => "the last " + Noun;

    // A share a row's words name, read back to its part of the whole: each word form above for every whole up to twelve.
    public static decimal? ShareNamed(string words)
    {
        for (var whole = 2; whole < Ordinals.Length; whole++)
        {
            for (var part = 1; part < whole; part++)
            {
                var named = new SalePart(Last, null, part, whole);

                if (named.Reduced == (part, whole) && (words == named.Words || words == named.Noun || words == named.LastWords || (whole == 2 && words == "the other half")))
                {
                    return (decimal)part / whole;
                }
            }
        }

        return null;
    }
}

public sealed partial class MarkRenderer
{
    // What a card selling on its time limit alone says until the plan point test has written its rule's record.
    public const string RecordBeingWorkedOut = "The record for this is being worked out.";

    public const string FirstBandWords = "the first price above the buy where it has turned down before";

    // From the second 23.10 correction of 2026-11-13 what a staged sale or the first band above says where its levels place no
    // price above the buy, as one line, with the record of picks like it beneath.
    public const string NoStagedLevelAbove = "No price level above tonight's close to sell at:";

    public const string NoBandAbove = "No price above tonight's close where it has turned down before:";

    // On the operator's review of PR 699 a plan selling on a rising give-up price draws that price and never "No fixed target".
    public const string TrailLabel = "Sell on a close under";

    public const string TrailValue = "a give-up price that rises";

    // A price as a sale line draws it.
    static string SaleDollars(decimal price) => "$" + price.ToString("N2", CultureInfo.InvariantCulture);

    // A plan's sale in lines, as every card draws it: the rule's target or the first band above with the backstop after it, each
    // staged level's share with the part kept and the backstop, or the time limit alone; where the plan's levels place no price
    // above the buy for a staged sale or the first band, one line saying so and how long it holds.
    // see: A pick's card draws the sale its traded plan was registered with, worked from the price the plan buys at
    public static IReadOnlyList<PlanSaleLine> PlanSaleLines(PlanSaleView sale, string seller = "this rule")
    {
        var after = FormattableString.Invariant($"{sale.Cap} trading days");
        var backstop = new PlanSaleLine("limit", "Otherwise sell after", after);

        // From the operator's order of 2026-11-14 a plan taking profit draws the price beside its other ways out: staged levels
        // keep their shares and what is left takes profit at its price; a trail or the dip's bounce stand beside it.
        // see: Each rule takes profit in the shape its test chose on the early years, never at the nearest price it turned down at
        // On the operator's ruling of 2026-11-14, clause 2, a take-profit on half: half at its price, the other half by the plan's own
        // way out, at its target or band above the buy, its staged levels, its trail or bounce, or after its trading days, the
        // stop on it moved up to the buy once the first half sells.
        // see: Each rule takes profit in the shape its test chose on the early years, never at the nearest price it turned down at
        // On the operator's answer of 2026-11-17 on item 5 a plan staging its sales with a take-profit on half draws each part in
        // the order its price is reached, naming its share of the whole holding, never two plans' shares.
        // see: A pick's card draws the sale its traded plan was registered with, worked from the price the plan buys at
        if (sale.StagesAndHalves && sale.TakeProfit is { } halving)
        {
            var parts = sale.Parts();
            var halfPart = parts.First(part => part.Kind == SalePart.HalfOfTheRest);
            var lines = new List<PlanSaleLine>();

            foreach (var part in parts)
            {
                lines.Add(part.Kind switch
                {
                    SalePart.Stage => new PlanSaleLine("stage", "Sell " + part.Words + " at", SaleDollars(part.Price!.Value)),
                    SalePart.HalfOfTheRest => new PlanSaleLine("target", "Take profit on " + part.Words + " at", SaleDollars(part.Price!.Value), (part == parts[0] ? string.Empty : "Half of what is left. ") + halving.Line),
                    _ => new PlanSaleLine("last", "Sell " + part.LastWords, "after " + after, "Its stop moves up to the buy, " + SaleDollars(sale.Buy) + ", once the " + halfPart.Noun + " at " + SaleDollars(halfPart.Price!.Value) + " sells."),
                });
            }

            return [.. lines, .. lines[^1].Key == "last" ? Array.Empty<PlanSaleLine>() : [backstop]];
        }

        if (sale.TakeProfit is { Half: true } half)
        {
            var aims = sale.Target is { } aim && aim > sale.Buy;
            var trails = !aims && sale.Trail is not null;
            var other = aims ? "at " + SaleDollars(sale.Target!.Value)
                : trails ? TakeProfitView.TrailingHalf(half.Price, sale.Buy, sale.Trail!.Value)
                : "after " + after;

            return
            [
                .. sale.Kind == PlanPoints.StagedSales ? sale.Stages.Select(stage => new PlanSaleLine("stage", "Sell " + PlanSaleView.SharesOf(sale.Stages.Count).Each + " at", SaleDollars(stage))) : [],
                new PlanSaleLine("target", TakeProfitView.HalfLabel, SaleDollars(half.Price), half.Line),
                new PlanSaleLine("other-half", TakeProfitView.OtherHalfLabel, other, trails ? TakeProfitView.TrailingHalfLine : half.OtherHalfLine),
                .. sale.RsiAbove is { } level ? [new PlanSaleLine("bounce", "Or sell", "When it bounces back", FormattableString.Invariant($"A close whose {EquityBrief.Core.Families.DipRule.RsiSessions}-session RSI stands above {level:0} sells it at the next day's open."))] : Array.Empty<PlanSaleLine>(),
                .. other.StartsWith("after ", StringComparison.Ordinal) ? Array.Empty<PlanSaleLine>() : [backstop],
            ];
        }

        if (sale.TakeProfit is { } taking)
        {
            var take = new PlanSaleLine("target", taking.LabelText, SaleDollars(taking.Price), taking.Line ?? (sale.Kind == PlanPoints.FirstBand ? FirstBandWords : null));

            if (sale.Kind == PlanPoints.StagedSales && sale.Stages.Count > 0)
            {
                var (each, last) = PlanSaleView.SharesOf(sale.Stages.Count);

                return
                [
                    .. sale.Stages.Select(stage => new PlanSaleLine("stage", "Sell " + each + " at", SaleDollars(stage))),
                    take with { Note = "On " + last + (taking.Line is { } said ? ". " + said : ".") },
                    backstop,
                ];
            }

            return
            [
                take,
                .. sale.Trail is { } distance && sale.Target is null ? [new PlanSaleLine("trail", "Or sell on a close under", "a give-up price that rises", FormattableString.Invariant($"{SaleDollars(distance)} behind its highest close."))] : Array.Empty<PlanSaleLine>(),
                .. sale.RsiAbove is { } level ? [new PlanSaleLine("bounce", "Or sell", "When it bounces back", FormattableString.Invariant($"A close whose {EquityBrief.Core.Families.DipRule.RsiSessions}-session RSI stands above {level:0} sells it at the next day's open."))] : Array.Empty<PlanSaleLine>(),
                backstop,
            ];
        }

        if (sale.OnTimeAlone)
        {
            return [new PlanSaleLine("limit", "No price target:", seller + " sells after " + after)];
        }

        if (sale.Kind == PlanPoints.StagedSales)
        {
            if (sale.Stages.Count == 0)
            {
                return [new PlanSaleLine("limit", NoStagedLevelAbove, "it is near its highs for the past year, so it holds for " + after + " unless the stop sells it first.")];
            }

            var (each, last) = PlanSaleView.SharesOf(sale.Stages.Count);

            return
            [
                .. sale.Stages.Select(stage => new PlanSaleLine("stage", "Sell " + each + " at", SaleDollars(stage))),
                new PlanSaleLine("keep", "Keep " + last, "until a close below the stop"),
                backstop,
            ];
        }

        if (sale.Kind == PlanPoints.FirstBand)
        {
            return sale.Target is { } band
                ? [new PlanSaleLine("target", TakeProfitView.Label, SaleDollars(band), FirstBandWords), backstop]
                : [new PlanSaleLine("limit", NoBandAbove, "it holds for " + after + " unless the stop sells it first.")];
        }

        return sale.Target is { } target ? [new PlanSaleLine("target", TakeProfitView.Label, SaleDollars(target)), backstop]
            : sale.Trail is { } trail ? [new PlanSaleLine("trail", TrailLabel, TrailValue, FormattableString.Invariant($"{SaleDollars(trail)} behind its highest close.")), backstop]
            : sale.RsiAbove is { } bounce ? [new PlanSaleLine("target", "Aim to sell", "When it bounces back", FormattableString.Invariant($"A close whose {EquityBrief.Core.Families.DipRule.RsiSessions}-session RSI stands above {bounce:0} sells it at the next day's open.")), backstop]
            : [backstop];
    }

    // On the operator's answer of 2026-11-17 on item 5 the line above the rows of a plan selling in more than two parts.
    public static string? SalePartsLine(PlanSaleView sale) =>
        sale.Parts() is { Count: > 2 } parts ? FormattableString.Invariant($"Sells in {parts.Count} parts as the price rises.") : null;

    public static string SalePartsMark(PlanSaleView sale) =>
        SalePartsLine(sale) is { } said ? Formatted($"<p class=\"sale-parts\" data-sale-parts>{Escaped(said)}</p>") : string.Empty;

    // The shares a plan's sale rows name, each read from its label, or from its note where the label names none and the note
    // opens "On the last", summed; a row naming no share names none.
    public static decimal SharesNamed(IEnumerable<(string Label, string? Note)> rows)
    {
        var sum = 0m;

        foreach (var (label, note) in rows)
        {
            var words = label.StartsWith("Take profit on ", StringComparison.Ordinal) && label.EndsWith(" at", StringComparison.Ordinal) ? label["Take profit on ".Length..^" at".Length]
                : label.StartsWith("Sell ", StringComparison.Ordinal) && label.EndsWith(" at", StringComparison.Ordinal) ? label["Sell ".Length..^" at".Length]
                : label.StartsWith("Sell the ", StringComparison.Ordinal) ? label["Sell ".Length..]
                : label.StartsWith("Keep the ", StringComparison.Ordinal) ? label["Keep ".Length..]
                : note is { } said && said.StartsWith("On the last ", StringComparison.Ordinal) ? said["On ".Length..].Split('.')[0]
                : null;

            sum += words is null ? 0m : SalePart.ShareNamed(words) ?? 0m;
        }

        // Thirds and sixths summed in decimals end a unit in the last place away from the whole.
        return Math.Round(sum, 12);
    }

    // A plan's row as the pages draw it, a card's row or tile: its label, and the note beneath where it has one.
    static readonly System.Text.RegularExpressions.Regex SaleRow = new(
        "<div (?:data-(?:plan|sale)=\"[^\"]+\"|class=\"plan-tile\" data-tile=\"[^\"]+\")[^>]*><span(?: class=\"tile-label\")?>([^<]*)</span><b[^>]*>[^<]*</b>(?:<span class=\"(?:sale-note|tile-under)\">([^<]*)</span>)?</div>",
        System.Text.RegularExpressions.RegexOptions.Singleline);

    static readonly System.Text.RegularExpressions.Regex TickerMark = new("data-ticker=\"([^\"]+)\"");

    // The plans on a page whose sale rows name shares adding up to more than the whole holding, or naming the last part and
    // adding up to less, each by the stock named last before it: a plan is a run of rows with nothing between them.
    public static IReadOnlyList<string> CardsSellingOtherThanTheWhole(string page)
    {
        var found = new List<string>();
        var run = new List<(string Label, string? Note)>();
        var (end, at) = (-1, 0);

        void Close()
        {
            var sum = SharesNamed(run);
            var namesTheLast = run.Any(row => row.Label.StartsWith("Sell the last", StringComparison.Ordinal) || row.Label.StartsWith("Keep the last", StringComparison.Ordinal) || row.Label == TakeProfitView.OtherHalfLabel || (row.Note?.StartsWith("On the last", StringComparison.Ordinal) ?? false));

            if (sum > 1m || (namesTheLast && sum != 1m))
            {
                var tickers = TickerMark.Matches(page[..at]);

                found.Add(tickers.Count > 0 ? tickers[^1].Groups[1].Value : "unnamed");
            }

            run.Clear();
        }

        foreach (System.Text.RegularExpressions.Match row in SaleRow.Matches(page))
        {
            if (run.Count > 0 && (row.Index != end))
            {
                Close();
            }

            at = row.Index;
            end = row.Index + row.Length;
            run.Add((System.Net.WebUtility.HtmlDecode(row.Groups[1].Value), row.Groups[2].Success ? System.Net.WebUtility.HtmlDecode(row.Groups[2].Value) : null));
        }

        if (run.Count > 0)
        {
            Close();
        }

        return found;
    }

    // A plan naming no price above its buy, held to its stop or its time limit: the rule's record over every pick like it from
    // 2019 through the newest year its test read, how far they moved by their sale on average and how many of them were up, said
    // as the rule's record and not the stock's chance; until the test has written it, that it is being worked out.
    // see: A pick's card draws the sale its traded plan was registered with, worked from the price the plan buys at
    public static string TimeLimitRecordWords(SetupLongRecord? record)
    {
        if (record is not { LiveMove: { } move, LiveUp: { } up })
        {
            return RecordBeingWorkedOut;
        }

        var percent = Math.Round(move * 100, 1, MidpointRounding.AwayFromZero);
        var moved = percent switch
        {
            > 0 => FormattableString.Invariant($"up about {percent:0.0}%"),
            < 0 => FormattableString.Invariant($"down about {-percent:0.0}%"),
            _ => "about level",
        };

        return FormattableString.Invariant($"Over {record.From} to {record.Through}, picks like this were {moved} on average by then, and {Math.Round(up * 100, MidpointRounding.AwayFromZero):0}% of them were up. {NotTheStocksChance}");
    }

    // The same as a card draws it, beneath the plan's tiles.
    public static string TimeLimitRecord(SetupLongRecord? record) =>
        Formatted($"<p class=\"time-record\" data-time-record=\"{(record is { LiveMove: not null, LiveUp: not null } ? "read" : "waiting")}\">{Escaped(TimeLimitRecordWords(record))}</p>");
}
