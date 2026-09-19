using System.Text.RegularExpressions;
using EquityBrief.Core.Facts;

namespace EquityBrief.Core.Research;

// One document a pass holds, with what the choice ranks it by.
//
// `Symbols` is how many listings the provider attributed the document to, which is
// what says how much of it is about this company: the company's own filing names one,
// an article about the company alone names its own listings, and a roundup names every
// company it mentions. 6.8 measured KEYS's stored year at 656 articles, 207 of them
// attributed to Keysight's own two listings alone and the widest to 50.
//
// `NamesTheCompany` is whether its title names the company, which a document the provider
// attributed to this company alone can still fail: a launch by another company that
// mentions this one in its text is attributed to this one.
public sealed record EvidenceDocument(StoredDocument Stored, int Symbols, bool NamesTheCompany = false);

// Which of the documents a pass fetched each section is handed.
//
// Code picks the documents a section may rest on, and the model writes the words
// around them. A quarter of one name's news is millions of characters, far past what any
// section can be handed and past what a reader of the section would want it resting
// on, so the choice is a rule rather than everything: the documents inside each episode's
// largest move for the cause of that episode, the company's own filing for what it sells
// and its segments, and the company's own filing and documents spread over the days since
// it for the sections built across the evidence. Within each, a title naming the company
// first, then the fewest companies named.
// see: Code owns every number
// see: A research pass reads a name's news from the last three months alone, inside each stored move and since the company's own filing, and hands each section the documents code picks from it
public static class Evidence
{
    // The documents one episode's cause may rest on, at most.
    public const int DocumentsPerMove = 2;

    // The documents a section built across the evidence is handed beside the
    // company's own filing, at most, one from each of as many equal stretches of the
    // days since the filing where each stretch has one.
    public const int DocumentsSinceTheFiling = 6;

    // The company's own filing is attributed to the company and nothing else.
    public const int OwnFiling = 1;

    public const string CauseSection = ClaimRules.CauseSection;
    public const string Sells = "What the company sells";
    public const string Segments = "The segment commentary";

    // The sections built across the evidence, which are handed the company's own
    // filing and what was published since it.
    public static readonly string[] AcrossTheEvidence =
    [
        "The dated calendar items",
        "The two cases",
        "The risks, each with what would confirm it",
        "The short version",
    ];

    public static IReadOnlyDictionary<string, IReadOnlyList<StoredDocument>> ForSections(
        IReadOnlyList<Fact> facts,
        IReadOnlyList<EvidenceDocument> documents,
        string? ownFilingId)
    {
        var handed = new Dictionary<string, IReadOnlyList<StoredDocument>>(StringComparer.Ordinal);

        var cause = Moves(facts, documents);

        if (cause.Count > 0)
        {
            handed[CauseSection] = cause;
        }

        var own = documents.FirstOrDefault(document => string.Equals(document.Stored.Id, ownFilingId, StringComparison.Ordinal));

        if (own is not null)
        {
            handed[Sells] = [own.Stored];
            handed[Segments] = [own.Stored];
        }

        var across = Across(documents, own);

        if (across.Count > 0)
        {
            foreach (var section in AcrossTheEvidence)
            {
                handed[section] = across;
            }
        }

        return handed;
    }

    // For each episode, the documents published inside its largest move, a title naming
    // the company first, then the newest, then the fewest companies named, at most two an
    // episode, listed once each in the order they were published. The newest before the
    // fewest companies, because a move's first session is the one before it moved and what
    // was published on it is about that session.
    public static IReadOnlyList<StoredDocument> Moves(IReadOnlyList<Fact> facts, IReadOnlyList<EvidenceDocument> documents)
    {
        var chosen = new Dictionary<string, EvidenceDocument>(StringComparer.Ordinal);

        foreach (var episode in MoveWindows.Episodes(facts))
        {
            var inside = Usable(documents.Where(document => document.Stored.PublishedOn is { } published && MoveWindows.Holds(episode.Largest, published)));

            foreach (var document in inside
                .OrderBy(document => document.NamesTheCompany ? 0 : 1)
                .ThenByDescending(document => document.Stored.PublishedOn)
                .ThenBy(document => document.Symbols)
                .ThenBy(document => document.Stored.Id, StringComparer.Ordinal)
                .Take(DocumentsPerMove))
            {
                chosen.TryAdd(document.Stored.Id, document);
            }
        }

        return
        [
            .. chosen.Values
                .OrderBy(document => document.Stored.PublishedOn)
                .ThenBy(document => document.Stored.Id, StringComparer.Ordinal)
                .Select(document => document.Stored),
        ];
    }

