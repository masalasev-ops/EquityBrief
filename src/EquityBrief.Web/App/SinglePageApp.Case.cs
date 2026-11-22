using System.Globalization;
using System.Text;
using EquityBrief.Core.Loop;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// One answer of the local model's over a case's figures: the case for, the case against, the model that wrote them and the
// day it did.
public sealed record CaseWordsLine(string For, string Against, string Model, DateOnly Written);

// A variant's words: the newest answer, and the earlier ones kept as the case's history, newest first.
public sealed record CaseWordsView(CaseWordsLine Newest, IReadOnlyList<CaseWordsLine> Earlier);

// The case for promotion's page: the night it is read for, the day the rule freeze ends while the day falls inside it, every
// running revision with its variants, the ideas the monthly runs have tested to date with those that passed, and the local
// model's words over each stored case by its variant.
public sealed record PromotionPageView(DateOnly? Night, DateOnly? FrozenUntil, IReadOnlyList<PromotionRowView> Rows, int Tested, int Passed, IReadOnlyDictionary<string, CaseWordsView>? Words = null)
{
    public IReadOnlyList<PromotionCaseView> Cases => [.. Rows.SelectMany(row => row.Variants).Where(one => one.Conditions.Meets)];

    // The variants nearest to meeting the rule, the three nearest, more conditions met first, then the share of the count
    // held, then the lead over the same nights.
    public IReadOnlyList<PromotionCaseView> Nearest =>
    [
        .. Rows.SelectMany(row => row.Variants)
            .Where(one => !one.Conditions.Meets)
            .OrderByDescending(one => PromotionRule.Nearness(one.Book, one.Conditions, one.Same, one.RunningSame))
            .Take(3),
    ];
}

// The case for promotion, under the Improvement loop. For each variant meeting the rule a case read in two minutes: what
// changes, the figures over the same nights and over the long history, what its picks looked like, the case for and the case
// against, its counted part code's, and what would change on Tonight's cards, with Promote and Keep, a press refused inside
// the rule freeze. Under the cases every running revision against its variants over the same nights, a book's own exit
// beside it; and with no case why none stands and the nearest misses.
// see: A background version replaces a live rule at a monthly run only where it beat it over the same nights and over the long history and closed 50 trades or a book's 12 months
// see: The Improvement loop names a setup's revision and its variants in Paper trading's words
public sealed partial class SinglePageApp
{
    public const string CaseRoute = "#/loop/case";

    public const string CasePostRoute = "/loop/promote/";

    public const string CaseName = "The case for promotion";

    public const string CaseLede = "From the monthly run of 6 Feb 2027, a variant can become its setup's running revision only where it beat that revision over the same nights and over the long history, and closed 50 trades, or 12 months for a book. You decide; nothing changes without your click.";

    // The case for and the case against in code's own words, until the local model's are written; each holds no figure.
    public const string CodeFor = "It did better than the running revision over the same nights, and over the later years of the long history it was not chosen on.";

    public const string CodeAgainst = "Its lead was measured over few nights and could still be luck: a run of bad weeks would close it.";

    public const string CodeWords = "In code's words. The local model's words take their place once written, and may hold no figure and no name.";

    public string CaseRegion(PromotionPageView view)
    {
        var region = new StringBuilder("<section class=\"case-page\">");

        region.Append(Cards.Masthead(CaseName, $"<span class=\"m-screen\">{CaseName}</span>", view.Night is { } night ? "Computed for " + Cards.Day(night) : "No night stored yet"));

        var freeze = view.FrozenUntil is { } until
            ? $"<p class=\"lm-freeze\" data-frozen=\"{Cards.Day(until)}\"><b>The rules are frozen until the monthly run of {Day(until)}.</b> Until then the buttons are drawn and a press is refused.</p>"
            : string.Empty;

        foreach (var one in view.Cases)
        {
            region.Append(Cards.Computed(
                "Improvement loop · " + CaseName,
                freeze + CaseBody(one, view),
                title: Escaped(CaseTitle(one)),
                lede: Escaped(CaseLede),
                region: "case",
                id: "case-" + one.Index + "-" + one.Family));
        }

        if (view.Cases.Count == 0)
        {
            region.Append(Cards.Computed(
                "Improvement loop · " + CaseName,
                freeze + NoCase(view),
                title: "No variant meets the rule",
                lede: Escaped(CaseLede),
                region: "case-none"));
        }

        region.Append(Cards.Computed(
            "Improvement loop · " + CaseName,
            string.Concat(view.Rows.GroupBy(row => row.IndexName).Select(index => RevisionsTable(index.Key, [.. index]))),
            title: "Every running revision against its variants, over the same nights",
            lede: "Each variant is read from its own first night, or from 15 Nov 2026 where it started then, beside the running revision over the same nights.",
            region: "case-table"));
        region.Append("</section>");

        return region.ToString();
    }

