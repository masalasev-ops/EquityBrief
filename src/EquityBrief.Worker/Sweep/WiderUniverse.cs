using System.Net;
using System.Text;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// One swing family read on both universes at its frozen settings: its figures over the S&P 500's history alone and over
// the 1,500, today's S&P 400 and 600 members beside that history, and the ideas' test of the second against the first.
public sealed record WiderReading(string Family, string Words, IdeaFigures FiveHundred, IdeaFigures FifteenHundred, IdeaTest Test)
{
    // Whether the family's edge improves on the 1,500, which is the ideas' test passing.
    public bool Improves => Test.Passes;
}

// What the wider universe's first test read: the history's span, the names on each universe, today's S&P 400 and 600
// members it added and how many of them the S&P 500's history never held, the nights scored, and when it ran.
public sealed record WiderRun(
    DateOnly From,
    DateOnly Through,
    int FiveHundredNames,
    int FifteenHundredNames,
    int WiderMembers,
    int Survivors,
    int ScoredNights,
    DateTimeOffset Started,
    DateTimeOffset Finished);

// The wider universe's first test, on the operator's ruling of 2026-10-04: each swing family at its frozen settings on
// the 1,500 against the 500 alone, judged by the ideas' run's test, with what luck passes over its tries. The 1,500 holds
// today's S&P 400 and 600 members as survivors read on every session, which flatters it, so every figure it states says
// so. Where no family's edge improves even so the widening is dropped and recorded as not adopted; where one improves,
// nothing is adopted until the result is confirmed on membership as it stood.
// see: A wider universe is tested first on today's members, and widened only where a family's edge improves even so and holds on membership as it stood
public static class WiderUniverse
{
    // The words every figure the 1,500 states carries.
    public const string SurvivorsOnly =
        "survivors only: today's S&P 400 and 600 members read as members on every session, which flatters the wider universe";

    // The families read, in the page's order, with the words each is named by: the pullback's base, the breakout and the
    // earnings drift, each as frozen.
    public static IReadOnlyList<(string Family, string Words)> Families { get; } =
    [
        ("pullback", "pullback's base"),
        (BreakoutRule.Name, "breakouts'"),
        (DriftRule.Name, "earnings drift's"),
    ];

    // What luck alone passes over a number of tries: an edge with no effect better in enough of the eight years in so
    // many of the 256 ways the years can fall.
    public static double Luck(int tries) => 1.0 * tries * SweepIdeas.LuckPatterns() / (1 << SweepFigures.Years);

    // A family's reading, its 1,500 tested against its 500 as the ideas' run tests an idea against its rule, on the edge.
    public static WiderReading Read(string family, string words, IdeaFigures fiveHundred, IdeaFigures fifteenHundred) =>
        new(family, words, fiveHundred, fifteenHundred, SweepIdeas.Test(fifteenHundred, fiveHundred, onTotals: false));

    // Whether the widening is dropped: no family's edge improves on the 1,500 even on survivors.
    public static bool Dropped(IReadOnlyList<WiderReading> readings) => !readings.Any(reading => reading.Improves);

    // The rule's answer in words.
    public static string Outcome(IReadOnlyList<WiderReading> readings) =>
        Dropped(readings)
            ? "No family's edge improves on the 1,500 even on survivors, so the widening is dropped and recorded as not adopted."
            : "The " + string.Join(", ", readings.Where(reading => reading.Improves).Select(reading => reading.Words))
              + " edge improves on the 1,500 on survivors, so nothing is adopted until the result is confirmed on membership as it stood, from the provider or from the iShares IJH and IJR holdings by date.";

    // One family's two figures in a sentence, the 1,500's carrying the survivors' words.
    public static string Line(WiderReading reading) => FormattableString.Invariant(
        $"The {reading.Words} edge is {Number(reading.FiveHundred.Edge)} over {reading.FiveHundred.Trades:N0} trades on the 500 alone and {Number(reading.FifteenHundred.Edge)} over {reading.FifteenHundred.Trades:N0} trades on the 1,500 ({SurvivorsOnly}); ")
        + (reading.Improves ? "it improves by the ideas' test." : "it does not improve by the ideas' test.");

    public static string InWords(IReadOnlyList<WiderReading> readings) => FormattableString.Invariant(
        $"{readings.Count(reading => reading.Improves)} of {readings.Count} families improve on the 1,500, where luck alone passes about {Luck(readings.Count):0.00}. ") + Outcome(readings);

