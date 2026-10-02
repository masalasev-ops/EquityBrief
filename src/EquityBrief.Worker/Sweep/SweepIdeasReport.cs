using System.Net;
using System.Text;

namespace EquityBrief.Worker.Sweep;

// What the ideas' run read: the history's span, the names and the nights scored, the candidates and the live
// design's picks among them, the ideas left out for a series the store does not hold, and when it ran.
public sealed record SweepIdeasRun(
    DateOnly From,
    DateOnly Through,
    int Names,
    int ScoredNights,
    int Candidates,
    int Picks,
    IReadOnlyList<string> LeftOut,
    DateTimeOffset Started,
    DateTimeOffset Finished);

// The ideas' run's report, in the order the plan of 2026-10-01 sets: the starting point beside today's rule and
// the base, its variants, each idea with its rule, its evidence, its figures and each test's answer, the tries
// against what luck alone would pass, the counting fault, the market series read, what the night would have to
// fetch, and the registration a freeze would use, stated and not run.
// see: A new idea is added to the base one at a time and kept only where it is better in six of eight years
public static class SweepIdeasReport
{
    // The counting fault the correction of 2026-10-01 fixed, and what it voids in the third run's report.
    public const string CountingFault =
        "The stepped plan's result was counted in multiples of the distance from its fill to its stop, and its fill can sit a hair above the stop, so an ordinary gain read as hundreds of times the risk: one NVDA trade of 2020-01-29 read as 1,474.7. Every design the third run ranked highest rested on a few such trades, so its first stage's ranking is void. Since the correction the stepped plan's result is counted on the risk its plan stated and a fill nearer the stop than the stop setting's floor is no trade. The first run's proposal bought at the close on section 10's plan, and Past picks divides a return by the listing's own buy to stop, so neither could be bought away from its stated price and neither carried the fault.";

    public static string Build(SweepIdeasRun run, IdeasProposal proposal, IReadOnlyList<SweepMarketSeries> market)
    {
        var page = new StringBuilder();
        var tries = proposal.Ideas.Count;
        var passes = proposal.Ideas.Count(reading => reading.Test.Passes);
        var luck = SweepIdeas.LuckPatterns();

        page.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>The ideas' run</title><style>");
        page.Append(SweepReport.Style);
        page.Append("</style></head><body><main>");
        page.Append("<p class=\"eyebrow\">Phase 12, the ideas on the base</p><h1>The ideas' run</h1>");
        page.Append(Invariant($"<p class=\"history\">Every figure on this page is history: the live design replayed over the stored history from {run.From:yyyy-MM-dd} to {run.Through:yyyy-MM-dd}, {run.ScoredNights:N0} sessions scored over {run.Names:N0} names the index held, {run.Picks:N0} of the sweep's {run.Candidates:N0} candidates read by the live design, one open trade a stock, and every result read as an edge over the same plan entered at the same close on every member that night. The base is today's rule with its reward-to-risk floor at 2, the setting the pullback froze at on 2026-10-02. None of it is a live record, and nothing here is registered.</p>"));

        if (run.LeftOut.Count > 0)
        {
            page.Append(Invariant($"<p class=\"left-out\">Left out, for a series the store does not hold: {Esc(string.Join(", ", run.LeftOut))}.</p>"));
        }

        page.Append("<h2>1. The starting point</h2>");
        page.Append(Invariant($"<p class=\"start\" data-kept=\"{Esc(string.Join(",", proposal.Kept))}\">{Esc(StartInWords(proposal))}</p>"));
        page.Append(Table([("Today's rule", proposal.Today), ("The base", proposal.Base), ("The starting point", proposal.StartFigures)]));
        page.Append(Invariant($"<p class=\"answers\">Against the base: {Esc(Answers(proposal.StartAgainstBase))}. Against today's rule: {Esc(Answers(proposal.StartAgainstToday))}. The base against today's rule: {Esc(Answers(proposal.BaseAgainstToday))}.</p>"));

        page.Append("<h2>2. Its variants</h2><p>Every idea outside the starting point that was higher than the base over the eight years, the market floor at 50% and the pullback's depth from 1.5 typical moves, each one change from the starting point.</p>");
        page.Append(Table([.. proposal.Variants.Select(variant => (variant.Key + ": " + variant.Change, variant.Figures))]));

        page.Append("<h2>3. Each idea</h2><p>Each idea added to the base alone, judged on the edge, a market switch on the year's total result in risks, a night with no trade counting nothing.</p>");

        foreach (var reading in proposal.Ideas.Concat(proposal.Together is { } together ? [together] : []))
        {
            page.Append(Invariant($"<section class=\"idea\" data-idea=\"{Esc(reading.Idea.Key)}\" data-passes=\"{(reading.Test.Passes ? "yes" : "no")}\"><h3>{Esc(reading.Idea.Key)}: {Esc(reading.Idea.Rule)}</h3>"));
            page.Append(Invariant($"<p class=\"evidence\">{Esc(reading.Idea.Evidence)}</p>"));
            page.Append(Table([(reading.Idea.Key, reading.Figures), ("The base", proposal.Base)]));
            page.Append(Invariant($"<p class=\"answers\">{(reading.Test.Passes ? "Passes" : "Fails")}: {Esc(Answers(reading.Test))}.</p></section>"));
        }

        page.Append("<h2>4. How many would pass by luck</h2>");
        page.Append(Invariant($"<p class=\"luck\" data-tries=\"{tries}\" data-passes=\"{passes}\">{tries} ideas were tried and {passes} passed. An idea with no effect is better in at least {SweepIdeas.YearsBetter} of the 8 years with {SweepIdeas.RecentYearsBetter} of the last {SweepIdeas.RecentYears} in {luck} of the 256 ways the years can fall, {100.0 * luck / 256:0}%, so luck alone passes about {tries * luck / 256.0:0.0} of {tries} before the other tests.</p>"));

        page.Append("<h2>5. The counting fault</h2>");
        page.Append(Invariant($"<p class=\"fault\">{Esc(CountingFault)}</p>"));

        page.Append("<h2>6. The market series read</h2>");

        if (market.Count == 0)
        {
            page.Append("<p class=\"pull\">The store holds no market series, so no switch reading one was tried.</p>");
        }
        else
        {
            page.Append(Invariant($"<p class=\"pull\">Pulled by <code>history-pull --market</code>, one request a series at a weight of 1, {market.Count} weighted calls in all: "));
            page.Append(string.Join("; ", market.Select(one => Invariant($"<span data-series=\"{Esc(one.Series)}\" data-sessions=\"{one.Closes.Count}\">{Esc(one.Series)}, {one.Closes.Count:N0} sessions from {one.First:yyyy-MM-dd} to {one.Last:yyyy-MM-dd}, by the pull {Esc(one.Pull)}</span>"))));
            page.Append(".</p>");
        }

        page.Append("<h2>7. What the night would fetch</h2>");
        page.Append(Invariant($"<p class=\"fetch\">{Esc(Fetch(proposal.Start))}</p>"));

        page.Append("<h2>8. The registration a freeze would use</h2>");
        page.Append(Invariant($"<p class=\"registration\">{Esc(Registration(proposal))} Stated, and not run: nothing is frozen until the operator says go.</p>"));

        page.Append(Invariant($"<p class=\"run\">Run from {run.Started:yyyy-MM-dd HH:mm:ss}Z to {run.Finished:yyyy-MM-dd HH:mm:ss}Z.</p>"));
        page.Append("</main></body></html>");

        return page.ToString();
    }

