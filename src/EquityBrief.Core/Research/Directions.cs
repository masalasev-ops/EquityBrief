using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Core.Report;

namespace EquityBrief.Core.Research;

// One direction the stored latest quarter states: what it is about, the kind of comparison, its sign, and the figures it
// rests on in words. A margin is wider or narrower than a year earlier, a line beat or missed its estimate, and a line
// rose or fell on a year earlier.
public sealed record DirectionFact(string Concept, string Kind, int Sign, string Stated);

// A sentence's direction the stored figures contradict: the fact it contradicts and what the sentence said.
public sealed record DirectionClash(DirectionFact Fact, string Said);

// Every sentence of a written section held to the directions the stored latest quarter states: a sentence saying a margin
// was narrower where the stored margins are wider, that a line missed where it beat its estimate, or that it fell where it
// rose on a year earlier, is one the figures contradict. A sentence naming the stored direction beside another source's
// contradicts nothing; a sentence about a segment, an adjusted figure or a period other than the latest quarter's is not
// read, since the stored lines are the company's whole quarter.
// see: A written sentence the stored latest quarter contradicts is withheld where it is drawn, and two sources disagreeing on a beat or a miss are named in one sentence in its place
public static partial class Directions
{
    public const string Margin = "margin";
    public const string Estimate = "estimate";
    public const string Year = "year";

    // The directions the latest quarter's lines state, each read from its own line.
    public static IReadOnlyList<DirectionFact> Of(LatestQuarter? quarter)
    {
        if (quarter is null)
        {
            return [];
        }

        var facts = new List<DirectionFact>();

        QuarterLine? Line(string name) => quarter.Lines.FirstOrDefault(line => string.Equals(line.Line, name, StringComparison.OrdinalIgnoreCase));

        foreach (var (line, concept) in new[] { ("Gross profit", "gross margin"), ("Operating income", "operating margin"), ("Net income", "net margin") })
        {
            if (Line(line) is { Margin: { } now, MarginYearEarlier: { } before } && Math.Round(now, 1) != Math.Round(before, 1))
            {
                facts.Add(new DirectionFact(concept, Margin, Math.Sign(now - before), Invariant($"{now:0.0}% against {before:0.0}% a year earlier")));
            }
        }

        foreach (var (line, concept) in new[] { ("Revenue", "revenue"), ("Earnings a share", "earnings a share") })
        {
            if (Line(line) is { AgainstEstimate: { } against } && Math.Abs(against) >= 0.05)
            {
                facts.Add(new DirectionFact(concept, Estimate, Math.Sign(against), Invariant($"{(against > 0 ? "+" : string.Empty)}{against:0.0}% against the estimate the page draws")));
            }
        }

        foreach (var (line, concept) in new[] { ("Revenue", "revenue"), ("Net income", "net income"), ("Earnings a share", "earnings a share"), ("Operating cash flow", "operating cash flow"), ("Free cash flow", "free cash flow") })
        {
            if (Line(line) is { OnTheYear: { } change } && Math.Abs(change) >= 0.05)
            {
                facts.Add(new DirectionFact(concept, Year, Math.Sign(change), Invariant($"{(change > 0 ? "+" : string.Empty)}{change:0.0}% on a year earlier")));
            }
        }

        return facts;
    }

    // The directions a section written on a day is held to: the latest quarter's where it was written on or after the day
    // that quarter was reported, and none where it was written before, since it then speaks of an earlier quarter.
    public static IReadOnlyList<DirectionFact> For(LatestQuarter? quarter, DateOnly? writtenOn) =>
        quarter is { Reported: { } reported } && writtenOn is { } written && written < reported ? [] : Of(quarter);

    // Each direction a sentence states that the facts contradict.
    public static IReadOnlyList<DirectionClash> Clashes(string sentence, IReadOnlyList<DirectionFact> facts)
    {
        if (facts.Count == 0 || NotTheWholeQuarter().IsMatch(sentence))
        {
            return [];
        }

        var clashes = new List<DirectionClash>();

        foreach (var fact in facts)
        {
            // The direction word stands within a few words of the concept it is said of: a margin's or a year's change in
            // the clause that names it, and a beat or a miss within the clause naming the estimate.
            var reach = fact.Kind switch { Margin => 6, Estimate => 12, _ => 4 };
            var said = Regex.Matches(sentence, Concept(fact.Concept) + @"(?:\W+\w+){0," + reach.ToString(CultureInfo.InvariantCulture) + @"}?\W+(?<word>" + Words(fact.Kind) + @")\b", RegexOptions.IgnoreCase)
                .Select(match => (Text: match.Value, Sign: SignOf(fact.Kind, match.Groups["word"].Value)))
                .ToList();

            // A beat or a miss is read only where the sentence names an estimate, and two sources disagreeing on one are
            // named: a sentence stating the stored direction of the same line beside the other's contradicts nothing.
            if (fact.Kind == Estimate && (!EstimateNamed().IsMatch(sentence) || said.Any(one => one.Sign == fact.Sign)))
            {
                continue;
            }

            clashes.AddRange(said.Where(one => one.Sign == -fact.Sign).Select(one => new DirectionClash(fact, one.Text)));
        }

        return clashes;
    }

