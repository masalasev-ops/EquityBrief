using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;

namespace EquityBrief.Api.Reading;

// What an index's families ran on, read off the settings its night stored: the floors and the gate every rule of the
// index shares, the cost its trades pay, the market check's floor, each family's own settings, and from 15.5 the
// families whose registered live rule the night read their list by.
// see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
public sealed record IndexRuleSettings(
    decimal LowestPrice,
    decimal DollarVolumeFloor,
    int DollarVolumeSessions,
    int? ProfitQuarters,
    string? Costs,
    double MarketFloor,
    string Pullback,
    int PullbackCap,
    IReadOnlyDictionary<string, string> Breakout,
    IReadOnlyDictionary<string, string> Drift,
    IReadOnlyDictionary<string, string> Heavyweights,
    IReadOnlyList<string>? ReadByLiveRule = null,
    string? Fundamentals = null)
{
    // The settings as the night stored them, each family's dials read off its key.
    public static IndexRuleSettings Read(string stored)
    {
        using var document = JsonDocument.Parse(stored);
        var root = document.RootElement;
        var floors = root.GetProperty("floors");
        var pullback = root.GetProperty("pullback");

        static IReadOnlyDictionary<string, string> Dials(JsonElement root, string name) =>
            root.TryGetProperty(name, out var key) && key.ValueKind == JsonValueKind.String
                ? key.GetString()!.Split('|').Select(dial => dial.Split('=', 2)).Where(parts => parts.Length == 2).ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);

        return new IndexRuleSettings(
            floors.GetProperty("price").GetDecimal(),
            floors.GetProperty("dollarVolume") is { ValueKind: JsonValueKind.Number } dollars ? dollars.GetDecimal() : 0m,
            floors.GetProperty("sessions").GetInt32(),
            root.TryGetProperty("profitGate", out var gate) && gate.ValueKind == JsonValueKind.Object ? gate.GetProperty("quarters").GetInt32() : null,
            root.TryGetProperty("costs", out var costs) && costs.ValueKind == JsonValueKind.String ? costs.GetString() : null,
            root.GetProperty("marketFloor").GetDouble(),
            pullback.GetProperty("setting").GetString() ?? string.Empty,
            pullback.GetProperty("cap").GetInt32(),
            Dials(root, "breakout"),
            Dials(root, "drift"),
            Dials(root, "heavyweights"),
            root.TryGetProperty("live", out var live) && live.ValueKind == JsonValueKind.Array
                ? [.. live.EnumerateArray().Select(rule => rule.TryGetProperty("family", out var family) ? family.GetString() ?? string.Empty : string.Empty)]
                : [],
            root.TryGetProperty("fundamentals", out var fundamentals) && fundamentals.ValueKind == JsonValueKind.Object && fundamentals.TryGetProperty("words", out var words)
                ? words.GetString()
                : null);
    }
}

// Each card's rule in words, written by code from the settings its index's rule holds and never by hand, so a setting
// changed changes the words: the S&P 500's from the settings its live rules run at, and the S&P 400's and 600's from
// the settings their night stored, with each way they differ from the S&P 500's.
// see: Every page reads one index at a time chosen under Universe, and every figure names its index
public static class RuleWords
{
    // The line the drift's card carries on the S&P 400 and 600.
    // see: The 400's and 600's earnings drift lists from the first night with the evidence against it on its card
    public const string DriftEvidence = "Published evidence finds this drift gone outside microcaps (Martineau 2022), and the 14.8 test measured -0.222 on the 1,500.";

    static string Number(double value) =>
        double.IsPositiveInfinity(value) ? "off" : value.ToString("0.##", CultureInfo.InvariantCulture);

    static string Dial(IReadOnlyDictionary<string, string> dials, string name) =>
        dials.TryGetValue(name, out var value) ? value : "not stored";

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    // ---- the S&P 500's ----

    // The pullback at the settings of the swing filter version its night ran under.
    public static string Pullback(FilterSettings settings) =>
        Invariant($"A stock stronger than {settings.StrengthFloor * 100:0}% of the members, in an uptrend, falls {Number(settings.DepthLow)} to {Number(settings.DepthHigh)} typical moves into a support band")
        + (double.IsPositiveInfinity(settings.DryUpCeiling) ? string.Empty : Invariant($" on volume under {Number(settings.DryUpCeiling)} times its average"))
        + Invariant($" and turns back up within {settings.ArrivalSessions} sessions, with no report due in the next {settings.EarningsWindowSessions}. ")
        + Invariant($"Stop {Number(settings.StopLow)} to {Number(settings.StopHigh)} typical moves below, target at the next band above and at least {Number(settings.RewardToRiskFloor)} times the risk.");

    public static string Breakout(BreakoutSettings settings) =>
        Breakout(settings.HighSessions.ToString(CultureInfo.InvariantCulture), Number(settings.VolumeMultiple), Number(settings.RangeCeiling), Number(settings.StopMoves));

    public static string Drift(DriftSettings settings) =>
        Drift(settings.WindowSessions.ToString(CultureInfo.InvariantCulture), Number(settings.ReactionMoves), Number(settings.VolumeMultiple), Number(settings.TargetRiskMultiple));

