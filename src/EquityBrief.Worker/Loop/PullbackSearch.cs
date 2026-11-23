using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Paper;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;

namespace EquityBrief.Worker.Loop;

// The pullbacks' search, the grammar's first search, the S&P 500's first: the swing filter's dials walked as lines of the
// grammar over every pullback its loose gates passed from 2005, each line the live rule's own plan, its buy a distance
// under the close and its sale the first band above. The first stage crosses the strength floor, the breadth floor, the
// depth band and the reward-to-risk floor at three levels each, 81 settings; the second walks the dry-up ceiling, the arrival
// window, the stop band and the least band strength one dial at a time from the setting the first stage chose on 2005 to
// 2018 alone. Every line of both stages is tested by the one tester and judged together in the run's one step-down, so every
// setting tried counts toward the bar. The list, its count and its measured time are printed first, and nothing is walked
// or written without the operator's word.
// see: The pullbacks' search walks the swing filter's dials as grammar lines, chosen on 2005 to 2018 and judged together from 2019
public sealed class PullbackSearch(IClock clock, string databaseFile, string dataRoot, TextWriter output) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.LongSetup, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read),
        ],
        Feeds: []);

    public const string Verb = "pullback-search";

    // The live setting's line timed, and the lines walked and judged, the one tester's own unless a test gives its own.
    public Func<string, CancellationToken, Task<(TimeSpan Read, TimeSpan Line, int Offered, int Bought)>>? Measure { get; init; }

    public Func<IReadOnlyList<string>, Func<IReadOnlyList<(IdeaLine Line, IdeaFigures Figures)>, IReadOnlyList<string>>, CancellationToken, Task<int>>? Walk { get; init; }

    public const string Source = PullbackSearchView.Source;

    // The first stage's levels, each the extended grid's: the operator's grid of 2026-11-23.
    public static IReadOnlyList<double> Strengths { get; } = [0.40, 0.50, 0.60];

    public static IReadOnlyList<double> Breadths { get; } = [0.40, 0.45, 0.50];

    public static IReadOnlyList<(double Low, double High)> Depths { get; } = [(1, 4), (1, 5), (1.5, 5)];

    public static IReadOnlyList<double> Rewards { get; } = [1.5, 2, 2.5];

    // The second stage's levels, one dial at a time: the fine grid's own, and the stop bands the extended grid holds.
    public static IReadOnlyList<double> DryUps { get; } = [1.0, 1.25, 1.5, 2.0, SweepGrid.Off];

    public static IReadOnlyList<int> Arrivals { get; } = [1, 2, 3, 5, 8];

    public static IReadOnlyList<(double Low, double High)> Stops => SweepGrid.Extended.StopBounds;

    public static IReadOnlyList<int> Bands { get; } = [0, 2, 4, 6];

    // The indices the search walks now: the S&P 400's and 600's grid holds their live rules' other gates, which a stored
    // setup's readings do not carry, and waits for the rebuilt ledger to store them.
    public static IReadOnlyList<string> Indices { get; } = ["GSPC"];

    // The live swing filter's dials as a filter: its registered floors, depth band, dry-up, arrival, reward and stop band,
    // the breadth off where its market gate is off, and no band strength, which the filter states none of.
    public static IdeaFilter LiveFilter(IReadOnlyDictionary<string, double> parameters) => new(
        parameters["strengthFloor"],
        parameters.GetValueOrDefault(SwingFilterRule.MarketGateParameter, 1) == 0 ? SweepGrid.MarketOff : parameters["breadthFloor"],
        parameters["depthLow"],
        parameters["depthHigh"],
        parameters["dryUpCeiling"],
        (int)parameters["arrivalSessions"],
        parameters["rewardToRiskFloor"],
        parameters["stopLow"],
        parameters["stopHigh"],
        0);

    // A plan as the grammar's buy, stop, take-profit and time limit, none where the grammar cannot write it.
    public static string? PlanParts(PlanCombination plan, int sessions)
    {
        var buy = plan.Entry == PlanPoints.NextOpen ? "buy next open"
            : PlanPoints.DistanceOf(plan.Entry) is { } distance ? FormattableString.Invariant($"buy {IdeaGrammar.Number(distance)} typical days under the close within {plan.Window} sessions")
            : IdeaGrammar.Entries.Contains(plan.Entry) ? FormattableString.Invariant($"buy at {plan.Entry} within {plan.Window} sessions")
            : null;
        var stop = PlanPoints.TypicalDays.Concat(PlanPoints.WiderDays).FirstOrDefault(days => PlanPoints.NamesStop(plan.Stop, days)) is var days && days > 0
            ? "stop " + IdeaGrammar.Number(days) + " typical days"
            : null;
        var profit = plan.Exit == PlanPoints.FirstBand && !plan.Half && (plan.TakeProfit is null || plan.Placing == TakeProfitPlacing.Risk)
                ? plan.TakeProfit is { } fallBack ? "take profit first band else " + IdeaGrammar.Number(fallBack) + " risks" : "take profit first band"
            : plan.Exit == PlanPoints.TimeLimit && plan.TakeProfit is { } multiple && (plan.Half || plan.Placing == TakeProfitPlacing.Risk)
                ? FormattableString.Invariant($"take profit {(plan.Half ? IdeaGrammar.Half : IdeaGrammar.Whole)} at {IdeaGrammar.Number(multiple)} risks")
            : plan.Exit == PlanPoints.TimeLimit && plan.TakeProfit is null ? "take profit none"
            : null;

        return buy is null || stop is null || profit is null
            ? null
            : string.Join(IdeaGrammar.Separator, buy, stop, profit, FormattableString.Invariant($"sell after {sessions} sessions"));
    }

    static string Line(string index, IdeaFilter filter, string plan) =>
        string.Join(IdeaGrammar.Separator, FormattableString.Invariant($"event pullback({IdeaGrammar.WordOf(index)}) loose"), filter.Part, plan);

    // The first stage: the four dials crossed, every other dial at the live filter's.
    public static IReadOnlyList<string> FirstStage(string index, IdeaFilter live, string plan) =>
    [
        .. from strength in Strengths
           from breadth in Breadths
           from depth in Depths
           from reward in Rewards
           select Line(index, live with { Strength = strength, Breadth = breadth, DepthLow = depth.Low, DepthHigh = depth.High, Reward = reward }, plan),
    ];

    // The second stage: from the setting chosen, each of the other four dials at each of its levels, the rest held.
    public static IReadOnlyList<string> SecondStage(string index, IdeaFilter chosen, string plan) =>
    [
        .. DryUps.Where(level => level != chosen.DryUp).Select(level => Line(index, chosen with { DryUp = level }, plan)),
        .. Arrivals.Where(level => level != chosen.Arrival).Select(level => Line(index, chosen with { Arrival = level }, plan)),
        .. Stops.Where(level => level != (chosen.StopLow, chosen.StopHigh)).Select(level => Line(index, chosen with { StopLow = level.Low, StopHigh = level.High }, plan)),
        .. Bands.Where(level => level != chosen.Band).Select(level => Line(index, chosen with { Band = level }, plan)),
    ];

    // The first stage's setting chosen on 2005 to 2018 alone: the highest early edge over the benchmark among lines with a
    // result there, the more early picks and then the line's own spelling breaking a tie.
    public static IdeaFilter? Chosen(IReadOnlyList<(IdeaLine Line, IdeaFigures Figures)> walked) =>
        walked
            .Where(one => one.Line.Filter is not null && one.Figures.EarlyEdge is not null)
            .OrderByDescending(one => one.Figures.EarlyEdge)
            .ThenByDescending(one => one.Figures.EarlyPicks)
            .ThenBy(one => one.Line.Canonical, StringComparer.Ordinal)
            .Select(one => one.Line.Filter)
            .FirstOrDefault();

    // The second stage's count, from the levels each dial holds less the one the chosen setting stands at.
    public static int SecondStageCount => (DryUps.Count - 1) + (Arrivals.Count - 1) + (Stops.Count - 1) + (Bands.Count - 1);

    public async Task<int> RunAsync(string index, bool word, CancellationToken cancellation = default)
    {
        if (!Indices.Contains(index, StringComparer.Ordinal))
        {
            output.WriteLine($"{Verb}: the {Core.Families.SetupFamilies.Pullback} on {index} is searched once the rebuilt ledger stores whether its live rule's other gates pass each setup; name '--index GSPC'");

            return 2;
        }

        var register = await new PaperReads(databaseFile).RegisteredCandidatesAsync();
        var standing = RuleRegister.Standing(Core.Families.SetupFamilies.Pullback, true, OwnStepCandidate.NameOf(index), register, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));

        if (RuleRegister.LiveAndVariants(standing).Live is not { } live)
        {
            output.WriteLine($"{Verb}: no live pullback rule stands on {index}");

            return 2;
        }

        var parameters = CandidateEvaluator.Read(live.Parameters);
        var filter = LiveFilter(parameters);

        if (PlanParts(RangeWidthTest.PlanOf(live), IdeaGrammar.FamilyTimeLimit(Core.Families.SetupFamilies.Pullback)) is not { } plan)
        {
            output.WriteLine($"{Verb}: the live rule's plan is not one the grammar writes; nothing is searched");

            return 2;
        }

        var first = FirstStage(index, filter, plan);

        output.WriteLine(FormattableString.Invariant($"{Verb}: the live rule, {live.Candidate}: {Line(index, filter, plan)}"));

        foreach (var line in first)
        {
            output.WriteLine($"{Verb}: first stage: {line}");
        }

        output.WriteLine(FormattableString.Invariant($"{Verb}: {first.Count} line(s) in the first stage, then {SecondStageCount} walked one dial at a time from the setting it chooses on 2005 to 2018: {first.Count + SecondStageCount} in all, every one read in the run's one step-down"));

        // The time, measured on the live setting's line before anything is walked for the search.
        var tester = new IdeaTestRun(clock, databaseFile, dataRoot, output);
        var measured = await (Measure ?? tester.MeasureAsync)(Line(index, filter, plan), cancellation);

        output.WriteLine(FormattableString.Invariant($"{Verb}: measured on the live setting's line: the walker read in {measured.Read.TotalSeconds:0} s and the line walked in {measured.Line.TotalSeconds:0.0} s, {measured.Offered} setup(s) offered and {measured.Bought} bought; about {(measured.Read + (measured.Line * (first.Count + SecondStageCount))).TotalMinutes:0} minute(s) for the search"));

        if (!word)
        {
            output.WriteLine($"{Verb}: the search walks only on the operator's word, given as '--word'; nothing was walked or written");

            return 2;
        }

        IReadOnlyList<string> Then(IReadOnlyList<(IdeaLine Line, IdeaFigures Figures)> walked) => Chosen(walked) is { } chosen ? SecondStage(index, chosen, plan) : [];

        return Walk is { } walk
            ? await walk(first, Then, cancellation)
            : await tester.RunAsync(first, Source, cancellation, then: Then);
    }
}
