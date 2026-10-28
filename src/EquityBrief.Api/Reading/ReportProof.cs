using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EquityBrief.Api.Reading;

// One rule's answer for one page: passed, failed or unexamined, with why where it is not a pass.
public sealed record ProofVerdict(string Rule, string Verdict, string? Why = null);

// One page's answers, rule by rule, and where its peer group names none of the business its own account describes, the
// group a code proposes for the operator to approve before any override is set.
public sealed record ProofPage(string Ticker, IReadOnlyList<ProofVerdict> Verdicts, PeerProposal? Peer = null);

// A peer group naming none of the business the page's own account describes: the stock, the group it is drawn against, the
// business the account names most and the group proposed for it, which no override sets until the operator approves it.
public sealed record PeerProposal(string Ticker, string Group, string Business, string Proposed);

// One rule over every page: how many passed, failed and were not examined, and the pages that failed.
public sealed record ProofRule(string Rule, string Says, int Passed, int Failed, int Unexamined, IReadOnlyList<string> Failing);

// The proof over a set of pages: when it was read, the index and night they are for, each rule's count and each page.
public sealed record ProofReport(DateTimeOffset ReadAt, string Index, DateOnly? Night, IReadOnlyList<ProofRule> Rules, IReadOnlyList<ProofPage> Pages);

// The report page's rules held over every name's page as the read surface draws it: each rule reads the drawn page and
// answers passed, failed or unexamined, and a rule that cannot be read on a page answers unexamined, which is never a pass.
// see: Every report page rule is held over every name's drawn page and reported passed, failed or unexamined with the pages each failed on
public static partial class ReportProof
{
    // Where the read surface draws the proof.
    public const string Route = "/proof/report-pages";

    public const string Passed = "pass";
    public const string Failed = "fail";
    public const string Unexamined = "unexamined";

    // Each rule by its key and what it holds, in the order the operator's corrections state them.
    public static IReadOnlyList<(string Rule, string Says)> Rules { get; } =
    [
        ("directions", "Every drawn sentence held to the stored latest quarter, a written section's or the cause of a move after that quarter was reported, read against its directions again, states none they contradict, a contradicted one withheld and two sources disagreeing on a beat or a miss named in one sentence in its place."),
        ("markers", "No source marker, bracketed or a document's label used as a word, is drawn in a written section or a move's cause, and a section's sources are listed by title beneath it."),
        ("abbreviations", "Every abbreviation a written section uses is spelled out where first used."),
        ("short-version", "The short version opens on the pick for a picked stock, on the held trade for a stock a family holds a trade in, and on the quarter for any other, before what worked and what did not."),
        ("held-trade", "A stock a family holds a trade in opens its short version on that trade, leaving the levels' plan to the plan region, and keeps a short card for it, and its plan and its chart draw the trade's own exits."),
        ("what-worked", "Each paragraph of the short version's what worked and what did not holds at most four sentences."),
        ("own-limit", "Each night the list picked a stock before is followed to the end of its own setup's limit, as the key beneath them states it."),
        ("one-value", "Each concept the page marks is drawn with one value."),
        ("header", "The masthead marks its price LIVE, CLOSED or LAST CLOSE."),
        ("levels-older", "Where the stored year holds no band on a side, bands from the pulled history are drawn and marked older."),
        ("pick-on-chart", "A picked or held stock's chart draws its stop and target."),
        ("levels-cover", "A picked or held stock's bands, stored and older, reach out as far as its target and its stop, or the page says no band the history read reaches one."),
        ("pick-exit", "A picked stock's plan opens on the pick's own exit, and where the levels' plan places none its exits line names the pick's."),
        ("next-report", "A picked stock's card states its next report, estimated where none is on file."),
        ("next-report-estimated", "A next report not on file is drawn with the day it is estimated at, marked estimated."),
        ("rules-family", "How the rules read it opens on the checks of the family that picked the stock."),
        ("trend-words", "A trend gate that failed is said in plain words."),
        ("news-matched", "The news region states how many stories it left out as another company's, and its counts add up over the stories it kept."),
        ("news-labelled", "Every story the news region keeps that admissibility admitted is labelled."),
        ("industry-label", "The peer group is labelled as the provider's classification or the stock's override."),
        ("industry-mismatch", "A peer group naming none of the business the page's own account describes is flagged, and one naming it is not."),
        ("sources-relevant", "The page's sources list is held to the documents naming the company or its industry, the rest folded apart beneath it, and says where fewer than two remain."),
        ("newest-quarter", "A newer quarter's results release stored before its filing is read by code into the latest quarter, labelled so."),
        ("segments-release", "A quarter reported before its filing is stored draws its results release's own segment table first, labelled so, and a segment commentary written from the filing's segments says so."),
        ("rerun-alone", "A failed section can be written again alone on the local model."),
        ("model-labels", "The page names the model each job ran on and whether it is paid or local."),
    ];

