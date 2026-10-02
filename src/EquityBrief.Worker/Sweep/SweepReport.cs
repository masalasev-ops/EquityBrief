using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// The sweep's report: one page, every figure on it history, in the seven parts the operator's ruling of
// 2026-09-30 lists, ending with the registration command the freeze would use, not run. It proposes and
// registers nothing.
public static class SweepReport
{
    // Above this share of index-nights with no bar served, the page says plainly the results read better than the
    // market was; proposed and the operator's to rule.
    public const double MissingThreshold = 0.03;

    // The most listings that differ between the two band supports the page names one by one.
    public const int DifferingListingsNamed = 20;

    public static string Build(
        SweepHistoryInputs inputs,
        IReadOnlyList<SweepCandidate> candidates,
        IReadOnlyList<RankRow> rows,
        IReadOnlyList<ConditionTrial> trials,
        IReadOnlyList<ConditionVerdict> verdicts,
        IReadOnlyList<CombinationRow> crossRows,
        IReadOnlyList<CombinationRow> carried,
        IReadOnlyList<SweepDesignResult> results,
        PointInTimeResult pointInTime,
        SweepRunner.State state,
        int nights,
        IReadOnlyList<DateOnly> calendar,
        int firstScored)
    {
        var grid = SweepGrid.Extended;
        var page = new StringBuilder();
        var liveSetting = grid.Carry(SweepGrid.Fine, DialSetting.LiveOnFine);
        var liveMeasures = SweepStages.Direct(candidates, SweepDesign.Live, liveSetting, ConditionSetting.Off, nights);
        var liveTrimmed = SweepStages.WithoutTheLargest(SweepStages.Picks(candidates, SweepDesign.Live), SweepDesign.Live, liveSetting, ConditionSetting.Off);
        var span = FormattableString.Invariant($"{calendar[firstScored]:yyyy-MM-dd} to {calendar[^1]:yyyy-MM-dd}");

        SweepTrimmed TrimmedAt(SweepDesign design, SweepSpace space, int[] point) =>
            SweepStages.WithoutTheLargest(SweepStages.Picks(candidates, design), design, space.Setting(point), space.Conditions(point));

        // The starting point across the designs: edge first, then depth.
        var starts = results.Select(result => (Result: result, Space: SweepSpace.For(result.ConditionsOn), Design: RankRow.Parse(result.DesignKey))).ToArray();
        var chosenAt = SweepSearch.AcrossDesigns([.. starts.Select(one => (one.Result.Proposal, one.Space.LivePoint()))]);
        SweepStart? start = null;

        if (chosenAt is { } at && starts[at].Result.Proposal is { } proposal)
        {
            start = new SweepStart(starts[at].Design, starts[at].Space, proposal.Point, proposal.Depth, proposal.Measures, starts[at].Result.Line);
        }

        var variants = start is null ? [] : SweepPlateau.Variants(candidates, start, nights);

        page.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Sweep report</title><style>");
        page.Append(Style);
        page.Append("</style></head><body><main>");
        page.Append("<p class=\"eyebrow\">Phase 12 / 12.5, the sweep</p><h1>Sweep report</h1>");
        page.Append(Invariant($"<p class=\"history\">Every figure on this page is history: the swing filter replayed over the stored history from {Esc(span)}, {nights:N0} sessions scored after a year of warm-up, with the index as it stood each night, one open trade a stock inside every variation, and every result read as an edge over the same plan entered at the same close on every member that night. None of it is a live record, and nothing here is registered.</p>"));

        // 1. The starting point.
        page.Append("<h2>1. The proposed starting point</h2>");

        if (start is null)
        {
            page.Append("<p class=\"note\">No setting of the designs stage 2 carried stands as a proposal: none sits on its design's plateau, meets the four floors and holds the live rule's edge in each of the last three years. The sweep proposes no starting point, and the live rule stands until the operator rules otherwise. Section 5's maps show where each design came closest.</p>");
        }
        else
        {
            var result = starts[chosenAt!.Value].Result;

            page.Append(Invariant($"<p>The deepest setting of its design's plateau among the designs within {SweepSearch.PlateauMargin:0.00} of the highest edge: it can move {start.Depth.Depth} step(s) on every dial in either direction before its edge falls under the line, {result.Line:0.000} times the risk, the best edge found less the margin. Its edge is {Mult(start.Measures.Edge)} times the risk over {start.Measures.Scored:N0} trades, against the live rule's {Mult(liveMeasures.Edge)} over {liveMeasures.Scored:N0}.</p>"));
            page.Append(DesignWords(start.Design, start.Space, start.Point));
            page.Append("<h3>How it differs from today's live rule</h3>");
            page.Append(Differences(start.Design, start.Space, start.Point));
            page.Append(Record("The starting point over the history", start.Measures, calendar, firstScored, TrimmedAt(start.Design, start.Space, start.Point)));
            page.Append(AcrossDesigns(starts, chosenAt.Value));
            page.Append(Invariant($"<h3>The same design at the other margins</h3><p>The margin, {SweepSearch.PlateauMargin:0.00} of the risk, is proposed and the operator's to rule; the design's proposal at each other margin follows, from the same leaders.</p><ul>"));

            foreach (var other in result.OtherMargins)
            {
                page.Append(Invariant($"<li>At a margin of {other.Margin:0.00}: {Esc(other.Setting)}{(float.IsNaN(other.Edge) ? string.Empty : Invariant($", an edge of {other.Edge:0.000} and a depth of {other.Depth}"))}.</li>"));
            }

            page.Append("</ul>");
        }

        page.Append(Record("Today's live rule over the same history", liveMeasures, calendar, firstScored, liveTrimmed));

        // 2. The variants.
        page.Append("<h2>2. The proposed variants</h2>");

        if (start is null)
        {
            page.Append("<p class=\"note\">No variants, since there is no starting point to move one setting of.</p>");
        }
        else
        {
            var passing = variants.Where(variant => variant.Passes).ToArray();

            page.Append(Invariant($"<p>{passing.Length} of the {variants.Count} one-change variants tested pass all four tests, each read on the edge: the one change reaching a setting on the plateau; open in history, neither side having the higher edge in more than {SweepPlateau.YearsEitherMayWin} of the 8 years; at least {Share(SweepPlateau.OutsideFloor, 0)} of its picks stocks the starting point does not pick; and at least {SweepPlateau.TradesAYear} trades in every year. Up to {SweepPlateau.MostVariants} are proposed, spread across the filter's parts. A condition switched on, switched off or moved is a variant as a dial moved is.</p>"));

            if (passing.Length < SweepPlateau.StrongestReported)
            {
                page.Append(Invariant($"<p class=\"note\">Fewer than {SweepPlateau.StrongestReported} pass, so the strongest {SweepPlateau.StrongestReported} by edge among the rest follow, each naming the test it fails and by how much.</p>"));
            }

            page.Append(VariantTable(variants, start.Line, passing.Length < SweepPlateau.StrongestReported ? SweepPlateau.StrongestReported : 0));
        }

        // 3. The seven conditions.
        page.Append("<h2>3. The seven conditions</h2>");
        page.Append(Conditions(trials, verdicts, rows));

        // 4. The strongest candidates beside the live rule, year by year.
        page.Append("<h2>4. The strongest candidates beside the live rule, year by year</h2>");
        page.Append(Invariant($"<p>Each design stage 2 carried, at its proposal, beside the live rule: trades listed and kept after one open trade a stock, with the listings an open trade kept off, the edge and the raw average result. Stage 1 ranked {rows.Count:N0} designs by the median edge of their viable coarse settings, a design needing {SweepStages.ViableForARank} viable settings to rank on it; the live rule's design ranks {LiveRank(rows)}.</p>"));

        foreach (var (result, space, design) in starts)
        {
            if (result.Proposal is { } held)
            {
                page.Append(Record(Words(design) + " at " + space.Describe(held.Point), held.Measures, calendar, firstScored, TrimmedAt(design, space, held.Point)));
            }
            else
            {
                page.Append("<h3>").Append(Esc(Words(design))).Append("</h3><p class=\"note\">").Append(Esc(string.Join("; ", result.Notes.DefaultIfEmpty("no proposal")))).Append("</p>");
            }
        }

        page.Append(Record("The live rule", liveMeasures, calendar, firstScored, liveTrimmed));
        page.Append(Ranking(rows, carried));

        // 5. The plateau maps.
        page.Append("<h2>5. The plateau maps, on the edge</h2>");

        if (starts.All(one => one.Result.Slices.Count == 0))
        {
            page.Append("<p class=\"note\">Stage 2 wrote no map; see section 7.</p>");
        }
        else
        {
            page.Append(Maps(starts));
        }

        // 6. What was read as it stands today.
        page.Append("<h2>6. What was read as it stands today, and the missing departures</h2>");
        page.Append(AsItStands(inputs, candidates, starts));

        // 7. The point-in-time result, machine time, decisions and failures.
        page.Append("<h2>7. The point-in-time result, machine time, the decisions the sweep took, and what failed</h2>");
        page.Append(PointInTime(pointInTime));
        page.Append(Machine(state, starts));
        page.Append(ResultSizesList(candidates));
        page.Append(SupportChoice(candidates, starts, nights));

        // The registration command the freeze would use.
        page.Append("<h2>The registration the freeze would use</h2>");
        page.Append(Command(start, variants, FormattableString.Invariant($"{inputs.Through:yyyy-MM-dd}")));
        page.Append("</main></body></html>");

        return page.ToString();
    }

