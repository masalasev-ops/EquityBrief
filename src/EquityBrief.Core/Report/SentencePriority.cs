using System.Text.RegularExpressions;

namespace EquityBrief.Core.Report;

// The sentences a paragraph drawn at most so many sentences keeps, chosen by what each says rather than by where it stands:
// the first sentence on demand, then the first on cost, then the first naming a segment, then the first on the main risk,
// each taken once, and where fewer than the most are chosen so, the paragraph's other sentences in order to fill it. The
// kept sentences are drawn in the paragraph's own order, so the paragraph still reads as written.
// see: What worked and what did not keeps four sentences a paragraph chosen by demand then cost then segments then the main risk
public static partial class SentencePriority
{
    // The priorities in order, each by the words a sentence on it uses.
    public static IReadOnlyList<string> Order { get; } = ["demand", "cost", "segments", "risk"];

    public static IReadOnlyList<string> Choose(IReadOnlyList<string> sentences, int most, IReadOnlyList<string>? segments = null)
    {
        if (sentences.Count <= most)
        {
            return sentences;
        }

        var chosen = new SortedSet<int>();

        foreach (var priority in Order)
        {
            if (chosen.Count == most)
            {
                break;
            }

            for (var at = 0; at < sentences.Count; at++)
            {
                if (!chosen.Contains(at) && Says(sentences[at], priority, segments))
                {
                    chosen.Add(at);

                    break;
                }
            }
        }

        for (var at = 0; at < sentences.Count && chosen.Count < most; at++)
        {
            chosen.Add(at);
        }

        return [.. chosen.Select(at => sentences[at])];
    }

    // Whether a sentence speaks to a priority: demand by revenue, sales, orders, volumes or customers; cost by margins,
    // costs, expenses or pricing; segments by a segment's own name or the word itself; the main risk by the words a risk
    // is stated in.
    public static bool Says(string sentence, string priority, IReadOnlyList<string>? segments = null) => priority switch
    {
        "demand" => Demand().IsMatch(sentence),
        "cost" => Cost().IsMatch(sentence),
        "segments" => Segment().IsMatch(sentence)
            || (segments ?? []).Any(segment => segment.Length > 2 && sentence.Contains(segment, StringComparison.OrdinalIgnoreCase)),
        "risk" => Risk().IsMatch(sentence),
        _ => false,
    };

    [GeneratedRegex(@"\b(?:revenues?|sales|orders?|demand|bookings|backlog|volumes?|customers?|organic|subscri\w*|traffic|shipments?|deposits|loans|premiums)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Demand();

    [GeneratedRegex(@"\b(?:margins?|costs?|expenses?|SG&A|spending|pricing|price increases?|inflation|tariffs?|efficienc\w*|productivity|cost of sales)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Cost();

    [GeneratedRegex(@"\bsegments?\b", RegexOptions.IgnoreCase)]
    private static partial Regex Segment();

    [GeneratedRegex(@"\b(?:risks?|threat\w*|headwinds?|pressures?|uncertain\w*|weak\w*|declin\w*|fell|miss\w*|slow\w*|downside|challeng\w*|competition|competitive|exposure|could)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Risk();
}