    public static string StartInWords(IdeasProposal proposal)
    {
        if (proposal.BaseIsVariant)
        {
            return "The starting point is today's rule: the base was not better than it by the same test, so the base stands as a variant.";
        }

        if (proposal.Kept.Count == 0)
        {
            return "The starting point is the base, today's rule with its reward-to-risk floor at 2: no idea passed the test on it.";
        }

        var rules = proposal.Ideas
            .Where(reading => proposal.Kept.Contains(reading.Idea.Key))
            .Select(reading => reading.Idea.Key + ", " + reading.Idea.Rule)
            .Concat(proposal.Together is { } together && proposal.Kept.Contains(together.Idea.Key) ? [together.Idea.Rule] : []);

        return "The starting point is the base, today's rule with its reward-to-risk floor at 2, with " + string.Join("; and ", rules) + ".";
    }

    // Each of the test's answers in words.
    public static string Answers(IdeaTest test)
    {
        var measure = test.OnTotals ? "total" : "edge";
        var parts = new List<string>
        {
            Invariant($"its {measure} better in {test.YearsBetter} of the 8 years, needing {SweepIdeas.YearsBetter}, and in {test.RecentYearsBetter} of the last {SweepIdeas.RecentYears}, needing {SweepIdeas.RecentYearsBetter}"),
            Invariant($"the last three years together {(test.RecentNoLower ? "no lower" : "lower")}"),
            Invariant($"without the five largest results {(test.BetterWithoutLargest ? "still higher" : "not higher")}"),
            Invariant($"{(test.EnoughTrades ? "at least" : "fewer than")} {SweepIdeas.TradeFloor:N0} trades"),
            test.OnTotals ? "no floor of nights" : Invariant($"{(test.EnoughNights ? "a stock listed on at least" : "a stock listed on fewer than")} {SweepIdeas.NightFloor * 100:0}% of the nights"),
        };

        if (test.OnTotals)
        {
            parts.Add(test.ResultHigher ? "the plain result a trade higher" : "the plain result a trade not higher");
        }

        return string.Join(", ", parts);
    }

