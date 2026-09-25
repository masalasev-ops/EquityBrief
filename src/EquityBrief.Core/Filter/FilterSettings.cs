using System.Text.Json;

namespace EquityBrief.Core.Filter;

// Which plan the trade gate reads: the ladder's first tranche as the night's listing kept it, or the
// swing trade's own entry at the close, stop at the setup band's low edge and target at the nearest
// resistance band's low edge above the close.
public enum TradeInput
{
    Ladder,
    Swing,
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
            ["trade"] = Trade == TradeInput.Ladder ? "ladder" : "swing",
        });

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
            trade switch
            {
                "ladder" => TradeInput.Ladder,
                "swing" => TradeInput.Swing,
                _ => throw new InvalidOperationException(FormattableString.Invariant($"A filter version's trade input is '{trade}', which is neither ladder nor swing.")),
            });
    }
}