    public static string CaseTitle(PromotionCaseView one) =>
        $"{MarkRenderer.FamilyName(one.Family)}, {one.IndexName}: the variant '{Lowered(one.VariantName)}' against {RevisionView.Named(one.Revision).ToLowerInvariant()}";

    static string CaseBody(PromotionCaseView one, PromotionPageView view)
    {
        var html = new StringBuilder("<div class=\"lm-case\">");
        var revision = RevisionView.Named(one.Revision).ToLowerInvariant();

        html.Append($"<div data-part=\"changes\"><h4>What changes</h4><p>{Escaped(string.Join(" ", one.Changes.Select(Sentence)))} Everything else stays as {revision} has it.</p></div>");
        html.Append("<div class=\"lm-wide\" data-part=\"figures\"><h4>The figures</h4>");
        html.Append($"<div class=\"tbl-wrap\"><table class=\"lm-table lm-narrow\"><thead><tr><th></th><th class=\"r\">The variant</th><th class=\"r\">{Escaped(RevisionView.Named(one.Revision))}, running since {Day(one.RevisionFrom)}</th></tr></thead><tbody>");
        html.Append($"<tr data-row=\"same\"><td>Same nights since {Day(one.Same.From)}, {(one.Book ? "points a month" : "per $1 risked")}</td><td class=\"r num\" data-h=\"The variant\">{Escaped(Record(one.Book, one.Same))}</td><td class=\"r num\" data-h=\"{Escaped(RevisionView.Named(one.Revision))}\">{Escaped(Record(one.Book, one.RunningSame))}</td></tr>");
        html.Append($"<tr data-row=\"long\"><td>Long history, chosen on 2005 to 2018, judged from 2019{(LongTestLine(one) is { } test ? $"<br><small data-part=\"long-test\">{Escaped(test)}</small>" : string.Empty)}</td><td class=\"r num\" data-h=\"The variant\">{Escaped(LongFigure(one.Book, one.Long))}</td><td class=\"r num\" data-h=\"{Escaped(RevisionView.Named(one.Revision))}\">{Escaped(LongFigure(one.Book, one.RunningLong))}</td></tr>");
        html.Append($"</tbody></table></div><p class=\"lm-small\" data-part=\"conditions\">{Escaped(ConditionsLine(one))}</p></div>");
        html.Append($"<div data-part=\"picks\"><h4>What its picks looked like</h4><p>{Escaped(PicksLine(one))}</p></div>");
        var words = view.Words is not null && view.Words.TryGetValue(CaseKey(one.Index, one.Family, one.Variant), out var written) ? written : null;

        html.Append($"<div data-part=\"for\"><h4>The case for</h4>{Quote(words?.Newest.For, CodeFor, words?.Newest)}</div>");
        html.Append($"<div data-part=\"against\"><h4>The case against</h4><p data-counted><b>Counted by code:</b> {Escaped(CountedAgainst(one, view.Tested, view.Passed))}</p>{Quote(words?.Newest.Against, CodeAgainst, words?.Newest)}</div>");

        if (words is { Earlier.Count: > 0 })
        {
            html.Append("<details class=\"lm-small\" data-part=\"earlier-words\"><summary>Earlier words, over the figures of an earlier monthly run</summary><ul>");

            foreach (var earlier in words.Earlier)
            {
                html.Append($"<li>{Escaped(Written(earlier))}: for, {Escaped(earlier.For)} Against, {Escaped(earlier.Against)}</li>");
            }

            html.Append("</ul></details>");
        }
        html.Append($"<div data-part=\"tonight\"><h4>What would change on Tonight's cards</h4><p>{Escaped(TonightLine(one))}</p></div>");
        html.Append("</div>");

        var disabled = view.FrozenUntil is null ? string.Empty : " disabled";
        var action = $"{CasePostRoute}{Uri.EscapeDataString(one.Index)}/{Uri.EscapeDataString(one.Family)}";
        var key = "case|" + one.Index + "|" + one.Family;

        html.Append($"<div class=\"loop-decision\" data-loop-key=\"{Escaped(key)}\"><div class=\"lm-buttons\">");
        html.Append($"<form class=\"loop-press\" method=\"post\" action=\"{Escaped(action)}\" data-loop-key=\"{Escaped(key)}\"><input type=\"hidden\" name=\"variant\" value=\"{Escaped(one.Variant)}\"><input type=\"hidden\" name=\"press\" value=\"promote\"><button type=\"submit\"{disabled}>Promote</button></form>");
        html.Append($"<form class=\"loop-press\" method=\"post\" action=\"{Escaped(action)}\" data-loop-key=\"{Escaped(key)}\"><input type=\"hidden\" name=\"variant\" value=\"{Escaped(one.Variant)}\"><input type=\"hidden\" name=\"press\" value=\"keep\"><button type=\"submit\" class=\"btn-2\"{disabled}>Keep {Escaped(revision)}</button></form>");
        html.Append($"</div><p class=\"lm-small\">{Escaped(PromoteLine(one.Revision))}</p><p class=\"loop-said\" aria-live=\"polite\"></p></div>");

        return html.ToString();
    }

    // The key a case's words are read under: its index, its setup and its variant.
    public static string CaseKey(string index, string family, string variant) => index + "|" + family + "|" + variant;

    // A case's words as the page draws them: the local model's with the day it wrote them and the model, or code's own
    // sentence where none is written.
    static string Quote(string? model, string code, CaseWordsLine? line) =>
        model is { } said && line is { } by
            ? $"<p class=\"ai-quote\" data-words=\"model\">{Escaped(said)}<small>{Escaped(Written(by))}</small></p>"
            : $"<p class=\"ai-quote\" data-words=\"code\">{Escaped(code)}<small>{Escaped(CodeWords)}</small></p>";

    public static string Written(CaseWordsLine line) => $"Written on {Day(line.Written)} by {line.Model}";

    // Which long test the long history's figures come from: its month, and the day its run started where the run's name
    // carries it, none where no long test judged the variant.
    public static string? LongTestLine(PromotionCaseView one)
    {
        if (one.LongTestMonth is not { } month
            || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
        {
            return null;
        }

        var stamp = one.LongTestRun?.Split('-')[^1];
        var ran = stamp is { Length: >= 8 } && DateOnly.TryParseExact(stamp[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? $", run on {Day(day)}" : string.Empty;

        return $"From the long test of {first.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}{ran}";
    }

    public static string PromoteLine(int revision) =>
        $"This variant becomes {RevisionView.Named(revision + 1).ToLowerInvariant()}; {RevisionView.Named(revision).ToLowerInvariant()} runs on as a variant, each keeping its own record.";

    // Each condition met or not, the count with it.
    public static string ConditionsLine(PromotionCaseView one)
    {
        var revision = RevisionView.Named(one.Revision).ToLowerInvariant();
        var count = one.Book
            ? $"{PromotionRule.Months} months, {Yes(one.Conditions.Enough)}, {one.Same.Months}"
            : $"{PromotionRule.Trades} closed trades, {Yes(one.Conditions.Enough)}, {one.Same.Closed}";

        return $"Each condition: beat {revision} over the same nights, {Yes(one.Conditions.SameNights)}; over the long history, {Yes(one.Conditions.LongHistory)}; {count}.";
    }

    public static string PicksLine(PromotionCaseView one)
    {
        var nights = Math.Max(1, one.Sessions);
        var own = one.Same.Listed * 1.0 / nights;
        var ran = one.RunningSame.Listed * 1.0 / nights;
        var more = own < ran ? "Fewer" : own > ran ? "More" : "As many";
        var revision = RevisionView.Named(one.Revision).ToLowerInvariant();

        return string.Create(CultureInfo.InvariantCulture, $"{more} picks, {own:0.0} a night against {ran:0.0} over the same {nights} nights. {one.Shared} of its {one.Same.Listed} picks were {revision}'s picks too.");
    }

    public static string CountedAgainst(PromotionCaseView one, int tested, int passed)
    {
        var past = one.Book ? $"{one.Same.Months - PromotionRule.Months} months past the {PromotionRule.Months}" : $"{one.Same.Closed - PromotionRule.Trades} trades past the {PromotionRule.Trades}";

        return string.Create(CultureInfo.InvariantCulture, $"{tested:#,0} ideas tested to date, {passed:#,0} passed. An idea with no edge passes the bar each is held to about 1 time in {Math.Round(1 / LoopGate.Bar):#,0}, so luck alone would pass about {PromotionRule.LuckyPasses(tested):0.0} of them. It is {past}.");
    }

    // The stocks the variant would keep of the running revision's picks tonight, those it would leave out and those it would add.
    public static string TonightLine(PromotionCaseView one)
    {
        var revision = RevisionView.Named(one.Revision).ToLowerInvariant();
        var kept = one.RunningTonight.Where(ticker => one.Tonight.Contains(ticker, StringComparer.Ordinal)).ToArray();
        var left = one.RunningTonight.Where(ticker => !one.Tonight.Contains(ticker, StringComparer.Ordinal)).ToArray();
        var added = one.Tonight.Where(ticker => !one.RunningTonight.Contains(ticker, StringComparer.Ordinal)).ToArray();

        if (one.RunningTonight.Count == 0 && one.Tonight.Count == 0)
        {
            return $"Neither picked a stock tonight, so Tonight's cards would read the same.";
        }

        var said = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"Tonight it would have kept {kept.Length} of {revision}'s {one.RunningTonight.Count} pick{(one.RunningTonight.Count == 1 ? string.Empty : "s")}"));

        said.Append(kept.Length > 0 ? ", " + string.Join(", ", kept) : string.Empty);
        said.Append(left.Length > 0 ? ", and left out " + string.Join(", ", left) : string.Empty);
        said.Append(added.Length > 0 ? ", and added " + string.Join(", ", added) : string.Empty);
        said.Append('.');

        return said.ToString();
    }

    static string NoCase(PromotionPageView view)
    {
        var nearest = view.Nearest;
        var html = new StringBuilder();

        html.Append(nearest.Count == 0
            ? "<p class=\"lm-p\" data-none=\"no-variant\">No variant meets the rule this month, and no variant runs beside a running revision to come near it.</p>"
            : $"<p class=\"lm-p\" data-none=\"nearest\">No variant meets the rule this month. The nearest {(nearest.Count == 1 ? "is" : nearest.Count.ToString(CultureInfo.InvariantCulture) + " are")}:</p>");

        if (nearest.Count > 0)
        {
            html.Append("<ol class=\"lm-p\" data-nearest>");

            foreach (var one in nearest)
            {
                html.Append($"<li data-index=\"{Escaped(one.Index)}\" data-family=\"{Escaped(one.Family)}\">{Escaped(NearLine(one))}</li>");
            }

            html.Append("</ol>");
        }

        return html.ToString();
    }

    // A near miss in the operator's words: the setup, the index, the variant of its revision and its change, its count and
    // whether it is ahead over the same nights.
    public static string NearLine(PromotionCaseView one)
    {
        var revision = RevisionView.Named(one.Revision).ToLowerInvariant();
        var count = one.Book ? $"{one.Same.Months} of its {PromotionRule.Months} months" : $"{one.Same.Closed} of its {PromotionRule.Trades} trades closed";
        var ahead = one.Conditions.SameNights ? $"ahead of {revision} over the same nights" : $"not ahead of {revision} over the same nights";

        return $"{MarkRenderer.FamilyName(one.Family)}, {one.IndexName}, variant of {revision}: {Lowered(one.VariantName)}, {count}, {ahead}.";
    }

    static string RevisionsTable(string index, IReadOnlyList<PromotionRowView> rows)
    {
        var html = new StringBuilder($"<h3 class=\"lm-h\">{Escaped(index)}</h3><div class=\"tbl-wrap\"><table class=\"lm-table\" data-index=\"{Escaped(index)}\"><thead><tr><th>Setup</th><th>Running revision</th><th>Its best variant</th><th>Meets the rule?</th></tr></thead><tbody>");

        foreach (var row in rows)
        {
            var best = row.Variants.OrderByDescending(one => one.Conditions.Meets).ThenByDescending(one => PromotionRule.Nearness(one.Book, one.Conditions, one.Same, one.RunningSame)).FirstOrDefault();
            var running = row.Revision is { } number && row.RevisionFrom is { } from
                ? $"{RevisionView.Named(number)}, running since {Day(from)}: {(row.Running is { } record ? Record(row.Book, record) : "none yet")}"
                : "No registered revision";

            html.Append($"<tr data-family=\"{Escaped(row.Family)}\"><td data-h=\"Setup\">{Escaped(MarkRenderer.FamilyName(row.Family))}</td><td data-h=\"Running revision\">{Escaped(running)}</td>");
            html.Append(best is null
                ? "<td data-h=\"Its best variant\">No variant runs beside it</td><td data-h=\"Meets the rule?\">No variant to read</td></tr>"
                : $"<td data-h=\"Its best variant\">{Escaped(best.VariantName)}: {Escaped(Record(best.Book, best.Same))}</td><td data-h=\"Meets the rule?\" data-meets=\"{(best.Conditions.Meets ? "yes" : "no")}\">{Escaped(Meets(best))}</td></tr>");

            if (row.OwnExit is { } own)
            {
                html.Append(string.Create(CultureInfo.InvariantCulture, $"<tr data-family=\"{Escaped(row.Family)}\" data-own-exit><td data-h=\"Setup\">{Escaped(MarkRenderer.FamilyName(row.Family))}, its own exit</td><td data-h=\"Running revision\">{Points(own.Book)} over the same nights</td><td data-h=\"Its best variant\">Its own exit, kept as a record: {Points(own.Own)}</td><td data-h=\"Meets the rule?\">A record only; brought to you after its first {PromotionRule.Months} months if it leads</td></tr>"));
            }
        }

        html.Append("</tbody></table></div>");

        return html.ToString();
    }

    // Whether a variant meets the rule, and where not the first condition it falls short of.
    public static string Meets(PromotionCaseView one) =>
        one.Conditions.Meets ? "Yes. Its case is above."
        : !one.Conditions.Enough ? (one.Book ? $"Not yet: {one.Same.Months} of {PromotionRule.Months} months." : $"Not yet: {one.Same.Closed} of {PromotionRule.Trades} trades.")
        : !one.Conditions.SameNights ? "Not yet: not ahead over the same nights."
        : "Not yet: not ahead over the long history.";

    public static string Record(bool book, PromotionRecord record) =>
        record.Figure is not { } figure
            ? (book ? "none yet" : $"none yet, {record.Closed} closed")
            : book && record.Months == 0 ? FirstMonth(figure)
            : book ? string.Create(CultureInfo.InvariantCulture, $"{Points(figure)} over {record.Months} month{(record.Months == 1 ? string.Empty : "s")}")
            : string.Create(CultureInfo.InvariantCulture, $"{Signed(figure)} over {record.Closed} closed");

    static string LongFigure(bool book, PromotionLong? figure) =>
        figure?.Figure is not { } made ? "not tested yet"
        : book ? Points(made)
        : Signed(made);

    static string Points(double points) => string.Create(CultureInfo.InvariantCulture, $"{(points >= 0 ? "+" : "-")}{Math.Abs(points):0.00} points a month");

    // A book's record before a whole month has passed: its change so far, never a rate over no months.
    public static string FirstMonth(double points) => string.Create(CultureInfo.InvariantCulture, $"{(points >= 0 ? "+" : "-")}{Math.Abs(points):0.00} points so far, in its first month");

    static string Signed(double risks) => string.Create(CultureInfo.InvariantCulture, $"{(risks >= 0 ? "+" : "-")}${Math.Abs(risks):0.00}");

    static string Yes(bool met) => met ? "yes" : "no";

    static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    static string Lowered(string words) => words.Length == 0 ? words : char.ToLowerInvariant(words[0]) + words[1..];

    static string Sentence(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..] + (words.EndsWith('.') ? string.Empty : ".");
}
