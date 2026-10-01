using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Filter;

namespace EquityBrief.Core.Families;

// One session of a name as a family's rule reads it.
public readonly record struct FamilyBar(DateOnly Session, decimal High, decimal Low, decimal Close, long Volume);

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