    public static string Build(WiderRun run, IReadOnlyList<WiderReading> readings)
    {
        var page = new StringBuilder();

        page.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>The S&amp;P 1500 on today's members</title><style>");
        page.Append(SweepReport.Style);
        page.Append("</style></head><body><main>");
        page.Append("<p class=\"eyebrow\">Phase 14, Part C's first test</p><h1>Each swing family on the S&amp;P 1500 against the 500 alone</h1>");
        page.Append(Invariant($"<p class=\"survivors\" data-survivors=\"{run.Survivors}\">The 1,500's history holds survivors only. Today's {run.WiderMembers:N0} members of the S&amp;P 400 and 600, {run.Survivors:N0} of them never held by the S&amp;P 500's history, are read as members on every session from {run.From:yyyy-MM-dd} to {run.Through:yyyy-MM-dd}, so a company that fell out of either index or failed before today is missing, which flatters the wider universe. Every figure the 1,500 states below carries these words.</p>"));
        page.Append(Invariant($"<p class=\"history\">Every figure on this page is history: each family's rule as frozen replayed over {run.ScoredNights:N0} nights, on the S&amp;P 500's history alone, {run.FiveHundredNames:N0} names, and on the 1,500, {run.FifteenHundredNames:N0} names. Nothing here is adopted, frozen or registered.</p>"));

        page.Append("<h2>1. The rule's answer</h2>");
        page.Append(Invariant($"<p class=\"outcome\" data-dropped=\"{(Dropped(readings) ? "yes" : "no")}\" data-tries=\"{readings.Count}\" data-improves=\"{readings.Count(reading => reading.Improves)}\" data-luck=\"{Luck(readings.Count):0.00}\">{Esc(InWords(readings))}</p>"));

        page.Append("<h2>2. Each family</h2><p>Each family's rule as frozen on both universes, judged on the edge by the ideas' run's test of the 1,500 against the 500 alone.</p>");

        foreach (var reading in readings)
        {
            page.Append(Invariant($"<section class=\"family\" data-family=\"{Esc(reading.Family)}\" data-improves=\"{(reading.Improves ? "yes" : "no")}\"><h3>The {Esc(reading.Words)} rule as frozen</h3>"));
            page.Append(Invariant($"<p class=\"family-line\">{Esc(Line(reading))}</p>"));
            page.Append(Table(reading));
            page.Append(Invariant($"<p class=\"answers\">{(reading.Improves ? "Improves" : "Does not improve")}: {Esc(SweepIdeasReport.Answers(reading.Test))}.</p></section>"));
        }

        page.Append(Invariant($"<p class=\"ran\">Ran from {run.Started:yyyy-MM-dd HH:mm} to {run.Finished:HH:mm} UTC.</p>"));
        page.Append("</main></body></html>");

        return page.ToString();
    }

    // A family's two rows: the 500 alone, and the 1,500 with the survivors' words in its label and on each figure's cell.
    static string Table(WiderReading reading)
    {
        var html = new StringBuilder("<div class=\"table\"><table><thead><tr><th>Universe</th><th>Trades</th><th>Nights listing</th><th>Edge</th><th>Error</th><th>Result</th><th>2024 to 2026 edge</th><th>Without the five largest, edge</th>");

        for (var year = 0; year < SweepFigures.Years; year++)
        {
            html.Append(Invariant($"<th>{SweepColumns.FirstScored.Year + year}</th>"));
        }

        html.Append("</tr></thead><tbody>");
        Row(html, "500", "The S&P 500 alone", reading.FiveHundred, survivors: false);
        Row(html, "1500", "The 1,500, " + SurvivorsOnly, reading.FifteenHundred, survivors: true);

        return html.Append("</tbody></table></div>").ToString();
    }

    static void Row(StringBuilder html, string universe, string label, IdeaFigures figures, bool survivors)
    {
        var mark = survivors ? Invariant($" title=\"{Esc(SurvivorsOnly)}\"") : string.Empty;

        html.Append(Invariant($"<tr data-universe=\"{universe}\" data-survivors=\"{(survivors ? "yes" : "no")}\" data-trades=\"{figures.Trades}\" data-edge=\"{Number(figures.Edge)}\"><td>{Esc(label)}</td><td class=\"num\"{mark}>{figures.Trades:N0}</td><td class=\"num\"{mark}>{figures.NightShare * 100:0}%</td><td class=\"num\"{mark}>{Number(figures.Edge)}</td><td class=\"num\"{mark}>{Number(figures.StandardError)}</td><td class=\"num\"{mark}>{Number(figures.Result)}</td><td class=\"num\"{mark}>{Number(figures.RecentEdge)}</td><td class=\"num\"{mark}>{Number(figures.EdgeWithoutLargest)}</td>"));

        for (var year = 0; year < SweepFigures.Years; year++)
        {
            html.Append(Invariant($"<td class=\"num\"{mark}>{Number(figures.YearEdge[year])} ({figures.YearTrades[year]})</td>"));
        }

        html.Append("</tr>");
    }

    static string Number(double? value) => SweepIdeasReport.Number(value);

    static string Esc(string text) => WebUtility.HtmlEncode(text);

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