    public static ProofPage Evaluate(string ticker, string page)
    {
        var verdicts = new List<ProofVerdict>();
        var prose = string.Join(" ", ProseOf().Matches(page).Select(match => match.Groups[1].Value).Concat(CauseOf().Matches(page).Select(match => match.Groups["said"].Value)).Select(said => WebUtility.HtmlDecode(Regex.Replace(said, "<[^>]+>", " "))));
        var written = prose.Trim().Length > 0;
        var picked = page.Contains("<div class=\"decision-card\"", StringComparison.Ordinal);

        void Add(string rule, bool? passed, string? why = null) =>
            verdicts.Add(new ProofVerdict(rule, passed switch { true => Passed, false => Failed, null => Unexamined }, why));

        // Every drawn sentence held to stored directions read against them again, a written section's paragraphs and a move's
        // written cause alike, a withheld sentence's place saying what the figures show and so read apart.
        var held = HeldSection().Matches(page)
            .Select(section => (Facts: section.Groups[1].Value, Texts: ProseOf().Matches(section.Groups[2].Value).Select(match => match.Groups[1].Value).ToArray()))
            .Concat(HeldCause().Matches(page).Select(cell => (Facts: cell.Groups[1].Value, Texts: new[] { cell.Groups[2].Value })))
            .ToArray();
        var contradicted = held
            .SelectMany(heldTo =>
            {
                var facts = WebUtility.HtmlDecode(heldTo.Facts)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(fact => fact.Split('|'))
                    .Where(parts => parts.Length == 3 && int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                    .Select(parts => new EquityBrief.Core.Research.DirectionFact(parts[0], parts[1], int.Parse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), string.Empty))
                    .ToArray();

                return heldTo.Texts
                    .Select(text => WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", " ")))
                    .SelectMany(paragraph => SentenceBreak().Split(paragraph))
                    .Where(sentence => !sentence.Contains(EquityBrief.Core.Research.Directions.WithheldOpens, StringComparison.Ordinal)
                        && EquityBrief.Core.Research.Directions.Clashes(sentence, facts).Count > 0);
            })
            .FirstOrDefault();

        Add("directions", held.Length == 0 ? null : contradicted is null, held.Length == 0 ? "no written section or cause is held to a stored latest quarter" : contradicted is null ? null : "drawn against the stored figures: " + contradicted);

        // A marker, bracketed or a document's label used as a word, in a written section or a move's cause.
        var marked = MarkerOf().Match(prose) is { Success: true } bracketed ? bracketed : BareLabelOf().Match(prose);

        Add("markers", written ? !marked.Success : null, written ? (marked.Success ? "a marker is drawn: " + marked.Value : null) : "no written section is drawn");
        Add("abbreviations", written ? Unspelled(prose) is not { } missing : null, written ? Unspelled(prose) : "no written section is drawn");

        var lead = ShortLead().Match(page);
        var heldOn = page.Contains("<section class=\"held-card\"", StringComparison.Ordinal);

        Add("short-version", lead.Success ? picked ? lead.Groups[1].Value == "pick" : heldOn ? lead.Groups[1].Value == "held" : lead.Groups[1].Value == "quarter" : null, lead.Success ? null : "no short version is drawn");

        // A stock a family holds a trade in: the short version, written or by code, opening on the trade, its short card,
        // the plan opening on the trade's own exits and the chart drawing its stop and target.
        var opensOnTheTrade = (lead.Success && lead.Groups[1].Value == "held") || page.Contains("<li data-summary-part=\"why\">Held on the trade ", StringComparison.Ordinal);
        var planHeld = HeldExit().IsMatch(page) && !page.Contains("data-exits=\"none\"", StringComparison.Ordinal);
        var planParagraph = ShortVersionPlan().IsMatch(page);
        var chartHeld = page.Contains("class=\"pick-levels\"", StringComparison.Ordinal);

        Add(
            "held-trade",
            heldOn ? opensOnTheTrade && planHeld && chartHeld && !planParagraph : null,
            !heldOn ? "no family holds a trade in it"
            : !opensOnTheTrade ? "the short version does not open on the held trade"
            : !planHeld ? "the plan does not open on the held trade's exits"
            : !chartHeld ? "the chart draws no stop and target for the held trade"
            : planParagraph ? "the short version draws its paragraph on the levels' plan beneath the held trade"
            : null);

        // Each paragraph of what worked and what did not, read as the page's own reader cuts its sentences.
        var worked = WhatWorked().Match(page);
        var longest = worked.Success
            ? ProseOf().Matches(worked.Groups[1].Value).Select(match => SentenceBreak().Split(WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, "<[^>]+>", " ")).Trim()).Length).DefaultIfEmpty(0).Max()
            : 0;

