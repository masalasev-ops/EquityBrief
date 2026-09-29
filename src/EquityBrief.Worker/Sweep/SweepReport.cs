using System.Globalization;
using System.Net;
using System.Text;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// The sweep's report: one page, every figure on it history, in the order the operator asked for it. It proposes
// and registers nothing.
public static class SweepReport
{
    // Above this share of index-nights with no bar served, the page says plainly the results read better than the
    // market was; proposed and the operator's to rule.
    public const double MissingThreshold = 0.03;

    public static string Build(
        SweepHistoryInputs inputs,
        IReadOnlyList<SweepCandidate> candidates,
        IReadOnlyList<RankRow> rows,
        IReadOnlyList<SweepDesign> carried,
        IReadOnlyDictionary<SweepDesign, SweepVariation[]> fine,
        SweepRunner.State state,
        int nights,
        IReadOnlyList<DateOnly> calendar,
        int firstScored)
    {
        var grid = SweepGrid.Fine;
        var page = new StringBuilder();

        // The starting point: the centre of the best plateau among the designs stage 2 carried.
        SweepStart? start = null;
        var centres = new List<(SweepDesign Design, DialSetting Setting, double Median, int Plateau)>();

        foreach (var (design, variations) in fine)
        {
            if (SweepPlateau.Centre(grid, variations) is { } centre)
            {
                centres.Add((design, centre.Setting, centre.NeighbourMedian, centre.PlateauSize));
            }
        }

        if (centres.OrderByDescending(one => one.Median).FirstOrDefault() is { Design: var chosen, Setting: var setting, Median: var median, Plateau: var plateau } && centres.Count > 0)
        {
            start = new SweepStart(chosen, setting, median, plateau, SweepStages.Direct(candidates, chosen, grid, setting, nights));
        }

        var variants = start is null ? [] : SweepPlateau.Variants(candidates, start, fine, nights);
        var liveMeasures = SweepStages.Direct(candidates, SweepDesign.Live, SweepGrid.Coarse, DialSetting.LiveOnCoarse, nights);
        var span = FormattableString.Invariant($"{calendar[firstScored]:yyyy-MM-dd} to {calendar[^1]:yyyy-MM-dd}");

        page.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Sweep report</title><style>");
        page.Append(Style);
        page.Append("</style></head><body><main>");
        page.Append("<p class=\"eyebrow\">Phase 12 / 12.5, the sweep</p><h1>Sweep report</h1>");
        page.Append(Invariant($"<p class=\"history\">Every figure on this page is history: the swing filter replayed over the stored history from {Esc(span)}, {nights:N0} sessions scored after a year of warm-up, with the index as it stood each night. None of it is a live record, and nothing here is registered.</p>"));
        page.Append("<div class=\"decision\"><b>The trigger's freshness.</b> It is computed per design and not per combination of dials, and this is the trigger's current meaning rather than a change to it. Read from the code, the night stores each member's trigger event whether or not its setup held, and the arrival reads that event off the sessions before; the one part of the event reading more than the day's prices is its return into the setup's band, a structural choice. No dial feeds the freshness, so the enumeration over the depth, the volume bar and the band floor was not needed, and a first firing read on the trigger's own condition is the same trigger.</div>");

        // 1. The starting point.
        page.Append("<h2>1. The proposed starting point</h2>");

        if (start is null)
        {
            page.Append("<p class=\"note\">No setting of the five designs stage 2 carried sits on a plateau and meets all four floors: beating its break-even in at least 6 of the 8 years and with its best year removed, at least 300 trades in at least 22 of the 30 blocks, and a stock listed on at least 60% of nights. The sweep proposes no starting point, and the live rule stands until the operator rules otherwise. The plateau maps below show where each design came closest.</p>");
        }
        else
        {
            page.Append(Invariant($"<p>The centre of a plateau of {start.PlateauSize:N0} settings, the one whose neighbours' median result is highest, {start.NeighbourMedian:0.000} times the risk a trade put up.</p>"));
            page.Append(DesignWords(start.Design, start.Setting, grid));
            page.Append("<h3>How it differs from today's live rule</h3>");
            page.Append(Differences(start.Design, start.Setting, grid));
            page.Append(Record("The starting point over the history", start.Measures, calendar, firstScored));
        }

        page.Append(Record("Today's live rule over the same history", liveMeasures, calendar, firstScored));

        // 2. The variants.
        page.Append("<h2>2. The proposed variants</h2>");

        if (start is null)
        {
            page.Append("<p class=\"note\">No variants, since there is no starting point to move one setting of.</p>");
        }
        else
        {
            var passing = variants.Where(variant => variant.Passes).ToArray();

            page.Append(Invariant($"<p>{passing.Length} of the {variants.Count} one-change variants tested pass all four tests: the one change reaching a setting that itself beats its break-even and no skill; open in history, neither side winning more than {SweepPlateau.YearsEitherMayWin} of the 8 years on the average result; at least {Share(SweepPlateau.OutsideFloor, 0)} of its picks stocks the starting point does not pick; and at least {SweepPlateau.TradesAYear} trades in every year. Up to six are proposed, spread across the filter's parts.</p>"));
            page.Append("<p class=\"note\">A move one step tighter on a dial picks some of the starting point's stocks and no others, so its share outside is nought by the way the test is drawn: only a looser setting, or a structural choice stage 2 carried, can pick a quarter of its stocks elsewhere. The quarter is the starting figure the ruling allows the report to argue.</p>");
            page.Append(VariantTable(variants));
        }

        // 3. Stage 1's ranking.
        page.Append("<h2>3. Stage 1's ranking of designs</h2>");
        page.Append(Ranking(rows, carried));

        // 4. The plateau maps.
        page.Append("<h2>4. The plateau maps from stage 2</h2>");

        if (fine.Count == 0)
        {
            page.Append("<p class=\"note\">Stage 2 did not run; see section 6.</p>");
        }

        foreach (var design in carried.Where(fine.ContainsKey))
        {
            page.Append(Maps(design, fine[design], grid, candidates, nights, calendar, firstScored));
        }

        // 5. What the ruling asks to be stated.
        page.Append("<h2>5. What the ruling asks to be stated</h2>");

        var missing = inputs.IndexNights == 0 ? 0 : (double)inputs.IndexNightsWithoutABar / inputs.IndexNights;

        page.Append(Invariant($"<p><b>The missing departures.</b> Of {inputs.IndexNights:N0} index-nights over the history, {inputs.IndexNightsWithoutABar:N0} have no bar served, {Share(missing, 2)}; {inputs.NamesWithoutBars} name(s) the index held have no bars at all and {inputs.NamesMissingSomeSessions} miss some sessions. "));
        page.Append(missing > MissingThreshold
            ? "That is above the 3% the page holds it to, so the results read better than the market was: the names missing are mostly the ones that failed.</p>"
            : "That is at or under the 3% the page holds it to. The count sees only the departures the index feed still lists; one it no longer lists would be invisible here.</p>");
        page.Append("<p><b>What could not be replayed as it stood.</b></p><ul>");
        page.Append("<li>The fundamental readings are left out, since the provider restates old figures.</li>");
        page.Append("<li>The bars are the provider's adjusted prices as served now, the pulled years scaled to the store's own over the sessions both hold, so prices differ from what a night saw while every ratio and distance in typical moves is the same.</li>");
        page.Append("<li>The earnings dates are as the provider files them now, and the membership spans as the index feed states them now.</li>");
        page.Append("<li>A name that left the index and whose bars the provider no longer serves is missing, counted above.</li>");
        page.Append("<li>A suspect series is read as none, since the store holds a name's suspicion as of now alone.</li>");
        page.Append("<li>The trend rule's versions are replayed by today's code for them, and the calendar is the days at least half the names spanning each hold, since the exchange's closure table covers the store's own years.</li>");
        page.Append("<li>A trigger's first firing reads the sessions before on the name's own series whether or not it was a member on them, where the night reads only the nights that stored a result for it.</li>");
        page.Append("</ul>");

        var averages = carried.Where(design => design.Support == SupportKind.Average).ToArray();

        page.Append(averages.Length == 0 && start?.Design.Support != SupportKind.Average
            ? "<p><b>A stop not at a band.</b> No design stage 2 carried stops one typical move below an average, so every one of them stops at a band.</p>"
            : "<p class=\"flag\"><b>A stop not at a band.</b> A design stage 2 carried reads the average itself as support and stops one typical move below it, a distance where every other design stops at a band. Adopting it would mean the live plan's stop no longer always comes from a price level, which section 10 states as a principle. That is the operator's decision, not a setting.</p>");

        // 6. Machine time, decisions, failures.
        page.Append("<h2>6. Machine time, the decisions the sweep took, and what failed</h2><ul>");

        foreach (var (stage, seconds) in state.Seconds)
        {
            page.Append("<li>").Append(Esc(StageName(stage))).Append(": ").Append(Duration(seconds)).Append("</li>");
        }

        page.Append("</ul><p><b>The timing checks, measured and recorded, not waited on.</b></p><ul>");

        foreach (var timing in state.Timings)
        {
            page.Append("<li>").Append(Esc(timing)).Append("</li>");
        }

        page.Append("</ul><p><b>The pauses for the night.</b></p><ul>");

        foreach (var pause in state.Pauses.DefaultIfEmpty("none"))
        {
            page.Append("<li>").Append(Esc(pause)).Append("</li>");
        }

        page.Append("</ul><p><b>The decisions it took itself.</b></p><ul>");
        page.Append("<li>The trigger's freshness per design, as stated at the top.</li>");
        page.Append(Invariant($"<li>Stage 2 ran on the {carried.Count} strongest designs by the ruling's ranking, the share of their coarse settings viable, with the median result of the viable ones breaking a tie. Designs whose stage 1 figures are the same to the last digit read the history the same way, the trend rule's versions reading an uptrend alike being the common case, so each such group took one place, read through its design nearest the live rule's in structural choices; the ranking marks every design a group holds.</li>"));
        page.Append("<li>The rising 200-day average is read as the average above its own value 20 sessions before; the 12-month measure as the return from 252 sessions back to 21 sessions back; support at an average as the 20-day or 50-day whose zone of half a typical move either side holds the close, the higher first, its strength the strength of the band the average sits in.</li>");
        page.Append("<li>A candidate a stage-1 setting reads is a member-session some setting of either grid could list; the rest are not stored, since no variation reads them.</li>");
        page.Append("<li>A variant is open where neither it nor the starting point has the higher average result in more than 5 of the 8 years, and its one change reaches a setting that beats both its break-even and no skill.</li>");
        page.Append("<li>A starting point's best year is the year its share beat its break-even by most in trades, removed to read whether the rest still beat it.</li>");
        page.Append("</ul><p><b>Failed or left out.</b></p><ul>");

        foreach (var failure in state.Failures.DefaultIfEmpty("nothing failed"))
        {
            page.Append("<li>").Append(Esc(failure)).Append("</li>");
        }

        if (state.StoppedAfterStageOne)
        {
            page.Append("<li>The run projected past five days and stopped after stage 1.</li>");
        }

        page.Append("</ul>");

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
        page.Append(Invariant($"<p>It had read the history through {Esc(state.Through ?? "no session")}, saved {state.CandidateChunks} chunk(s) of candidates{(state.CandidatesDone ? ", all of them" : string.Empty)}, {state.RankChunks} chunk(s) of stage 1{(state.RanksDone ? ", all of them" : string.Empty)}, and {state.FineDone.Count} design(s) of stage 2. Started again, it goes on from the first chunk missing. Nothing is proposed and nothing registered.</p>"));
        page.Append("<h2>Machine time</h2><ul>");

        foreach (var (stage, seconds) in state.Seconds)
        {
            page.Append("<li>").Append(Esc(StageName(stage))).Append(": ").Append(Duration(seconds)).Append("</li>");
        }

        page.Append("</ul><h2>Timings, failures and pauses</h2><ul>");

        foreach (var line in state.Timings.Concat(state.Failures).Concat(state.Pauses))
        {
            page.Append("<li>").Append(Esc(line)).Append("</li>");
        }

        return page.Append("</ul></main></body></html>").ToString();
    }

