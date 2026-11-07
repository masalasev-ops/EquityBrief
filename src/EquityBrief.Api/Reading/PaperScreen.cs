using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// One paper account before it is walked: its key, family, standing, name and words, the register's name its test row
// carries, none for the live or provisional rule, whose row is read by its standing, whether it is a book, the trades it
// counts with each one's stored result, the day it starts, the name its stored trades are kept under, none where they
// are the S&P 500's page list or the operator's, the words naming the version it counts, and the trades it leaves out
// as listed before that version, each with the words naming the version that listed it. Paper trading's page reads
// besides its short name, what it changes in words, and the stocks its list kept that no fill bought, each with the night
// that listed it and why.
public sealed record PaperAccountSource(
    string Key,
    string Family,
    string Standing,
    string Name,
    string Words,
    string? Tested,
    bool Book,
    IReadOnlyList<(PaperTrade Trade, double? Result)> Trades,
    DateOnly? Since,
    string? Listed = null,
    string? Version = null,
    IReadOnlyList<(PaperTrade Trade, double? Result, string Version)>? Earlier = null,
    string? Short = null,
    string? Changes = null,
    IReadOnlyList<(string Ticker, DateOnly Listed, string Why)>? Unfilled = null,
    bool ReadsReleases = false,
    int? JudgedFrom = null)
{
    // The first session its version's registration counts from, the day the account starts, none for an account no
    // registration starts.
    public DateOnly? VersionFrom { get; init; }

    // The trades listed before its version's first session that a replay under its version reproduced, kept in its rule's
    // record and not in the account, and the session that replay counted from; none where no replay carried the record back.
    // see: A paper account counts from its rule's current version's first session, and the same trades before it stand apart under the account
    public IReadOnlyList<(PaperTrade Trade, double? Result)> Same { get; init; } = [];

    public DateOnly? SameSince { get; init; }

    // Whether a stored trade is one of this account's: kept under its name, or under its name and the filter version it
    // was listed from.
    public bool Keeps(PaperRow row) =>
        Listed is { } name && (row.Account == name || row.Account.StartsWith(name + ", from version ", StringComparison.Ordinal));
}

// An index's paper accounts as a page reads them: each before its walk, each drawn, the closes they were marked by, the
// sessions they were walked over, each stock's company and readings on the night, the index's fund's closes, and each
// open holding's standing tonight.
public sealed record PaperAccounts(
    IReadOnlyList<PaperAccountSource> Sources,
    IReadOnlyList<PaperAccountView> Views,
    Func<string, DateOnly, decimal?> Close,
    IReadOnlyList<DateOnly>? Sessions = null,
    IReadOnlyDictionary<string, PaperStockRow>? Stocks = null,
    Func<DateOnly, decimal?>? Fund = null,
    IReadOnlyDictionary<string, string>? Standings = null,
    PaperCombinedView? Combined = null,
    TradeGroupsView? Groups = null);

// The projection from stored trades and test rows to paper trading's accounts and their trade logs. The accounts are an
// index's: each family's live or provisional rule's list, the page's own, each registered variant's own list or book,
// numbered as its card numbers it, and the operator's own trades. Each is walked by the core's one function from the
// account settings, and every figure its page draws beside the walk, an open trade's standing, a closed trade's ending,
// the stocks not bought, a rule's trades against its variants', its test years, the chance luck alone would do as well
// and its record's progress, is the core's too, read off the newest per-rule test and its own trades, never merged. Not in
// the read API, whose claim is that it computes nothing, and not in the page, whose claim is the same.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
// see: A rule's scorecard draws its test years and its record since it went live as two halves, and never adds them together
public static class PaperScreen
{
    // The keys the page's link carries: the account a trade log draws and the family whose variants the chart adds.
    public const string AccountQuery = "account";

    public const string FamilyQuery = PaperLink.FamilyKey;

    // The operator's own account's key.
    public const string YouKey = "you";

    // The most holdings a book can hold, each sector's leaders across the eleven sectors, which a holding's equal share
    // is sized across.
    public static int BookHoldings => GicsSectors.Funds.Count * HeavyweightRule.Leaders;

    // The fund an index's accounts are compared with.
    public static string Fund(string index) => index switch
    {
        "MID" => "IJH",
        "SML" => "IJR",
        _ => "SPY",
    };

    static string ListKey(string family) => ReadApi.PageList + "-" + family;

    // The words a provisional account and the operator's own name what they count, having no registered version.
    public const string ProvisionalVersion = "provisional settings, no registered version: every trade since its first";

    public const string YourVersion = "your own trades: every one since your first";

    // The words an earlier trade listed before its account's rule first registered is named by.
    public const string BeforeRegistration = "before its first registration";

    // A registered version as the page names it: the day it registered and its code version.
    public static string VersionWords(CandidateRow row) =>
        "the version registered " + Stamp(DateOnly.FromDateTime(row.RegisteredAt.UtcDateTime)) + (row.EvaluatorVersion.Length > 0 ? ", code " + row.EvaluatorVersion : string.Empty);

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    // Where a registered rule's account starts: its standing registration's first session, never earlier, whatever a replay
    // carried its record back to.
    // see: A paper account counts from its rule's current version's first session, and the same trades before it stand apart under the account
    public static DateOnly StartOf(CandidateRow standing) => FamilyRecords.FirstSession(standing.RegisteredAt);

    // Where the Run page's record of a registered rule counts from: its standing registration's first session, or the
    // session a replay under its version found it counting from where that replay reproduced every trade. The S&P 400's and
    // 600's records read no replay.
    public static DateOnly RecordFrom(CandidateRow standing, IReadOnlyList<FamilyReplayRow> replays) =>
        FamilyRecords.StartOf(
            new RegisterRow(standing.Id, standing.Candidate, string.Empty, string.Empty, standing.Evaluator, standing.Parameters, standing.EvaluatorVersion, standing.Event, standing.Retires, standing.RegisteredAt, standing.Evidence),
            replays).First;

    // The version that listed a trade: its rule's newest registration whose first session is on or before the trade's buy.
    public static string VersionAt(IReadOnlyList<CandidateRow> lineage, DateOnly bought) =>
        lineage.Where(row => FamilyRecords.FirstSession(row.RegisteredAt) <= bought).MaxBy(row => (row.RegisteredAt, row.Id)) is { } listed
            ? VersionWords(listed)
            : BeforeRegistration;

