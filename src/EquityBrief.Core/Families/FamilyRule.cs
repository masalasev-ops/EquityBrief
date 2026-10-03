using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Filter;

namespace EquityBrief.Core.Families;

// One session of a name as a family's rule reads it.
public readonly record struct FamilyBar(DateOnly Session, decimal High, decimal Low, decimal Close, long Volume);

// What the families' rules the family evaluator reads take for one member on one night, together, which a
// registered family rule is handed so it reads its own family's, with the night's market series a registered
// rule's market switch reads, none reading every switch as closed.
public sealed record FamilyMember(BreakoutInputs Breakout, DriftInputs Drift, MarketCloses? Market = null);

// The index's and the VIX's closes on the store's sessions to the night, oldest first, as a registered family
// rule's market switch reads them: a session a series holds no close for reads none, and a day a series holds that
// is none of the sessions, an exchange holiday the VIX is quoted on, is passed over. Each is read as the ideas' run
// read it over the history, the index's close against the average of a count of its closes ending on the night,
// none where any of them is missing, and the VIX's close against its close a count of sessions before, so a
// variant registered from that run is the rule it measured.
// see: The breakout and the earnings drift each register a variant listing only on nights its market switch is open, and each family is registered again whole to add it
public sealed class MarketCloses
{
    public const string Index = "GSPC";

    public const string Vix = "VIX";

    readonly double[] index;
    readonly double[] vix;

    MarketCloses(IReadOnlyList<DateOnly> sessions, double[] index, double[] vix)
    {
        Sessions = sessions;
        this.index = index;
        this.vix = vix;
    }

    public IReadOnlyList<DateOnly> Sessions { get; }

    // No session at all, which reads every switch as closed.
    public static MarketCloses None { get; } = new([], [], []);

    // Each series' closes on the sessions given, the night last.
    public static MarketCloses On(IReadOnlyList<DateOnly> sessions, IReadOnlyDictionary<DateOnly, double> index, IReadOnlyDictionary<DateOnly, double> vix)
    {
        double[] Closes(IReadOnlyDictionary<DateOnly, double> series) =>
            [.. sessions.Select(session => series.TryGetValue(session, out var close) ? close : double.NaN)];

        return new(sessions, Closes(index), Closes(vix));
    }

    // The index's close on the night and the average of the count of its closes ending on it, summed from the
    // night back as the ideas' run sums it; the average is none where the sessions hold fewer or one is missing.
    public (double? Close, double? Average) IndexOver(int count)
    {
        var night = index.Length - 1;

        if (night < 0)
        {
            return (null, null);
        }

        var close = Held(index[night]);

        if (count < 1 || night + 1 < count)
        {
            return (close, null);
        }

        var sum = 0.0;

        for (var back = 0; back < count; back++)
        {
            if (double.IsNaN(index[night - back]))
            {
                return (close, null);
            }

            sum += index[night - back];
        }

        return (close, sum / count);
    }

    // The VIX's close on the night and its close the count of sessions before it, each none where it is missing.
    public (double? Close, double? Before) VixAgainst(int count)
    {
        var night = vix.Length - 1;

        if (night < 0)
        {
            return (null, null);
        }

        var back = night - count;

        return (Held(vix[night]), count >= 1 && back >= 0 ? Held(vix[back]) : null);
    }

    static double? Held(double close) => double.IsNaN(close) ? null : close;
}