    static string Record(string title, SweepMeasures measures, IReadOnlyList<DateOnly> calendar, int firstScored)
    {
        var html = new StringBuilder();

        html.Append("<h3>").Append(Esc(title)).Append(" <span class=\"tag\">history</span></h3>");
        html.Append(Invariant($"<p>{measures.Scored:N0} trades scored of {measures.Listed:N0} listed; won {Pct(measures.Share)} against a break-even of {Pct(measures.BreakEven)} and no skill at {Pct(measures.NoSkill)}; an average result of {Mult(measures.AverageMultiple)} times the risk; beating its break-even in {measures.YearsBeatingBreakEven} of 8 years and both in {measures.YearsBeatingBoth}; trades in {measures.BlocksWithTrades} of 30 blocks; a stock listed on {Share(measures.ListingShareOfNights, 0)} of nights.</p>"));
        html.Append("<table><tr><th>Year</th><th>Trades</th><th>Won</th><th>Break-even</th><th>No skill</th><th>Average result</th></tr>");

        for (var year = 0; year < SweepFigures.Years; year++)
        {
            html.Append(Invariant($"<tr><td>{SweepColumns.FirstScored.Year + year}</td><td class=\"num\">{measures.YearScored[year]:N0}</td><td class=\"num\">{Pct(measures.YearShare[year])}</td><td class=\"num\">{Pct(measures.YearBreakEven[year])}</td><td class=\"num\">{Pct(measures.YearNoSkill[year])}</td><td class=\"num\">{Mult(measures.YearAverageMultiple[year])}</td></tr>"));
        }

        return html.Append("</table>").ToString();
    }

