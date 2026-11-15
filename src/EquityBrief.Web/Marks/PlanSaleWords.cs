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
    // A plan selling on its time limit alone, which names no price to sell at.
    public bool OnTimeAlone => Kind == PlanPoints.TimeLimit;

    // From the second 23.10 correction of 2026-11-13 a plan naming no price above its buy to sell at, held to its stop or its
    // time limit: the time limit alone, and the staged sales or the first band above where its levels place none above it.
    // see: A plan whose levels place no price above its buy says so in one line and draws the record of picks like it beneath
    public bool HoldsToItsLimit =>
        OnTimeAlone || (Kind == PlanPoints.StagedSales && Stages.Count == 0) || (Kind == PlanPoints.FirstBand && Target is null);

    // What the sale aims at, which what the plan risks for what it aims at reads: its target, or the staged levels' mean; none
    // where it names no price above the buy.
    public decimal? Aim =>
        OnTimeAlone ? null
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

        if (Kind == PlanPoints.StagedSales && Stages.Count > 0)
        {
            var (each, last) = SharesOf(Stages.Count);

            steps.AddRange(Stages.Select(stage => $"Sell {each} at the next trading day's open after a day that closes at or above {At(stage)}."));
            steps.Add($"Keep {last} until a day that closes below the stop or the time limit.");
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

public sealed partial class MarkRenderer
{
    // What a card selling on its time limit alone says until the plan point test has written its rule's record.
    public const string RecordBeingWorkedOut = "The record for this is being worked out.";

    public const string FirstBandWords = "the first price above the buy where it has turned down before";

    // From the second 23.10 correction of 2026-11-13 what a staged sale or the first band above says where its levels place no
    // price above the buy, as one line, with the record of picks like it beneath.
    public const string NoStagedLevelAbove = "No price level above tonight's close to sell at:";

    public const string NoBandAbove = "No price above tonight's close where it has turned down before:";

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
                ? [new PlanSaleLine("target", "Aim to sell at", SaleDollars(band), FirstBandWords), backstop]
                : [new PlanSaleLine("limit", NoBandAbove, "it holds for " + after + " unless the stop sells it first.")];
        }

        return sale.Target is { } target ? [new PlanSaleLine("target", "Aim to sell at", SaleDollars(target)), backstop]
            : sale.Trail is { } trail ? [new PlanSaleLine("target", "Aim to sell at", NoFixedTarget, FormattableString.Invariant($"It sells on a close under a give-up price that rises behind its highest close, {SaleDollars(trail)} under it.")), backstop]
            : sale.RsiAbove is { } bounce ? [new PlanSaleLine("target", "Aim to sell", "When it bounces back", FormattableString.Invariant($"A close whose {EquityBrief.Core.Families.DipRule.RsiSessions}-session RSI stands above {bounce:0} sells it at the next day's open.")), backstop]
            : [backstop];
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