        Add("what-worked", worked.Success ? longest <= EquityBrief.Web.Marks.MarkRenderer.WhatWorkedSentences : null, !worked.Success ? "no written what worked and what did not is drawn" : longest > EquityBrief.Web.Marks.MarkRenderer.WhatWorkedSentences ? string.Create(CultureInfo.InvariantCulture, $"a paragraph of {longest} sentences is drawn") : null);

        var pickedBefore = page.Contains("data-card=\"on-the-list-before\"", StringComparison.Ordinal);

        Add("own-limit", pickedBefore ? page.Contains("or the end of its own setup's limit, ", StringComparison.Ordinal) : null, pickedBefore ? null : "the list picked it on no night before");

        var marks = EquityBrief.Core.Report.ConceptCheck.Drawn(page);
        var clashes = EquityBrief.Core.Report.ConceptCheck.Clashes(page);

        Add("one-value", marks.Count == 0 ? null : clashes.Count == 0, clashes.Count > 0 ? string.Join("; ", clashes.Select(clash => clash.Concept + ": " + string.Join(" and ", clash.Values))) : marks.Count == 0 ? "no concept is marked" : null);
        Add("header", Regex.IsMatch(page, "<span class=\"pill (?:live|closed)\">(?:LIVE|CLOSED|LAST CLOSE)</span>"));
        var sideEmpty = !page.Contains("data-role=\"resistance\"", StringComparison.Ordinal) || !page.Contains("data-role=\"support\"", StringComparison.Ordinal);

        Add("levels-older", sideEmpty ? page.Contains("<table class=\"older-bands\"", StringComparison.Ordinal) ? true : null : null, sideEmpty ? page.Contains("<table class=\"older-bands\"", StringComparison.Ordinal) ? null : "no band on a side and no pulled history turned there" : "the stored year holds bands on both sides");
        Add("pick-on-chart", picked || heldOn ? page.Contains("class=\"pick-levels\"", StringComparison.Ordinal) : null, picked || heldOn ? null : "no family picked or holds it");

        // The bands the page draws, stored and older, against the stop and the target the chart draws for the pick.
        var pickLevels = PickLevels().Match(page);
        var edges = BandEdges().Matches(page)
            .Select(band => (Low: decimal.Parse(band.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture), High: decimal.Parse(band.Groups[2].Value, NumberStyles.Number, CultureInfo.InvariantCulture)))
            .ToArray();
        decimal? Level(string name) => pickLevels.Success && decimal.TryParse(pickLevels.Groups[name].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var level) ? level : null;
        // Each level is reached by a band drawn, or the page says no band reaches it, one or the other and never both.
        var reachesTarget = Level("target") is not { } aim || edges.Any(edge => edge.High >= aim) != page.Contains("data-beyond=\"target\"", StringComparison.Ordinal);
        var reachesStop = Level("stop") is not { } floor || edges.Any(edge => edge.Low <= floor) != page.Contains("data-beyond=\"stop\"", StringComparison.Ordinal);