    static string DesignWords(SweepDesign design, DialSetting setting, SweepGrid grid)
    {
        var html = new StringBuilder("<table><tr><th>Check</th><th>Its value</th></tr>");

        foreach (var (check, value) in Checks(design, setting, grid))
        {
            html.Append("<tr><td>").Append(Esc(check)).Append("</td><td>").Append(Esc(value)).Append("</td></tr>");
        }

        return html.Append("</table>").ToString();
    }

    // Each check in plain words with its value, the design's choices and the dials together.
    static IReadOnlyList<(string Check, string Value)> Checks(SweepDesign design, DialSetting setting, SweepGrid grid)
    {
        static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

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
        ];
    }

    static string Differences(SweepDesign design, DialSetting setting, SweepGrid grid)
    {
        var live = Checks(SweepDesign.Live, LiveOnFine(), grid).ToDictionary(pair => pair.Check, pair => pair.Value);
        var html = new StringBuilder("<ul>");
        var any = false;

        foreach (var (check, value) in Checks(design, setting, grid))
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

    // The live settings on the fine grid, which holds every one of them.
    public static DialSetting LiveOnFine() => new(2, 1, 2, 3, 2, 2, 0, 2, 0);

    static string VariantTable(IReadOnlyList<SweepVariant> variants)
    {
        var html = new StringBuilder("<table><tr><th>Proposed</th><th>Part</th><th>The one change</th><th>On the plateau</th><th>Open</th><th>Different</th><th>Trades a year</th><th>Won</th><th>Average result</th></tr>");

        foreach (var variant in variants)
        {
            html.Append(Invariant($"<tr class=\"{(variant.Passes ? "pass" : "fail")}\"><td>{(variant.Passes ? "yes" : "no")}</td><td>{Esc(variant.Part)}</td><td>{Esc(variant.Change)}</td><td>{Mark(variant.OnThePlateau)}</td><td>{Mark(variant.Open)} ({variant.YearsItWins} to {variant.YearsTheStartWins})</td><td>{Mark(variant.DifferentEnough)} ({Share(variant.OutsideShare, 0)})</td><td>{Mark(variant.EnoughTrades)} (fewest {variant.FewestTradesInAYear})</td><td class=\"num\">{Pct(variant.Measures.Share)}</td><td class=\"num\">{Mult(variant.Measures.AverageMultiple)}</td></tr>"));
        }

        return html.Append("</table>").ToString();
    }

    static string Ranking(IReadOnlyList<RankRow> rows, IReadOnlyList<SweepDesign> carried)
    {
        var ordered = SweepRunner.Ranked(rows).ToList();
        var liveKey = SweepDesign.Live.Key;
        var liveRank = ordered.FindIndex(row => row.Key == liveKey);
        var html = new StringBuilder();

        html.Append(Invariant($"<p>{rows.Count:N0} designs, each over {SweepGrid.Coarse.Variations:N0} coarse settings, {(long)rows.Count * SweepGrid.Coarse.Variations:N0} variations. A setting is viable where it has at least {SweepMeasures.TradeFloor} scored trades and beats both its break-even and no skill in at least {SweepMeasures.YearsBeating} of the 8 years. The live rule's own design ranks {(liveRank < 0 ? "nowhere" : (liveRank + 1).ToString("N0", CultureInfo.InvariantCulture))} of {ordered.Count:N0}.</p>"));
        html.Append("<table><tr><th>Rank</th><th>Design</th><th>Viable settings</th><th>Share</th><th>Median result of the viable</th><th></th></tr>");

        foreach (var (row, rank) in ordered.Select((row, at) => (row, at)).Where(pair => pair.at < 25 || pair.row.Key == liveKey || carried.Any(design => design.Key == pair.row.Key)))
        {
            var marks = new List<string>();

            if (carried.Any(design => design.Key == row.Key))
            {
                marks.Add("carried to stage 2");
            }

            if (row.Key == liveKey)
            {
                marks.Add("the live rule's design");
            }

            var first = ordered.FindIndex(other => SweepRunner.SameFigures(other, row));

            if (first < rank)
            {
                marks.Add(Invariant($"the same figures as rank {first + 1}"));
            }

            html.Append(Invariant($"<tr{(row.Key == liveKey ? " class=\"live\"" : string.Empty)}><td class=\"num\">{rank + 1}</td><td>{Esc(Words(row.Design))}</td><td class=\"num\">{row.Viable:N0}</td><td class=\"num\">{Share(row.ViableShare, 2)}</td><td class=\"num\">{Mult(row.MedianMultiple)}</td><td>{Esc(string.Join(", ", marks))}</td></tr>"));
        }

        html.Append("</table>");

        if (ordered.FirstOrDefault(row => row.Key == liveKey) is { LiveScored: not null } live)
        {
            html.Append(Invariant($"<p>The live rule's own settings, strength 0.50, depth 1 to 5, dry-up under 1.5, freshness 3, reward to risk 1.5, the stop 0.5 to 4, the market at 45% and band strength 0, all among the coarse values: {live.LiveScored:N0} scored trades, won {Pct(live.LiveShare)} against a break-even of {Pct(live.LiveBreakEven)} and no skill at {Pct(live.LiveNoSkill)}, an average result of {Mult(live.LiveMultiple)}, beating both in {live.LiveYearsBeatingBoth} of 8 years. <span class=\"tag\">history</span></p>"));
        }

        return html.ToString();
    }

    static string Maps(SweepDesign design, SweepVariation[] variations, SweepGrid grid, IReadOnlyList<SweepCandidate> candidates, int nights, IReadOnlyList<DateOnly> calendar, int firstScored)
    {
        var html = new StringBuilder();
        var centre = SweepPlateau.Centre(grid, variations);
        var held = centre?.Setting ?? Best(grid, variations);

        html.Append("<h3>").Append(Esc(Words(design))).Append(" <span class=\"tag\">history</span></h3>");
        html.Append(centre is { } found
            ? Invariant($"<p>A plateau of {found.PlateauSize:N0} settings; its centre meeting the floors is {Esc(held.Describe(grid))}.</p>")
            : Invariant($"<p>No setting on a plateau meets the floors; the maps hold the other dials at the setting with the highest average result among those beating both, {Esc(held.Describe(grid))}.</p>"));

        foreach (var (rowsName, columnsName) in new[] { ("strength", "reward to risk"), ("depth low", "depth high"), ("dry-up", "freshness"), ("market", "band strength"), ("stop", "reward to risk") })
        {
            html.Append(Map(grid, variations, held, rowsName, columnsName));
        }

        html.Append(Record("Its centre year by year", SweepStages.Direct(candidates, design, grid, held, nights), calendar, firstScored));

        return html.ToString();
    }

    // One map over two dials, every other held at the setting given: each cell its average result, marked where
    // it beats both its break-even and no skill.
    static string Map(SweepGrid grid, SweepVariation[] variations, DialSetting held, string rowsName, string columnsName)
    {
        var (rowCount, rowLabel, rowSet) = Axis(grid, rowsName);
        var (columnCount, columnLabel, columnSet) = Axis(grid, columnsName);
        var html = new StringBuilder();

        html.Append("<table class=\"map\"><caption>").Append(Esc(rowsName)).Append(" by ").Append(Esc(columnsName)).Append(", average result where it beats both marked</caption><tr><th></th>");

        for (var column = 0; column < columnCount; column++)
        {
            html.Append("<th>").Append(Esc(columnLabel(column))).Append("</th>");
        }

        html.Append("</tr>");

        for (var row = 0; row < rowCount; row++)
        {
            html.Append("<tr><th>").Append(Esc(rowLabel(row))).Append("</th>");

            for (var column = 0; column < columnCount; column++)
            {
                var setting = columnSet(rowSet(held, row), column);
                var variation = variations[SweepPlateau.Index(grid, setting)];
                var here = setting == held;

                html.Append(Invariant($"<td class=\"{(variation.BeatsBoth ? "good" : "poor")}{(here ? " here" : string.Empty)}\" title=\"{variation.Scored} trades\">{(float.IsNaN(variation.AverageMultiple) ? "none" : variation.AverageMultiple.ToString("0.00", CultureInfo.InvariantCulture))}</td>"));
            }

            html.Append("</tr>");
        }

        return html.Append("</table>").ToString();
    }

    static (int Count, Func<int, string> Label, Func<DialSetting, int, DialSetting> Set) Axis(SweepGrid grid, string name)
    {
        static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        return name switch
        {
            "strength" => (grid.StrengthBars.Count, at => Number(grid.StrengthBars[at]), (setting, at) => setting with { Strength = at }),
            "depth low" => (grid.DepthLows.Count, at => Number(grid.DepthLows[at]), (setting, at) => setting with { DepthLow = at }),
            "depth high" => (grid.DepthHighs.Count, at => Number(grid.DepthHighs[at]), (setting, at) => setting with { DepthHigh = at }),
            "dry-up" => (grid.DryUpCeilings.Count, at => double.IsPositiveInfinity(grid.DryUpCeilings[at]) ? "off" : Number(grid.DryUpCeilings[at]), (setting, at) => setting with { DryUp = at }),
            "freshness" => (grid.Freshness.Count, at => grid.Freshness[at].ToString(CultureInfo.InvariantCulture), (setting, at) => setting with { Freshness = at }),
            "reward to risk" => (grid.RewardToRiskFloors.Count, at => Number(grid.RewardToRiskFloors[at]), (setting, at) => setting with { RewardToRisk = at }),
            "stop" => (grid.StopBounds.Count, at => $"{Number(grid.StopBounds[at].Low)}-{Number(grid.StopBounds[at].High)}", (setting, at) => setting with { Stop = at }),
            "market" => (grid.MarketFloors.Count, at => double.IsNegativeInfinity(grid.MarketFloors[at]) ? "off" : Number(grid.MarketFloors[at] * 100) + "%", (setting, at) => setting with { Market = at }),
            _ => (grid.BandStrengths.Count, at => grid.BandStrengths[at].ToString(CultureInfo.InvariantCulture), (setting, at) => setting with { Band = at }),
        };
    }

    static DialSetting Best(SweepGrid grid, SweepVariation[] variations)
    {
        var best = 0;

        for (var index = 1; index < variations.Length; index++)
        {
            var one = variations[index];
            var held = variations[best];

            if (one.BeatsBoth && one.Scored >= SweepMeasures.TradeFloor && (!held.BeatsBoth || held.Scored < SweepMeasures.TradeFloor || one.AverageMultiple > held.AverageMultiple))
            {
                best = index;
            }
        }

        return DialSetting.Of(grid, best / grid.CellsPerStop, best % grid.CellsPerStop);
    }

    // The command the freeze would use, once its verb is built. Not run.
    static string Command(SweepStart? start, IReadOnlyList<SweepVariant> variants, string through)
    {
        if (start is null)
        {
            return "<p>No starting point is proposed, so there is no registration to state.</p>";
        }

        var grid = SweepGrid.Fine;
        var s = start.Setting;
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
        var chosen = variants.Where(variant => variant.Passes).Select(variant => variant.Change).ToArray();
        var flagged = chosen.Length == 0 ? string.Empty : $" --variants \"{string.Join("; ", chosen)}\"";
        var command = $"dotnet run --project src/EquityBrief.Worker -- register --freeze --settings {settings}{structural}{flagged} --evidence \"the sweep over the history through {through}\"";

        var html = new StringBuilder("<pre>").Append(Esc(command)).Append("</pre>");

        html.Append("<p>Not run. The `register --freeze` verb is built with the freeze itself, once the operator approves the starting point and the variants; it opens the filter version holding these settings, retires the six candidates registered today, and registers the starting rule, the approved variants and, where its evaluator has landed, the fundamentals candidate, at one instant. ");

        if (structural.Length > 0)
        {
            html.Append("The starting point's design differs from the live rule's in a structural choice, so the filter's code has to express that choice before the freeze can register it, and that change moves the filter's pin with its own remedy. ");
        }

        if (!double.IsPositiveInfinity(dryUp) && grid.BandStrengths[s.Band] == 0 && !double.IsNegativeInfinity(market))
        {
            html.Append("Every setting it names is one the filter already holds.");
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
        "series" => "the indicators, swings, trend labels, bands and cross-sections, every name and session",
        "candidates" => "the candidates and their plans' eight exits",
        "stage1" => "stage 1, every design over the coarse settings",
        "stage2" => "stage 2, the carried designs over the fine settings",
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

    const string Style = """
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
        table{border-collapse:collapse;width:100%;margin:8px 0 16px;background:var(--card);display:block;overflow-x:auto}
        th,td{border-bottom:1px solid var(--line);padding:5px 8px;text-align:left;vertical-align:top}td.num{text-align:right;font-variant-numeric:tabular-nums}
        tr.live td{background:var(--flag)}tr.fail td{color:var(--muted)}
        table.map td{text-align:right;font-variant-numeric:tabular-nums}td.good{background:var(--good)}td.poor{background:var(--poor)}td.here{outline:2px solid var(--accent)}
        caption{text-align:left;color:var(--muted);padding:4px 0}pre{white-space:pre-wrap;background:var(--card);border:1px solid var(--line);padding:10px;border-radius:6px}
        """;
}
