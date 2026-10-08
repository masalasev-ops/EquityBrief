using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Cards;

// The thresholds of the breakouts forming list, read from its settings block where it names one and these defaults
// where it does not: how near the look-back high a close stands, in the member's typical moves; the most the newer
// ranges may be of the older, wider than the rule's own ceiling so a member whose ranges have not yet narrowed to it
// is still drawn; the rows a rule draws; and the sessions ahead within which the next earnings date is named.
// see: The forming list advises and never lists a stock
public sealed record FormingSettings(double WithinMoves, double RangeCeiling, int Rows, int EarningsWithinSessions)
{
    public const string Section = "EquityBrief:Forming";

    public const double DefaultWithinMoves = 1.5;

    public const double DefaultRangeCeiling = 1.0;

    public const int DefaultRows = 10;

    public const int DefaultEarningsWithinSessions = 20;

    public static FormingSettings Defaults { get; } = new(DefaultWithinMoves, DefaultRangeCeiling, DefaultRows, DefaultEarningsWithinSessions);

    public static FormingSettings From(Func<string, string?> value)
    {
        var within = Read(value, nameof(WithinMoves), DefaultWithinMoves, text => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
        var ceiling = Read(value, nameof(RangeCeiling), DefaultRangeCeiling, text => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
        var rows = Read(value, nameof(Rows), DefaultRows, text => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture));
        var earnings = Read(value, nameof(EarningsWithinSessions), DefaultEarningsWithinSessions, text => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture));

        if (!(within > 0) || !(ceiling > 0) || rows < 1 || earnings < 0)
        {
            throw new InvalidOperationException(
                $"The forming list's settings under '{Section}' hold a value out of its range: the moves and the ceiling above nothing, the rows at least one and the earnings sessions at or above nothing.");
        }

        return new FormingSettings(within, ceiling, rows, earnings);
    }

    static T Read<T>(Func<string, string?> value, string key, T fallback, Func<string, T> parse)
    {
        var text = value($"{Section}:{key}");

        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        try
        {
            return parse(text.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"The forming list's setting '{Section}:{key}' holds '{text}', which is not a number.");
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException($"The forming list's setting '{Section}:{key}' holds '{text}', which is out of range.");
        }
    }
}

// One member forming a breakout under one breakout rule on a night: the price it would have to close above, how far
// its close stands under it in its typical moves, the volume the rule would need against tonight's, its ranges' ratio
// against the rule's ceiling, and the gates it still fails in the rule's order, the new high always among them.
public sealed record FormingMember(
    string Ticker,
    decimal Close,
    decimal High,
    double MovesUnder,
    double VolumeNeeded,
    double Volume,
    double RangeRatio,
    double RangeCeiling,
    IReadOnlyList<string> Missing);

// The breakouts forming under a rule: members passing the rule's market check, not closing above its look-back high,
// within the stated typical moves of it, with their ranges' ratio at or under the stated ceiling and a typical move to
// place a stop by. Ordered the nearest miss first, the fewest gates still failing and then the nearest to the high. The
// list reads the rule's own gates as the rule stored them and lists no stock: a member closing above its high on the
// volume shown, with its range under the rule's ceiling and the market check open, is read by the rule's other gates
// that night as on any other.
// see: The forming list advises and never lists a stock
public static class FormingList
{
    // What a member forming a breakout reads under the rule's gates as the rule evaluates them over its inputs, none
    // where it is not forming one: the market check closed, no bar, a close at or above the high, a close further under
    // it than the stated moves, ranges wider than the stated ceiling, or no typical move to place the stop by.
    public static FormingMember? Read(BreakoutInputs inputs, BreakoutSettings rule, FormingSettings settings)
    {
        if (inputs.Bars.Count == 0 || inputs.TypicalMove is not { } move || !(move > 0) || inputs.VolumeAverage50 is not { } average || !(average > 0))
        {
            return null;
        }

        var gates = BreakoutRule.Evaluate(inputs, rule).Gates.ToDictionary(gate => gate.Name, StringComparer.Ordinal);

        if (!gates.TryGetValue(FamilyRule.Market, out var market) || !market.Passed
            || !gates.TryGetValue(BreakoutRule.NewHigh, out var newHigh) || newHigh.Passed
            || !gates.TryGetValue(BreakoutRule.Volume, out var volume)
            || !gates.TryGetValue(BreakoutRule.Tightened, out var tightened)
            || !gates.TryGetValue(FamilyRule.Trade, out var trade) || !trade.Passed
            || !Price(newHigh, "high", out var high) || !Figure(tightened, "ratio", out var ratio))
        {
            return null;
        }

        var close = inputs.Bars[^1].Close;
        var under = Statistic.FromPrice(high - close) / move;

        if (!(under >= 0) || under > settings.WithinMoves || ratio > settings.RangeCeiling)
        {
            return null;
        }

        var missing = new List<string> { BreakoutRule.NewHigh };

        if (!volume.Passed)
        {
            missing.Add(BreakoutRule.Volume);
        }

        if (!tightened.Passed)
        {
            missing.Add(BreakoutRule.Tightened);
        }

        return new FormingMember(inputs.Ticker, close, high, under, rule.VolumeMultiple * average, Statistic.FromVolume(inputs.Bars[^1].Volume), ratio, rule.RangeCeiling, missing);
    }

    // The nearest misses first: the fewest gates still failing, then the nearest to the high, then the ticker.
    public static IReadOnlyList<FormingMember> Order(IEnumerable<FormingMember> forming) =>
    [
        .. forming
            .OrderBy(member => member.Missing.Count)
            .ThenBy(member => member.MovesUnder)
            .ThenBy(member => member.Ticker, StringComparer.Ordinal),
    ];

    static bool Price(Gate gate, string key, out decimal value) =>
        decimal.TryParse(gate.Values.GetValueOrDefault(key), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    static bool Figure(Gate gate, string key, out double value) =>
        double.TryParse(gate.Values.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
