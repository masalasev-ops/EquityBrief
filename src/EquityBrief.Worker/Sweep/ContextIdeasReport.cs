using System.Net;
using System.Text;

namespace EquityBrief.Worker.Sweep;

// What the context run read: the history's span, the names and the nights scored, how many names the revenue pull
// names a filer for, and when it ran.
public sealed record ContextIdeasRun(
    DateOnly From,
    DateOnly Through,
    int Names,
    int ScoredNights,
    int NamesWithRevenue,
    DateTimeOffset Started,
    DateTimeOffset Finished);

// The context run's report: each family's tries, passes and what luck passes in plain words, the family as frozen,
// each idea with its rule, its figures and each test's answer, and for the drift's how far the revenue reached. It
// proposes nothing, since nothing it shows is frozen or registered.
// see: The context checks are read on the frozen families one at a time, and nothing they show is frozen or registered
public static class ContextIdeasReport
{
    public static string InWords(ContextIdeasRead read) => FormattableString.Invariant(
        $"{read.Drift.Count} ideas were tried on the earnings drift as frozen and {read.Drift.Count(one => one.Test.Passes)} passed, where luck alone passes about {ContextIdeasRead.Luck(read.Drift.Count):0.00}; ")
        + FormattableString.Invariant($"{read.Pullback.Count} on the pullback's base and {read.Pullback.Count(one => one.Test.Passes)} passed, where luck alone passes about {ContextIdeasRead.Luck(read.Pullback.Count):0.00}.")
        + (read.Passes == 0
            ? string.Empty
            : " Passed: " + string.Join(", ", read.Drift.Concat(read.Pullback).Where(one => one.Test.Passes).Select(one => one.Key)) + ".");

    public static string Build(ContextIdeasRun run, ContextIdeasRead read)
    {
        var page = new StringBuilder();

        page.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>The context checks over the history</title><style>");
        page.Append(SweepReport.Style);
        page.Append("</style></head><body><main>");
        page.Append("<p class=\"eyebrow\">Phase 14, the context checks</p><h1>The context checks on the frozen families</h1>");
        page.Append(Invariant($"<p class=\"history\">Every figure on this page is history: the earnings drift as frozen and the pullback's base replayed over the stored history from {run.From:yyyy-MM-dd} to {run.Through:yyyy-MM-dd}, {run.Names:N0} names and {run.ScoredNights:N0} nights scored, the revenue pull naming a filer stating revenue for {run.NamesWithRevenue:N0} of the names. No model read any of it, and nothing here is frozen or registered.</p>"));

        page.Append("<h2>1. The tries, the passes and luck</h2>");
        page.Append(Invariant($"<p class=\"tries\" data-passes=\"{read.Passes}\">{Esc(InWords(read))} Luck: an idea with no effect is better in at least {SweepIdeas.YearsBetter} of 8 years with {SweepIdeas.RecentYearsBetter} of the last {SweepIdeas.RecentYears} in {SweepIdeas.LuckPatterns()} of 256 cases, before the other tests.</p>"));

        page.Append("<h2>2. The earnings drift's revenue</h2>");
        page.Append(Invariant($"<p class=\"frozen\">{Esc(read.DriftFrozen)}</p>"));
        page.Append(Invariant($"<p class=\"revenue-reach\" data-listings=\"{read.DriftListings}\" data-with-revenue=\"{read.DriftListingsWithRevenue}\">Of the {read.DriftListings:N0} listings the drift as frozen makes, {read.DriftListingsWithRevenue:N0} read a revenue growth for their print's quarter; the rest read none, their filer stating no quarter the reading finds, and the filter leaves them off while the order keeps them after every one reading one.</p>"));
        page.Append(SweepIdeasReport.Table([("As frozen", read.DriftBase)]));
        page.Append(Invariant($"<p>Each idea judged on the edge against the drift as frozen, by the year tests, the test without the {SweepIdeas.LargestLeftOut} largest results and at least {SweepIdeas.TradeFloor:N0} trades, with no floor of nights.</p>"));

        foreach (var reading in read.Drift)
        {
            Idea(page, reading);
            page.Append(Invariant($"<p class=\"filed-after\" data-read=\"{reading.RevenueRead}\" data-after=\"{reading.FiledAfterTheBuy}\">Of its trades, {reading.RevenueRead:N0} read a growth, {reading.FiledAfterTheBuy:N0} of them over a quarter first filed after the session they were bought on, the release stating the figure its filing states.</p>"));
        }

        page.Append("<h2>3. The pullback's order by its RSI's fall</h2>");
        page.Append("<p>The base is the pullback's rule as frozen, every name it passes a night. Beside its idea stand the base's first three a night in the list's own order, the idea the ideas' run passed, as context and not as what the idea is judged against.</p>");
        page.Append(SweepIdeasReport.Table([("Base", read.PullbackBase), ("First three in the list's order", read.PullbackFirstThree)]));

        foreach (var reading in read.Pullback)
        {
            Idea(page, reading);
        }

        page.Append(Invariant($"<p class=\"ran\">Ran from {run.Started:yyyy-MM-dd HH:mm} to {run.Finished:HH:mm} UTC.</p>"));
        page.Append("</main></body></html>");

        return page.ToString();
    }

    static void Idea(StringBuilder page, ContextReading reading)
    {
        page.Append(Invariant($"<section class=\"idea\" data-idea=\"{Esc(reading.Key)}\" data-passes=\"{(reading.Test.Passes ? "yes" : "no")}\"><h3>{Esc(reading.Key)}: {Esc(reading.Rule)}</h3>"));
        page.Append(SweepIdeasReport.Table([(reading.Key, reading.Figures)]));
        page.Append(Invariant($"<p class=\"answers\">{(reading.Test.Passes ? "Passes" : "Fails")}: {Esc(SweepIdeasReport.Answers(reading.Test))}.</p></section>"));
    }

    static string Esc(string text) => WebUtility.HtmlEncode(text);

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
