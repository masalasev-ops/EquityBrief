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

    // The columns of the read and write matrix, and what each holds. Six
    // aggregate, which section 16 states of itself: computed tables is the row
    // naming six stores, listings is the listings beside the rule each evening's
    // list was drawn by and, from 13.1, the sessions the setup families drew and
    // the page's list they drew, research and theme is the two research rows, sources is
    // source documents, version scores and blocks is the scores a night writes
    // beside the blocks frozen from them, which no component touches one of
    // without the other, fundamentals is the filings beside the copy each
    // fetch stores of what is as of the fetch, and beside the quarters the night
    // fetches after a report with the asks that fetched them, and pulled history
    // is the pulled bars beside the pulled earnings, which one pull writes and one
    // purge removes together, and beside them the surprises, the market series, and
    // the companies, their share counts, their splits and their revenue as first
    // filed, and from 14.8 today's members of the S&P 400 and 600, each a pull's
    // and removed whole with it. The fundamental readings are a computed table, one
    // row a member a night, as the swing readings are, and so are the family
    // results, one row a member a night under each setup family but the pullback.
    // From 13.9 the forward returns column holds the family trades beside the
    // forward returns, each a record of what became of a trade, and from the
    // market switches' ruling the bars column holds the index's and the VIX's
    // daily series beside the members' bars, each a fetched series of sessions.
    // From 14.3 the fundamentals column holds each member's company as a fetch
    // answered it, the listings column the sector heavyweights' reading of each
    // rebalance beside the families' lists, and the forward returns column the
    // heavyweights' holdings, each a record of what became of one. From 14.6 the
    // listings column holds each registered heavyweights rule's readings beside
    // the page's book's, the forward returns column each rule's holdings beside
    // the page's, and the fundamentals column the estimates the night asked for,
    // each an answer of the provider's fundamentals. From 15.1's second half the
    // computed tables column holds each S&P 400 and 600 member's answers under its
    // index's families, the listings column each index's nights and lists, and
    // the forward returns column each index's trades and heavyweight holdings.
    // From 15.4 the listings column holds the answer each recorded sweep states,
    // which a family's card reads beside its picks. From 15.2's second half the
    // computed tables column holds every member's readings each night and the
    // market switches the S&P 400's and 600's rules may read.
    internal static readonly (string Column, DataStore[] Stores)[] Columns =
    [
        ("Membership", [DataStore.Membership]),
        ("Bars", [DataStore.Bar, DataStore.MarketBar, DataStore.KeptBar]),
        ("Calendar", [DataStore.Calendar]),
        ("Pulled history", [DataStore.PulledBar, DataStore.PulledEarnings, DataStore.PulledSurprise, DataStore.PulledMarketBar, DataStore.PulledCompany, DataStore.PulledShares, DataStore.PulledSplit, DataStore.PulledRevenue, DataStore.PulledMember, DataStore.PulledIncome, DataStore.PulledSnapshot, DataStore.PulledHolding]),
        ("Computed tables", [DataStore.Indicator, DataStore.Swing, DataStore.VolumeProfile, DataStore.Level, DataStore.Ladder, DataStore.Move, DataStore.PeerReading, DataStore.EarningsReaction, DataStore.SwingReading, DataStore.MarketReading, DataStore.GateResult, DataStore.FilterVersion, DataStore.ShapeProposal, DataStore.FundamentalReading, DataStore.FamilyResult, DataStore.IndexFamilyResult, DataStore.MemberReading, DataStore.SwitchReading]),
        ("Listings", [DataStore.Listing, DataStore.ListRule, DataStore.FamilyNight, DataStore.FamilyPick, DataStore.HeavyweightNight, DataStore.HeavyweightRuleNight, DataStore.IndexFamilyNight, DataStore.IndexFamilyPick, DataStore.IndexHeavyweightRuleNight, DataStore.SweepAnswer, DataStore.DecisionCard, DataStore.RuleNight, DataStore.FormingRow]),
        ("Forward returns", [DataStore.ForwardReturn, DataStore.FamilyTrade, DataStore.HeavyweightHolding, DataStore.HeavyweightRuleHolding, DataStore.IndexFamilyTrade, DataStore.IndexHeavyweightHolding, DataStore.IndexRuleTrade, DataStore.IndexHeavyweightRuleHolding, DataStore.RuleRecord, DataStore.RulePick, DataStore.Setup, DataStore.SetupNight, DataStore.LedgerSummary, DataStore.LoopRun, DataStore.LoopProposal, DataStore.LoopTest, DataStore.LoopFinding, DataStore.LoopReading, DataStore.LoopModel]),
        ("Facts", [DataStore.Facts]),
        ("Fundamentals", [DataStore.Fundamentals, DataStore.FundamentalsSnapshot, DataStore.ReportedQuarter, DataStore.QuarterAsk, DataStore.Company, DataStore.EstimateReading, DataStore.DividendReading, DataStore.FiledFact, DataStore.FiledFactPull, DataStore.FilingDay]),
        ("News", [DataStore.NewsPulse, DataStore.NewsArticle, DataStore.NewsLabel]),
        ("Research and theme", [DataStore.ResearchSection, DataStore.ThemeSection]),
        ("Sources", [DataStore.SourceDocument]),
        ("Candidate register", [DataStore.CandidateRegister]),
        ("Rule versions", [DataStore.RuleVersion]),
        ("Version scores and blocks", [DataStore.VersionScore, DataStore.VersionBlock]),
        ("Series state", [DataStore.SeriesState]),
        ("Research requests", [DataStore.ResearchRequest]),
        ("Watch list", [DataStore.WatchList, DataStore.TakenTrade, DataStore.TakenRecord]),
        ("Run log", [DataStore.RunLog]),
    ];

    // Empty from 8.3, and kept rather than deleted.
    //
    // It held the candidate register until the registrar arrived with its column,
    // which was the one omission section 16's key gave no reason for. Keeping the
    // list is what lets the other direction be asserted: a store dropped out of
    // the matrix later would have to be written in here to pass, which is a line
    // somebody has to choose to add rather than an absence nothing notices.
    internal static readonly DataStore[] WithoutAColumn = [];

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
        ["every store but the pulled history and the market series"] = [.. Columns.SelectMany(column => column.Stores).Except([DataStore.PulledBar, DataStore.PulledEarnings, DataStore.PulledSurprise, DataStore.PulledMarketBar, DataStore.PulledCompany, DataStore.PulledShares, DataStore.PulledSplit, DataStore.PulledRevenue, DataStore.PulledMember, DataStore.PulledIncome, DataStore.PulledSnapshot, DataStore.PulledHolding, DataStore.MarketBar])],
        ["none"] = [],
        ["read API"] = [],
        ["a file the user chooses"] = [],
        ["files in the checkout's sampleReports folder"] = [],
        ["every component that writes appends"] = [DataStore.RunLog],
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
        ["the phase report, as a page and as data"] = [],
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
        ["dividend calendar feed"] = Feed.DividendCalendar,
        ["news feed"] = Feed.News,
        ["company financials"] = Feed.CompanyFinancials,
        ["company financials feed"] = Feed.CompanyFinancials,
        ["filings archive"] = Feed.FilingsArchive,
        ["research model"] = Feed.ResearchModel,
        ["local model"] = Feed.LocalModel,
        ["search tool"] = Feed.SearchTool,
        ["identifier mapping feed"] = Feed.IdentifierMapping,
    };

    // Prose that names a store under a name the mechanical rule does not reach.
    static readonly Dictionary<string, DataStore> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bar store"] = DataStore.Bar,
        ["kept bars"] = DataStore.KeptBar,
        ["run log"] = DataStore.RunLog,
        ["news pulse"] = DataStore.NewsPulse,
        ["news articles"] = DataStore.NewsArticle,
        ["news labels"] = DataStore.NewsLabel,
        ["forward returns"] = DataStore.ForwardReturn,
        ["research store"] = DataStore.ResearchSection,
        ["theme store"] = DataStore.ThemeSection,
        ["source documents"] = DataStore.SourceDocument,
        ["series state"] = DataStore.SeriesState,
        ["listings"] = DataStore.Listing,
        ["list rules"] = DataStore.ListRule,
        ["family results"] = DataStore.FamilyResult,
        ["family nights"] = DataStore.FamilyNight,
        ["family picks"] = DataStore.FamilyPick,
        ["family trades"] = DataStore.FamilyTrade,
        ["indicators"] = DataStore.Indicator,
        ["swings"] = DataStore.Swing,
        ["volume profile"] = DataStore.VolumeProfile,
        ["levels"] = DataStore.Level,
        ["ladders"] = DataStore.Ladder,
        ["moves"] = DataStore.Move,
        ["peer readings"] = DataStore.PeerReading,
        ["earnings reactions"] = DataStore.EarningsReaction,
        ["swing readings"] = DataStore.SwingReading,
        ["market readings"] = DataStore.MarketReading,
        ["gate results"] = DataStore.GateResult,
        ["filter versions"] = DataStore.FilterVersion,
        ["shape proposals"] = DataStore.ShapeProposal,
        ["facts"] = DataStore.Facts,
        ["fundamentals"] = DataStore.Fundamentals,
        ["fundamentals snapshots"] = DataStore.FundamentalsSnapshot,
        ["reported quarters"] = DataStore.ReportedQuarter,
        ["quarter asks"] = DataStore.QuarterAsk,
        ["companies"] = DataStore.Company,
        ["heavyweight nights"] = DataStore.HeavyweightNight,
        ["heavyweight holdings"] = DataStore.HeavyweightHolding,
        ["heavyweight rule nights"] = DataStore.HeavyweightRuleNight,
        ["heavyweight rule holdings"] = DataStore.HeavyweightRuleHolding,
        ["index family nights"] = DataStore.IndexFamilyNight,
        ["index family results"] = DataStore.IndexFamilyResult,
        ["index family picks"] = DataStore.IndexFamilyPick,
        ["index family trades"] = DataStore.IndexFamilyTrade,
        ["index heavyweight holdings"] = DataStore.IndexHeavyweightHolding,
        ["index rule trades"] = DataStore.IndexRuleTrade,
        ["index heavyweight rule nights"] = DataStore.IndexHeavyweightRuleNight,
        ["index heavyweight rule holdings"] = DataStore.IndexHeavyweightRuleHolding,
        ["sweep answers"] = DataStore.SweepAnswer,
        ["decision cards"] = DataStore.DecisionCard,
        ["rule records"] = DataStore.RuleRecord,
        ["rule nights"] = DataStore.RuleNight,
        ["rule picks"] = DataStore.RulePick,
        ["forming rows"] = DataStore.FormingRow,
        ["setups"] = DataStore.Setup,
        ["setup nights"] = DataStore.SetupNight,
        ["ledger summaries"] = DataStore.LedgerSummary,
        ["loop runs"] = DataStore.LoopRun,
        ["loop proposals"] = DataStore.LoopProposal,
        ["loop tests"] = DataStore.LoopTest,
        ["loop findings"] = DataStore.LoopFinding,
        ["loop readings"] = DataStore.LoopReading,
        ["loop models"] = DataStore.LoopModel,
        ["filed facts"] = DataStore.FiledFact,
        ["facts pulls"] = DataStore.FiledFactPull,
        ["filing days"] = DataStore.FilingDay,
        ["estimate readings"] = DataStore.EstimateReading,
        ["fundamental readings"] = DataStore.FundamentalReading,
        ["member readings"] = DataStore.MemberReading,
        ["switch readings"] = DataStore.SwitchReading,
        ["membership"] = DataStore.Membership,
        ["calendar"] = DataStore.Calendar,
        ["pulled bars"] = DataStore.PulledBar,
        ["pulled earnings"] = DataStore.PulledEarnings,
        ["pulled surprises"] = DataStore.PulledSurprise,
        ["pulled market series"] = DataStore.PulledMarketBar,
        ["pulled companies"] = DataStore.PulledCompany,
        ["pulled share counts"] = DataStore.PulledShares,
        ["pulled splits"] = DataStore.PulledSplit,
        ["pulled revenue"] = DataStore.PulledRevenue,
        ["pulled members"] = DataStore.PulledMember,
        ["pulled income"] = DataStore.PulledIncome,
        ["pulled snapshots"] = DataStore.PulledSnapshot,
        ["pulled holdings"] = DataStore.PulledHolding,
        ["market series"] = DataStore.MarketBar,
        ["candidate register"] = DataStore.CandidateRegister,
        ["rule versions"] = DataStore.RuleVersion,
        ["version scores"] = DataStore.VersionScore,
        ["version blocks"] = DataStore.VersionBlock,
        ["research requests"] = DataStore.ResearchRequest,
        ["watch list"] = DataStore.WatchList,
        ["taken trades"] = DataStore.TakenTrade,
        ["taken records"] = DataStore.TakenRecord,
        ["dividend readings"] = DataStore.DividendReading,
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