        Add(
            "levels-cover",
            pickLevels.Success ? reachesTarget && reachesStop : null,
            !pickLevels.Success ? "no family picked or holds it"
            : !reachesTarget ? (page.Contains("data-beyond=\"target\"", StringComparison.Ordinal) ? "the page says no band reaches the target of " + pickLevels.Groups["target"].Value + " where one drawn does" : "no band drawn reaches the target of " + pickLevels.Groups["target"].Value + " and the page does not say so")
            : !reachesStop ? (page.Contains("data-beyond=\"stop\"", StringComparison.Ordinal) ? "the page says no band reaches the stop of " + pickLevels.Groups["stop"].Value + " where one drawn does" : "no band drawn reaches the stop of " + pickLevels.Groups["stop"].Value + " and the page does not say so")
            : null);
        Add(
            "pick-exit",
            picked ? page.Contains("class=\"picks-exit\"", StringComparison.Ordinal) && !page.Contains("data-exits=\"none\"", StringComparison.Ordinal) : null,
            !picked ? "no family picked it"
            : !page.Contains("class=\"picks-exit\"", StringComparison.Ordinal) ? "the plan does not open on the pick's own exit"
            : page.Contains("data-exits=\"none\"", StringComparison.Ordinal) ? "the plan says it places no exit beside the pick's own"
            : null);
        Add("next-report", picked ? !page.Contains("No report date is on file for it, so a report inside the hold cannot be ruled out.", StringComparison.Ordinal) : null, picked ? null : "no family picked it");

        var notAnnounced = page.Contains("data-reportDate=\"reported\"", StringComparison.Ordinal);

        Add("next-report-estimated", notAnnounced ? page.Contains("data-reportDate=\"reported\" data-estimated=", StringComparison.Ordinal) : null, notAnnounced ? null : "the next report is not drawn as not announced");
        Add("rules-family", picked ? RulesOpenOnTheFamily().IsMatch(page) : null, picked ? null : "no family picked it");

        var trendFailed = page.Contains("not an uptrend", StringComparison.Ordinal);

        Add("trend-words", trendFailed ? page.Contains("class=\"trend-why\"", StringComparison.Ordinal) : null, trendFailed ? null : "no trend gate failed on the page");

        // The news region states how many stories it left out as another company's, and its counts add up over the stories
        // it kept: every story is an opinion or in the whole list, and every one in the whole list is positive, negative,
        // neutral or not labelled.
        var news = NewsRegion().Match(page);
        int Count(string name) => int.Parse(news.Groups[name].Value, CultureInfo.InvariantCulture);
        var leftOutStated = news.Success && news.Groups["left"].Success;
        var adds = leftOutStated
            && Count("articles") == Count("all") + Count("opinion")
            && Count("all") == Count("positive") + Count("negative") + Count("neutral") + Count("unlabelled");

        Add(
            "news-matched",
            news.Success ? adds : null,
            !news.Success ? "no news region is drawn" : !leftOutStated ? "the region states no count of the stories it left out" : adds ? null : "its counts do not add up over the stories it kept");

        // Every story the region keeps that admissibility admitted is labelled: none drawn as holding no label at all.
        var waiting = NewsRow().Matches(page).Count(row => row.Groups[1].Value == "unlabelled");

        Add("news-labelled", news.Success ? waiting == 0 : null, !news.Success ? "no news region is drawn" : waiting > 0 ? string.Create(CultureInfo.InvariantCulture, $"{waiting} admitted {(waiting == 1 ? "story holds" : "stories hold")} no label") : null);

        var group = PeerGroup().Match(page);

        Add("industry-label", group.Success ? group.Groups[2].Value.StartsWith("the provider's classification", StringComparison.Ordinal) || group.Groups[2].Value.StartsWith("the industry set for the stock", StringComparison.Ordinal) : null, group.Success ? null : "no peer group is drawn");

        // The group read against the page's own account of what the company sells, as the page reads it.
        var industry = group.Success ? WebUtility.HtmlDecode(group.Groups[1].Value) : null;
        var sells = SectionProse(page, EquityBrief.Web.Marks.MarkRenderer.WhatItSells);
        var business = industry is null || sells is null ? null : EquityBrief.Core.Report.IndustryCheck.Mismatch(industry, sells);
        var flagged = page.Contains("class=\"notice industry-mismatch\"", StringComparison.Ordinal);