    // The page a run that stopped on a chunk failing twice writes: where it stopped and why, what it had done,
    // and the timings it measured. It proposes nothing.
    public static string Stopped(SweepRunner.State state, string reason)
    {
        var page = new StringBuilder();

        page.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Sweep report</title><style>");
        page.Append(Style);
        page.Append("</style></head><body><main>");
        page.Append("<p class=\"eyebrow\">Phase 12 / 12.5, the sweep</p><h1>Sweep report</h1>");
        page.Append("<p class=\"flag\"><b>The run stopped before it finished.</b> ").Append(Esc(reason)).Append("</p>");
        page.Append(Invariant($"<p>It had read the history through {Esc(state.Through ?? "no session")}, saved {state.CandidateChunks} chunk(s) of candidates{(state.CandidatesDone ? ", all of them" : string.Empty)}, {state.RankChunks} chunk(s) of stage 1{(state.RanksDone ? ", all of them" : string.Empty)}, {state.CrossChunks} chunk(s) of step (c){(state.CrossDone ? ", all of them" : string.Empty)} and {state.SearchDone.Count} design(s) of stage 2. Started again under the same build, it goes on from the first chunk missing. Nothing is proposed and nothing registered.</p>"));
        page.Append(Machine(state, []));

        return page.Append("</main></body></html>").ToString();
    }

    // The page a run whose point-in-time check found a difference writes: every difference, and nothing else
    // run.
    public static string StoppedAtPointInTime(SweepRunner.State state, PointInTimeResult result)
    {
        var page = new StringBuilder();

        page.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Sweep report</title><style>");
        page.Append(Style);
        page.Append("</style></head><body><main>");
        page.Append("<p class=\"eyebrow\">Phase 12 / 12.5, the sweep</p><h1>Sweep report</h1>");
        page.Append("<p class=\"flag\"><b>The sweep did not read every session as it stood, so the run stopped before stage 1.</b> Every difference is listed; nothing after the check ran, nothing is proposed and nothing registered.</p>");
        page.Append(PointInTime(result));
        page.Append(Machine(state, []));

        return page.Append("</main></body></html>").ToString();
    }

    static string Record(string title, SweepMeasures measures, IReadOnlyList<DateOnly> calendar, int firstScored, SweepTrimmed? trimmed = null)
    {
        var html = new StringBuilder();

        html.Append("<h3>").Append(Esc(title)).Append(" <span class=\"tag\">history</span></h3>");
        html.Append(Invariant($"<p>{measures.Scored:N0} trades scored of {measures.Listed:N0} kept, {measures.Blocked:N0} listing(s) kept off by an open trade of the same stock; won {Pct(measures.Share)} against a break-even of {Pct(measures.BreakEven)} and no skill at {Pct(measures.NoSkill)}; an edge of {Mult(measures.Edge)} and a raw average of {Mult(measures.AverageMultiple)} times the risk; beating its break-even in {measures.YearsBeatingBreakEven} of 8 years and both in {measures.YearsBeatingBoth}; trades in {measures.BlocksWithTrades} of 30 blocks; a stock listed on {Share(measures.ListingShareOfNights, 0)} of nights.</p>"));

        if (trimmed is not null)
        {
            html.Append(Trimmed(trimmed));
        }

        html.Append("<table><tr><th>Year</th><th>Trades</th><th>Kept off</th><th>Won</th><th>Break-even</th><th>No skill</th><th>Edge</th><th>Raw average</th></tr>");

        for (var year = 0; year < SweepFigures.Years; year++)
        {
            html.Append(Invariant($"<tr><td>{SweepColumns.FirstScored.Year + year}</td><td class=\"num\">{measures.YearScored[year]:N0}</td><td class=\"num\">{BlockedIn(measures, year)}</td><td class=\"num\">{Pct(measures.YearShare[year])}</td><td class=\"num\">{Pct(measures.YearBreakEven[year])}</td><td class=\"num\">{Pct(measures.YearNoSkill[year])}</td><td class=\"num\">{Mult(measures.YearEdge[year])}</td><td class=\"num\">{Mult(measures.YearAverageMultiple[year])}</td></tr>"));
        }

        return html.Append("</table>").ToString();
    }

    // The same record without its largest results by size, stated under it so a figure a few trades carry is
    // seen beside the one that states it.
    public static string Trimmed(SweepTrimmed trimmed) =>
        Invariant($"<p class=\"trimmed\" data-left-out=\"{trimmed.LeftOut}\" data-left=\"{trimmed.Left}\">Without its {trimmed.LeftOut} largest result(s) by size, wins and losses alike: an edge of {Mult(trimmed.Edge)} and a raw average of {Mult(trimmed.AverageMultiple)} times the risk over the {trimmed.Left:N0} trade(s) left.</p>");

    // A result beyond this many times the risk is counted on the page, by the kind of plan that made it.
    public const double ResultBound = 20;

