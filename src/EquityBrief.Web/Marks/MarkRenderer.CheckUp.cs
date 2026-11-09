using System.Text;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.Marks;

// What a change that reached the operator draws on the Monthly check-up: the change, its record year by year, its four
// checks and the word given or the presses asking for it, and the verdict across the three indexes beside them.
public sealed record CheckUpChange(LoopProposalRow Proposal, IReadOnlyList<LoopTestRow> Years, string Presses, LoopDecisionRow? Word, LoopPooledRow? Pooled, bool RunsBeside);

// The page's opening: how many changes each state holds, read off the run and the words given on it.
public sealed record CheckUpState(int Tried, int Setups, int Put, int Waiting, IReadOnlyList<LoopDecisionRow> Words, DateOnly Next);

// The Monthly check-up's opening card, a change that reached the operator and a strategy's page of the changes tried. The
// marks draw what the tester stored; the one working here is a stored chance read as one in so many.
// see: The Monthly check-up opens on what needs the reader and draws each setup's changes on a page of its own
public sealed partial class MarkRenderer
{
    public const string CheckUpName = LoopPlain.PageName;

    // A run's month as words, or the month as stored where it cannot be read.
    public static string FittedMonth(string month) => FittedIn(month) ?? month;

    // The sentence the page opens on and the line beneath it, by what the run holds for the operator.
    public static (string Headline, string Beneath, string State) CheckUpOpening(CheckUpState state)
    {
        if (state.Waiting > 0)
        {
            return (
                state.Waiting == 1 ? "One change is waiting for you." : Formatted($"{state.Waiting} changes are waiting for you."),
                state.Waiting == 1 ? "It passed all four checks below. Approve it or decline it." : "Each passed all four checks below. Approve or decline each one.",
                "waiting");
        }

        if (state.Words.Count == 0)
        {
            return (
                "Nothing needs you this month.",
                state.Tried == 0
                    ? "No change was tried this month."
                    : Formatted($"None of the {state.Tried} changes tried is put to you, so every strategy stays as it is."),
                "quiet");
        }

        var newest = state.Words.OrderByDescending(word => word.DecidedAt, StringComparer.Ordinal).First();
        var day = LongDay(DateOnly.ParseExact(DayOf(newest.DecidedAt), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        var what = state.Words.Count == 1 ? "one change" : Formatted($"{state.Words.Count} changes");
        var approved = state.Words.Where(word => word.Decision == LoopDecisions.Approved).ToArray();

        return (
            "Nothing needs you this month.",
            approved.Length == 0
                ? Formatted($"You declined {what} on {day}, so every strategy stays as it is.")
                : approved.Any(word => word.Outcome is null)
                    ? Formatted($"You approved {what} on {day}. {(approved.Length == 1 ? "It goes" : "They go")} live with the next night's run.")
                    : Formatted($"You decided on {what}, the last on {day}. Your decisions below say what was applied."),
            "decided");
    }

    static string LongDay(DateOnly day) => day.ToString("MMMM d, yyyy", Invariant);

    // The opening card's body: the sentence, the line beneath it and the month's four counts.
    public static string CheckUpHeadline(CheckUpState state)
    {
        var (headline, beneath, kind) = CheckUpOpening(state);
        var html = new StringBuilder();

        html.Append(Formatted($"<div class=\"cu-open\" data-state=\"{kind}\" data-waiting=\"{state.Waiting}\"><h3 class=\"cu-head\" data-summary>{Escaped(headline)}</h3><p class=\"cu-beneath\">{Escaped(beneath)}</p></div>"));
        html.Append("<div class=\"cu-tiles\">");
        html.Append(Tile(state.Tried.ToString("N0", Invariant), "changes tested"));
        html.Append(Tile(state.Put.ToString(Invariant), "put to you"));
        html.Append(Tile(state.Setups.ToString(Invariant), state.Setups == 1 ? "strategy checked" : "strategies checked"));
        html.Append(Tile(state.Next.ToString("MMM d", Invariant), "next check-up"));
        html.Append("</div>");

        return html.ToString();

        static string Tile(string figure, string label) =>
            $"<div class=\"cu-tile\"><b class=\"cu-figure\">{Escaped(figure)}</b><span class=\"cu-label\">{Escaped(label)}</span></div>";
    }

    // What the operator's word on a change has become, as a short state for its heading and its row.
    static (string Words, string Kind) WordState(LoopDecisionRow? word) => word switch
    {
        null => ("Waiting for you", "waiting"),
        { Decision: LoopDecisions.Declined } => ("Declined", "declined"),
        { Outcome: LoopDecisions.Applied } => ("Approved and live", "applied"),
        { Outcome: LoopDecisions.Refused } => ("Approved, not applied", "refused"),
        _ => ("Approved, goes live with the next night's run", "approved"),
    };

    // A change that reached the operator: what it does, its years against today's rule, its four checks, what the three
    // indexes together said, and its presses or the word given.
    public string CheckUpChangeBlock(CheckUpChange change)
    {
        var proposal = change.Proposal;
        var (state, kind) = WordState(change.Word);
        var html = new StringBuilder();

        html.Append(Formatted($"<div class=\"cu-change\" data-family=\"{Escaped(proposal.Family)}\" data-proposal=\"{Escaped(proposal.Proposal)}\" data-state=\"{kind}\">"));
        html.Append(Formatted($"<div class=\"cu-change-head\"><span class=\"fam-tag\" data-family=\"{Escaped(proposal.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(proposal.Family)}\"></span>{Escaped(FamilyName(proposal.Family))}</span><span class=\"pill cu-state\" data-state=\"{kind}\">{Escaped(state)}</span></div>"));
        html.Append($"<p class=\"cu-what\">{Escaped(LoopPlain.Explained(proposal))}</p>");
        html.Append("<div class=\"cu-grid\"><div class=\"cu-years\"><h4>How it would have done, year by year</h4>");
        html.Append(CheckUpYears(proposal, change.Years));
        html.Append("</div><div class=\"cu-checks\"><h4>The four checks every change must pass</h4>");
        html.Append(CheckUpChecks(proposal));
        html.Append("</div></div>");

        if (change.Pooled is { Passed: false } pooled)
        {
            html.Append($"<p class=\"cu-weigh\" data-pooled=\"did not pass\"><b>One thing to weigh.</b> Today's rules for this strategy, added up across {Escaped(IndicesSaid(pooled.Indices))} together, did not beat luck: {Escaped(LoopPlain.Luck(pooled.Adjusted).ToLowerInvariant())}, better in {pooled.Better} of {Counted(pooled.Counted, "year", "years")}. That is a test of today's rules, not of this change, and it cannot stop a change, so it is shown here for you to judge.</p>");
        }

        html.Append("<p class=\"cu-beside\">Today's rule was picked on these same years, so it had a head start in this comparison.</p>");

        if (change.RunsBeside)
        {
            html.Append(Formatted($"<p class=\"cu-beside\">Today's rule keeps running beside the new one for {LoopDecisions.ReplacedSessions} trading days, so you can compare them.</p>"));
        }

        html.Append(change.Presses);
        html.Append("</div>");

        return html.ToString();
    }

    // A change's four checks, each ticked or crossed with its figure against what it needed, each figure the tester's.
    static string CheckUpChecks(LoopProposalRow proposal)
    {
        var book = proposal.Unit == LoopPoints;
        var ahead = proposal.Trimmed is { } trimmed ? LoopFigure(proposal.Unit, trimmed) : "none";
        var checks = new (string Part, bool Met, string Title, string Said)[]
        {
            ("gate", proposal.Gate, "Unlikely to be luck", Formatted($"{LoopPlain.Luck(proposal.Adjusted)} that this is luck. It had to be under {LoopPlain.Luck(LoopGate.Bar).ToLowerInvariant()}.")),
            ("stable", proposal.Stable, "Better in most years", Formatted($"Better in {proposal.Better} of {Counted(proposal.Counted, "full year", "full years")}. It needed {LoopScreens.StableOf} of {LoopScreens.StableIn}, with the latest one better.")),
            ("trimmed", proposal.Trimmed > 0, "Not down to a few lucky trades", Formatted($"{ahead} ahead of today's rule with the {LoopScreens.LeftOut} biggest results on each side taken out.")),
            ("counts", proposal.Counts, "Enough history", book
                ? Formatted($"{Counted(proposal.Units, "month", "months")} of results. It needed {LoopScreens.BookMonths}.")
                : Formatted($"{Counted(proposal.Units, "trade", "trades")} in all. It needed {LoopScreens.SwingTrades}.")),
        };
        var html = new StringBuilder("<ul class=\"cu-check-list\">");

        foreach (var (part, met, title, said) in checks)
        {
            html.Append(Formatted($"<li data-part=\"{part}\" data-met=\"{Flag(met)}\"><span class=\"cu-tick\" aria-hidden=\"true\">{(met ? "&#10003;" : "&#10007;")}</span><div><b>{Escaped(title)}</b> <span class=\"cu-met\">{(met ? "Met" : "Not met")}</span><p>{said}</p></div></li>"));
        }

        html.Append("</ul>");

        return html.ToString();
    }

    // Each test year as a pair of bars from one line, today's rule above and the change beneath with both figures beside
    // them, the year still running marked as not counted.
    static string CheckUpYears(LoopProposalRow proposal, IReadOnlyList<LoopTestRow> years)
    {
        if (years.Count == 0)
        {
            return "<p class=\"degraded\">No test year is stored for this change.</p>";
        }

        const int Width = 420;
        const int Label = 86;
        const int Figures = 56;
        const int Row = 34;
        var book = proposal.Unit == LoopPoints;
        double Shown(double total) => book ? total * 100 : total;
        var low = Math.Min(0, years.Min(year => Math.Min(Shown(year.CurrentTotal), Shown(year.ProposedTotal))));
        var high = Math.Max(0, years.Max(year => Math.Max(Shown(year.CurrentTotal), Shown(year.ProposedTotal))));
        var span = high - low > 0 ? high - low : 1;
        var track = Width - Label - Figures - 8;
        double X(double value) => Label + ((value - low) / span * track);
        var zero = X(0);
        var height = years.Count * Row;
        var html = new StringBuilder();

        html.Append("<p class=\"cu-legend\"><span class=\"cu-key cu-key-rule\"></span>Today's rule <span class=\"cu-key cu-key-change\"></span>With the change</p>");
        html.Append(Formatted($"<svg class=\"cu-bars\" viewBox=\"0 0 {Width} {height}\" role=\"img\" aria-label=\"Each test year, today's rule above and the change beneath\">"));
        html.Append(Formatted($"<line class=\"cu-axis\" x1=\"{Number(zero)}\" y1=\"0\" x2=\"{Number(zero)}\" y2=\"{height}\"/>"));

        foreach (var (year, at) in years.Select((year, at) => (year, at)))
        {
            var y = at * Row;

            string Bar(double value, int top, string kind) =>
                Formatted($"<rect class=\"{kind}\" x=\"{Number(Math.Min(X(value), zero))}\" y=\"{top}\" width=\"{Number(Math.Abs(X(value) - zero))}\" height=\"11\" rx=\"2\"/>");

            html.Append(Formatted($"<g data-year=\"{year.Year}\" data-complete=\"{(year.Complete ? 1 : 0)}\">"));
            html.Append(Formatted($"<text class=\"cu-year\" x=\"0\" y=\"{y + 20}\">{year.Year}{(year.Complete ? string.Empty : " so far")}</text>"));
            html.Append(Bar(Shown(year.CurrentTotal), y + 4, "cu-rule"));
            html.Append(Bar(Shown(year.ProposedTotal), y + 17, "cu-new"));
            html.Append(Formatted($"<text class=\"cu-fig\" x=\"{Width}\" y=\"{y + 13}\" text-anchor=\"end\">{Shown(year.CurrentTotal):+0.0;-0.0;0.0}</text>"));
            html.Append(Formatted($"<text class=\"cu-fig cu-fig-new\" x=\"{Width}\" y=\"{y + 27}\" text-anchor=\"end\">{Shown(year.ProposedTotal):+0.0;-0.0;0.0}</text>"));
            html.Append("</g>");
        }

        html.Append("</svg>");
        html.Append($"<p class=\"cu-note\">{(book ? "Points ahead of the easy alternative in each year." : "Gain over the easy alternative in each year, for every $1 risked on a trade.")}{(years.Any(year => !year.Complete) ? " A year still running does not count." : string.Empty)}</p>");

        return html.ToString();
    }

    // The changes tried for a strategy, under each kind of change, every row its own plain words and the tester's figures,
    // the kinds that found nothing to change saying so in a line.
    public string CheckUpGroups(IReadOnlyList<LoopProposalRow> proposals, LoopProposalRow? put, IReadOnlyList<LoopDecisionRow> words)
    {
        var html = new StringBuilder();

        foreach (var group in LoopPlain.Groups)
        {
            var own = proposals.Where(proposal => LoopPlain.GroupOf(proposal.Proposal) == group).ToArray();

            if (own.Length == 0)
            {
                continue;
            }

            var passed = own.Count(proposal => proposal.Passed);
            var drawn = own.Where(proposal => proposal.Words is not null).OrderByDescending(proposal => proposal.Passed).ThenBy(proposal => proposal.Adjusted ?? 1).ToArray();
            var quiet = own.Length - drawn.Length;

            html.Append(Formatted($"<details class=\"cu-group\" data-group=\"{Escaped(group)}\" data-tried=\"{own.Length}\" data-passed=\"{passed}\"{(passed > 0 ? " open" : string.Empty)}><summary><b>{Escaped(group)}</b> <span class=\"cu-count\">{own.Length} tried, {(passed == 0 ? "none" : passed.ToString(Invariant))} passed</span></summary>"));

            if (drawn.Length > 0)
            {
                html.Append("<div class=\"tbl-wrap\"><table class=\"cu-tried\"><thead><tr><th>What it tried</th><th>Chance it is luck</th><th>Better years</th><th class=\"r\">Ahead of today's rule</th><th>Result</th></tr></thead><tbody>");

                foreach (var proposal in drawn)
                {
                    var result = ResultOf(proposal, put, words);

                    html.Append($"<tr data-proposal=\"{Escaped(proposal.Proposal)}\" data-passed=\"{(proposal.Passed ? 1 : 0)}\"><td>{Escaped(LoopPlain.Tried(proposal))}</td><td class=\"num\">{Escaped(LoopPlain.Luck(proposal.Adjusted))}</td><td class=\"num\">{proposal.Better.ToString(Invariant)} of {proposal.Counted.ToString(Invariant)}</td><td class=\"r num\">{LoopFigure(proposal.Unit, proposal.Trimmed)}</td><td class=\"cu-result\" data-result=\"{Escaped(result)}\">{Escaped(result)}</td></tr>");
                }

                html.Append("</tbody></table></div>");
            }

            if (quiet > 0)
            {
                html.Append($"<p class=\"cu-quiet\" data-quiet=\"{quiet.ToString(Invariant)}\">{(quiet == own.Length ? (quiet == 1 ? "It found" : Formatted($"All {quiet} found")) : (quiet == 1 ? "1 more found" : Formatted($"{quiet} more found")))} nothing better than today's rule.</p>");
            }

            html.Append("</details>");
        }

        return html.ToString();

        static string ResultOf(LoopProposalRow proposal, LoopProposalRow? put, IReadOnlyList<LoopDecisionRow> words)
        {
            if (!proposal.Passed)
            {
                return "Did not pass";
            }

            if (put?.Proposal != proposal.Proposal)
            {
                return "Passed";
            }

            return words.FirstOrDefault(word => word.Family == proposal.Family && word.Proposal == proposal.Proposal) switch
            {
                null => "Passed, waiting for you",
                { Decision: LoopDecisions.Declined } => "Passed, declined",
                _ => "Passed, approved",
            };
        }
    }

    // What a strategy's month came to, for its line on the opening page.
    public static (string Words, string Kind) CheckUpResult(LoopSummaryRow row, LoopDecisionRow? word)
    {
        if (row.Tested == 0)
        {
            return ("Nothing tried", "none");
        }

        if (row.Put is null)
        {
            return ("No change", "none");
        }

        return word switch
        {
            null => ("Waiting for you", "waiting"),
            { Decision: LoopDecisions.Declined } => ("No change, you declined", "none"),
            { Outcome: LoopDecisions.Refused } => ("No change, not applied", "none"),
            _ => ("Changed", "changed"),
        };
    }

    // The strongest change's chance against the bar and what the three indexes said, in a strategy's full workings.
    public static string CheckUpStrongest(LoopSummaryRow row)
    {
        var html = new StringBuilder("<div class=\"cu-strongest\">");

        html.Append(row.Strongest is { } strongest
            ? Formatted($"<p><b>The strongest change tried.</b> {PMeter(strongest.Adjusted)} <span class=\"loop-better\" data-better=\"{strongest.Better}\" data-counted=\"{strongest.Counted}\">{(strongest.Adjusted is { } p ? Formatted($"p {p.ToString("0.0000", Invariant)}, ") : "p not read, ")}better in {strongest.Better} of {Counted(strongest.Counted, "year", "years")}</span></p>")
            : "<p class=\"degraded\">No change was tried.</p>");
        html.Append(Formatted($"<p><b>{PooledHeading}.</b> {PooledCell(row.Pooled)}</p><p class=\"loop-understated\">The rule today was chosen on these same test years, so a proposal reads understated against it.</p></div>"));

        return html.ToString();
    }
}
