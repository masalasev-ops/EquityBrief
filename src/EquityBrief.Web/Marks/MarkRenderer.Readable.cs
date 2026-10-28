using System.Text.RegularExpressions;
using EquityBrief.Core.Research;

namespace EquityBrief.Web.Marks;

// A written section as a reader reads it: each sentence held to the directions the stored latest quarter states, one the
// figures contradict withheld with what the figures show, a beat or a miss another source counted against its own
// consensus named beside the stored one in that one sentence; its bracketed markers taken out and a document's label the
// writer used as a word drawn as the kind of document it names; and each abbreviation spelled out where it is first used.
// The stored prose is never changed.
// see: A written sentence the stored latest quarter contradicts is withheld where it is drawn, and two sources disagreeing on a beat or a miss are named in one sentence in its place
// see: A written section is drawn without its bracketed markers, its sources listed by title beneath it and every abbreviation spelled out where first used
// see: A document's label a writer used as a word is drawn as the kind of document it names, in a section and in a move's cause alike
public sealed partial class MarkRenderer
{
    // The words a written section's parts open on, which a withheld sentence keeps so the part is still read as itself.
    static readonly string[] Openers =
    [
        ClaimRules.CaseFor,
        ClaimRules.CaseAgainst,
        ClaimRules.GuidanceOpens,
        ClaimRules.WorkingOpens,
        ClaimRules.NotWorkingOpens,
    ];

    public static string Readable(string prose, IReadOnlyList<DirectionFact>? facts, Func<int, string>? documents = null)
    {
        var paragraphs = prose.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var read = new List<string>(paragraphs.Length);

        // Sentences are cut with their markers out, since a marker standing after a sentence's full stop would otherwise
        // join it to the next and a run of them would be read as one sentence.
        foreach (var paragraph in paragraphs)
        {
            var sentences = Sentence().Split(Named(Unmarked(paragraph), documents));

            read.Add(string.Join(" ", sentences.Select(sentence => Held(sentence, facts ?? []))));
        }

        return Abbreviations.SpelledOut(Unmarked(string.Join("\n\n", read)));
    }

    // One sentence held to the stored directions: one they contradict is withheld, a beat or a miss its own source counted
    // against another consensus among them, since the page draws the stored side and the two sources are named in its place.
    static string Held(string sentence, IReadOnlyList<DirectionFact> facts)
    {
        var clashes = Directions.Clashes(Unmarked(sentence), facts);

        if (clashes.Count == 0)
        {
            return sentence;
        }

        var opener = Openers.FirstOrDefault(words => sentence.StartsWith(words, StringComparison.Ordinal))
            ?? (RiskOpener().Match(sentence) is { Success: true } risk ? risk.Value : null);

        return (opener is null ? string.Empty : opener + ": ") + Directions.Withheld(clashes);
    }

    // The prose with its bracketed markers taken out, the space before each with it.
    public static string Unmarked(string prose) => Marker().Replace(prose, string.Empty);

    // The prose with each document's label the writer used as a word, D1 for the first document it cites, drawn as the kind
    // of document it names, the documents read by their place in the section's own list and a cited document where none is.
    public static string Named(string prose, Func<int, string>? documents) =>
        BareLabel().Replace(prose, label =>
        {
            var noun = documents?.Invoke(int.Parse(label.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)) ?? DocumentNoun(null);

            return label.Index == 0 || SentenceEnds().IsMatch(prose[..label.Index]) ? Capitalised(noun) : noun;
        });

    // What kind of document one is, in the words a sentence names it by: a filing by its form, a results release, or an
    // article.
    public static string DocumentNoun(SourceCell? document)
    {
        if (document is null)
        {
            return "a cited document";
        }

        var said = document.Title + " " + document.Url;

        return Regex.IsMatch(said, @"\b10-?Q\b", RegexOptions.IgnoreCase) ? "the quarterly filing"
            : Regex.IsMatch(said, @"\b10-?K\b", RegexOptions.IgnoreCase) ? "the annual filing"
            : document.Title.StartsWith("Previous results release", StringComparison.Ordinal) ? "the previous results release"
            : document.Title.StartsWith("Results release", StringComparison.Ordinal) || Regex.IsMatch(said, @"\bex-?99", RegexOptions.IgnoreCase) ? "the results release"
            : document.Url.Contains("sec.gov", StringComparison.OrdinalIgnoreCase) ? "a filing"
            : "a cited article";
    }