    // Each kind of plan's results over every saved candidate and exit: how many, the largest, the smallest and
    // how many lie beyond the bound either way, so a plan whose results are counted on a risk near nothing shows
    // on the page that reads it.
    public static IReadOnlyList<(string Plan, int Results, float Largest, float Smallest, int Beyond)> ResultSizes(IReadOnlyList<SweepCandidate> candidates, double bound = ResultBound)
    {
        (string Plan, int From, int To)[] kinds =
        [
            ("the stepped plan", SweepCandidate.PlanAt(PlanRule.Ladder, default), SweepCandidate.PlanAt(PlanRule.Ladder, default)),
            ("the plan at the nearest bands", SweepCandidate.PlanAt(PlanRule.NearestBands, SupportKind.AnchoredBand), SweepCandidate.PlanAt(PlanRule.NearestBands, SupportKind.Average)),
            ("section 10's plan", SweepCandidate.PlanAt(PlanRule.Clear, SupportKind.AnchoredBand), SweepCandidate.PlanAt(PlanRule.Clear, SupportKind.Average)),
        ];

        var sizes = new List<(string, int, float, float, int)>();

        foreach (var (plan, from, to) in kinds)
        {
            var results = 0;
            var beyond = 0;
            var largest = float.NaN;
            var smallest = float.NaN;

            foreach (var candidate in candidates)
            {
                for (var at = from; at <= to; at++)
                {
                    if (candidate.Plans[at] is not { } outcomes)
                    {
                        continue;
                    }

                    for (var exit = 0; exit < SweepAxes.Exits; exit++)
                    {
                        var multiple = outcomes.Multiple[exit];

                        if (float.IsNaN(multiple))
                        {
                            continue;
                        }

                        results++;
                        beyond += Math.Abs(multiple) > bound ? 1 : 0;
                        largest = float.IsNaN(largest) || multiple > largest ? multiple : largest;
                        smallest = float.IsNaN(smallest) || multiple < smallest ? multiple : smallest;
                    }
                }
            }

            sizes.Add((plan, results, largest, smallest, beyond));
        }

        return sizes;
    }

    static string ResultSizesList(IReadOnlyList<SweepCandidate> candidates)
    {
        var html = new StringBuilder(Invariant($"<p><b>The size of the results, by the kind of plan.</b> Over every saved candidate and exit, whatever setting reads it; the stepped plan's are counted on the risk its plan stated, and a fill nearer its stop than a setting's stop floor is no trade under that setting.</p><ul class=\"result-sizes\">"));

        foreach (var (plan, results, largest, smallest, beyond) in ResultSizes(candidates))
        {
            html.Append(results == 0
                ? Invariant($"<li data-plan=\"{Esc(plan)}\" data-results=\"0\">{Esc(plan)}: no result.</li>")
                : Invariant($"<li data-plan=\"{Esc(plan)}\" data-results=\"{results}\" data-beyond=\"{beyond}\">{Esc(plan)}: {results:N0} result(s), the largest {largest:0.0} and the smallest {smallest:0.0} times the risk, {beyond:N0} beyond {ResultBound:0} either way.</li>"));
        }

        return html.Append("</ul>").ToString();
    }

    // The record carries the blocked count over the history alone; a year's is not kept, and the cell says so.
    static string BlockedIn(SweepMeasures measures, int year) => year == 0 ? measures.Blocked.ToString("N0", CultureInfo.InvariantCulture) + " in all" : string.Empty;

    static string DesignWords(SweepDesign design, SweepSpace space, int[] point)
    {
        var html = new StringBuilder("<table><tr><th>Check</th><th>Its value</th></tr>");

        foreach (var (check, value) in Checks(design, space, point))
        {
            html.Append("<tr><td>").Append(Esc(check)).Append("</td><td>").Append(Esc(value)).Append("</td></tr>");
        }

        return html.Append("</table>").ToString();
    }