        Add(
            "industry-mismatch",
            industry is null || sells is null ? null : (business is not null) == flagged,
            industry is null ? "no peer group is drawn"
            : sells is null ? "the page draws no account of what the company sells"
            : (business is not null) == flagged ? null
            : business is not null ? "the group " + industry + " names none of " + business + " and no flag is drawn"
            : "a flag is drawn where the group names the business");

        // The sources list held to the documents naming the company or its industry, saying where fewer than two remain.
        var sources = SourcesRegion().Match(page);
        var tested = sources.Success && sources.Groups["tested"].Value == "tested";
        var kept = tested ? int.Parse(sources.Groups["kept"].Value, CultureInfo.InvariantCulture) : 0;
        var few = tested && kept + int.Parse(sources.Groups["left"].Value, CultureInfo.InvariantCulture) > 0 && kept < EquityBrief.Core.Report.IndustryCheck.FewestSources;
        var saysFew = page.Contains("data-documents=\"few\">Not enough relevant sources: ", StringComparison.Ordinal);

        Add("sources-relevant", tested ? few == saysFew : null, !sources.Success ? "no sources list is drawn" : !tested ? "the stock's industry is not known, so its sources are held to nothing" : few != saysFew ? "fewer than two relevant documents remain and the list does not say so" : null);

        // A release reports a quarter newer than the card's where it was accepted after the end of the quarter that follows
        // the card's own; one accepted before that is the card's own quarter's, filed beside it.
        var quarterCard = QuarterCard().Match(page);
        var newestRelease = ReleaseDay().Matches(page).Select(match => match.Groups[1].Value).DefaultIfEmpty(string.Empty).Max(StringComparer.Ordinal)!;
        string? following = null;