    // The short version's lead, written by code before its prose: for a pick, the setup that picked it, the buy, the stop,
    // the target and the last day; then the latest quarter's revenue and earnings a share against their estimates and a
    // year earlier, and its operating margin against a year earlier, in short labelled paragraphs.
    // see: The short version opens on the pick and then the quarter as code writes them, before what worked and what did not
    // see: A stock held on a family's open trade is drawn with that trade first, its short card and its own exits on the plan and the chart
    public static string ShortVersionLead(DecisionCardView? pick, EquityBrief.Core.Report.LatestQuarter? quarter, HeldTradeView? held = null)
    {
        var lead = new System.Text.StringBuilder("<div class=\"short-lead\">");

        // A stock the night lists under no family but one holds a trade in opens on that trade: its setup and night, the
        // fill, its exits with the last day its own limit gives it, where it stands and its reward to risk at the fill.
        if (pick is null && held is { } holding)
        {
            var heldCard = holding.Card;
            var trade = holding.Pick;
            var parts = new List<string>
            {
                trade.Buy is { } fill ? Formatted($"bought at {Price(fill)}") + (trade.FilledOn is { } on ? FormattableString.Invariant($" at the open of {on:d MMM yyyy}") : string.Empty) : "it buys at the next open",
            };
            var exits = new List<string>();

            if (heldCard.Target is { } heldTarget)
            {
                exits.Add(Formatted($"at {Price(heldTarget)}"));
            }

            if (heldCard.Stop is { } heldStop)
            {
                exits.Add(Formatted($"at a close below {Price(heldStop)}"));
            }

            if (heldCard.Cap is { } heldCap)
            {
                exits.Add(Formatted($"after {heldCap} trading days") + (heldCard.Hits is { } heldHits ? FormattableString.Invariant($", about {heldHits.Through:d MMM yyyy}") : string.Empty));
            }

            if (exits.Count > 0)
            {
                parts.Add("sold " + (exits.Count == 1 ? exits[0] : string.Join(", ", exits.Take(exits.Count - 1)) + " or " + exits[^1]) + ", whichever comes first");
            }

            parts.Add(PickStatus.Words(trade.Status).ToLowerInvariant() + (trade.Sessions is { } sessions ? Formatted($" after {sessions} {(sessions == 1 ? "session" : "sessions")}") : string.Empty));

            if (holding.RewardToRisk is { } ratio)
            {
                parts.Add(Formatted($"its reward to risk at the fill {ratio:0.00}"));
            }

            lead.Append(FormattableString.Invariant($"<p class=\"short-part\" data-part=\"held\"><b>The held trade.</b> The {Escaped(SetupOf(heldCard.Family))} picked it on {trade.Night:d MMM yyyy} and holds it: {Escaped(string.Join("; ", parts))}.</p>"));
        }

        if (pick is { } card)
        {
            var parts = new List<string>
            {
                card.Entry is { } entry ? Formatted($"buy at the next open, at about {Price(entry)}") : "buy at the next open",
            };

            if (card.Stop is { } stop)
            {
                parts.Add(Formatted($"sell at a close below {Price(stop)}"));
            }

            if (card.Target is { } target)
            {
                parts.Add(Formatted($"sell at {Price(target)}"));
            }

            if (card.Cap is { } cap)
            {
                parts.Add(Formatted($"otherwise sell after {cap} trading days") + (card.Hits is { } hits ? FormattableString.Invariant($", about {hits.Through:d MMM yyyy}") : string.Empty));
            }

            lead.Append(FormattableString.Invariant($"<p class=\"short-part\" data-part=\"pick\"><b>The pick.</b> The {Escaped(SetupOf(card.Family))} picked it on {card.Night:d MMM yyyy}: {Escaped(string.Join("; ", parts))}.</p>"));
        }

        if (quarter is { } latest)
        {
            EquityBrief.Core.Report.QuarterLine? Line(string name) => latest.Lines.FirstOrDefault(line => string.Equals(line.Line, name, StringComparison.OrdinalIgnoreCase));

            var said = new List<string>();

            if (Line("Revenue") is { Reported: { } revenue } sales)
            {
                said.Add("revenue " + Figures.Money(revenue) + Against(sales));
            }

            if (Line("Earnings a share") is { Reported: { } eps } perShare)
            {
                said.Add("earnings a share " + Figures.PerShare(eps) + Against(perShare));
            }

            if (Line("Operating income") is { Margin: { } margin, MarginYearEarlier: { } before })
            {
                said.Add(Formatted($"an operating margin of {margin:0.0}% against {before:0.0}% a year earlier"));
            }

            if (said.Count > 0)
            {
                lead.Append(FormattableString.Invariant($"<p class=\"short-part\" data-part=\"quarter\"><b>The quarter.</b> In the quarter to {latest.Quarter:yyyy-MM-dd}{(latest.Reported is { } reported ? FormattableString.Invariant($", reported {reported:yyyy-MM-dd}") : string.Empty)}: {Escaped(string.Join("; ", said))}.</p>"));
            }
        }

        return lead.Length == "<div class=\"short-lead\">".Length ? string.Empty : lead.Append("</div>").ToString();

        static string Against(EquityBrief.Core.Report.QuarterLine line) =>
            (line.AgainstEstimate is { } against ? Formatted($", {(against >= 0 ? "+" : string.Empty)}{against:0.0}% against its estimate") : string.Empty)
            + (line.OnTheYear is { } year ? Formatted($" and {(year >= 0 ? "+" : string.Empty)}{year:0.0}% on a year earlier") : string.Empty);
    }

    [GeneratedRegex(@"(?<=[.!?][""”']?)\s+(?=[A-Z""“])")]
    private static partial Regex Sentence();

    [GeneratedRegex(@"\s*\[(?:N|D\d+|C\d+)\]")]
    private static partial Regex Marker();

    [GeneratedRegex(@"(?<![A-Za-z0-9\[\-])D(\d{1,2})(?![A-Za-z0-9\]\-])")]
    private static partial Regex BareLabel();

    [GeneratedRegex(@"[.!?][""”']?\s+$")]
    private static partial Regex SentenceEnds();

    // The words a risk opens on with its place, which the risks are cut into parts at.
    [GeneratedRegex(@"^(?:The|A) (?:first|second|third|fourth|fifth|sixth|clearest|biggest|main) risk")]
    private static partial Regex RiskOpener();
}