    // Each check in plain words with its value, the design's choices, the dials and the conditions together.
    static IReadOnlyList<(string Check, string Value)> Checks(SweepDesign design, SweepSpace space, int[] point)
    {
        static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        var grid = space.Grid;
        var setting = space.Setting(point);
        var conditions = space.Conditions(point);
        var dryUp = grid.DryUpCeilings[setting.DryUp];
        var market = grid.MarketFloors[setting.Market];

        return
        [
            ("The market", double.IsNegativeInfinity(market) ? "not read" : $"at least {Number(market * 100)}% of the index above its 200-day average"),
            ("The trend", design.Uptrend switch
            {
                UptrendRule.Classifier => "the trend classifier reads an uptrend, as live",
                UptrendRule.ClassifierBelowBoth => "the classifier's uptrend under its version reading a close below both averages as a downtrend",
                UptrendRule.ClassifierBelowBothUnderACross => "the classifier's uptrend under its version reading a close below both averages under a cross as a downtrend",
                UptrendRule.ClassifierHoldsTwoNights => "the classifier's uptrend under its version holding a new label two nights",
                UptrendRule.CloseAboveTwoHundred => "the close above the 200-day average",
                UptrendRule.FiftyAboveTwoHundred => "the 50-day average above the 200-day",
                _ => "the 200-day average above its value 20 sessions before",
            }),
            ("The strength", $"{design.Strength switch { StrengthMeasure.ThreeAndSixMonths => "the mean of its places among the members' 3 and 6-month returns, as live", StrengthMeasure.SixMonths => "its place among the members' 6-month returns", _ => "its place among the members' returns over 12 months less the latest one" }}, at least {Number(grid.StrengthBars[setting.Strength])}"),
            ("The pullback", $"{Number(grid.DepthLows[setting.DepthLow])} to {Number(grid.DepthHighs[setting.DepthHigh])} typical moves below the highest high of the last {design.ReferenceHigh} sessions"),
            ("The volume while it came down", double.IsPositiveInfinity(dryUp) ? "not read" : $"under {Number(dryUp)} times its fifty-day average"),
            ("The support", $"{design.Support switch { SupportKind.AnchoredBand => "a support band with a member that is not a moving average holds the close, as live", SupportKind.AnyBand => "any support band holds the close", _ => "the close within half a typical move of its 20 or 50-day average" }}, the band at least {grid.BandStrengths[setting.Band]} strong"),
            ("The trigger", $"{design.Trigger switch { TriggerKind.AbovePreviousHigh => "a close above the previous session's high, as live", TriggerKind.AbovePreviousClose => "a close above the previous session's close", _ => "a close in the top quarter of the session's range" }}, or back into the band after a close below it, first fired within the last {grid.Freshness[setting.Freshness]} session(s)"),
            ("The trade", $"{design.Plan switch { PlanRule.Ladder => "the ladder's first tranche", PlanRule.NearestBands => "entered at the close, stopped at the setup band's low edge and won at the nearest band above", _ => "section 10's plan, as live" }}, a reward to risk of at least {Number(grid.RewardToRiskFloors[setting.RewardToRisk])}, the stop {Number(grid.StopBounds[setting.Stop].Low)} to {Number(grid.StopBounds[setting.Stop].High)} typical moves below the entry"),
            ("The exit", $"held up to {design.Hold} sessions{(design.BreakEven ? ", the stop moved to the entry once a close stands the risk above it" : ", the stop never moved")}"),
            ("The earnings", design.EarningsWindow == 0 ? "no exclusion for an earnings date" : $"left off with an earnings date within {design.EarningsWindow} sessions"),
            ("The conditions", SweepConditions.Describe(conditions)),
        ];
    }

    static string Differences(SweepDesign design, SweepSpace space, int[] point)
    {
        var liveSpace = SweepSpace.For([]);
        var live = Checks(SweepDesign.Live, liveSpace, liveSpace.LivePoint()).ToDictionary(pair => pair.Check, pair => pair.Value);
        var html = new StringBuilder("<ul>");
        var any = false;

        foreach (var (check, value) in Checks(design, space, point))
        {
            if (live[check] != value)
            {
                any = true;
                html.Append("<li><b>").Append(Esc(check)).Append(":</b> ").Append(Esc(value)).Append(", where the live rule reads ").Append(Esc(live[check])).Append("</li>");
            }
        }

        if (!any)
        {
            html.Append("<li>None: the starting point is today's live rule.</li>");
        }

        return html.Append("</ul>").ToString();
    }

    static string AcrossDesigns((SweepDesignResult Result, SweepSpace Space, SweepDesign Design)[] starts, int chosen)
    {
        var best = starts.Where(one => one.Result.Proposal is not null).Max(one => one.Result.Proposal!.Summary.Edge);
        var html = new StringBuilder(Invariant($"<h3>The five designs' proposals, edge first and then depth</h3><p>The proposals within {SweepSearch.PlateauMargin:0.00} of the highest edge, {best:0.000}, are kept, and the deepest of them is the starting point, ties to the higher edge and then to the setting nearest the live rule.</p><table><tr><th>Design</th><th>Proposal</th><th>Edge</th><th>Depth</th><th>Within the margin</th><th></th></tr>"));

        for (var at = 0; at < starts.Length; at++)
        {
            var (result, space, design) = starts[at];

            if (result.Proposal is not { } proposal)
            {
                html.Append(Invariant($"<tr><td>{Esc(Words(design))}</td><td>{Esc(string.Join("; ", result.Notes.DefaultIfEmpty("none")))}</td><td></td><td></td><td></td><td></td></tr>"));

                continue;
            }

            html.Append(Invariant($"<tr{(at == chosen ? " class=\"live\"" : string.Empty)}><td>{Esc(Words(design))}</td><td>{Esc(space.Describe(proposal.Point))}</td><td class=\"num\">{proposal.Summary.Edge:0.000}</td><td class=\"num\">{proposal.Depth.Depth}</td><td>{Mark(proposal.Summary.Edge >= best - SweepSearch.PlateauMargin)}</td><td>{(at == chosen ? "the starting point" : string.Empty)}</td></tr>"));
        }

        return html.Append("</table>").ToString();
    }

    static string VariantTable(IReadOnlyList<SweepVariant> variants, float line, int strongestFailing)
    {
        var html = new StringBuilder("<table><tr><th>Proposed</th><th>Part</th><th>The one change</th><th>On the plateau</th><th>Open</th><th>Different</th><th>Trades a year</th><th>Edge</th><th>Raw average</th><th>What it fails</th></tr>");
        var failingShown = 0;

        foreach (var variant in variants)
        {
            if (!variant.Passes && failingShown++ >= strongestFailing)
            {
                continue;
            }

            html.Append(Invariant($"<tr class=\"{(variant.Passes ? "pass" : "fail")}\"><td>{(variant.Passes ? "yes" : "no")}</td><td>{Esc(variant.Part)}</td><td>{Esc(variant.Change)}</td><td>{Mark(variant.OnThePlateau)}</td><td>{Mark(variant.Open)} ({variant.YearsItWins} to {variant.YearsTheStartWins})</td><td>{Mark(variant.DifferentEnough)} ({Share(variant.OutsideShare, 0)})</td><td>{Mark(variant.EnoughTrades)} (fewest {variant.FewestTradesInAYear})</td><td class=\"num\">{Mult(variant.Measures.Edge)}</td><td class=\"num\">{Mult(variant.Measures.AverageMultiple)}</td><td>{Esc(string.Join("; ", variant.Failing(line)))}</td></tr>"));
        }

        return html.Append("</table>").ToString();
    }

    static string Conditions(IReadOnlyList<ConditionTrial> trials, IReadOnlyList<ConditionVerdict> verdicts, IReadOnlyList<RankRow> rows)
    {
        var html = new StringBuilder();

        html.Append(Invariant($"<p>Each of the {SweepConditions.Settings.Count} condition settings was added alone to the {SweepSearch.DesignsTried} strongest distinct designs of stage 1, each at its coarse centre. A setting is kept on a design where it raises the edge in at least {SweepSearch.YearsUpToKeep} of the 8 years, at least {SweepSearch.RecentYearsUpToKeep} of them among the last {SweepMeasures.RecentYears}, with at least {SweepMeasures.TradeFloor} scored trades left; a setting survives where at least {SweepSearch.DesignsKeeping} of the {SweepSearch.DesignsTried} designs keep it, and a condition survives where any of its settings does. No best setting is chosen here: every survivor entered stage 2 as a dial, off at one end and each tested setting after it. Every setting's count of ten is printed, since the designs are not independent of one another.</p>"));
        html.Append("<table><tr><th>Condition</th><th>Survives</th><th>Its settings, each with the designs that kept it</th></tr>");

        foreach (var verdict in verdicts)
        {
            html.Append(Invariant($"<tr class=\"{(verdict.Survives ? "pass" : "fail")}\"><td>{verdict.Condition}. {Esc(verdict.Name)}</td><td>{Mark(verdict.Survives)}</td><td>{Esc(string.Join("; ", verdict.Settings.Select(setting => FormattableString.Invariant($"{setting.Setting}: {setting.DesignsKept} of {SweepSearch.DesignsTried}"))))}</td></tr>"));
        }

        html.Append("</table>");

        var kept = trials.Where(trial => trial.Kept).ToArray();

        if (kept.Length > 0)
        {
            html.Append("<h3>Each setting kept, on the designs that kept it, year by year</h3><table><tr><th>Setting</th><th>Design</th><th>Years the edge rose</th><th>Of the last three</th><th>Trades with</th><th>Trades without</th><th>Edge with</th><th>Edge without</th></tr>");

            foreach (var trial in kept.OrderBy(trial => trial.Condition).ThenBy(trial => trial.SettingKey, StringComparer.Ordinal).ThenBy(trial => trial.DesignKey, StringComparer.Ordinal))
            {
                html.Append(Invariant($"<tr><td>{Esc(trial.Setting)}</td><td>{Esc(Words(RankRow.Parse(trial.DesignKey)))}</td><td class=\"num\">{trial.YearsUp}</td><td class=\"num\">{trial.RecentYearsUp}</td><td class=\"num\">{trial.ScoredWith:N0}</td><td class=\"num\">{trial.ScoredWithout:N0}</td><td class=\"num\">{Mult(trial.EdgeWith)}</td><td class=\"num\">{Mult(trial.EdgeWithout)}</td></tr>"));
            }

            html.Append("</table>");
        }

        var dropped = trials.Where(trial => !trial.Kept).GroupBy(trial => trial.Condition).Select(group => (group.Key, Best: group.OrderByDescending(trial => trial.YearsUp).ThenByDescending(trial => trial.RecentYearsUp).First())).ToArray();

        if (dropped.Length > 0)
        {
            html.Append("<h3>Where each condition came closest on a design that dropped it</h3><ul>");

            foreach (var (condition, best) in dropped.OrderBy(pair => pair.Key))
            {
                html.Append(Invariant($"<li>{Esc(SweepConditions.Name(condition))}: {Esc(best.Setting)} on {Esc(Words(RankRow.Parse(best.DesignKey)))} raised the edge in {best.YearsUp} of 8 years, {best.RecentYearsUp} of the last three, with {best.ScoredWith:N0} trades left.</li>"));
            }

            html.Append("</ul>");
        }

        return html.ToString();
    }

    static string LiveRank(IReadOnlyList<RankRow> rows)
    {
        var ordered = SweepStages.Ranked(rows);
        var liveKey = SweepDesign.Live.Key;
        var at = ordered.ToList().FindIndex(row => row.Key == liveKey);

        return at < 0 ? "nowhere" : Invariant($"{at + 1:N0} of {ordered.Count:N0}");
    }

    static string Ranking(IReadOnlyList<RankRow> rows, IReadOnlyList<CombinationRow> carried)
    {
        var ordered = SweepStages.Ranked(rows).ToList();
        var liveKey = SweepDesign.Live.Key;
        var html = new StringBuilder();

        html.Append(Invariant($"<h3>Stage 1's ranking of designs</h3><p>{rows.Count:N0} designs, each over {SweepGrid.Coarse.Variations:N0} coarse settings, {(long)rows.Count * SweepGrid.Coarse.Variations:N0} variations, each walked with one open trade a stock. A setting is viable where it has at least {SweepMeasures.TradeFloor} scored trades and beats both its break-even and no skill in at least {SweepMeasures.YearsBeating} of the 8 years; a design ranks by the median edge of its viable settings where it has at least {SweepStages.ViableForARank} of them, and below every such design by its share otherwise.</p>"));
        html.Append("<table><tr><th>Rank</th><th>Design</th><th>Viable settings</th><th>Share</th><th>Median edge of the viable</th><th>Median raw average</th><th></th></tr>");

        foreach (var (row, rank) in ordered.Select((row, at) => (row, at)).Where(pair => pair.at < 25 || pair.row.Key == liveKey || carried.Any(design => design.DesignKey == pair.row.Key)))
        {
            var marks = new List<string>();

            if (carried.Any(design => design.DesignKey == row.Key))
            {
                marks.Add("carried to stage 2");
            }

            if (row.Key == liveKey)
            {
                marks.Add("the live rule's design");
            }

            var first = ordered.FindIndex(other => SweepStages.SameFigures(other, row));

            if (first < rank)
            {
                marks.Add(Invariant($"the same figures as rank {first + 1}"));
            }

            html.Append(Invariant($"<tr{(row.Key == liveKey ? " class=\"live\"" : string.Empty)}><td class=\"num\">{rank + 1}</td><td>{Esc(Words(row.Design))}</td><td class=\"num\">{row.Viable:N0}</td><td class=\"num\">{Share(row.ViableShare, 2)}</td><td class=\"num\">{Mult(row.MedianEdge)}</td><td class=\"num\">{Mult(row.MedianMultiple)}</td><td>{Esc(string.Join(", ", marks))}</td></tr>"));
        }

