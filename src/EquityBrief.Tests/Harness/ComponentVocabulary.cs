using System.Text.RegularExpressions;
using EquityBrief.Core.Components;
using DataStore = EquityBrief.Core.Components.Store;

namespace EquityBrief.Tests.Harness;

// The three vocabularies the corpus uses for one set of stores, and how they
// collapse into one.
//
// The catalogue names stores in prose, "membership", "bar store", "every store".
// The read and write matrix names them in column headings, some of which
// aggregate several. SCHEMA names them as tables, in snake case. Declaring in the
// finest vocabulary all three share, the SCHEMA table, means the other two are
// derived rather than tabulated, and only the residue needs a lexicon.
//
// The lexicon's force is that it is total in both directions: a term that
// resolves to nothing throws, and a term nobody uses is a fault. Neither half
// works alone. Without the first, an unrecognised cell reads as a component
// touching nothing; without the second, the lexicon rots as the document moves.
internal static class ComponentVocabulary
{
    // Store to SCHEMA table, derived from the member name rather than listed.
    internal static string TableName(DataStore store) =>
        Regex.Replace(store.ToString(), "(?<!^)([A-Z])", "_$1").ToLowerInvariant();

    // The columns of the read and write matrix, and what each holds. Three
    // aggregate, which section 16 states of itself: computed tables is the row
    // naming six stores, research and theme is the two research rows, and
    // sources is source documents.
    internal static readonly (string Column, DataStore[] Stores)[] Columns =
    [
        ("Membership", [DataStore.Membership]),
        ("Bars", [DataStore.Bar]),
        ("Calendar", [DataStore.Calendar]),
        ("Computed tables", [DataStore.Indicator, DataStore.Swing, DataStore.VolumeProfile, DataStore.Level, DataStore.Ladder, DataStore.Move]),
        ("Listings", [DataStore.Listing]),
        ("Forward returns", [DataStore.ForwardReturn]),
        ("Facts", [DataStore.Facts]),
        ("Fundamentals", [DataStore.Fundamentals]),
        ("News pulse", [DataStore.NewsPulse]),
        ("Research and theme", [DataStore.ResearchSection, DataStore.ThemeSection]),
        ("Sources", [DataStore.SourceDocument]),
        ("Series state", [DataStore.SeriesState]),
        ("Run log", [DataStore.RunLog]),
    ];

    // The candidate register has no column. Section 16's key gives a reason for
    // every other omission and not for this one, which is logged rather than
    // silently absorbed: it arrives with the registrar in phase 6.
    internal static readonly DataStore[] WithoutAColumn = [DataStore.CandidateRegister];

    internal static string ColumnFor(DataStore store) =>
        Columns.FirstOrDefault(column => column.Stores.Contains(store)).Column
        ?? throw new InvalidOperationException(
            $"{store} has no column in the read and write matrix and is not declared as one that " +
            "lacks one. A store nobody placed in the matrix is a store the matrix cannot assert.");

    // A heading as the table reader hands it over. The reader turns a <br> into
    // a space, so "Member<br>ship" arrives as "Member ship" and the spaces have
    // to go rather than merely be trimmed.
    static string Normalised(string text) =>
        Regex.Replace(text, "[^A-Za-z]", string.Empty).ToLowerInvariant();

    internal static DataStore[] StoresIn(string columnHeading)
    {
        var wanted = Normalised(columnHeading);

        var match = Columns.FirstOrDefault(column => Normalised(column.Column) == wanted);

        return match.Stores
            ?? throw new InvalidOperationException(
                $"The read and write matrix carries a column '{columnHeading}' that maps to no " +
                "store. A column nobody mapped is one claim per row made against nothing.");
    }