    // The words a withheld sentence's place opens on, which say what it said and what the figures show and so state the
    // contradicted direction themselves.
    public const string WithheldOpens = "A sentence is withheld here";

    // What a sentence the figures contradict is replaced by where it is drawn, in one sentence: a margin's or a year's change
    // as what it said against what the figures show, and a beat or a miss as the two sources it sets against each other,
    // the sentence's own counting the line against its consensus and the stored figures against the estimate the page draws.
    public static string Withheld(IReadOnlyList<DirectionClash> clashes)
    {
        var changes = clashes.Where(clash => clash.Fact.Kind != Estimate).ToArray();
        var counted = clashes.Where(clash => clash.Fact.Kind == Estimate).ToArray();
        var parts = new List<string>();

        if (changes.Length > 0)
        {
            parts.Add("it said " + string.Join(" and ", changes.Select(clash => Invariant($"the {clash.Fact.Concept}{(clash.Fact.Kind == Margin ? " was" : string.Empty)} {Said(clash.Fact.Kind, -clash.Fact.Sign)}"))) + ", where the stored figures show " + string.Join(" and ", changes.Select(clash => Invariant($"{clash.Fact.Stated}, {Said(clash.Fact.Kind, clash.Fact.Sign)}"))));
        }

        if (counted.Length > 0)
        {
            parts.Add(string.Join(" and ", counted.Select(clash => Invariant($"its source counted the {clash.Fact.Concept} {(clash.Fact.Sign > 0 ? "a miss" : "a beat")} against its own consensus, where the stored figures put the {clash.Fact.Concept} at {clash.Fact.Stated}, {(clash.Fact.Sign > 0 ? "a beat" : "a miss")}"))));
        }

        return WithheldOpens + ": " + string.Join("; and ", parts) + ".";
    }

    static string Said(string kind, int sign) => kind switch
    {
        Margin => sign > 0 ? "wider" : "narrower",
        Estimate => sign > 0 ? "beat its estimate" : "missed its estimate",
        _ => sign > 0 ? "rose" : "fell",
    };

    static string Concept(string concept) => concept switch
    {
        "revenue" => @"\b(?<!organic\s)(?<!segment\s)(?:revenue|revenues|sales)\b(?!\s+growth)",
        "earnings a share" => @"\b(?:earnings a share|earnings per share|EPS|diluted earnings)\b",
        "net income" => @"\bnet income\b",
        "operating cash flow" => @"\boperating cash flow\b",
        "free cash flow" => @"\bfree cash flow\b",
        _ => @"\b(?<!adjusted\s)(?<!segment\s)" + Regex.Escape(concept) + @"s?\b",
    };

    static string Words(string kind) => kind switch
    {
        Margin => "wider|widened|widening|expanded|expanding|improved|narrower|narrowed|narrowing|contracted|contracting|compressed|shrank|slipped",
        Estimate => @"beat|beats|topped|exceeded|surpassed|above|ahead|missed|misses|miss|below|fell\s+(?:just\s+)?short|came\s+in\s+short|short\s+of",
        _ => "rose|grew|increased|climbed|declined|fell(?!\\s+(?:just\\s+)?short)|decreased|dropped|shrank",
    };

    static int SignOf(string kind, string word)
    {
        var said = Regex.Replace(word.ToLowerInvariant(), @"\s+", " ");

        return kind switch
        {
            Margin => said is "wider" or "widened" or "widening" or "expanded" or "expanding" or "improved" ? 1 : -1,
            Estimate => said.StartsWith("beat", StringComparison.Ordinal) || said is "topped" or "exceeded" or "surpassed" or "above" or "ahead" ? 1 : -1,
            _ => said is "rose" or "grew" or "increased" or "climbed" ? 1 : -1,
        };
    }

    [GeneratedRegex(@"\b(?:segment|segments|adjusted|organic|guidance|guided|outlook|prior quarter|previous quarter|last quarter|a quarter earlier|year before last)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotTheWholeQuarter();

    [GeneratedRegex(@"\b(?:estimate|estimates|consensus|expectations|expected)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EstimateNamed();

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
