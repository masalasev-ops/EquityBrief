using System.Text.Json;

namespace EquityBrief.Core.Filter;

// Which plan the trade gate reads: the ladder's first tranche as the night's listing kept it; the swing
// trade at the nearest bands, entered at the close, stopped at the setup band's low edge and won at the
// nearest band's low edge above the close; or the swing trade clear of the noise, section 10's plan,
// entered at the close, stopped at the setup band's low edge or the next support band's below it where
// that is less than a typical move below the entry, and won at the lowest low edge of a band two
// typical moves or more above it.
// see: The swing filter's trade gate reads section 10's plan for the swing trade, and the plan at the nearest bands is the variant in the reward to risk variant's place
public enum TradeInput
{
    Ladder,
    Swing,
    Clear,
}

// The swing filter's settings: the nine thresholds its gates and its exclusion read, and which plan the
// trade gate reads. Section 17 states each as proposed; the store's open filter version, where one is
// open, carries its own.
// see: The swing filter's starting settings are ruled from shape counts before tonight's list switches to it
public sealed record FilterSettings(
    double BreadthFloor,
    double StrengthFloor,
    double DepthLow,
    double DepthHigh,
    double DryUpCeiling,
    double TightnessCeiling,
    double BreakoutVolumeMultiple,
    double RewardToRiskFloor,
    double StopLow,
    double StopHigh,
    int EarningsWindowSessions,
    int ArrivalSessions,
    TradeInput Trade)
{
    // Section 17's proposed values, which the filter runs on until a version is opened.
    public const double ProposedBreadthFloor = 0.5;

    public const double ProposedStrengthFloor = 2.0 / 3.0;

    public const double ProposedDepthLow = 2;

    public const double ProposedDepthHigh = 5;

    public const double ProposedDryUpCeiling = 1;

    public const double ProposedTightnessCeiling = 0.7;

    public const double ProposedBreakoutVolumeMultiple = 1.5;

    public const double ProposedRewardToRiskFloor = 2;

    public const double ProposedStopLow = 1;

    public const double ProposedStopHigh = 2.5;

    public const int ProposedEarningsWindowSessions = 15;

    // The sessions back from tonight, tonight among them, inside which the trigger's first firing counts.
    // see: Arrival is a trigger that first fired within the last three sessions, and the trade is read from tonight's close
    public const int ProposedArrivalSessions = 3;

    public static FilterSettings Proposed { get; } = new(
        ProposedBreadthFloor,
        ProposedStrengthFloor,
        ProposedDepthLow,
        ProposedDepthHigh,
        ProposedDryUpCeiling,
        ProposedTightnessCeiling,
        ProposedBreakoutVolumeMultiple,
        ProposedRewardToRiskFloor,
        ProposedStopLow,
        ProposedStopHigh,
        ProposedEarningsWindowSessions,
        ProposedArrivalSessions,
        TradeInput.Ladder);

    // The settings as a version stores them, each threshold under its own name and the trade gate's
    // input as a word, so a version reads the same whichever build opened it.
    public string Write() =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["breadthFloor"] = BreadthFloor,
            ["strengthFloor"] = StrengthFloor,
            ["depthLow"] = DepthLow,
            ["depthHigh"] = DepthHigh,
            ["dryUpCeiling"] = DryUpCeiling,
            ["tightnessCeiling"] = TightnessCeiling,
            ["breakoutVolumeMultiple"] = BreakoutVolumeMultiple,
            ["rewardToRiskFloor"] = RewardToRiskFloor,
            ["stopLow"] = StopLow,
            ["stopHigh"] = StopHigh,
            ["earningsWindowSessions"] = EarningsWindowSessions,
            ["arrivalSessions"] = ArrivalSessions,
            ["trade"] = Word(Trade),
        });

    // The trade gate's input as a version stores it and a row's values name it.
    public const string LadderWord = "ladder";

    public const string SwingWord = "swing";

    public const string ClearWord = "clear";

    public static string Word(TradeInput input) => input switch
    {
        TradeInput.Ladder => LadderWord,
        TradeInput.Swing => SwingWord,
        _ => ClearWord,
    };

    // The input a word names, or null for a word that names none.
    public static TradeInput? InputOf(string? word) => word switch
    {
        LadderWord => TradeInput.Ladder,
        SwingWord => TradeInput.Swing,
        ClearWord => TradeInput.Clear,
        _ => null,
    };

    // A version's settings, every one of them named: a version missing one is refused rather than
    // read with a proposed value filled in, since a filter run on a value nobody accepted is not the
    // version it names.
    public static FilterSettings Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        double Number(string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDouble()
                : throw new InvalidOperationException($"A filter version's settings name no {name}.");

        var trade = root.TryGetProperty("trade", out var input) ? input.GetString() : null;

        return new FilterSettings(
            Number("breadthFloor"),
            Number("strengthFloor"),
            Number("depthLow"),
            Number("depthHigh"),
            Number("dryUpCeiling"),
            Number("tightnessCeiling"),
            Number("breakoutVolumeMultiple"),
            Number("rewardToRiskFloor"),
            Number("stopLow"),
            Number("stopHigh"),
            (int)Number("earningsWindowSessions"),
            (int)Number("arrivalSessions"),
            InputOf(trade) ?? throw new InvalidOperationException(FormattableString.Invariant($"A filter version's trade input is '{trade}', which is not ladder, swing or clear.")));
    }
}
