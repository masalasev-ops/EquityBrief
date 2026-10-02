using System.Net;
using System.Text;

namespace EquityBrief.Worker.Sweep;

// What the ideas' run on a frozen family read: the family, the history's span, the names and the nights scored,
// the listings its rule as frozen makes, the switches left out for a series the store does not hold, and when it ran.
public sealed record FamilyIdeasRun(
    string Family,
    string Words,
    DateOnly From,
    DateOnly Through,
    int Names,
    int ScoredNights,
    int Listings,
    IReadOnlyList<string> LeftOut,
    DateTimeOffset Started,
    DateTimeOffset Finished);

// The ideas' run's report on a frozen family: the tries, the passes and what luck passes in plain words, the rule as
// frozen, each idea with its rule, its figures and each test's answer, and what was read. It proposes nothing, since
// nothing it shows is frozen or registered.
// see: The frozen families are read with the pullback's ideas one at a time, and nothing they show is frozen or registered
public static class FamilyIdeasReport
{
    public static string InWords(FamilyIdeasRead read) => FormattableString.Invariant(
        $"{read.Tries} ideas were tried on the {read.Family} as frozen and {read.Passes} passed, where luck alone passes about {read.Luck:0.0} of {read.Tries}.")
        + (read.Passes == 0
            ? string.Empty
            : " Passed: " + string.Join(", ", read.Ideas.Where(one => one.Test.Passes).Select(one => one.Idea.Key)) + ".");

    public static string Build(FamilyIdeasRun run, FamilyIdeasRead read)
    {
        var page = new StringBuilder();

        page.Append(Invariant($"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>The ideas on the frozen {Esc(run.Family)}</title><style>"));
        page.Append(SweepReport.Style);
        page.Append("</style></head><body><main>");
        page.Append(Invariant($"<p class=\"eyebrow\">Phase 13, the ideas on a frozen family</p><h1>The ideas on the {Esc(run.Words)} rule as frozen</h1>"));
        page.Append(Invariant($"<p class=\"history\">Every figure on this page is history: the rule as frozen replayed over the stored history from {run.From:yyyy-MM-dd} to {run.Through:yyyy-MM-dd}, {run.Names:N0} names and {run.ScoredNights:N0} nights scored, {run.Listings:N0} listings its rule makes before five a night and one open trade a stock. Nothing here is frozen or registered.</p>"));

        if (run.LeftOut.Count > 0)
        {
            page.Append(Invariant($"<p class=\"left-out\">Left out, for a series the store does not hold: {Esc(string.Join(", ", run.LeftOut))}.</p>"));
        }

        page.Append("<h2>1. The tries, the passes and luck</h2>");
        page.Append(Invariant($"<p class=\"tries\" data-tries=\"{read.Tries}\" data-passes=\"{read.Passes}\" data-luck=\"{read.Luck:0.00}\">{Esc(InWords(read))} Luck: an idea with no effect is better in at least {SweepIdeas.YearsBetter} of 8 years with {SweepIdeas.RecentYearsBetter} of the last {SweepIdeas.RecentYears} in {SweepIdeas.LuckPatterns()} of 256 cases, before the other tests.</p>"));

        page.Append("<h2>2. The rule as frozen</h2>");
        page.Append(Invariant($"<p class=\"frozen\">{Esc(read.Frozen)}</p>"));
        page.Append(SweepIdeasReport.Table([("As frozen", read.Base)]));

        page.Append("<h2>3. Each idea</h2><p>Each idea added to the rule as frozen alone, judged on the edge, a market switch on the year's total result in risks, a night with no trade counting nothing.</p>");

        foreach (var reading in read.Ideas)
        {
            page.Append(Invariant($"<section class=\"idea\" data-idea=\"{Esc(reading.Idea.Key)}\" data-passes=\"{(reading.Test.Passes ? "yes" : "no")}\"><h3>{Esc(reading.Idea.Key)}: {Esc(reading.Idea.Rule)}</h3>"));
            page.Append(SweepIdeasReport.Table([(reading.Idea.Key, reading.Figures)]));

            if (read.Unchanged(reading))
            {
                page.Append("<p class=\"unchanged\">Every figure is the rule's own as frozen: the idea changes nothing the rule makes.</p>");
            }

            page.Append(Invariant($"<p class=\"answers\">{(reading.Test.Passes ? "Passes" : "Fails")}: {Esc(SweepIdeasReport.Answers(reading.Test))}.</p></section>"));
        }

        page.Append(Invariant($"<p class=\"ran\">Ran from {run.Started:yyyy-MM-dd HH:mm} to {run.Finished:HH:mm} UTC.</p>"));
        page.Append("</main></body></html>");

        return page.ToString();
    }

    static string Esc(string text) => WebUtility.HtmlEncode(text);

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