    // A registered rule's account's trades split at its start: those it counts, bought on or after it, and those it leaves
    // out, each named by the version that listed it; the stocks no fill bought counted from its start alike.
    // From 21.9 an account whose rule stands at the graded exposure with each trade marked where its night held one of the
    // exposure's two switches, which sizes it at half the risk setting; a night whose readings are not held is sized
    // whole. Every other account as it was.
    // see: A breakout's or a drift's graded exposure stands in its market check's place as a variant, and a night it holds one switch sizes its trades at half
    public static IReadOnlyList<PaperAccountSource> Halved(IReadOnlyList<PaperAccountSource> accounts, IReadOnlyList<CandidateRow> register, IReadOnlyDictionary<DateOnly, (double? Breadth, double? VixOverMedian)> nights) =>
    [
        .. accounts.Select(account =>
            register.LastOrDefault(row => row.Candidate == account.Words) is { } row
            && Core.Loop.RuleHooks.Of(Core.Candidates.CandidateEvaluator.Read(row.Parameters)) is { Exposure: not null } hooks
                ? account with { Trades = [.. account.Trades.Select(one => (one.Trade with { Halved = HalvedOn(hooks, nights, one.Trade.ListedOn) }, one.Result))] }
                : account),
    ];

    // From 21.14 an account whose rule reads a release reading, the drift's tone variant on each index, marked so its card
    // says its history before the local model's cutoff does not count, in the operator's words of 2026-10-30; and from 21.17
    // it and a Fundamentals rule asking a flag first each marked with the cutoff's year its scorecard's verdict is judged
    // from, the sentence staying the tone variant's alone.
    // see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
    // see: A rule or a change reading a release reading is judged on the test years from the model's cutoff's year alone, the earlier years printed beside it
    public static IReadOnlyList<PaperAccountSource> Toned(IReadOnlyList<PaperAccountSource> accounts, IReadOnlyList<CandidateRow> register, DateOnly? cutoff = null) =>
    [
        .. accounts.Select(account =>
        {
            if (register.LastOrDefault(row => row.Candidate == account.Words) is not { } row)
            {
                return account;
            }

            var parameters = Core.Candidates.CandidateEvaluator.Read(row.Parameters);
            var toned = parameters.GetValueOrDefault(Core.Releases.ReleaseTone.VersionParameter) > 0;
            var flagged = Core.Candidates.CandidateEvaluators.Find(row.Evaluator) is Core.Candidates.ImprovingCandidate && parameters.GetValueOrDefault(Core.Candidates.ImprovingCandidate.FlagParameter) > 0;

            return toned || flagged ? account with { ReadsReleases = toned, JudgedFrom = cutoff?.Year } : account;
        }),
    ];

    public const string ToneNote = "Its history before 2025 does not count: the model that read those releases was trained after them. Its record from today is what counts.";

    // Whether a graded exposure held one switch on a night: its picks there are the half count.
    public static bool HalvedOn(Core.Loop.RuleHooks hooks, IReadOnlyDictionary<DateOnly, (double? Breadth, double? VixOverMedian)> nights, DateOnly night)
    {
        if (!nights.TryGetValue(night, out var held))
        {
            return false;
        }

        var readings = new double?[Core.Ledger.LedgerReadings.Count];

        readings[Core.Loop.RuleHooks.BreadthColumn] = held.Breadth;
        readings[Core.Loop.RuleHooks.VixOverMedianColumn] = held.VixOverMedian;

        return hooks.PicksUnder(readings) == Core.Loop.RuleHooks.ExposureHalf;
    }

    // The nights of every trade of the accounts whose rule stands at the graded exposure, which their sizing reads.
    public static IReadOnlyList<DateOnly> ExposedNights(IReadOnlyList<PaperAccountSource> accounts, IReadOnlyList<CandidateRow> register) =>
    [
        .. accounts
            .Where(account => register.LastOrDefault(row => row.Candidate == account.Words) is { } row && Core.Loop.RuleHooks.Of(Core.Candidates.CandidateEvaluator.Read(row.Parameters)).Exposure is not null)
            .SelectMany(account => account.Trades.Select(one => one.Trade.ListedOn))
            .Distinct(),
    ];

    // A registered rule's account from its version's first session: the trades listed from then its own, those listed
    // before it from where a replay under its version found the record counting apart as the same trades, and the rest
    // earlier versions'.
    public static PaperAccountSource Started(PaperAccountSource account, IReadOnlyList<(PaperTrade Trade, double? Result)> trades, CandidateRow standing, IReadOnlyList<FamilyReplayRow> replays, IReadOnlyList<CandidateRow> lineage)
    {
        var start = StartOf(standing);
        var record = RecordFrom(standing, replays);
        bool IsSame((PaperTrade Trade, double? Result) one) => one.Trade.ListedOn >= record && one.Trade.ListedOn < start;

        return Split(account, [.. trades.Where(one => !IsSame(one))], start, VersionWords(standing), lineage) with
        {
            VersionFrom = start,
            Same = [.. trades.Where(IsSame)],
            SameSince = record < start ? record : null,
        };
    }

    static PaperAccountSource Split(PaperAccountSource account, IReadOnlyList<(PaperTrade Trade, double? Result)> trades, DateOnly start, string version, IReadOnlyList<CandidateRow> lineage) =>
        account with
        {
            Trades = [.. trades.Where(one => one.Trade.ListedOn >= start)],
            Since = start,
            Version = version,
            Earlier = [.. trades.Where(one => one.Trade.ListedOn < start).Select(one => (one.Trade, one.Result, VersionAt(lineage, one.Trade.ListedOn)))],
            Unfilled = [.. (account.Unfilled ?? []).Where(one => one.Listed >= start)],
        };

    // A variant's tab is its number and the settings it differs by: its short name is the number, and what it changes the
    // rest.
    const string TabJoint = " · ";

    static string TabShort(string tab) => tab.IndexOf(TabJoint, StringComparison.Ordinal) is var at and > 0 ? tab[..at] : tab;

    static string TabChanges(string tab) => tab.IndexOf(TabJoint, StringComparison.Ordinal) is var at and > 0 ? tab[(at + TabJoint.Length)..] : tab;