        html.Append("</table>");

        if (ordered.FirstOrDefault(row => row.Key == liveKey) is { LiveScored: not null } live)
        {
            html.Append(Invariant($"<p>The live rule's own settings, strength 0.50, depth 1 to 5, dry-up under 1.5, freshness 3, reward to risk 1.5, the stop 0.5 to 4, the market at 45% and band strength 0, all among the coarse values: {live.LiveScored:N0} scored trades, won {Pct(live.LiveShare)} against a break-even of {Pct(live.LiveBreakEven)} and no skill at {Pct(live.LiveNoSkill)}, an edge of {Mult(live.LiveEdge)} and a raw average of {Mult(live.LiveMultiple)}, beating both in {live.LiveYearsBeatingBoth} of 8 years. <span class=\"tag\">history</span></p>"));
        }

        html.Append(Invariant($"<h3>Step (c), the designs sent on to stage 2</h3><p>Every surviving condition was crossed on and off at the middle of its tested settings with the ten designs and each one's structural neighbours short of the exit, over the coarse settings; the {carried.Count} strongest distinct rows on the edge went to stage 2.</p><table><tr><th>Design</th><th>Conditions on</th><th>Viable settings</th><th>Median edge</th></tr>"));

        foreach (var row in carried)
        {
            html.Append(Invariant($"<tr><td>{Esc(Words(RankRow.Parse(row.DesignKey)))}</td><td>{Esc(SweepConditions.Describe(ConditionSetting.FromIndexes([.. row.Combination.Split(',').Select(part => int.Parse(part, CultureInfo.InvariantCulture))])))}</td><td class=\"num\">{row.Viable:N0}</td><td class=\"num\">{Mult(row.MedianEdge)}</td></tr>"));
        }

