using EquityBrief.Core.Facts;

namespace EquityBrief.Core.Research;

// One document a pass holds, with what the choice ranks it by.
//
// `Symbols` is how many listings the provider attributed the document to, which is
// what says how much of it is about this company: the company's own filing names one,
// an article about the company alone names its own listings, and a roundup names every
// company it mentions. 6.8 measured KEYS's stored year at 656 articles, 207 of them
// attributed to Keysight's own two listings alone and the widest to 50.
public sealed record EvidenceDocument(StoredDocument Stored, int Symbols);

// Which of the documents a pass fetched each section is handed.
//
// Code picks the documents a section may rest on, and the model writes the words
// around them. A year of one name's news is millions of characters, far past what any
// section can be handed and past what a reader of the section would want it resting
// on, so the choice is a rule rather than everything: the documents inside each move
// for the cause of that move, the company's own filing for what it sells and its
// segments, and the company's own filing and the documents since it for the sections
// built across the evidence. Within each, the fewest companies named first.
// see: Code owns every number
// see: A research pass hands each section the documents code picks for it, the company's own filing first
public static class Evidence
{
    // The documents one move's cause may rest on, at most. Two, measured over KEYS's
    // eight moves in the stored year its captured news covers: five of the moves
    // overlap, and two a move picks six documents across all eight, every one naming
    // the company alone, which is a prompt the local lane's context holds with the
    // company's own release among them.
    public const int DocumentsPerMove = 2;

    // The documents a section built across the evidence is handed beside the
    // company's own filing, at most. Six, measured over KEYS's quarter from its
    // release on 2026-08-18 to the night the fixture holds: 56 articles, of which the
    // six naming the fewest companies name Keysight alone, three under its one ticker
    // and three under its two listings, and the sixth is taken by id from three of
    // those published on the same day.
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

    // The documents each section is handed, by figure 12.2's names. A section that
    // rests on no document, and the industry cycle, which rests on the theme record,
    // are absent from the map.
    //
    // Admitted documents where there are any, and the refused ones only where a
    // section has no admitted document to rest on, so the section is inserted citing
    // what was fetched and the checker leaves it out saying no admissible source was
    // found, rather than the pass deciding that itself.
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

    // For each stored move, the documents published inside it naming the fewest
    // companies and then the earliest, at most two a move, listed once each in the
    // order they were published.
    public static IReadOnlyList<StoredDocument> Moves(IReadOnlyList<Fact> facts, IReadOnlyList<EvidenceDocument> documents)
    {
        var chosen = new Dictionary<string, EvidenceDocument>(StringComparer.Ordinal);

        foreach (var move in MoveWindows.In(facts))
        {
            var inside = Usable(documents.Where(document => document.Stored.PublishedOn is { } published && MoveWindows.Holds(move, published)));

            foreach (var document in inside
                .OrderBy(document => document.Symbols)
                .ThenBy(document => document.Stored.PublishedOn)
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

    // The company's own filing first, then the documents published on or after the day
    // it was filed naming the fewest companies and then the newest. With no filing of
    // its own, the documents across the whole window, chosen the same way.
    public static IReadOnlyList<StoredDocument> Across(IReadOnlyList<EvidenceDocument> documents, EvidenceDocument? own)
    {
        var since = own?.Stored.PublishedOn;

        var candidates = Usable(documents.Where(document =>
            !ReferenceEquals(document, own)
            && (since is null || document.Stored.PublishedOn is { } published && published >= since)));

        var rest = candidates
            .OrderBy(document => document.Symbols)
            .ThenByDescending(document => document.Stored.PublishedOn)
            .ThenBy(document => document.Stored.Id, StringComparer.Ordinal)
            .Take(DocumentsSinceTheFiling)
            .Select(document => document.Stored);

        return own is null ? [.. rest] : [own.Stored, .. rest];
    }

    // Admitted documents where any are admitted, and otherwise the refusals, so a
    // section with nothing admissible still cites what the pass fetched for it.
    static IReadOnlyList<EvidenceDocument> Usable(IEnumerable<EvidenceDocument> documents)
    {
        var all = documents.ToArray();
        var admitted = all.Where(document => document.Stored.Admitted).ToArray();

        return admitted.Length > 0 ? admitted : all;
    }
}