    public static string Heavyweights(HeavyweightSettings settings) =>
        Heavyweights(
            settings.Largest == HeavyweightRule.EveryCompany ? "every" : settings.Largest.ToString(CultureInfo.InvariantCulture),
            settings.LookBack.ToString(CultureInfo.InvariantCulture),
            settings.Leaders.ToString(CultureInfo.InvariantCulture),
            settings.FundReturn ? "the sector fund's" : "their sector's members' mean",
            settings.HighBeta ? "their beta against the index at least one" : null,
            settings.Weekly ? "week" : "month",
            settings.SoldOnLeading && settings.SoldUnderAverage ? "both" : settings.SoldUnderAverage ? "break" : "drop",
            "of the index");

    // ---- the S&P 400's and 600's ----

    // What every rule of the index shares and the S&P 500's rules do not: the floors, the profit check, the index's own
    // ranking and breadth, and the cost its trades pay.
    public static string Differs(IndexRuleSettings settings, string index) =>
        Invariant($"Unlike the S&P 500's rules, a stock is read only at ${settings.LowestPrice:0.##} or more and trading at least ${settings.DollarVolumeFloor:#,0} a day over {settings.DollarVolumeSessions} sessions")
        + (settings.ProfitQuarters is { } quarters ? Invariant($", and only where its net income over its last {quarters} quarters sums above zero") : string.Empty)
        + Invariant($"; it is ranked among the {index}'s own members, and the lists close while the {index}'s own breadth is under {settings.MarketFloor * 100:0}%")
        + (settings.Costs is { } costs ? $"; each trade's result is read after {costs}, half paid at each end." : ".");

    public static string Pullback(IndexRuleSettings settings, string index) =>
        Invariant($"A stock dips into a support band and turns back up, on the sweep's base setting: {settings.Pullback}; held at most {settings.PullbackCap} sessions. ")
        + Differs(settings, index);

    public static string Breakout(IndexRuleSettings settings, string index) =>
        Breakout(Dial(settings.Breakout, "high"), Dial(settings.Breakout, "volume"), Dial(settings.Breakout, "ceiling"), Dial(settings.Breakout, "stop"))
        + " " + Differs(settings, index);

    public static string Drift(IndexRuleSettings settings, string index) =>
        Drift(Dial(settings.Drift, "window"), Dial(settings.Drift, "reaction"), Dial(settings.Drift, "volume"), Dial(settings.Drift, "target"))
        + " " + Differs(settings, index);

    // The fundamentals-first family on any index, in the words its night's settings state, its business read from the
    // facts the company filed before the night as first filed; on the S&P 400 and 600 with the floors their rules read.
    // see: The fundamentals-first family buys an improving business in an uptrend at the pullback's buy point
    public static string Fundamentals(IndexRuleSettings settings, string index) =>
        Invariant($"A company with {settings.Fundamentals ?? FundamentalsRule.Provisional.Words}, its four newest quarters and its newest quarter profitable, closing above its 200-day average with the 50-day above it, bought at a pullback's buy point on the sweep's base setting and held at most {settings.PullbackCap} sessions; its business read from the facts it filed before the night as first filed. ")
        + (index == "S&P 500" ? "Read on the S&P 500 by the S&P 400's and 600's step, apart from its own families." : Differs(settings, index));

    // The index's sector heavyweights, naming the design and the sector comparison its index reads.
    public static string Heavyweights(IndexRuleSettings settings, string index, string fund)
    {
        var dials = settings.Heavyweights;
        var members = Dial(dials, "sector") == "members";

        return Heavyweights(
                Dial(dials, "size"),
                Dial(dials, "look-back"),
                Dial(dials, "leaders"),
                members ? $"their sector's members' mean in the {index}" : "the sector fund's",
                Dial(dials, "beta") == "off" ? null : $"their beta against {fund} at least one",
                Dial(dials, "rebalance"),
                Dial(dials, "exit"),
                $"of the {index}")
            + (members ? $" Design (a): a sector's return is its members' mean within the {index}, since no sector fund is read at the index's level. " : $" Design (a), a sector's return its fund's. ")
            + Differs(settings, index);
    }

    // ---- the words both share ----

    static string Breakout(string high, string volume, string ceiling, string stop) =>
        $"A stock closes above its highest price of the {high} sessions before on {Number(volume)} times its average volume"
        + (ceiling is "off" or "not stored" ? string.Empty : Invariant($", after its daily ranges narrowed to at most {Number(ceiling)} of those over the {BreakoutRule.RangeSessions} sessions before them"))
        + $". Stop {Number(stop)} typical moves below, raised as the price climbs and never lowered; no target.";

    static string Drift(string window, string reaction, string volume, string target) =>
        $"A company beats its estimate and the stock closes up at least {Number(reaction)} typical moves on {Number(volume)} times its usual volume. "
        + $"Bought within {window} sessions while it holds above that day's low. Stop at that day's low, target at the next band above or {Number(target)} times the risk, whichever is nearer.";

    static string Heavyweights(string size, string lookBack, string leaders, string comparison, string? beta, string period, string exit, string within) =>
        $"On each {period}'s first session, or the first after it whose stored year holds the closes its readings need, among each sector's {(size == "every" ? "companies" : size + " largest companies")} {within} by value, the {leaders} whose {lookBack}-session returns beat {comparison} by the most, where they beat it at all, their close above their 50-day average and that above their 200-day"
        + (beta is null ? string.Empty : ", " + beta)
        + $", bought at that close. "
        + exit switch
        {
            "break" => "Sold at a close under its 200-day average.",
            "both" => $"Sold at the close of a later {period}'s rebalance where it no longer leads, or at a close under its 200-day average.",
            _ => $"Held while it leads: sold at the close of a later {period}'s rebalance where the rule would no longer buy it.",
        };

    static string Number(string stored) =>
        double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? Number(value) : stored;
}