        return html.Append("</table>").ToString();
    }

    // The maps: every pair of dials sliced through each design's proposal, drawn one pair at a time from two
    // lists, opening on the strength and reward to risk pair.
    static string Maps((SweepDesignResult Result, SweepSpace Space, SweepDesign Design)[] starts)
    {
        var html = new StringBuilder();
        var data = new List<object>();

        foreach (var (result, space, design) in starts)
        {
            if (result.Slices.Count == 0)
            {
                continue;
            }

            data.Add(new
            {
                design = Words(design),
                proposal = result.Proposal is { } proposal ? space.Describe(proposal.Point) : "none",
                line = result.Line,
                slices = result.Slices.Select(slice => new { rowDial = slice.RowDial, columnDial = slice.ColumnDial, rowLabels = slice.RowLabels, columnLabels = slice.ColumnLabels, edge = slice.Edge.Select(row => row.Select(value => float.IsNaN(value) ? (float?)null : value).ToArray()).ToArray(), on = slice.OnThePlateau, scored = slice.Scored, row = slice.Row, column = slice.Column }).ToArray(),
            });
        }

        html.Append(Invariant($"<p>Each map slices two dials through the design's proposal, the other dials held there, reaching {SweepSearch.SliceReach} steps either side; a cell is its edge, marked where it is on the plateau, above the design's line and meeting the floors. Choose the design and the two dials.</p>"));
        html.Append("<div class=\"maps\"><label>Design <select id=\"map-design\"></select></label> <label>Rows <select id=\"map-rows\"></select></label> <label>Columns <select id=\"map-columns\"></select></label><div id=\"map\"></div></div>");
        html.Append("<script id=\"map-data\" type=\"application/json\">").Append(JsonSerializer.Serialize(data).Replace("</", "<\\/", StringComparison.Ordinal)).Append("</script>");
        html.Append(MapScript);

        return html.ToString();
    }

    static string AsItStands(SweepHistoryInputs inputs, IReadOnlyList<SweepCandidate> candidates, (SweepDesignResult Result, SweepSpace Space, SweepDesign Design)[] starts)
    {
        var html = new StringBuilder();
        var missing = inputs.IndexNights == 0 ? 0 : (double)inputs.IndexNightsWithoutABar / inputs.IndexNights;

        html.Append(Invariant($"<p><b>The missing departures.</b> Of {inputs.IndexNights:N0} index-nights over the history, {inputs.IndexNightsWithoutABar:N0} have no bar served, {Share(missing, 2)}; {inputs.NamesWithoutBars} name(s) the index held have no bars at all and {inputs.NamesMissingSomeSessions} miss some sessions. "));
        html.Append(missing > MissingThreshold
            ? "That is above the 3% the page holds it to, so the results read better than the market was: the names missing are mostly the ones that failed.</p>"
            : "That is at or under the 3% the page holds it to. The count sees only the departures the index feed still lists; one it no longer lists would be invisible here.</p>");
        html.Append("<p><b>What is read as it stands today, not as it stood on the session.</b></p><ul>");
        html.Append(Invariant($"<li>The sector labels, as the index feed files them today: {inputs.NamesWithoutASector} name(s) the index held carry none, most of them departed, and each is left in and reads no sector rank, since removing them would flatter the sector condition through survivorship; {candidates.Count(candidate => candidate.SectorRank < 0):N0} of {candidates.Count:N0} candidates carry no rank.</li>"));
        html.Append(Invariant($"<li>The earnings surprises: {inputs.Surprises:N0} read from {(inputs.SurpriseSource == SweepHistory.PulledSurprises ? "the pulled surprises, reaching the years before the store's own" : inputs.SurpriseSource == SweepHistory.CalendarSurprises ? "the calendar's own year alone, today's members only, so the beat condition reads survivors over one year and can only be a variant" : "nowhere, no surprise being stored, so the beat condition read nothing")}; the provider's percent is split-proof and the actual is never read.</li>"));
        html.Append("<li>The bars are the provider's adjusted prices as served now, the pulled years scaled to the store's own over the sessions both hold, so the 52-week high ratio shifts slightly with a dividend inside the year and not with a split, and every ratio and distance in typical moves is the same.</li>");
        html.Append("<li>The earnings dates are as the provider files them now, and the membership spans as the index feed states them now.</li>");
        html.Append("<li>The fundamental readings are left out, since the provider restates old figures.</li>");
        html.Append("<li>A suspect series is read as none, since the store holds a name's suspicion as of now alone.</li>");
        html.Append("<li>The trend rule's versions are replayed by today's code for them, and the calendar is the days at least half the names spanning each hold, since the exchange's closure table covers the store's own years.</li>");
        html.Append("<li>A trigger's first firing reads the sessions before on the name's own series whether or not it was a member on them, where the night reads only the nights that stored a result for it.</li>");
        html.Append("</ul>");

        var averages = starts.Where(one => one.Design.Support == SupportKind.Average).ToArray();

        html.Append(averages.Length == 0
            ? "<p><b>A stop not at a band.</b> No design stage 2 carried stops one typical move below an average, so every one of them stops at a band.</p>"
            : "<p class=\"flag\"><b>A stop not at a band.</b> A design stage 2 carried reads the average itself as support and stops one typical move below it, a distance where every other design stops at a band. Adopting it would mean the live plan's stop no longer always comes from a price level, which section 10 states as a principle. That is the operator's decision, not a setting.</p>");

        return html.ToString();
    }

    static string PointInTime(PointInTimeResult result)
    {
        var html = new StringBuilder();

        html.Append(Invariant($"<p><b>The point-in-time check.</b> {result.Compared:N0} of {result.Samples:N0} name-sessions rebuilt with the night's own components over one scratch store a worker, {result.FromTheLiveList} of them the live list's, in {result.Seconds:0.0} s: the averages, ATR, RSI and fifty-day volume, the swings the year holds, every band and the live label, each compared exactly. {(result.Clean ? "No difference." : Invariant($"{result.Differences.Count:N0} difference(s), listed."))}</p>"));

        if (!result.Clean)
        {
            html.Append("<table><tr><th>Name</th><th>Session</th><th>What</th><th>The sweep read</th><th>The night computes</th></tr>");

            foreach (var difference in result.Differences)
            {
                html.Append(Invariant($"<tr><td>{Esc(difference.Ticker)}</td><td>{difference.Session:yyyy-MM-dd}</td><td>{Esc(difference.What)}</td><td>{Esc(difference.Sweep)}</td><td>{Esc(difference.Night)}</td></tr>"));
            }

            html.Append("</table>");
        }

        return html.ToString();
    }

    static string Machine(SweepRunner.State state, (SweepDesignResult Result, SweepSpace Space, SweepDesign Design)[] starts)
    {
        var html = new StringBuilder("<p><b>Machine time by step.</b></p><ul>");

        foreach (var (stage, seconds) in state.Seconds)
        {
            html.Append("<li>").Append(Esc(StageName(stage))).Append(": ").Append(Duration(seconds)).Append("</li>");
        }

        html.Append("</ul><p><b>The timing checks, measured and recorded, not waited on.</b></p><ul>");

        foreach (var timing in state.Timings.DefaultIfEmpty("none"))
        {
            html.Append("<li>").Append(Esc(timing)).Append("</li>");
        }

        html.Append("</ul><p><b>The pauses for the night.</b></p><ul>");

        foreach (var pause in state.Pauses.DefaultIfEmpty("none"))
        {
            html.Append("<li>").Append(Esc(pause)).Append("</li>");
        }

        html.Append("</ul><p><b>The decisions it took itself.</b></p><ul>");
        html.Append(Invariant($"<li>The plateau's line is the best edge among the evaluated settings meeting the floors less {SweepSearch.PlateauMargin:0.00}, fixed before any depth is measured; depth is counted to at most {SweepSearch.MostDepth} steps; the {SweepSearch.Leaders} highest-edge settings meeting the floors are the leaders; the sample is balanced so every value of every dial appears equally often, drawn with seed {SweepSearch.Seed}, sized from the first {SweepSearch.TimedPoints:N0} settings' time against a budget of {state.SampleBudgetSeconds / 3600:0.00} hours a design and never more than {SweepSearch.MostSampled:N0} settings; a grid end on a dial of three or more values is looked beyond by up to {SweepSpace.Beyond} values.</li>"));
        html.Append("<li>The trigger's freshness is computed per design and not per combination of dials: the arrival reads the event alone, as the live trigger reads it, so no dial feeds it.</li>");
        html.Append("<li>The rising 200-day average is read as the average above its own value 20 sessions before; the 12-month measure as the return from 252 sessions back to 21 sessions back; support at an average as the 20-day or 50-day whose zone of half a typical move either side holds the close, the higher first, its strength the strength of the band the average sits in.</li>");
        html.Append("<li>A candidate a setting reads is a member-session some setting of the extended grid could list; the rest are not stored, since no variation reads them.</li>");
        html.Append("<li>Wilder's ATR and RSI are seeded where the night's year of bars begins on each session, the swings the night's year holds are the ones read, and the point-in-time check holds the sweep to the night's own components.</li>");

        foreach (var decision in state.Decisions)
        {
            html.Append("<li>").Append(Esc(decision)).Append("</li>");
        }

        foreach (var (result, _, design) in starts)
        {
            foreach (var line in result.Refinement.Concat(result.Extensions).Concat(result.Limits).Concat(result.Notes))
            {
                html.Append("<li>").Append(Esc(Words(design) + ": " + line)).Append("</li>");
            }
        }

        html.Append("</ul><p><b>Failed or left out.</b></p><ul>");

        foreach (var failure in state.Failures.DefaultIfEmpty("nothing failed"))
        {
            html.Append("<li>").Append(Esc(failure)).Append("</li>");
        }

        if (state.StoppedAfterStageOne)
        {
            html.Append("<li>The run projected past five days and stopped after stage 1.</li>");
        }

        if (state.StoppedAtPointInTime)
        {
            html.Append("<li>The point-in-time check found a difference and the run stopped before stage 1.</li>");
        }

        return html.Append("</ul>").ToString();
    }

    // The support choice: for each design carried, the candidates each kind of band admits and the listings at
    // the proposal that differ between the two, with the setup-level differences counted.
    static string SupportChoice(IReadOnlyList<SweepCandidate> candidates, (SweepDesignResult Result, SweepSpace Space, SweepDesign Design)[] starts, int nights)
    {
        var html = new StringBuilder("<p><b>The support choice.</b> ");
        var anchored = candidates.Count(candidate => candidate.Band[(int)SupportKind.AnchoredBand] >= 0);
        var any = candidates.Count(candidate => candidate.Band[(int)SupportKind.AnyBand] >= 0);
        var onlyAny = candidates.Count(candidate => candidate.Band[(int)SupportKind.AnchoredBand] < 0 && candidate.Band[(int)SupportKind.AnyBand] >= 0);

        html.Append(Invariant($"Of {candidates.Count:N0} candidates, an anchored support band holds the close on {anchored:N0} and any support band on {any:N0}; the {onlyAny:N0} that differ are the ones whose only band holding the close is made of averages alone.</p><ul>"));

        foreach (var (result, space, design) in starts)
        {
            if (result.Proposal is not { } proposal || design.Support == SupportKind.Average)
            {
                continue;
            }

            var setting = space.Setting(proposal.Point);
            var conditions = space.Conditions(proposal.Point);
            var anchoredKept = new HashSet<(int, int)>();
            var anyKept = new HashSet<(int, int)>();

            SweepStages.Direct(candidates, design with { Support = SupportKind.AnchoredBand }, setting, conditions, nights, anchoredKept);
            SweepStages.Direct(candidates, design with { Support = SupportKind.AnyBand }, setting, conditions, nights, anyKept);

            var differing = anchoredKept.Union(anyKept).Except(anchoredKept.Intersect(anyKept)).OrderBy(pair => pair.Item2).ThenBy(pair => pair.Item1).ToArray();

            html.Append(Invariant($"<li>{Esc(Words(design))} at its proposal: anchored bands list {anchoredKept.Count:N0} name-sessions and any band {anyKept.Count:N0}, {differing.Length:N0} differing"));

            if (differing.Length == 0)
            {
                html.Append("; the two read the history alike here and share one rank.</li>");
            }
            else
            {
                html.Append(": ").Append(Esc(string.Join(", ", differing.Take(DifferingListingsNamed).Select(pair => FormattableString.Invariant($"name {pair.Item1} on session {pair.Item2}"))))).Append(differing.Length > DifferingListingsNamed ? ", and more" : string.Empty).Append(".</li>");
            }
        }

        return html.Append("</ul>").ToString();
    }

    // The command the freeze would use, once its verb is built. Not run.
    static string Command(SweepStart? start, IReadOnlyList<SweepVariant> variants, string through)
    {
        if (start is null)
        {
            return "<p>No starting point is proposed, so there is no registration to state.</p>";
        }

        var grid = start.Space.Grid;
        var s = start.Space.Setting(start.Point);
        var conditions = start.Space.Conditions(start.Point);
        string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        var dryUp = grid.DryUpCeilings[s.DryUp];
        var market = grid.MarketFloors[s.Market];
        var settings = string.Join(
            ",",
            $"strengthFloor={Number(grid.StrengthBars[s.Strength])}",
            $"depthLow={Number(grid.DepthLows[s.DepthLow])}",
            $"depthHigh={Number(grid.DepthHighs[s.DepthHigh])}",
            $"dryUpCeiling={(double.IsPositiveInfinity(dryUp) ? "off" : Number(dryUp))}",
            $"arrivalSessions={grid.Freshness[s.Freshness]}",
            $"rewardToRiskFloor={Number(grid.RewardToRiskFloors[s.RewardToRisk])}",
            $"stopLow={Number(grid.StopBounds[s.Stop].Low)}",
            $"stopHigh={Number(grid.StopBounds[s.Stop].High)}",
            $"breadthFloor={(double.IsNegativeInfinity(market) ? "off" : Number(market))}",
            $"bandStrengthFloor={grid.BandStrengths[s.Band]}");
        var structural = start.Design == SweepDesign.Live with { Hold = start.Design.Hold, BreakEven = start.Design.BreakEven }
            ? string.Empty
            : $" --design \"{start.Design.Key}\"";
        var conditioned = conditions.IsOff ? string.Empty : $" --conditions \"{conditions.Key}\"";
        var chosen = variants.Where(variant => variant.Passes).Select(variant => variant.Change).ToArray();
        var flagged = chosen.Length == 0 ? string.Empty : $" --variants \"{string.Join("; ", chosen)}\"";
        var command = $"dotnet run --project src/EquityBrief.Worker -- register --freeze --settings {settings}{structural}{conditioned}{flagged} --evidence \"the sweep over the history through {through}\"";

        var html = new StringBuilder("<pre>").Append(Esc(command)).Append("</pre>");

        html.Append("<p>Not run. The `register --freeze` verb is built with the freeze itself, once the operator approves the starting point and the variants; it opens the filter version holding these settings, retires the six candidates registered today, and registers the starting rule, the approved variants and, where its evaluator has landed, the fundamentals candidate, at one instant. ");

        if (structural.Length > 0)
        {
            html.Append("The starting point's design differs from the live rule's in a structural choice, so the filter's code has to express that choice before the freeze can register it, and that change moves the filter's pin with its own remedy. ");
        }

        if (conditioned.Length > 0)
        {
            html.Append("The starting point switches a condition on, which the filter does not read today, so the freeze's change adds its reading to the filter before it can register it. ");
        }

        if (!double.IsPositiveInfinity(dryUp) && grid.BandStrengths[s.Band] == 0 && !double.IsNegativeInfinity(market))
        {
            html.Append("Every dial it names is one the filter already holds.");
        }
        else
        {
            html.Append("A setting it names as off, or the band strength floor, is one the filter does not hold today, which the freeze's change adds.");
        }

        return html.Append("</p>").ToString();
    }

    static string Words(SweepDesign design) =>
        $"{design.Strength}, {design.Uptrend}, {design.Support}, high of {design.ReferenceHigh}, {design.Trigger}, {design.Plan}, hold {design.Hold}{(design.BreakEven ? " with break-even" : string.Empty)}, earnings {(design.EarningsWindow == 0 ? "off" : design.EarningsWindow.ToString(CultureInfo.InvariantCulture))}";

    static string StageName(string stage) => stage switch
    {
        "read" => "reading the history from the store",
        "series" => "the indicators, swings, trend labels, condition readings and cross-sections, every name and session; no band is built here",
        "candidates" => "the candidates, their plans' eight exits and each plan's benchmark on every member",
        "point-in-time" => "the point-in-time check, rebuilding the sample with the night's own components",
        "stage1" => "stage 1, every design over the coarse settings with one open trade a stock",
        "step-b" => "step (b), each condition setting alone on the ten strongest designs",
        "step-c" => "step (c), the survivors crossed with the designs and their neighbours",
        "stage2" => "stage 2, the carried designs sampled, their leaders' depth measured and each proposal refined",
        _ => stage,
    };

    static string Duration(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);

        return FormattableString.Invariant($"{(int)span.TotalHours}h {span.Minutes:00}m {span.Seconds:00}s");
    }

    // A share of one as a per cent, the sign against the figure.
    static string Share(double value, int places) =>
        (value * 100).ToString(places == 0 ? "0" : "0." + new string('0', places), CultureInfo.InvariantCulture) + "%";

    static string Mark(bool held) => held ? "yes" : "no";

    static string Pct(double? value) => value is { } held ? held.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "none";

    static string Mult(double? value) => value is { } held ? held.ToString("0.000", CultureInfo.InvariantCulture) : "none";

    static string Esc(string text) => WebUtility.HtmlEncode(text);

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    const string MapScript = """
        <script>
        (function () {
          var data = JSON.parse(document.getElementById('map-data').textContent);
          var design = document.getElementById('map-design'), rows = document.getElementById('map-rows'), columns = document.getElementById('map-columns'), map = document.getElementById('map');
          data.forEach(function (one, at) { var o = document.createElement('option'); o.value = at; o.textContent = one.design; design.appendChild(o); });
          function dials(at) { var names = []; data[at].slices.forEach(function (s) { if (names.indexOf(s.rowDial) < 0) names.push(s.rowDial); if (names.indexOf(s.columnDial) < 0) names.push(s.columnDial); }); return names; }
          function fill(select, names, chosen) { select.innerHTML = ''; names.forEach(function (n) { var o = document.createElement('option'); o.value = n; o.textContent = n; if (n === chosen) o.selected = true; select.appendChild(o); }); }
          function draw() {
            var one = data[design.value]; var r = rows.value, c = columns.value;
            var slice = one.slices.filter(function (s) { return (s.rowDial === r && s.columnDial === c) || (s.rowDial === c && s.columnDial === r); })[0];
            if (!slice) { map.innerHTML = '<p class="note">Choose two different dials.</p>'; return; }
            var flip = slice.rowDial !== r;
            var rl = flip ? slice.columnLabels : slice.rowLabels, cl = flip ? slice.rowLabels : slice.columnLabels;
            var h = '<table class="map"><caption>' + r + ' by ' + c + ' through ' + one.proposal + ', the edge where it is on the plateau marked; line ' + one.line.toFixed(3) + '</caption><tr><th></th>';
            cl.forEach(function (l) { h += '<th>' + l + '</th>'; }); h += '</tr>';
            rl.forEach(function (l, i) { h += '<tr><th>' + l + '</th>'; cl.forEach(function (_, j) { var e = flip ? slice.edge[j][i] : slice.edge[i][j]; var on = flip ? slice.on[j][i] : slice.on[i][j]; var n = flip ? slice.scored[j][i] : slice.scored[i][j]; var here = flip ? (j === slice.row && i === slice.column) : (i === slice.row && j === slice.column); h += '<td class="' + (on ? 'good' : 'poor') + (here ? ' here' : '') + '" title="' + n + ' trades">' + (e === null ? 'none' : e.toFixed(3)) + '</td>'; }); h += '</tr>'; });
            map.innerHTML = h + '</table>';
          }
          design.addEventListener('change', function () { var names = dials(design.value); fill(rows, names, names[0]); fill(columns, names, names[5] || names[1]); draw(); });
          rows.addEventListener('change', draw); columns.addEventListener('change', draw);
          if (data.length) { var names = dials(0); fill(rows, names, 'strength'); fill(columns, names, 'reward to risk'); draw(); }
        })();
        </script>
        """;

    internal const string Style = """
        :root{--bg:#f7f6f2;--card:#fffefa;--ink:#1f2328;--muted:#5d6470;--line:#d9d6cc;--good:#d6ecd9;--poor:#f1dede;--flag:#fff1cc;--accent:#2f4b6e}
        @media (prefers-color-scheme: dark){:root:not([data-theme="light"]){--bg:#15181c;--card:#1c2026;--ink:#e6e8eb;--muted:#9aa3ad;--line:#343a42;--good:#1f3a26;--poor:#3d2224;--flag:#3b3316;--accent:#9dbbe0}}
        :root[data-theme="dark"]{--bg:#15181c;--card:#1c2026;--ink:#e6e8eb;--muted:#9aa3ad;--line:#343a42;--good:#1f3a26;--poor:#3d2224;--flag:#3b3316;--accent:#9dbbe0}
        body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.55 system-ui,-apple-system,"Segoe UI",sans-serif}
        main{max-width:1100px;margin:0 auto;padding:24px 16px 64px}
        h1{font-size:28px;margin:4px 0 12px}h2{font-size:20px;margin:36px 0 10px;border-top:1px solid var(--line);padding-top:18px}h3{font-size:16px;margin:22px 0 8px}
        .eyebrow{text-transform:uppercase;letter-spacing:.08em;font-size:12px;color:var(--muted);margin:0}
        .history{background:var(--card);border:1px solid var(--line);padding:10px 14px;border-radius:6px}
        .decision,.flag{background:var(--flag);border:1px solid var(--line);padding:10px 14px;border-radius:6px}
        .note{color:var(--muted)}.tag{font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--muted);border:1px solid var(--line);border-radius:4px;padding:1px 5px;margin-left:6px}
        nav.runs{font-size:13px;color:var(--muted);margin:0 0 12px}nav.runs a{margin-right:10px}
        table{border-collapse:collapse;width:100%;margin:8px 0 16px;background:var(--card);display:block;overflow-x:auto}
        th,td{border-bottom:1px solid var(--line);padding:5px 8px;text-align:left;vertical-align:top}td.num{text-align:right;font-variant-numeric:tabular-nums}
        tr.live td{background:var(--flag)}tr.fail td{color:var(--muted)}
        table.map td{text-align:right;font-variant-numeric:tabular-nums}td.good{background:var(--good)}td.poor{background:var(--poor)}td.here{outline:2px solid var(--accent)}
        .maps label{margin-right:12px}
        caption{text-align:left;color:var(--muted);padding:4px 0}pre{white-space:pre-wrap;background:var(--card);border:1px solid var(--line);padding:10px;border-radius:6px}
        """;
}
