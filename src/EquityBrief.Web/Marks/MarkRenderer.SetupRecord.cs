using System.Globalization;
using EquityBrief.Web.App;

namespace EquityBrief.Web.Marks;

// A setup's record over the long history on an index, as a pick's card, Home and Tonight's tiles draw it: the traded plan's
// mean edge a pick offered against the ruled benchmark from 2019 through the newest session its run read, in dollars for each
// dollar risked, with the picks it is over; or, for a setup the test of the buy and sell points does not read, the year the
// filings it reads reach back to. The traded plan is whichever plan the setup trades.
// see: A pick's card draws its setup's record over the long history under its entry check, the rule's record and never the stock's
public sealed record SetupLongRecord(string Index, string Family, double? EdgePerPick, int? Picks, int From, int Through, int? ReachesBack = null)
{
    // From the 23.10 correction of 2026-11-13 the plan its live rule states, walked over its setups from 2019: its fills' mean
    // move from the fill to the sale as a fraction of the fill, the share that sold above the fill and over how many fills; none
    // before the test has written them.
    // see: A pick's card draws the sale its traded plan was registered with, worked from the price the plan buys at
    public double? LiveMove { get; init; }

    public double? LiveUp { get; init; }

    public int? LiveFills { get; init; }

    // A record of a figure: an edge over at least the picks a record needs.
    public bool Measured => ReachesBack is null && EdgePerPick is not null && Picks >= EquityBrief.Core.Cards.TakenWalk.RecordMinimum;

    public bool Made => Measured && EdgePerPick > 0;

    public bool Lost => Measured && EdgePerPick <= 0;

    // A record of an edge over fewer picks than a record needs, drawn as its count against the minimum alone.
    public bool Short => ReachesBack is null && EdgePerPick is not null && Picks is { } picks && picks < EquityBrief.Core.Cards.TakenWalk.RecordMinimum;

    // What the record says, as the page marks it.
    public string State => Made ? "made" : Lost ? "lost" : Short ? "short" : ReachesBack is not null ? "none" : "unread";

    // The setups whose filings the test of the buy and sell points cannot read, none of them selling at a price set in advance.
    public static IReadOnlyList<string> FilingFamilies { get; } =
    [
        EquityBrief.Core.Families.ImprovingRule.Name,
        EquityBrief.Core.Families.FundamentalsRule.Name,
        EquityBrief.Core.Families.HeavyweightRule.Name,
        EquityBrief.Core.Families.QualityRule.Name,
    ];

    // A filing-based setup's record: the first year 90 in every 100 of the index's members read its score, three learning years
    // before its first test year; none for a price family or an index with no year measured.
    public static SetupLongRecord? Filings(string index, string family) =>
        FilingFamilies.Contains(family) && EquityBrief.Core.Loop.LongTest.Measured.TryGetValue((family, index), out var tested)
            ? new SetupLongRecord(index, family, null, null, EquityBrief.Core.History.LongLedger.StandingFrom.Year, EquityBrief.Core.History.LongLedger.StandingFrom.Year, tested - EquityBrief.Core.History.LongFirstYears.LearningYears)
            : null;
}

// Every setup's record over the long history the store holds, on each index: the price families' newest run of the test of
// the buy and sell points, and each filing-based family's year its filings reach back to.
public sealed record SetupRecords(IReadOnlyList<SetupLongRecord> Records)
{
    public static SetupRecords None { get; } = new([]);

    public SetupLongRecord? Of(string index, string family) =>
        Records.FirstOrDefault(record => record.Index == index && record.Family == family);
}

// From the 22.16 correction of 2026-11-09 a traded plan that buys at a zone: the price it waits for, the trading days it waits,
// tonight's close and, where it sets no target, the trading days after which it sells. No plan buys at a zone before 23.10. From
// the 23.10 correction of 2026-11-10 one whose stock's levels set no price tonight places no buy, its price and stop drawn as none.
// see: A plan that buys at a zone draws the price it waits for and how long it waits beside tonight's close on every card
public sealed record ZoneBuy(decimal Price, int Within, decimal Close, int? Sessions = null, bool NoBuy = false)
{
    // From the 23.10 correction of 2026-11-13 the sale the plan was registered with, worked from the price it waits for.
    // see: A pick's card draws the sale its traded plan was registered with, worked from the price the plan buys at
    public PlanSaleView? Sale { get; init; }
}

public sealed partial class MarkRenderer
{
    // What a card says in its plan's place where the plan its rule trades sets no buy price tonight.
    public const string NoBuyTonight = "No buy tonight: the plan its rule trades buys only at a lower price set from the stock's own levels, and tonight those levels set none, so there is no trade.";

    // The line beneath a zone plan's prices: tonight's close, the price the buy waits for and for how long, and that no dip is no
    // trade.
    // see: A plan that buys at a zone draws the price it waits for and how long it waits beside tonight's close on every card
    public static string ZoneWaitLine(ZoneBuy zone) =>
        Formatted($"Tonight's close was ${zone.Close:N2}. The buy waits for the price to dip to about ${zone.Price:N2} within {zone.Within} trading days; if it never does, there is no trade.");