        if (QuarterTitle().Match(page) is { Success: true } titled)
        {
            var next = DateOnly.ParseExact(titled.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(3);

            following = new DateOnly(next.Year, next.Month, DateTime.DaysInMonth(next.Year, next.Month)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var newer = following is not null && string.CompareOrdinal(newestRelease, following) > 0;

        Add(
            "newest-quarter",
            !quarterCard.Success || following is null ? null : quarterCard.Groups[1].Value == "Reported" || !newer,
            !quarterCard.Success ? "no latest quarter is drawn"
            : following is null ? "the latest quarter names no quarter"
            : quarterCard.Groups[1].Value == "Filed" && newer ? "a results release of " + newestRelease + ", after the quarter to " + following + " ended, is newer than the quarter the filing of " + quarterCard.Groups[2].Value + " reports" : null);

        // A quarter reported before its filing is stored draws the release's own segment table first where it reads, and a
        // segment commentary beneath it written from the filing's segments says so.
        var fromRelease = quarterCard.Success && quarterCard.Groups[1].Value == "Reported";
        var releaseTable = page.Contains("<div class=\"sub segments-release\"", StringComparison.Ordinal);
        var unsaid = page.Contains("data-written-from=\"filing\"", StringComparison.Ordinal) && !page.Contains("<p class=\"degraded written-from\">", StringComparison.Ordinal);

        Add(
            "segments-release",
            fromRelease && releaseTable ? !unsaid : null,
            !fromRelease ? "the latest quarter is the filing's"
            : !releaseTable ? "the results release states no segment table summing to the quarter's reported revenue"
            : unsaid ? "a segment commentary written from the filing's segments does not say so"
            : null);

        var unwritten = Unwritten().Matches(page)
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
            .Where(section => section != EquityBrief.Core.Research.ClaimRules.CycleSection)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var bare = unwritten.FirstOrDefault(section => !page.Contains("<form class=\"research-control section-again\" method=\"post\" action=\"/passes/" + Uri.EscapeDataString(ticker) + "\" data-kind=\"section\" data-section=\"" + WebUtility.HtmlEncode(section) + "\"", StringComparison.Ordinal));

        Add(
            "rerun-alone",
            unwritten.Length == 0 ? null : bare is null,
            unwritten.Length == 0 ? "no section of the name's own is left out or not written" : bare is null ? null : bare + " is drawn with no control writing it again alone");
        Add("model-labels", page.Contains("<table class=\"model-jobs\"", StringComparison.Ordinal) || page.Contains("class=\"model-jobs\" data-jobs=\"0\"", StringComparison.Ordinal));

        return new ProofPage(
            ticker,
            verdicts,
            industry is not null && business is not null ? new PeerProposal(ticker, industry, business, EquityBrief.Core.Report.IndustryCheck.Proposed(business)) : null);
    }

    // Every rule's counts over the pages, in the rules' order.
    public static IReadOnlyList<ProofRule> Counted(IReadOnlyList<ProofPage> pages) =>
    [
        .. Rules.Select(rule =>
        {
            var answers = pages.Select(page => (page.Ticker, Verdict: page.Verdicts.FirstOrDefault(verdict => verdict.Rule == rule.Rule)?.Verdict ?? Unexamined)).ToArray();

            return new ProofRule(
                rule.Rule,
                rule.Says,
                answers.Count(answer => answer.Verdict == Passed),
                answers.Count(answer => answer.Verdict == Failed),
                answers.Count(answer => answer.Verdict == Unexamined),
                [.. answers.Where(answer => answer.Verdict == Failed).Select(answer => answer.Ticker)]);
        }),
    ];

    // The report as one page to read: each rule with its counts and the pages it failed on.
    public static string Html(ProofReport report)
    {
        var html = new StringBuilder();

        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Report page proof</title>");
        html.Append("<style>:root{--ink:#1c1c1c;--paper:#fbfaf7;--rule:#d8d8d8;--up:#2f7d4f;--down:#b23b3b;--muted:#6a6a6a}@media (prefers-color-scheme: dark){:root{--ink:#ecebe7;--paper:#161616;--rule:#3a3a3a;--up:#5fb382;--down:#e07070;--muted:#a0a0a0}}body{background:var(--paper);color:var(--ink);font:15px/1.5 system-ui,sans-serif;margin:0;padding:16px}main{max-width:1000px;margin:auto}table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid var(--rule);padding:6px 8px;text-align:left;vertical-align:top}td.n{text-align:right;font-variant-numeric:tabular-nums}.pass{color:var(--up)}.fail{color:var(--down)}.unexamined{color:var(--muted)}.wrap{overflow-x:auto}</style></head><body><main>");
        html.Append(string.Create(CultureInfo.InvariantCulture, $"<h1>Report page proof</h1><p>{report.Pages.Count} pages of the {WebUtility.HtmlEncode(report.Index)} drawn as the read surface draws them{(report.Night is { } night ? string.Create(CultureInfo.InvariantCulture, $" for the night of {night:yyyy-MM-dd}") : string.Empty)}, read at {report.ReadAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC. A rule that cannot be read on a page is unexamined there, and unexamined is never counted as a pass.</p>"));
        html.Append("<div class=\"wrap\"><table><thead><tr><th>Rule</th><th>Passed</th><th>Failed</th><th>Unexamined</th><th>Failed on</th></tr></thead><tbody>");

        foreach (var rule in report.Rules)
        {
            html.Append(string.Create(CultureInfo.InvariantCulture, $"<tr data-rule=\"{rule.Rule}\"><td><b>{WebUtility.HtmlEncode(rule.Rule)}</b><br>{WebUtility.HtmlEncode(rule.Says)}</td><td class=\"n pass\">{rule.Passed}</td><td class=\"n fail\">{rule.Failed}</td><td class=\"n unexamined\">{rule.Unexamined}</td><td>{WebUtility.HtmlEncode(string.Join(", ", rule.Failing.Take(40)))}{(rule.Failing.Count > 40 ? string.Create(CultureInfo.InvariantCulture, $" and {rule.Failing.Count - 40} more") : string.Empty)}</td></tr>"));
        }

        html.Append("</tbody></table></div>");

        // Every page whose peer group names none of the business its own account describes, with the group proposed for
        // it: a list for the operator to approve before any override is set, which the proof sets none of.
        // see: A stock's industry group is the provider's until an override is set and is checked against its own account, and the sources list and the cycle read only documents naming the company or its industry in their opening
        var proposals = report.Pages.Select(page => page.Peer).OfType<PeerProposal>().ToArray();

        html.Append(string.Create(CultureInfo.InvariantCulture, $"<h2>Peer groups to approve</h2><p data-proposals=\"{proposals.Length}\">{proposals.Length} {(proposals.Length == 1 ? "page's peer group names" : "pages' peer groups name")} none of the business the page's own account of what the company sells describes. Each is listed with the group proposed from that account, and no override is set until you approve it.</p>"));

        if (proposals.Length > 0)
        {
            html.Append("<div class=\"wrap\"><table><thead><tr><th>Stock</th><th>Its group</th><th>The business its account describes</th><th>The group proposed</th></tr></thead><tbody>");

            foreach (var proposal in proposals)
            {
                html.Append(string.Create(CultureInfo.InvariantCulture, $"<tr data-proposal=\"{WebUtility.HtmlEncode(proposal.Ticker)}\"><td>{WebUtility.HtmlEncode(proposal.Ticker)}</td><td>{WebUtility.HtmlEncode(proposal.Group)}</td><td>{WebUtility.HtmlEncode(proposal.Business)}</td><td>{WebUtility.HtmlEncode(proposal.Proposed)}</td></tr>"));
            }

            html.Append("</tbody></table></div>");
        }

        html.Append("</main></body></html>");

        return html.ToString();
    }

    // The first written abbreviation drawn without its words before it, none where every one is spelled out.
    static string? Unspelled(string prose)
    {
        foreach (var (abbreviation, words) in EquityBrief.Core.Research.Abbreviations.Spelled)
        {
            var first = Regex.Match(prose, $@"(?<![A-Za-z0-9&]){Regex.Escape(abbreviation)}(?![A-Za-z0-9&])");

            if (first.Success && !prose[..first.Index].Contains(words, StringComparison.OrdinalIgnoreCase))
            {
                return abbreviation + " is drawn before its words";
            }
        }

        return null;
    }

    [GeneratedRegex("<p class=\"prose[^\"]*\">(.*?)</p>", RegexOptions.Singleline)]
    private static partial Regex ProseOf();

    [GeneratedRegex(@"\[(?:N|D\d+|C\d+)\]")]
    private static partial Regex MarkerOf();

    // The plan opening on the exits of a trade a family holds the stock on.
    [GeneratedRegex("<p class=\"picks-exit\"[^>]*data-held=\"")]
    private static partial Regex HeldExit();

    // A paragraph of the written short version opening on the levels' plan, which a pick's or a held trade's page leaves to
    // the plan region.
    [GeneratedRegex("data-section=\"The short version\"[^>]*>(?:(?!</section>).)*?<p class=\"prose[^\"]*\">The plan ", RegexOptions.Singleline)]
    private static partial Regex ShortVersionPlan();

    // The short version's what worked and what did not, its label and the prose after it in its section.
    [GeneratedRegex("<p class=\"short-part-label\"><b>What worked and what did not\\.</b></p>(.*?)</section>", RegexOptions.Singleline)]
    private static partial Regex WhatWorked();

    // The stop and the target the chart draws for a pick or a held trade.
    [GeneratedRegex("<g class=\"pick-levels\"[^>]*data-pick-stop=\"(?<stop>[^\"]+)\" data-pick-target=\"(?<target>[^\"]+)\"")]
    private static partial Regex PickLevels();

    // Every band the levels region draws, stored and older, by its edges.
    [GeneratedRegex("<tr class=\"band(?: older)?\"[^>]*?data-low-edge=\"([0-9.]+)\" data-high-edge=\"([0-9.]+)\"")]
    private static partial Regex BandEdges();

    // Each story row the news region draws, by its state.
    [GeneratedRegex("<li class=\"news-row\" data-article=\"[^\"]+\" data-state=\"([^\"]+)\"")]
    private static partial Regex NewsRow();

    // The sources region: how many documents it lists, how many it folds apart and whether their relevance was tested.
    [GeneratedRegex("<section class=\"sources-region\"[^>]*?data-documents=\"(?<kept>\\d+)\" data-left-out=\"(?<left>\\d+)\" data-relevance=\"(?<tested>[a-z]+)\"")]
    private static partial Regex SourcesRegion();

    // A document's label a writer used as a word, D1 for the first document a section cites.
    [GeneratedRegex(@"(?<![A-Za-z0-9\[\-])D\d{1,2}(?![A-Za-z0-9\]\-])")]
    private static partial Regex BareLabelOf();

    // A move's written cause as the moves table draws it, in the column the cause section fills or in a row whose cause the
    // research wrote; a story's title or a filing a row names is the document's own and none of the page's writing.
    [GeneratedRegex("<td class=\"cause\" data-cause=\"written\"[^>]*>(?<said>.*?)</td>|data-cause=\"research\">(?:(?!</tr>).)*?<td class=\"cause\"[^>]*>(?<said>.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex CauseOf();

    // A move's written cause held to the stored directions, with the directions and its words.
    [GeneratedRegex("<td class=\"cause\"[^>]*?data-directions=\"([^\"]*)\"[^>]*>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex HeldCause();

    [GeneratedRegex("<div class=\"short-lead\"><p class=\"short-part\" data-part=\"([a-z]+)\"")]
    private static partial Regex ShortLead();

    // The rules card's body opening on the picking family's own checks.
    [GeneratedRegex("data-card=\"rules\"><div class=\"card-h\">.*?</div><div class=\"card-b\"><section class=\"picked-by\"", RegexOptions.Singleline)]
    private static partial Regex RulesOpenOnTheFamily();

    // The latest quarter's card: whether it is dated as filed or as reported, and its day.
    [GeneratedRegex("id=\"quarter\"><div class=\"spine\">.*?<span class=\"dl-k\">(Filed|Reported)</span><b>(\\d{4}-\\d{2}-\\d{2})</b>", RegexOptions.Singleline)]
    private static partial Regex QuarterCard();

    // A written section's drawn prose, joined, none where the page draws no such section or it holds no prose.
    static string? SectionProse(string page, string section)
    {
        var drawn = Regex.Match(page, "<section class=\"written-section\"[^>]*data-section=\"" + Regex.Escape(WebUtility.HtmlEncode(section)) + "\"[^>]*>(.*?)</section>", RegexOptions.Singleline);
        var prose = drawn.Success
            ? string.Join(" ", ProseOf().Matches(drawn.Groups[1].Value).Select(match => WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, "<[^>]+>", " ")))).Trim()
            : string.Empty;

        return prose.Length == 0 ? null : prose;
    }

    // A written section held to the stored directions, with the directions it was held to and its body.
    [GeneratedRegex("<section class=\"written-section\"[^>]*?data-directions=\"([^\"]*)\"[^>]*>(.*?)</section>", RegexOptions.Singleline)]
    private static partial Regex HeldSection();

    // Where a sentence ends and the next begins, as the page's own reader cuts a section's prose.
    [GeneratedRegex("(?<=[.!?][\"”']?)\\s+(?=[A-Z\"“])")]
    private static partial Regex SentenceBreak();

    // The news region's counts and the count of the stories it left out.
    [GeneratedRegex("<div class=\"news-region\"[^>]*?data-articles=\"(?<articles>\\d+)\" data-all=\"(?<all>\\d+)\" data-positive=\"(?<positive>\\d+)\" data-negative=\"(?<negative>\\d+)\" data-neutral=\"(?<neutral>\\d+)\" data-opinion=\"(?<opinion>\\d+)\" data-unlabelled=\"(?<unlabelled>\\d+)\"(?: data-left-out=\"(?<left>\\d+)\")?")]
    private static partial Regex NewsRegion();

    // The peer group's label: the group and what the page says it is.
    [GeneratedRegex("<div class=\"sub\">Against (.+?)'s S&amp;P 500 members on the night, ([^<]*)</div>")]
    private static partial Regex PeerGroup();

    // Each section the research region draws as left out or not written.
    [GeneratedRegex("<p class=\"(?:left-out|not-written)\" data-section=\"([^\"]+)\">")]
    private static partial Regex Unwritten();

    // The quarter the latest quarter's card reads.
    [GeneratedRegex("<h2>The quarter to (\\d{4}-\\d{2}-\\d{2}), line by line</h2>")]
    private static partial Regex QuarterTitle();

    // Each results release the page draws management's words from, by the day it was filed or accepted.
    [GeneratedRegex("(?:data-guidance=\"[^\"]*\"[^>]*?data-filed|class=\"guidance release-opening\"[^>]*?data-accepted)=\"(\\d{4}-\\d{2}-\\d{2})\"")]
    private static partial Regex ReleaseDay();
}
