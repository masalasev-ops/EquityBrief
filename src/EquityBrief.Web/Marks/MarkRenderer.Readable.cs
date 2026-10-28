using System.Text.RegularExpressions;
using EquityBrief.Core.Research;

namespace EquityBrief.Web.Marks;

// A written section as a reader reads it: each sentence held to the directions the stored latest quarter states, a margin's
// or a year's change the figures contradict withheld with what the figures show and a beat or a miss another source states
// against them given the figures' side beside it; its bracketed markers taken out; and each abbreviation spelled out where
// it is first used. The stored prose is never changed.
// see: A written sentence stating a direction the stored latest quarter contradicts is withheld where it is drawn, and a beat or a miss another source states against it is named beside it
// see: A written section is drawn without its bracketed markers, its sources listed by title beneath it and every abbreviation spelled out where first used
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

    public static string Readable(string prose, IReadOnlyList<DirectionFact>? facts)
    {
        var paragraphs = prose.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var read = new List<string>(paragraphs.Length);

        // Sentences are cut with their markers out, since a marker standing after a sentence's full stop would otherwise
        // join it to the next and a run of them would be read as one sentence.
        foreach (var paragraph in paragraphs)
        {
            var sentences = Sentence().Split(Unmarked(paragraph));

            read.Add(string.Join(" ", sentences.Select(sentence => Held(sentence, facts ?? []))));
        }

        return Abbreviations.SpelledOut(Unmarked(string.Join("\n\n", read)));
    }

    // One sentence held to the stored directions.
    static string Held(string sentence, IReadOnlyList<DirectionFact> facts)
    {
        var clashes = Directions.Clashes(Unmarked(sentence), facts);

        if (clashes.Count == 0)
        {
            return sentence;
        }

        var contradicted = clashes.Where(clash => clash.Fact.Kind != Directions.Estimate).ToArray();

        if (contradicted.Length > 0)
        {
            var opener = Openers.FirstOrDefault(words => sentence.StartsWith(words, StringComparison.Ordinal))
                ?? (RiskOpener().Match(sentence) is { Success: true } risk ? risk.Value : null);

            return (opener is null ? string.Empty : opener + ": ") + Directions.Withheld(contradicted);
        }

        // A beat or a miss another source states against the stored estimate keeps its sentence and is given the stored
        // side beside it, so the two sources are both named.
        return Unmarked(sentence).TrimEnd('.', ' ') + "; "
            + string.Join("; ", clashes.Select(clash => "the estimate the page draws puts the " + clash.Fact.Concept + " at " + clash.Fact.Stated.Replace(" against the estimate the page draws", string.Empty, StringComparison.Ordinal) + ", a " + (clash.Fact.Sign > 0 ? "beat" : "miss")))
            + ".";
    }

    // The prose with its bracketed markers taken out, the space before each with it.
    public static string Unmarked(string prose) => Marker().Replace(prose, string.Empty);

    // The short version's lead, written by code before its prose: for a pick, the setup that picked it, the buy, the stop,
    // the target and the last day; then the latest quarter's revenue and earnings a share against their estimates and a
    // year earlier, and its operating margin against a year earlier, in short labelled paragraphs.
    // see: The short version opens on the pick and then the quarter as code writes them, before what worked and what did not
    public static string ShortVersionLead(DecisionCardView? pick, EquityBrief.Core.Report.LatestQuarter? quarter)
    {
        var lead = new System.Text.StringBuilder("<div class=\"short-lead\">");

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

    // The words a risk opens on with its place, which the risks are cut into parts at.
    [GeneratedRegex(@"^(?:The|A) (?:first|second|third|fourth|fifth|sixth|clearest|biggest|main) risk")]
    private static partial Regex RiskOpener();
}