    // Whole-cell phrases, matched before the cell is split on commas, because
    // splitting them produces fragments that resolve to nothing.
    static readonly Dictionary<string, DataStore[]> WholeCells = new(StringComparer.OrdinalIgnoreCase)
    {
        ["every store"] = [.. Columns.SelectMany(column => column.Stores)],
        ["none"] = [],
        ["read API"] = [],
        ["a file the user chooses"] = [],
        ["every component appends"] = [DataStore.RunLog],
        ["the store's schema"] = [],
        ["the store's schema version"] = [],
        // Prose describing a hand-off rather than a store. The trend classifier
        // returns its label to a caller; nothing is written. The caller is the
        // ladder builder rather than the facts assembler, settled at 4.0,
        // because the facts assembler is built a phase after the screen that
        // draws the label and the ladder row is where the label is stored.
        ["returns the label to the ladder builder"] = [],
        // The harness writes files, not stores, which is the distinction the
        // section 16 key draws for it in so many words.
        ["artifacts/phase-report.html"] = [],
        ["artifacts/phase-report.json"] = [],
        ["this document, the code, and the fixture, including the fixture's own store; never a store under the data root"] = [],
    };

    // Terms naming a feed rather than a store. A feed has no column, and saying
    // so here is what keeps it from reading as a component touching nothing.
    static readonly Dictionary<string, Feed> Feeds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["index membership feed"] = Feed.IndexMembership,
        ["bulk price feed"] = Feed.BulkPrice,
        ["historical price feed"] = Feed.HistoricalPrice,
        ["splits and dividends feed"] = Feed.SplitsAndDividends,
        ["earnings calendar feed"] = Feed.EarningsCalendar,
        ["news feed"] = Feed.News,
        ["company financials"] = Feed.CompanyFinancials,
        ["company financials feed"] = Feed.CompanyFinancials,
        ["filings archive"] = Feed.FilingsArchive,
        ["research model"] = Feed.ResearchModel,
        ["local model"] = Feed.LocalModel,
        ["search tool"] = Feed.SearchTool,
    };

    // Prose that names a store under a name the mechanical rule does not reach.
    static readonly Dictionary<string, DataStore> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bar store"] = DataStore.Bar,
        ["run log"] = DataStore.RunLog,
        ["news pulse"] = DataStore.NewsPulse,
        ["forward returns"] = DataStore.ForwardReturn,
        ["research store"] = DataStore.ResearchSection,
        ["theme store"] = DataStore.ThemeSection,
        ["source documents"] = DataStore.SourceDocument,
        ["series state"] = DataStore.SeriesState,
        ["listings"] = DataStore.Listing,
        ["indicators"] = DataStore.Indicator,
        ["swings"] = DataStore.Swing,
        ["volume profile"] = DataStore.VolumeProfile,
        ["levels"] = DataStore.Level,
        ["ladders"] = DataStore.Ladder,
        ["moves"] = DataStore.Move,
        ["facts"] = DataStore.Facts,
        ["fundamentals"] = DataStore.Fundamentals,
        ["membership"] = DataStore.Membership,
        ["calendar"] = DataStore.Calendar,
    };

    internal sealed record CellReading(DataStore[] Stores, Feed[] Feeds, string[] Unresolved);

    internal static CellReading Read(string cell)
    {
        var text = cell.Trim();

        if (text.Length == 0)
        {
            return new CellReading([], [], []);
        }

        if (WholeCells.TryGetValue(text, out var whole))
        {
            return new CellReading(whole, [], []);
        }

        var stores = new List<DataStore>();
        var feeds = new List<Feed>();
        var unresolved = new List<string>();

        foreach (var raw in text.Split(','))
        {
            // A parenthetical is a note about when, not a name.
            var term = Regex.Replace(raw, @"\(.*?\)", string.Empty).Trim();

            if (term.Length == 0)
            {
                continue;
            }

            if (Aliases.TryGetValue(term, out var store))
            {
                stores.Add(store);
            }
            else if (Feeds.TryGetValue(term, out var feed))
            {
                feeds.Add(feed);
            }
            else if (WholeCells.TryGetValue(term, out var nested))
            {
                stores.AddRange(nested);
            }
            else
            {
                unresolved.Add(term);
            }
        }

        return new CellReading([.. stores.Distinct()], [.. feeds.Distinct()], [.. unresolved]);
    }

    // Every phrase the lexicon carries, so the reverse direction can assert that
    // nothing in it has stopped being used.
    internal static IReadOnlyList<string> Lexicon() =>
        [.. WholeCells.Keys, .. Feeds.Keys, .. Aliases.Keys];
}