    // The company's own filing first, then documents published on or after the day it was
    // filed, one from each of six equal stretches of the days from the filing to the newest,
    // each the best of its stretch, and where a stretch has none the next best of the rest.
    // With no filing of its own, the same over the whole window.
    public static IReadOnlyList<StoredDocument> Across(IReadOnlyList<EvidenceDocument> documents, EvidenceDocument? own)
    {
        var since = own?.Stored.PublishedOn;

        var candidates = Usable(documents.Where(document =>
                !ReferenceEquals(document, own)
                && document.Stored.PublishedOn is { } published
                && (since is null || published >= since)))
            .ToArray();

        if (candidates.Length == 0)
        {
            return own is null ? [] : [own.Stored];
        }

        var first = since ?? candidates.Min(document => document.Stored.PublishedOn!.Value);
        var last = candidates.Max(document => document.Stored.PublishedOn!.Value);
        var days = last.DayNumber - first.DayNumber + 1;
        var chosen = new List<EvidenceDocument>();

        for (var stretch = 0; stretch < DocumentsSinceTheFiling; stretch++)
        {
            var from = first.DayNumber + (stretch * days / DocumentsSinceTheFiling);
            var to = first.DayNumber + ((stretch + 1) * days / DocumentsSinceTheFiling);

            if (Ranked(candidates.Where(document => document.Stored.PublishedOn!.Value.DayNumber >= from
                    && document.Stored.PublishedOn!.Value.DayNumber < to)).FirstOrDefault() is { } best)
            {
                chosen.Add(best);
            }
        }

        chosen.AddRange(Ranked(candidates.Except(chosen)).Take(DocumentsSinceTheFiling - chosen.Count));

        var rest = chosen
            .OrderBy(document => document.Stored.PublishedOn)
            .ThenBy(document => document.Stored.Id, StringComparer.Ordinal)
            .Select(document => document.Stored);

        return own is null ? [.. rest] : [own.Stored, .. rest];
    }

    // The order every choice here ranks by: a title naming the company first, then the
    // fewest companies named, then the newest, and the id where all three tie.
    static IEnumerable<EvidenceDocument> Ranked(IEnumerable<EvidenceDocument> documents) =>
        documents
            .OrderBy(document => document.NamesTheCompany ? 0 : 1)
            .ThenBy(document => document.Symbols)
            .ThenByDescending(document => document.Stored.PublishedOn)
            .ThenBy(document => document.Stored.Id, StringComparer.Ordinal);

    // Admitted documents where any are admitted, and otherwise the refusals, so a
    // section with nothing admissible still cites what the pass fetched for it.
    static IReadOnlyList<EvidenceDocument> Usable(IEnumerable<EvidenceDocument> documents)
    {
        var all = documents.ToArray();
        var admitted = all.Where(document => document.Stored.Admitted).ToArray();

        return admitted.Length > 0 ? admitted : all;
    }

    // Whether a title names the company: its ticker as a word, or the first word of its name
    // once a leading "The" is taken off, read without regard to case. The first word rather
    // than the whole name, because a title writes a company's short name and never its legal
    // one, and a name whose first word is a common one ranks every title using that word
    // alongside it, which costs order and never a document.
    public static bool NamesTheCompany(string? title, string ticker, string? company)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var words = new List<string> { ticker };

        if (FirstWord(company) is { } word)
        {
            words.Add(word);
        }

        return words.Any(named => Regex.IsMatch(title, @"(?<![\p{L}\p{N}])" + Regex.Escape(named) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase));
    }

    static string? FirstWord(string? company)
    {
        var words = (company ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var start = words.Length > 1 && string.Equals(words[0], "The", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        return start < words.Length ? words[start].TrimEnd(',', '.') : null;
    }
}