    public const string RecordNote = "The rule's record over every stock it bought, not this stock's chance.";

    // The index a record is on, by its name.
    static string IndexNamed(string index) => Universes.ByCode(index)?.Name ?? index;

    // A dollar figure of a record, its sign left to the words around it.
    static string RecordDollars(double edge) => "$" + Math.Abs(edge).ToString("0.00", CultureInfo.InvariantCulture);

    // The box beneath a pick's entry check line, headed with the setup and its index: what picks like it made or lost for each
    // dollar risked against the index's other stocks bought the same way, over how many, or that it has no long record yet;
    // and beneath it that this is the rule's record and not the stock's chance. None where the record is unread.
    // see: A pick's card draws its setup's record over the long history under its entry check, the rule's record and never the stock's
    public static string SetupRecordBox(SetupLongRecord? record)
    {
        if (record is null || record.State == "unread")
        {
            return string.Empty;
        }

        var line = record switch
        {
            { Made: true } => Formatted($"From {record.From} to {record.Through}, picks like this made about <b class=\"made\">{Escaped(RecordDollars(record.EdgePerPick!.Value))}</b> more for every $1 risked than buying the index's other stocks the same way, over {record.Picks!.Value.ToString("N0", CultureInfo.InvariantCulture)} picks."),
            { Lost: true } => Formatted($"From {record.From} to {record.Through}, picks like this lost about <b class=\"lost\">{Escaped(RecordDollars(record.EdgePerPick!.Value))}</b> for every $1 risked, compared with buying the index's other stocks the same way, over {record.Picks!.Value.ToString("N0", CultureInfo.InvariantCulture)} picks."),
            { Short: true } => Formatted($"From {record.From} to {record.Through}, {record.Picks!.Value} picks like this, short of the {EquityBrief.Core.Cards.TakenWalk.RecordMinimum} a record needs."),
            _ => Formatted($"<b class=\"none\">No long record yet:</b> {Escaped(FilingsUntested(record))}"),
        };

        return Formatted($"<div class=\"setup-record\" data-record=\"{record.State}\" data-family=\"{Escaped(record.Family)}\" data-index=\"{Escaped(record.Index)}\"><span class=\"rec-h\">{Escaped("This setup's record on the " + IndexNamed(record.Index))}</span><span>{line}</span><span class=\"rec-sub\">{Escaped(RecordNote)}</span></div>");
    }

    // The line at the foot of a setup's tile on Tonight: what its plan as it stands made or lost for each dollar risked on the
    // index from 2019, or that it has no long record yet; none where the record is unread.
    // see: Each setup's tile on Tonight ends on its record over the long history with its plan as it stands
    public static string TileRecordLine(SetupLongRecord? record)
    {
        if (record is null || record.State == "unread")
        {
            return string.Empty;
        }

        var line = record switch
        {
            { Made: true } or { Lost: true } => Formatted($"{record.From} to {record.Through} on the {Escaped(IndexNamed(record.Index))}, with its plan as it stands: <b class=\"{record.State}\">{record.State} {Escaped(RecordDollars(record.EdgePerPick!.Value))}</b> per $1 risked against the index's stocks bought the same way."),
            { Short: true } => Formatted($"{record.From} to {record.Through} on the {Escaped(IndexNamed(record.Index))}: {record.Picks!.Value} picks, short of the {EquityBrief.Core.Cards.TakenWalk.RecordMinimum} a record needs."),
            _ => Formatted($"<b class=\"none\">No long record yet:</b> {Escaped(FilingsUntestedShort(record))}"),
        };

        return Formatted($"<span class=\"tile-record\" data-record=\"{record.State}\">{line}</span>");
    }

    // From the operator's order of 2026-11-14 why a setup reading filings has no record here from 2019: the walk that gives the
    // other setups theirs replays prices alone, and its filings reach back only so far; its own per-rule test over the long
    // history judges it from its first test year on the index, three learning years after its filings cover the index, on the
    // Monthly check-up.
    public static string FilingsUntested(SetupLongRecord record) => FormattableString.Invariant(
        $"the walk that gives the other setups their record from {EquityBrief.Core.History.LongLedger.StandingFrom.Year} replays prices alone, and this setup buys on filings, which reach back only to {record.ReachesBack}. Its own test over the long history judges it from {EquityBrief.Core.Loop.LongTest.FirstTestYear(record.Family, record.Index)}, on the Monthly check-up.");

    public static string FilingsUntestedShort(SetupLongRecord record) => FormattableString.Invariant(
        $"the walk from {EquityBrief.Core.History.LongLedger.StandingFrom.Year} replays prices alone, and this setup buys on filings; its own test over the long history judges it from {EquityBrief.Core.Loop.LongTest.FirstTestYear(record.Family, record.Index)}.");
}