    // Every account on an index in the one family order, each family's live or provisional rule first and its variants
    // after it as its card numbers them, then the operator's own. A registered rule's account counts the trades of its
    // current version alone, from its version's first session; a provisional account and the operator's own count every
    // trade from their first. The register is read as the night's page reads it, as of the instant given where a night run
    // again read it later, and the rule a freeze keeps in the background has no account.
    // see: A paper account counts from its rule's current version's first session, and the same trades before it stand apart under the account
    public static IReadOnlyList<PaperAccountSource> Accounts(
        bool large,
        string indexName,
        DateOnly night,
        IReadOnlyList<CandidateRow> register,
        IndexRuleSettings? provisional,
        IReadOnlyList<PaperRow> rows,
        IReadOnlyList<PickCell> pageCells,
        IReadOnlyList<(PaperTrade Trade, double? Result, string Family)> yours,
        IReadOnlyList<FamilyReplayRow>? replays = null,
        DateTimeOffset? asOf = null)
    {
        var accounts = new List<PaperAccountSource>();
        var replayed = large ? replays ?? [] : [];

        // The fundamentals-first family keeps no account, its rule running out of sight.
        // see: Business first leaves Tonight, paper trading and Past picks while its rule keeps running out of sight
        foreach (var family in SetupFamilies.PageOrder.Where(family => family != FundamentalsRule.Name))
        {
            // From 22.3 the short-term dip's in the page's order, after the swing families' and before the sector
            // heavyweights': its live rule's trades from its freeze, or the provisional rule's from its first, and each
            // version's beside it, named by what it changes.
            // see: A dip rule is registered as the family on its index and kept by the dip's own step alone
            if (family == DipRule.Name)
            {
                var (liveDip, versions) = RuleScreen.Accounts(family, large, indexName, register, night, null, asOf);
                var dipChips = liveDip is not null
                    ? RuleScreen.ChipsOf(liveDip)
                    : [.. DipRule.Provisional.KeyedChipsOn(EquityBrief.Web.App.Universes.Offered.FirstOrDefault(universe => universe.Name == indexName)?.Code ?? EquityBrief.Web.App.Universes.Large.Code).Select(chip => new RuleChip(chip.Key, chip.Words))];
                var dipHead = new PaperAccountSource(
                    ListKey(family),
                    family,
                    liveDip is not null ? PaperAccountView.Live : PaperAccountView.Provisional,
                    liveDip is not null ? "Live" : "Provisional",
                    liveDip?.Candidate ?? SetupFamilies.Dips.Label + ", the page's list on provisional settings",
                    null,
                    false,
                    [],
                    null,
                    liveDip?.Candidate ?? ReadApi.PageList + " " + family,
                    Short: liveDip is not null ? "Live rule" : "Provisional rule",
                    Changes: dipChips.Count > 0 ? string.Join(TabJoint, dipChips.Select(chip => chip.Words)) : null);
                var dipRegistrations = RuleScreen.Registrations(family, large, indexName, register);

                dipHead = dipHead with { Unfilled = Unfilled(rows.Where(dipHead.Keeps)) };
                accounts.Add(liveDip is not null
                    ? Started(dipHead, Of(rows.Where(dipHead.Keeps)), liveDip, [], [.. dipRegistrations.Where(row => FamilyRecords.IsLive(row.Candidate))])
                    : dipHead with { Trades = Of(rows.Where(dipHead.Keeps)), Since = rows.Where(dipHead.Keeps).Select(row => (DateOnly?)row.Bought).Min(), Version = ProvisionalVersion, Earlier = [] });

                foreach (var (row, tab) in versions)
                {
                    var version = new PaperAccountSource(RunScreen.Slug(row.Candidate), family, PaperAccountView.Variant, tab, row.Candidate, row.Candidate, false, [], null, row.Candidate, Short: TabShort(tab), Changes: TabChanges(tab));

                    accounts.Add(Started(version with { Unfilled = Unfilled(rows.Where(version.Keeps)) }, Of(rows.Where(version.Keeps)), row, [], [.. dipRegistrations.Where(one => one.Candidate == row.Candidate)]));
                }

                continue;
            }

            var (live, variants) = RuleScreen.Accounts(family, large, indexName, register, night, large ? null : provisional, asOf);
            var book = family == HeavyweightRule.Name;
            var pageList = large && SetupFamilies.InPageOrder.Any(swing => swing.Name == family);
            var listed = pageList ? null : !large && live is not null ? live.Candidate : ReadApi.PageList + " " + family;
            var chips = live is not null
                ? RuleScreen.ChipsOf(live)
                : (!large || family == FundamentalsRule.Name) && provisional is not null ? RuleScreen.ProvisionalChips(family, provisional) : [];

            // The S&P 500's page lists every family but the fundamentals-first family by its live rule, registered or not.
            var isLive = live is not null || (large && family != FundamentalsRule.Name);
            var head = new PaperAccountSource(
                ListKey(family),
                family,
                isLive ? PaperAccountView.Live : PaperAccountView.Provisional,
                isLive ? "Live" : "Provisional",
                live?.Candidate ?? FamilyWords(family) + (isLive ? ", the page's own list" : ", the page's list on provisional settings"),
                null,
                book,
                [],
                null,
                listed,
                Short: isLive ? "Live rule" : "Provisional rule",
                Changes: chips.Count > 0 ? string.Join(TabJoint, chips.Select(chip => chip.Words)) : null);
            var cells = pageCells.Where(cell => cell.Family == family).ToArray();
            IReadOnlyList<(PaperTrade, double?)> trades = pageList ? PageTrades(cells, [.. rows.Where(row => live is not null && row.Account == live.Candidate)]) : Of(rows.Where(head.Keeps));
            var registrations = RuleScreen.Registrations(family, large, indexName, register);

            head = head with { Unfilled = pageList ? PageUnfilled(cells) : Unfilled(rows.Where(head.Keeps)) };

            // A provisional account starts at the first night its list kept a trade, filled at the next open or not, so
            // pricing entries at the open moves no account's start.
            var first = pageList
                ? cells.Where(cell => cell.RepeatOf is null && cell.Status != PickStatus.Missing && cell.Buy is not null).Select(cell => (DateOnly?)cell.Night).Min()
                : rows.Where(head.Keeps).Select(row => (DateOnly?)row.Bought).Min();

            accounts.Add(live is not null
                ? Started(head, trades, live, replayed, [.. registrations.Where(row => FamilyRecords.IsLive(row.Candidate))])
                : head with { Trades = trades, Since = first, Version = ProvisionalVersion, Earlier = [] });

            foreach (var (row, tab) in variants)
            {
                var variant = new PaperAccountSource(
                    RunScreen.Slug(row.Candidate),
                    family,
                    PaperAccountView.Variant,
                    tab,
                    row.Candidate,
                    row.Candidate,
                    book,
                    [],
                    null,
                    row.Candidate,
                    Short: TabShort(tab),
                    Changes: TabChanges(tab));

                accounts.Add(Started(variant with { Unfilled = Unfilled(rows.Where(variant.Keeps)) }, Of(rows.Where(variant.Keeps)), row, replayed, [.. registrations.Where(one => one.Candidate == row.Candidate)]));
            }
        }

        // From 22.11 Quality at a fair price's book after the setups', every index's: its live rule's holdings from its
        // freeze, or the provisional rule's from its first, and each version's beside it, named by what it changes.
        // see: A quality rule is registered as the family on its index and kept by the family's own step alone
        {
            var family = QualityRule.Name;
            var (live, versions) = RuleScreen.Accounts(family, large, indexName, register, night, null, asOf);
            var chips = live is not null ? RuleScreen.ChipsOf(live) : RuleChips.Of(RuleKind.Quality, QualityCandidate.ParametersOf(QualityRule.Starting));
            var head = new PaperAccountSource(
                ListKey(family),
                family,
                live is not null ? PaperAccountView.Live : PaperAccountView.Provisional,
                live is not null ? "Live" : "Provisional",
                live?.Candidate ?? QualityRule.Label + ", the page's list on its starting settings",
                null,
                true,
                [],
                null,
                live?.Candidate ?? ReadApi.PageList + " " + family,
                Short: live is not null ? "Live rule" : "Provisional rule",
                Changes: chips.Count > 0 ? string.Join(TabJoint, chips.Select(chip => chip.Words)) : null);

            head = head with { Unfilled = Unfilled(rows.Where(head.Keeps)) };

            var registrations = RuleScreen.Registrations(family, large, indexName, register);

            accounts.Add(live is not null
                ? Started(head, Of(rows.Where(head.Keeps)), live, [], [.. registrations.Where(row => FamilyRecords.IsLive(row.Candidate))])
                : head with { Trades = Of(rows.Where(head.Keeps)), Since = rows.Where(head.Keeps).Select(row => (DateOnly?)row.Bought).Min(), Version = ProvisionalVersion, Earlier = [] });

            foreach (var (row, tab) in versions)
            {
                var version = new PaperAccountSource(RunScreen.Slug(row.Candidate), family, PaperAccountView.Variant, tab, row.Candidate, row.Candidate, true, [], null, row.Candidate, Short: TabShort(tab), Changes: TabChanges(tab));

                accounts.Add(Started(version with { Unfilled = Unfilled(rows.Where(version.Keeps)) }, Of(rows.Where(version.Keeps)), row, [], [.. registrations.Where(one => one.Candidate == row.Candidate)]));
            }
        }

        // From 22.15 the Fundamentals family's trades after Quality at a fair price's, every index's: its live rule's from its
        // freeze, or the provisional rule's from its first night, and each version's beside it, named by what it changes.
        // see: A Fundamentals rule is registered as the family on its index and kept by the family's own step alone
        {
            var family = ImprovingRule.Name;
            var (live, versions) = RuleScreen.Accounts(family, large, indexName, register, night, null, asOf);
            var chips = live is not null ? RuleScreen.ChipsOf(live) : RuleChips.Of(RuleKind.Improving, ImprovingCandidate.ParametersOf(ImprovingGrid.Starting));
            var head = new PaperAccountSource(
                ListKey(family),
                family,
                live is not null ? PaperAccountView.Live : PaperAccountView.Provisional,
                live is not null ? "Live" : "Provisional",
                live?.Candidate ?? ImprovingRule.Label + ", the page's list on its starting settings",
                null,
                true,
                [],
                null,
                live?.Candidate ?? ReadApi.PageList + " " + family,
                Short: live is not null ? "Live rule" : "Provisional rule",
                Changes: chips.Count > 0 ? string.Join(TabJoint, chips.Select(chip => chip.Words)) : null);

            head = head with { Unfilled = Unfilled(rows.Where(head.Keeps)) };

            var registrations = RuleScreen.Registrations(family, large, indexName, register);

            accounts.Add(live is not null
                ? Started(head, Of(rows.Where(head.Keeps)), live, [], [.. registrations.Where(row => FamilyRecords.IsLive(row.Candidate))])
                : head with { Trades = Of(rows.Where(head.Keeps)), Since = rows.Where(head.Keeps).Select(row => (DateOnly?)row.Bought).Min(), Version = ProvisionalVersion, Earlier = [] });

            foreach (var (row, tab) in versions)
            {
                var version = new PaperAccountSource(RunScreen.Slug(row.Candidate), family, PaperAccountView.Variant, tab, row.Candidate, row.Candidate, true, [], null, row.Candidate, Short: TabShort(tab), Changes: TabChanges(tab));

                accounts.Add(Started(version with { Unfilled = Unfilled(rows.Where(version.Keeps)) }, Of(rows.Where(version.Keeps)), row, [], [.. registrations.Where(one => one.Candidate == row.Candidate)]));
            }
        }

        accounts.Add(new PaperAccountSource(
            YouKey,
            YouKey,
            PaperAccountView.You,
            "You",
            "your own trades taken from the cards on this index",
            null,
            false,
            [.. yours.Select(one => (one.Trade, one.Result))],
            yours.Count > 0 ? yours.Min(one => one.Trade.Bought) : null,
            Version: YourVersion,
            Earlier: [],
            Short: "You",
            Unfilled: []));

        return accounts;
    }