// The market switches a registered breakout or earnings drift rule states, each a count of sessions and nought
// where the rule holds none: the index closing above the average of that many of its closes ending on the night,
// and the VIX closing under its close that many sessions before. A switch is read after the family's own gates as a
// gate of its own, so a night it is closed on, or cannot be read on, lists no member under the rule, and a rule
// stating none answers as its family's rule does.
// see: The breakout and the earnings drift each register a variant listing only on nights its market switch is open, and each family is registered again whole to add it
public readonly record struct MarketSwitches(int IndexAverageSessions, int VixLookbackSessions)
{
    public const string IndexAverageParameter = "indexAverageSessions";

    public const string VixLookbackParameter = "vixLookbackSessions";

    public const string IndexGate = "index over its average";

    public const string VixGate = "VIX under its close before";

    public static MarketSwitches None => default;

    public bool Any => IndexAverageSessions > 0 || VixLookbackSessions > 0;

    public static MarketSwitches Of(IReadOnlyDictionary<string, double> parameters) =>
        new((int)parameters[IndexAverageParameter], (int)parameters[VixLookbackParameter]);

    // The words a registration's name ends with for the switches it states, none where it states none.
    public string Words =>
        (IndexAverageSessions > 0 ? FamilyRule.Invariant($", only on nights the index closes above its {IndexAverageSessions}-session average") : string.Empty)
        + (VixLookbackSessions > 0 ? FamilyRule.Invariant($", only on nights the VIX closes under its close {VixLookbackSessions} sessions before") : string.Empty);

    // The family's answer with each switch the registration states read after its gates.
    public FamilyResult Read(FamilyResult result, MarketCloses? market) =>
        Any ? result with { Gates = [.. result.Gates, .. GatesOn(market)] } : result;

    // Each switch the registration states as a gate read on the night's closes, the index's first.
    public IReadOnlyList<Gate> GatesOn(MarketCloses? market)
    {
        var closes = market ?? MarketCloses.None;
        var gates = new List<Gate>();

        if (IndexAverageSessions > 0)
        {
            gates.Add(IndexGateOn(closes, IndexAverageSessions));
        }

        if (VixLookbackSessions > 0)
        {
            gates.Add(VixGateOn(closes, VixLookbackSessions));
        }

        return gates;
    }

    static Gate IndexGateOn(MarketCloses closes, int count)
    {
        var (close, average) = closes.IndexOver(count);
        var values = FamilyRule.Values(("close", FamilyRule.Figure(close)), ("average", FamilyRule.Figure(average)), ("sessions", FamilyRule.Whole(count)));

        return close is { } held && average is { } mean
            ? new Gate(
                IndexGate,
                held > mean,
                FamilyRule.Invariant($"the index closed {held:0.##} {(held > mean ? "above" : "at or below")} the average of its {count} closes to the night, {mean:0.##}"),
                values)
            : new Gate(IndexGate, false, FamilyRule.Invariant($"the index's stored closes do not hold all {count} sessions to the night"), values);
    }

    static Gate VixGateOn(MarketCloses closes, int count)
    {
        var (close, before) = closes.VixAgainst(count);
        var values = FamilyRule.Values(("close", FamilyRule.Figure(close)), ("before", FamilyRule.Figure(before)), ("sessions", FamilyRule.Whole(count)));

        return close is { } held && before is { } earlier
            ? new Gate(
                VixGate,
                held < earlier,
                FamilyRule.Invariant($"the VIX closed {held:0.##} {(held < earlier ? "under" : "at or above")} its close {count} sessions before, {earlier:0.##}"),
                values)
            : new Gate(VixGate, false, FamilyRule.Invariant($"the VIX's stored closes do not hold the night and the session {count} before it"), values);
    }
}

// One member's answer under one family on one night: the family's gates in order, each with the sentence
// saying why and the values that decided it, the trade it is bought on where one could be placed, the
// figure the family's order reads, and what keeps a name passing every gate off the family's list.
public sealed record FamilyResult(
    string Ticker,
    string Family,
    IReadOnlyList<Gate> Gates,
    decimal? Entry,
    decimal? Stop,
    decimal? Target,
    double? OrderBy,
    IReadOnlyList<string> Exclusions,
    double? ThenBy = null)
{
    public bool Passed => Gates.Count > 0 && Gates.All(gate => gate.Passed) && Exclusions.Count == 0;

    // The gates the member did not pass, which is what a name close to a buy point is counted by.
    public int Missed => Gates.Count(gate => !gate.Passed);
}

// What every family's rule shares: the market gate as the night's swing filter stored it, the exclusions a
// series that cannot be read carries, the order of the names passing, and the words a value is stored in.
// see: The market check closes every family's list together
public static class FamilyRule
{
    public const string Market = SwingGates.Market;

    public const string Trade = SwingGates.Trade;

    // The trade gate's value naming the plan's reward to risk, where the family's plan has a target.
    public const string RewardToRiskValue = "reward to risk";

    // The market gate where the night's swing filter stored none, which closes every family's list.
    public static Gate NoMarketCheck { get; } = new(Market, false, "the swing filter stored no market check for the night", Values());

    // The names passing in the family's order: the figure its order reads, largest first, and then the
    // ticker, so two reads of one night cannot disagree.
    public static IReadOnlyList<FamilyResult> Ranked(IEnumerable<FamilyResult> results) =>
    [
        .. results
            .Where(result => result.Passed)
            .OrderByDescending(result => result.OrderBy)
            .ThenByDescending(result => result.ThenBy)
            .ThenBy(result => result.Ticker, StringComparer.Ordinal),
    ];

    // A family's gates as a row stores them, in the form the swing filter's row keeps its own in, and read
    // back from either.
    public static string GatesJson(IReadOnlyList<Gate> gates) =>
        JsonSerializer.Serialize(new
        {
            gates = gates.Select(gate => new { gate = gate.Name, passed = gate.Passed, reason = gate.Reason, values = gate.Values }),
        });

    public static IReadOnlyList<Gate> GatesOf(string stored)
    {
        using var document = JsonDocument.Parse(stored);

        return
        [
            .. document.RootElement.GetProperty("gates").EnumerateArray().Select(gate => new Gate(
                gate.GetProperty("gate").GetString()!,
                gate.GetProperty("passed").GetBoolean(),
                gate.GetProperty("reason").GetString() ?? string.Empty,
                gate.GetProperty("values").EnumerateObject().ToDictionary(value => value.Name, value => value.Value.GetString() ?? string.Empty, StringComparer.Ordinal))),
        ];
    }

    public static IReadOnlyDictionary<string, string> Values(params (string Key, string Value)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);

    public static string Figure(double? value) => value is { } held ? held.ToString("R", CultureInfo.InvariantCulture) : "none";

    public static string Whole(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Price(decimal? value) => value is { } held ? held.ToString(CultureInfo.InvariantCulture) : "none";

    public static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