    // What the night would have to fetch were the starting point frozen.
    public static string Fetch(IdeaRule start)
    {
        var series = start.Switches.Select(SweepIdeas.SeriesOf).OfType<string>().Distinct().Order(StringComparer.Ordinal).ToArray();

        if (series.Length > 0)
        {
            return Invariant($"The starting point reads {string.Join(" and ", series)} each night, so a freeze would add one request a series a night at a weight of 1, a fixed cost whatever the index's size, and a sentence to CLAUDE.md's first hard rule naming it.");
        }

        return start.HasSwitch
            ? "The starting point's switch reads the members' own bars, which the night already holds, so the night would fetch nothing new."
            : "No market switch is in the starting point, so the night would fetch nothing new.";
    }

    // The registration a freeze would use: none where the starting point is the base the pullback froze at.
    public static string Registration(IdeasProposal proposal)
    {
        if (proposal.BaseIsVariant)
        {
            return "A filter version at today's settings with its reward-to-risk floor back at 1.5, opened by the shape command's freeze, with the swing family registered again beside it.";
        }

        if (proposal.Kept.Count == 0)
        {
            return "None: the starting point is the base, which the pullback's freeze of 2026-10-02 opened as filter version 5.";
        }

        var changes = new List<string>();
        var start = proposal.Start;

        if (start.Setting.Stop == SweepIdeas.AtLeastAMove)
        {
            changes.Add("a filter version with the stop setting's low bound at 1 typical move, opened by the shape command's freeze with the swing family registered again beside it");
        }

        if (start.PerNight != int.MaxValue)
        {
            changes.Add(Invariant($"the list keeping the first {start.PerNight} a night, a change to the list's rule the freeze carries as a ruling and its code"));
        }

        if (start.Exit != IdeaExit.Base)
        {
            changes.Add("the swing plan's exit as the idea states it, a change to how the list's trades are sold and scored, on the pages and in Past picks, which the freeze carries as a ruling and its code");
        }

        if (start.HasSwitch)
        {
            changes.Add("a market gate reading the switches the starting point holds, a gate the filter does not read today, carried as a ruling and its code");
        }

        return "The freeze would carry " + string.Join("; and ", changes) + ".";
    }

    // One row a rule: its trades, the nights a stock was listed, its edge with its standard error, its plain
    // result, the share of its trades whose stop sat under one typical move, its total, the last three years'
    // edge and total, 2026 alone, the edge and the total without the five largest results by size, and each
    // year's edge, total and trades.
    static string Table(IReadOnlyList<(string Label, IdeaFigures Figures)> rows)
    {
        var html = new StringBuilder("<div class=\"table\"><table><thead><tr><th>Rule</th><th>Trades</th><th>Nights listing</th><th>Edge</th><th>Error</th><th>Result</th><th>Stops under a typical move</th><th>Total</th><th>2024 to 2026 edge</th><th>2024 to 2026 total</th><th>2026 edge</th><th>Without the five largest, edge</th><th>Without the five largest, total</th>");

        for (var year = 0; year < 8; year++)
        {
            html.Append(Invariant($"<th>{SweepColumns.FirstScored.Year + year}</th>"));
        }

        html.Append("</tr></thead><tbody>");

        foreach (var (label, figures) in rows)
        {
            html.Append(Invariant($"<tr data-key=\"{Esc(figures.Key)}\" data-trades=\"{figures.Trades}\" data-edge=\"{Number(figures.Edge)}\" data-close-stops=\"{Number(figures.CloseStops)}\"><td>{Esc(label)}</td><td class=\"num\">{figures.Trades:N0}</td><td class=\"num\">{figures.NightShare * 100:0}%</td><td class=\"num\">{Number(figures.Edge)}</td><td class=\"num\">{Number(figures.StandardError)}</td><td class=\"num\">{Number(figures.Result)}</td><td class=\"num\">{(figures.CloseStops is { } share ? Invariant($"{share * 100:0}%") : "none")}</td><td class=\"num\">{Number(figures.Total)}</td><td class=\"num\">{Number(figures.RecentEdge)}</td><td class=\"num\">{Number(figures.RecentTotal)}</td><td class=\"num\">{Number(figures.YearEdge[^1])}</td><td class=\"num\">{Number(figures.EdgeWithoutLargest)}</td><td class=\"num\">{Number(figures.TotalWithoutLargest)}</td>"));

            for (var year = 0; year < 8; year++)
            {
                html.Append(Invariant($"<td class=\"num\">{Number(figures.YearEdge[year])}, {Number(figures.YearTotal[year])} ({figures.YearTrades[year]})</td>"));
            }

            html.Append("</tr>");
        }

        return html.Append("</tbody></table></div>").ToString();
    }

    public static string Number(double? value) => FamilySweepReport.Number(value);

    static string Esc(string text) => WebUtility.HtmlEncode(text);

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