    // From 21.20 each variant registered after the register was read for the night, which no night has read yet, in the
    // families' page order: numbered and named by what it changes as its card will number it once the register is read past
    // its registration, with the day it was registered and its first night. A night reads the rows registered before it
    // starts, so a variant registered on a trading day before that day's night starts is first read that evening, and one
    // registered later by the next trading day's night; with no usual start to read it is placed on the next trading day.
    // see: A variant registered after the newest night is shown on Paper trading at once with the night that first reads it
    public static IReadOnlyList<PaperComingView> Coming(
        bool large,
        string indexName,
        DateOnly night,
        IReadOnlyList<CandidateRow> register,
        IndexRuleSettings? provisional,
        TimeOnly? nightStarts,
        DateTimeOffset? asOf = null)
    {
        var read = asOf ?? TonightScreen.PastTheNight(night);
        var families = SetupFamilies.PageOrder.Where(family => family != FundamentalsRule.Name).Append(QualityRule.Name).Append(ImprovingRule.Name);
        var coming = new List<PaperComingView>();

        foreach (var family in families)
        {
            var settings = large || family == DipRule.Name ? null : provisional;
            var standing = RuleScreen.Accounts(family, large, indexName, register, night, settings, read).Variants.Select(one => one.Row.Candidate).ToHashSet(StringComparer.Ordinal);
            var (_, variants) = RuleScreen.Accounts(family, large, indexName, register, night, settings, DateTimeOffset.MaxValue);

            foreach (var (row, tab) in variants.Where(one => !standing.Contains(one.Row.Candidate) && one.Row.RegisteredAt >= read))
            {
                coming.Add(new PaperComingView(family, TabShort(tab), TabChanges(tab), row.Candidate, DateOnly.FromDateTime(row.RegisteredAt.UtcDateTime), FirstNight(row.RegisteredAt, nightStarts)));
            }
        }

        return coming;
    }

    // The night that first reads a row registered at an instant: that day's where the exchange trades it and its night has
    // not started, the next trading day's otherwise, none where the closure table cannot place the day.
    public static DateOnly? FirstNight(DateTimeOffset registered, TimeOnly? nightStarts)
    {
        var day = DateOnly.FromDateTime(registered.UtcDateTime);

        try
        {
            if (nightStarts is { } starts && TimeOnly.FromDateTime(registered.UtcDateTime) < starts &&EquityBrief.Core.Bars.ExchangeClosures.IsSession(day))
            {
                return day;
            }

            do
            {
                day = day.AddDays(1);
            }
            while (!EquityBrief.Core.Bars.ExchangeClosures.IsSession(day));

            return day;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // A trade is bought at its fill on the session after its night, sized from the close its plan was set at; one still
    // waiting on that session, or holding none, is bought by no account. A trade with no target whose family trails reads
    // the distance it trails as its close less its stop where its table stores none, as its walk does.
    // see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
    static IReadOnlyList<(PaperTrade, double?)> Of(IEnumerable<PaperRow> rows) =>
    [
        .. rows.Where(row => row.Fill is not null && row.FilledOn is not null).Select(row => (new PaperTrade(
            row.Ticker,
            row.FilledOn!.Value,
            row.Fill!.Value,
            row.Stop,
            row.Ended,
            PaperAccount.SaleOf(row.Fill!.Value, row.Stop, row.Ended, row.Result, row.Book, row.Entry),
            null,
            row.Place,
            row.Book ? row.BookSize ?? BookHoldings : null,
            row.Entry,
            row.MarkedFrom,
            row.Bought,
            row.Target,
            row.Trail ?? (row.Target is null && !row.Book && row.Stop is { } stop && SetupFamilies.Named(row.Family) is { Trails: true } ? row.Entry - stop : null),
            row.Cap,
            row.Past,
            row.Edge,
            row.Book ? row.Reason : null,
            row.Exit,
            row.RiskMoves,
            FiredOn: row.FiredOn), row.Result)),
    ];

    // The stocks a list or a book kept that no fill bought: waiting on their next session's open, or holding none for the
    // reason the fill step stored.
    static IReadOnlyList<(string Ticker, DateOnly Listed, string Why)> Unfilled(IEnumerable<PaperRow> rows) =>
        [.. rows.Where(row => row.Fill is null).Select(row => (row.Ticker, row.Bought, row.Unfilled ?? PaperAccount.Waiting))];

    // The S&P 500's page list of one swing family as its trades: every trade but a repeat listing, one with no outcome
    // stored and one not filled at its next session's open, each bought at its fill and sold where its result puts it, in
    // the order its night listed them, with its edge read off its live rule's own trade of the same stock and night where
    // that rule keeps one, and how it ended in the words its stored outcome reads as.
    static IReadOnlyList<(PaperTrade, double?)> PageTrades(IEnumerable<PickCell> cells, IReadOnlyList<PaperRow> own) =>
    [
        .. cells
            .Where(cell => cell.RepeatOf is null && cell.Status != PickStatus.Missing && cell.Buy is not null && cell.FilledOn is not null)
            .GroupBy(cell => cell.Night)
            .SelectMany(night => night.Select((cell, place) => (new PaperTrade(
                cell.Ticker,
                cell.FilledOn!.Value,
                cell.Buy!.Value,
                cell.Stop,
                cell.SoldOn ?? cell.EndedOn,
                cell.Sale
                    ?? PaperAccount.SaleOf(cell.Buy!.Value, cell.Stop, cell.EndedOn, cell.Result, false, cell.Planned)
                    ?? (cell.EndedOn is not null && cell.ReturnPct is { } made ? cell.Buy!.Value * (1m + (Statistic.ToPrice(made) / 100m)) : null),
                null,
                place + 1,
                null,
                cell.Planned,
                cell.Buy,
                cell.Night,
                cell.Target,
                cell.Trailing && cell.Planned is { } planned && cell.Stop is { } stop ? planned - stop : null,
                cell.Cap,
                cell.Past,
                own.FirstOrDefault(row => row.Ticker == cell.Ticker && row.Bought == cell.Night)?.Edge,
                cell.Status switch
                {
                    PickStatus.Target => PaperAccount.ReachedTarget,
                    PickStatus.Stopped => cell.Trailing ? PaperAccount.UnderTrail : PaperAccount.UnderStop,
                    PickStatus.Time => PaperAccount.TimeLimit,
                    _ => null,
                },
                FiredOn: cell.FiredOn,
                FiredWhy: cell.FiredWords), cell.Result))),
    ];

    // The S&P 500's page list's trades no fill bought, a repeat listing and one with no outcome stored left out.
    static IReadOnlyList<(string Ticker, DateOnly Listed, string Why)> PageUnfilled(IEnumerable<PickCell> cells) =>
    [
        .. cells
            .Where(cell => cell.RepeatOf is null && cell.Status != PickStatus.Missing && cell.Buy is not null && cell.FilledOn is null)
            .Select(cell => (cell.Ticker, cell.Night, cell.Unfilled ?? PaperAccount.Waiting)),
    ];

    // The operator's taken trades on an index as their account's: each bought at its fill on its fill's session, sold at
    // its end or its recorded exit, a holding's result read as a fraction of its fill.
    public static IReadOnlyList<(PaperTrade Trade, double? Result, string Family)> Yours(IReadOnlyList<TakenTradeRow> taken, string index) =>
    [
        .. taken.Where(trade => trade.Index == index).Select(trade =>
        {
            var book = trade.Family == HeavyweightRule.Name;

            return (new PaperTrade(
                trade.Ticker,
                trade.FillDate,
                trade.Fill,
                book ? null : trade.Stop,
                trade.EndedOn ?? trade.ExitDate,
                trade.EndPrice ?? trade.ExitPrice,
                null,
                0,
                book ? BookHoldings : null), book && trade.Result is { } percent ? percent / 100 : trade.Result, trade.Family);
        }),
    ];

    // The family's words a provisional account is named by.
    static string FamilyWords(string family) =>
        family == HeavyweightRule.Name ? SetupFamilies.SectorHeavyweights.Heading : SetupFamilies.Named(family)?.Label ?? family;

    // Every account's trades priced with their round trips: the stored round trip, else the member readings' cost on the
    // buy session, else the published table at the company value read that session; none where its bar is not held.
    public static IReadOnlyList<PaperAccountSource> Costed(
        IReadOnlyList<PaperAccountSource> accounts,
        IReadOnlyList<PaperRow> rows,
        IReadOnlyDictionary<(string Ticker, DateOnly Session), (double? Cost, decimal? Value)> members,
        IReadOnlyDictionary<(string Ticker, DateOnly Session), decimal> closes)
    {
        return
        [
            .. accounts.Select(account => account with
            {
                Trades =
                [
                    .. account.Trades.Select(one =>
                    {
                        var trade = one.Trade;
                        var row = rows.FirstOrDefault(held => held.Ticker == trade.Ticker && held.Bought == trade.ListedOn && account.Keeps(held));
                        var member = members.GetValueOrDefault((trade.Ticker, trade.Bought));

                        return (trade with
                        {
                            RoundTrip = PaperAccount.RoundTripOf(trade.Buy, trade.Stop, row?.CostRisk, row?.CostFraction, member.Cost, closes.ContainsKey((trade.Ticker, trade.Bought)), member.Value, trade.Planned),
                        }, one.Result);
                    }),
                ],
            }),
        ];
    }

    // Each account walked and drawn: its verdict off the newest per-rule test, its test years, its walk where the account
    // settings are set, and its record's label, in the one family order, each family's live or provisional rule first and
    // its variants by their numbers; and beside them the figures the core works for its page, its trades against its
    // family's others', its years better, each test year as a box, the chance luck alone would do as well, its record's
    // progress and the trades its walk holds open and has closed.
    public static IReadOnlyList<PaperAccountView> Views(
        IReadOnlyList<PaperAccountSource> accounts,
        IReadOnlyList<RuleTestRow> tests,
        IReadOnlyList<RuleTestYearRow> years,
        AccountSettings? settings,
        IReadOnlyList<DateOnly> sessions,
        DateOnly night,
        Func<string, DateOnly, decimal?> close,
        Func<DateOnly, decimal?> fund)
    {
        var views = accounts.Select(account =>
        {
            var test = account.Standing == PaperAccountView.You
                ? null
                : account.Tested is null
                    ? tests.FirstOrDefault(row => row.Family == account.Family && row.Standing != PaperAccountView.Variant)
                    : tests.FirstOrDefault(row => row.Family == account.Family && row.Rule == account.Tested);
            var verdict = account.Standing == PaperAccountView.You
                ? RuleVerdict.Yours
                : test is null ? RuleVerdict.NotTested : RuleVerdict.Of(test.Passed, test.Why, test.Adjusted, test.Gate, test.Stable, test.Trimmed, test.Counts);
            // A rule's account caps each position at a tenth of its value whatever the settings state; the operator's own walks
            // the settings their cards size by.
            // see: Each rule's paper account sizes a position by its risk and caps it at a tenth of the account, whatever a pick's card is capped at
            var walked = settings is { } held && account.Since is { } from
                ? PaperAccount.Walk(account.Standing == PaperAccountView.You ? held : held with { PositionCap = PaperAccount.AccountCap }, [.. account.Trades.Select(one => one.Trade)], [.. sessions.Where(session => session >= from && session <= night)], close, fund)
                : null;
            var ended = account.Trades.Count(one => one.Trade.Ended is not null);
            var months = account.Since is { } first ? RuleVerdict.MonthsBetween(first, night) : 0;
            PaperYearView[] tested = test is null
                ? []
                : [.. years.Where(year => year.Family == test.Family && year.Rule == test.Rule).Select(year => new PaperYearView(year.Year, year.Complete, year.Units, year.Total, year.ComparedUnits, year.ComparedTotal))];
            var own = account.Trades.Select(one => one.Trade).ToArray();
            var family = accounts.Where(other => other.Family == account.Family && other.Standing != PaperAccountView.You && other.Key != account.Key);

            // A variant's trades are read against every trade its live or provisional rule's list took, its earlier
            // versions' and the same trades before its version among them, and the rule's own against its variants' counted
            // trades.
            IEnumerable<PaperTrade> Taken(PaperAccountSource other) =>
                other.Trades.Select(one => one.Trade).Concat(account.Standing == PaperAccountView.Variant ? (other.Earlier ?? []).Select(one => one.Trade).Concat(other.Same.Select(one => one.Trade)) : []);
            var (open, closed) = walked is { } walk ? PaperAccount.Held(walk, night) : (0, 0);

            return new PaperAccountView(
                account.Key,
                account.Family,
                account.Standing,
                account.Name,
                account.Words,
                verdict,
                test?.Against,
                account.Book ? MarkRenderer.LoopPoints : "risks",
                tested,
                walked,
                account.Trades.Count,
                ended,
                account.Standing == PaperAccountView.You ? null : RuleVerdict.TooFew(account.Book, ended, months),
                account.Since,
                test?.Adjusted,
                RuleVerdict.CountLine(account.Book, account.Trades.Count, ended, months, account.Standing == PaperAccountView.You),
                account.Version,
                account.Earlier?.Count ?? 0,
                account.Short,
                account.Changes,
                account.Standing == PaperAccountView.You
                    ? null
                    : PaperAccount.Overlap(own, [.. (account.Standing == PaperAccountView.Variant ? family.Where(other => other.Standing != PaperAccountView.Variant) : family).SelectMany(Taken)]),
                test?.Better,
                test?.Counted,
                [.. tested.Select(year => PaperAccount.Year(year.Year, year.Complete, year.Units, year.Total, year.ComparedTotal))],
                test?.Adjusted is { } adjusted ? PaperAccount.Luck(adjusted) : null,
                account.Standing == PaperAccountView.You ? null : PaperAccount.Progress(account.Book, own, account.Since, night),
                open,
                closed,
                account.ReadsReleases ? ToneNote : null)
            {
                JudgedFrom = account.JudgedFrom,
                VersionFrom = account.VersionFrom,
                Tested = test?.Rule,
            };
        });

        return
        [
            .. views
                .GroupBy(view => view.Family)
                .OrderBy(group => SetupFamilies.OrderOf(group.Key))
                .SelectMany(group => group.OrderBy(view => view.Head ? 0 : 1)),
        ];
    }

    // Paper trading's page: the trades the live and provisional rules bought, those of the family the chips show or every
    // family, open and closed and newest first, each open one's figures tonight with the variants of its family holding it
    // too and each closed one's ending; the trades of earlier versions, folded; the stocks they did not buy; the trades only
    // the chosen family's variants took; and the index's fund over the days the tiles count, from the earliest start among
    // them. A holding's standing tonight is handed in, worked by the heavyweights rule's own functions.
    public static PaperPageView Page(
        IReadOnlyList<PaperAccountSource> sources,
        IReadOnlyList<PaperAccountView> views,
        DateOnly night,
        IReadOnlyList<DateOnly> sessions,
        Func<string, DateOnly, decimal?> close,
        IReadOnlyDictionary<string, PaperStockRow> stocks,
        Func<string, string?> standing,
        string? show,
        string variants,
        string fundName,
        Func<DateOnly, decimal?> fund,
        PaperCombinedView? combined = null)
    {
        var heads = views.Where(view => view.Head && (show is null || view.Family == show)).ToArray();
        var source = sources.ToDictionary(account => account.Key, StringComparer.Ordinal);

        double? Move(string ticker) => stocks.TryGetValue(ticker, out var held) ? held.TypicalMove : null;

        string? Company(string ticker) => stocks.TryGetValue(ticker, out var held) ? held.Company : null;

        PaperPosition? Opened(PaperTrade trade) =>
            trade.Ended is null || trade.Ended > night ? PaperAccount.Position(trade, night, sessions, close, Move(trade.Ticker), trade.Stop is null ? standing(trade.Ticker) : null) : null;

        PaperEnded? Shut(PaperTrade trade, double? result) =>
            trade.Ended is { } end && end <= night ? PaperAccount.Ended(trade, result, sessions, close) : null;

        IReadOnlyList<(string Name, IReadOnlyList<PaperTrade> Trades)> VariantsOf(string family) =>
        [
            .. views
                .Where(view => view.Family == family && view.Standing == PaperAccountView.Variant)
                .Select(view => (view.Short ?? view.Name, (IReadOnlyList<PaperTrade>)[.. source[view.Key].Trades.Select(one => one.Trade)])),
        ];

        PaperBoughtView Drawn(PaperAccountView head, PaperTrade trade, double? result, string? version) =>
            new(
                head.Key,
                head.Family,
                trade.Ticker,
                Company(trade.Ticker),
                trade.ListedOn,
                trade.Bought,
                trade.Buy,
                trade.Stop,
                trade.Target,
                trade.Trail is not null,
                trade.Cap,
                trade.Ended,
                Opened(trade),
                Shut(trade, result),
                version is null && (trade.Ended is null || trade.Ended > night) ? PaperAccount.HeldBy(trade.Ticker, night, VariantsOf(head.Family)) : [],
                trade.Past,
                version,
                PaperAccount.Selling(trade, night, close));

        var bought = heads
            .SelectMany(head => (head.Walk?.Trades ?? [])
                .Where(one => one.Skipped is null)
                .Select(one => Drawn(head, one.Trade, source[head.Key].Trades.FirstOrDefault(own => own.Trade == one.Trade).Result, null)))
            .OrderByDescending(trade => trade.Bought)
            .ThenBy(trade => SetupFamilies.OrderOf(trade.Family))
            .ThenBy(trade => trade.Ticker, StringComparer.Ordinal)
            .ToArray();
        PaperBoughtView[] earlier =
        [
            .. heads
                .SelectMany(head => (source[head.Key].Earlier ?? []).Select(one => Drawn(head, one.Trade, one.Result, one.Version)))
                .OrderByDescending(trade => trade.Bought)
                .ThenBy(trade => trade.Ticker, StringComparer.Ordinal),
        ];
        // Each account's same trades before its version, drawn apart under the account and in none of its figures.
        // see: A paper account counts from its rule's current version's first session, and the same trades before it stand apart under the account
        PaperSameView[] same =
        [
            .. heads
                .Where(head => source[head.Key] is { SameSince: not null, Same.Count: > 0 })
                .Select(head => new PaperSameView(
                    head.Family,
                    source[head.Key].SameSince!.Value,
                    [.. source[head.Key].Same.Select(one => Drawn(head, one.Trade, one.Result, null)).OrderByDescending(trade => trade.Bought).ThenBy(trade => trade.Ticker, StringComparer.Ordinal)])),
        ];
        var chosen = views.FirstOrDefault(view => view.Head && view.Family == variants);
        var headTrades = chosen is null ? [] : source[chosen.Key].Trades.Select(one => one.Trade).Concat((source[chosen.Key].Earlier ?? []).Select(one => one.Trade)).Concat(source[chosen.Key].Same.Select(one => one.Trade)).ToArray();
        PaperOnlyView[] only =
        [
            .. views
                .Where(view => view.Family == variants && view.Standing == PaperAccountView.Variant)
                .SelectMany(view => source[view.Key].Trades
                    .Where(one => !headTrades.Any(trade => trade.Ticker == one.Trade.Ticker && trade.ListedOn == one.Trade.ListedOn))
                    .Select(one => new PaperOnlyView(one.Trade.Ticker, Company(one.Trade.Ticker), [view.Short ?? view.Name], one.Trade.ListedOn, one.Trade.Bought, one.Trade.Buy, one.Trade.Stop is null, Opened(one.Trade), Shut(one.Trade, one.Result))))
                .OrderByDescending(trade => trade.Bought)
                .ThenBy(trade => trade.Ticker, StringComparer.Ordinal),
        ];
        var from = views.Where(view => view.Head && view.Since is not null).Select(view => view.Since).Min();
        var counted = from is { } start ? PaperAccount.FundOver([.. sessions.Where(session => session >= start && session <= night)], fund) : null;

        return new PaperPageView(
            views,
            [.. bought.Where(trade => trade.Open is not null)],
            [.. bought.Where(trade => trade.Closed is not null)],
            earlier,
            PaperAccount.NotBought([.. heads.Select(head => (head.Walk, source[head.Key].Unfilled ?? []))]),
            only,
            new PaperFundView(fundName, counted?.Return, from, counted?.From),
            combined)
        {
            Same = same,
        };
    }

    // From the 21.11 correction of 2026-11-07 the live setups' accounts, averaged, on an index: each family's live or
    // provisional account walked, given an equal share of one account of the settings' size, over the sessions from the day
    // every one of them counts, the latest of their starts; none before the settings are set or where no such account is
    // walked. Each trade as its own setup's account bought it, with its return on its position, its sale over its buy, or
    // tonight's close over its buy session's while it is open.
    // see: The averaged account gives each live rule's own account an equal share, from the day every one of them counts
    public static PaperCombinedView? Averaged(
        IReadOnlyList<PaperAccountView> views,
        AccountSettings? settings,
        IReadOnlyList<DateOnly> sessions,
        DateOnly night,
        Func<string, DateOnly, decimal?> close,
        Func<DateOnly, decimal?> fund)
    {
        var heads = views.Where(view => view.Head && view.Since is not null && view.Walk is not null).OrderBy(view => SetupFamilies.OrderOf(view.Family)).ToArray();

        if (settings is not { } held || heads.Length == 0)
        {
            return null;
        }

        var since = heads.Max(view => view.Since!.Value);
        var walked = PaperAccount.Averaged(held.Size, [.. heads.Select(view => (view.Family, view.Walk!))], [.. sessions.Where(session => session >= since && session <= night)], fund);
        var (open, closed) = PaperAccount.Held(walked.Walk, night);

        double? ReturnOf(PaperTrade trade) =>
            trade.Ended is { } ended && ended <= night
                ? trade.Sale is { } sale && trade.Buy > 0m ? Statistic.FromRatio((sale / trade.Buy) - 1m) : null
                : close(trade.Ticker, night) is { } now && (trade.MarkedFrom ?? close(trade.Ticker, trade.Bought)) is { } then && then > 0m ? Statistic.FromRatio((now / then) - 1m) : null;

        return new PaperCombinedView(
            walked.Walk,
            since,
            open,
            closed,
            walked.Accounts,
            [
                .. walked.Trades.Select(one => new PaperCombinedTradeView(
                    one.Family,
                    one.Taken.Trade.Ticker,
                    one.Taken.Trade.Bought,
                    one.Taken.Trade.Buy,
                    one.Taken.Shares,
                    one.Taken.Skipped,
                    one.Taken.Trade.Ended,
                    one.Taken.Skipped is null ? ReturnOf(one.Taken.Trade) : null,
                    one.Taken.Trade.Ended is null || one.Taken.Trade.Ended > night)),
            ]);
    }

    // An account's trade log: each trade with its result in the account's unit, an open one's so far at tonight's close
    // by the ratio of its closes, and why the walk skipped it where it did.
    public static IReadOnlyList<PaperTradeView> Log(PaperAccountSource account, PaperWalk? walk, DateOnly night, Func<string, DateOnly, decimal?> close)
    {
        var skipped = walk?.Trades.Where(one => one.Skipped is not null).ToDictionary(one => (one.Trade.Ticker, one.Trade.Bought), one => one.Skipped) ?? [];

        return [.. account.Trades.Select(one => Logged(account.Book, one.Trade, one.Result, night, close, skipped.GetValueOrDefault((one.Trade.Ticker, one.Trade.Bought)), null))];
    }

    // The trades an account leaves out as listed before the version it counts, each with the version that listed it and an
    // open one's result so far as the log reads it; walked by nothing.
    public static IReadOnlyList<PaperTradeView> EarlierLog(PaperAccountSource account, DateOnly night, Func<string, DateOnly, decimal?> close) =>
    [
        .. (account.Earlier ?? []).Select(one => Logged(account.Book, one.Trade, one.Result, night, close, null, one.Version)),
        .. account.SameSince is { } since ? account.Same.Select(one => Logged(account.Book, one.Trade, one.Result, night, close, null, MarkRenderer.SameWords(since))) : [],
    ];

    static PaperTradeView Logged(bool book, PaperTrade trade, double? stored, DateOnly night, Func<string, DateOnly, decimal?> close, string? skipped, string? version)
    {
        var open = trade.Ended is null;
        var result = stored;

        if (open && close(trade.Ticker, night) is { } now && close(trade.Ticker, trade.Bought) is { } then && then > 0m)
        {
            var ratio = now / then;

            result = book || trade.Stop is null
                ? Statistic.FromRatio(ratio - 1m)
                : trade.Buy > trade.Stop ? Statistic.FromRatio((ratio - 1m) * trade.Buy / (trade.Buy - trade.Stop.Value)) : null;
        }
        else if (open)
        {
            result = null;
        }

        return new PaperTradeView(trade.Ticker, trade.Bought, trade.Buy, trade.Stop, trade.Ended, result, open, skipped, version, trade.Halved);
    }
}
