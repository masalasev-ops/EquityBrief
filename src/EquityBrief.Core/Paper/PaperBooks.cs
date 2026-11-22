using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Paper;

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
    // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
    public IReadOnlyList<(PaperTrade Trade, double? Result)> Same { get; init; } = [];

    // The revisions of its rule up to the one it stands in, the last that one; none for an account no registration starts.
    public IReadOnlyList<RevisionView> Revisions { get; init; } = [];

    public DateOnly? SameSince { get; init; }

    // From the 19.4 correction of 2026-11-07 a version's reach against its live rule, setting by setting; none for a live
    // or provisional rule, the operator's own, or a version whose live rule holds no registered settings to read against.
    // see: A version is marked as narrowing or widening its live rule by the direction of each setting it changes
    public Core.Cards.VersionReachKind? Reach { get; init; }

    // A variant's settings that differ from its running revision, each a plain sentence; none for any other account.
    public IReadOnlyList<ChangeLine> Differences { get; init; } = [];

    // Whether a stored trade is one of this account's: kept under its name, or under its name and the filter version it
    // was listed from.
    public bool Keeps(PaperRow row) =>
        Listed is { } name && (row.Account == name || row.Account.StartsWith(name + ", from version ", StringComparison.Ordinal));
}

// The projection from stored trades and test rows to paper trading's accounts and their trade logs. The accounts are an
// index's: each family's live or provisional rule's list, the page's own, each registered variant's own list or book,
// numbered as its card numbers it, and the operator's own trades. Each is walked by the core's one function from the
// account settings, and every figure its page draws beside the walk, an open trade's standing, a closed trade's ending,
// the stocks not bought, a rule's trades against its variants', its test years, the chance luck alone would do as well
// and its record's progress, is the core's too, read off the newest per-rule test and its own trades, never merged. Not in
// the read API, whose claim is that it computes nothing, and not in the page, whose claim is the same.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
// see: A rule's scorecard draws its test years and its record since it went live as two halves, and never adds them together
public static class PaperBooks
{
    // The operator's own account's key.
    public const string YouKey = "you";

    // The name the S&P 500's page list's trades are kept under, beside each family's name.
    public const string PageList = "list";

    // The unit a book's record is read in.
    public const string Points = "points";

    // The name a family's own step keeps its provisional rule's trades under, before any freeze registers one.
    public const string Provisional = "provisional";

    // An index's code by the name a reader knows it by, the S&P 500's where the name is none of the three.
    public static string IndexCodeOf(string indexName) =>
        new[] { FundHoldings.MidCapIndex, FundHoldings.SmallCapIndex }.FirstOrDefault(code => OwnStepCandidate.NameOf(code) == indexName) ?? "GSPC";

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

    static string ListKey(string family) => PageList + "-" + family;

    // From the 19.4 correction of 2026-11-07 how each monthly setup sells a holding with no price stop, in the operator's
    // words, and a Fundamentals holding's state tonight by its rule's hold score.
    // see: A holding with no price stop draws how its rule sells it and whether it still qualifies tonight by its own rule
    public static string HeavyweightSells(bool underAverage, DateOnly? next) =>
        "Sold at the reshuffle if it no longer leads its sector" + (underAverage ? " or on a close under its 200-day average" : string.Empty) + NextOn(next);

    public static string QualitySells(DateOnly? next) => "Sold at the reshuffle if it no longer qualifies" + NextOn(next);

    public static string FundamentalsSells(int holdInNine, int sessions) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Sells when a filing drops it below {holdInNine} in every 9 of the checks that apply, or after {sessions} trading days");

    public static string FundamentalsTonight(int passed, int applied, int holdInNine) =>
        Core.Ledger.FundamentalScorecard.Clears(passed, applied, holdInNine)
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Still qualifies: {passed} of {applied} apply, at least {holdInNine} in every 9")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"No longer qualifies: {passed} of {applied} apply, under {holdInNine} in every 9");

    static string NextOn(DateOnly? next) =>
        next is { } on ? ", next on " + on.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    // The words a provisional account and the operator's own name what they count, having no registered version.
    public const string ProvisionalVersion = "provisional settings, no registered version: every trade since its first";

    public const string YourVersion = "your own trades: every one since your first";

    // The words an earlier trade listed before its account's rule first registered is named by.
    public const string BeforeRegistration = "before its first registration";

    // A registered version as the page names it: the day it registered and its code version.
    public static string VersionWords(CandidateRow row) =>
        "the version registered " + Stamp(DateOnly.FromDateTime(row.RegisteredAt.UtcDateTime)) + (row.EvaluatorVersion.Length > 0 ? ", code " + row.EvaluatorVersion : string.Empty);

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    // The revisions of a rule's registrations and the changes in its code, in the order they ran.
    // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
    // see: Any change that changes what a rule's trades do starts a new revision, in its settings or in its code
    public static IReadOnlyList<RuleRevision> RevisionsOf(IReadOnlyList<CandidateRow> lineage) =>
        RuleRevisions.Of(
            lineage.Select(row => (row.Id, row.Candidate, row.RegisteredAt, row.Parameters)),
            lineage.Count > 0 ? [.. CodeRevisions.Of(lineage[0].Evaluator).Where(change => change.From <= (Reading.Value ?? DateOnly.MaxValue))] : []);

    // The night a page reads, which a change in code that ran after it does not reach; none outside a page's read.
    static readonly AsyncLocal<DateOnly?> Reading = new();

    // The night a page reads its revisions as of, until the returned scope is disposed.
    public static IDisposable ReadingNight(DateOnly night)
    {
        var before = Reading.Value;

        Reading.Value = night;

        return new Restored(() => Reading.Value = before);
    }

    sealed class Restored(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    // The revision a standing registration stands in, none where the lineage does not hold it.
    public static RuleRevision? RevisionOf(CandidateRow standing, IReadOnlyList<CandidateRow> lineage) =>
        RevisionsOf([.. lineage.Where(row => row.RegisteredAt <= standing.RegisteredAt)]) is { Count: > 0 } revisions ? revisions[^1] : null;

    // Where a registered rule's account starts without a lineage to read its revision from: its standing registration's first
    // session, never earlier, whatever a replay carried its record back to.
    // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
    public static DateOnly StartOf(CandidateRow standing) => FamilyRecords.FirstSession(standing.RegisteredAt);

    // Where the Run page's record of a registered rule counts from: its standing registration's first session, or the
    // session a replay under its version found it counting from where that replay reproduced every trade. The S&P 400's and
    // 600's records read no replay.
    public static DateOnly RecordFrom(CandidateRow standing, IReadOnlyList<FamilyReplayRow> replays) =>
        FamilyRecords.StartOf(
            new RegisterRow(standing.Id, standing.Candidate, string.Empty, string.Empty, standing.Evaluator, standing.Parameters, standing.EvaluatorVersion, standing.Event, standing.Retires, standing.RegisteredAt, standing.Evidence),
            replays).First;

    // The version that listed a trade: its rule's newest registration whose first session is on or before the trade's buy.
    public static string VersionAt(IReadOnlyList<RevisionView> revisions, DateOnly listed) =>
        revisions.LastOrDefault(revision => revision.From <= listed) is { } revision ? revision.Name : BeforeRegistration;

    public static string VersionAt(IReadOnlyList<CandidateRow> lineage, DateOnly bought) =>
        RuleRevisions.At(RevisionsOf(lineage), bought) is { } revision
            ? RevisionView.Named(revision.Number)
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
        var revision = RevisionOf(standing, lineage);
        var start = revision?.From ?? StartOf(standing);
        var record = RecordFrom(standing, replays);
        bool IsSame((PaperTrade Trade, double? Result) one) => one.Trade.ListedOn >= record && one.Trade.ListedOn < start;

        return Split(account, [.. trades.Where(one => !IsSame(one))], start, revision is not null ? RevisionView.Named(revision.Number) : VersionWords(standing), lineage) with
        {
            VersionFrom = start,
            Same = [.. trades.Where(IsSame)],
            SameSince = record < start ? record : null,
            Revisions = RevisionViews([.. lineage.Where(row => row.RegisteredAt <= standing.RegisteredAt)]),
        };
    }

    // Each revision of a rule as a page draws it: its number, the sessions it ran and what it changed from the one before, in
    // the words its chips are drawn in.
    // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
    public static IReadOnlyList<RevisionView> RevisionViews(IReadOnlyList<CandidateRow> lineage)
    {
        var revisions = RevisionsOf(lineage);
        var views = new List<RevisionView>();
        IReadOnlyList<RuleChip>? before = null;
        CandidateRow? beforeRow = null;

        foreach (var revision in revisions)
        {
            var first = lineage.FirstOrDefault(row => row.Id == revision.FirstId);
            var chips = first is not null ? RuleRegister.ChipsOf(first) : [];
            string[]? changed = before is null
                ? null
                : revision.Code is { } code ? [code]
                : RuleSentences.Between(before, chips).Select(change => change.Sentence).ToArray() is { Length: > 0 } sentences
                    ? sentences
                    : beforeRow is not null && first is not null ? [.. RuleSentences.Parameters(CandidateEvaluator.Read(beforeRow.Parameters), CandidateEvaluator.Read(first.Parameters))] : [];

            views.Add(new RevisionView(revision.Number, revision.From, revision.Until, changed is null ? null : changed.Length == 0 ? RevisionView.CodeOnly : RuleSentences.Joined(changed)) { Registration = revision.FirstId });
            before = chips;
            beforeRow = first;
        }

        return views;
    }

    // From the 22.25 correction of 2026-11-11 a registered rule's account with the trades its family's earlier live
    // registrations kept under other words, which an approved change rewording the live rule left in no group: each among
    // the earlier versions' whatever night listed it, never this version's or the same, named by the version that listed it,
    // and a stock its own words kept at the same listing night counted once.
    // see: Every count of trades names its group and one function works the four groups
    // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
    static PaperAccountSource WithEarlierWords(PaperAccountSource account, IReadOnlyList<PaperRow> rows, CandidateRow live, IReadOnlyList<CandidateRow> lineage)
    {
        string[] words = [.. lineage.Select(row => row.Candidate).Where(candidate => !string.Equals(candidate, live.Candidate, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal)];

        if (words.Length == 0)
        {
            return account;
        }

        var counted = account.Trades.Select(one => one.Trade)
            .Concat(account.Same.Select(one => one.Trade))
            .Concat((account.Earlier ?? []).Select(one => one.Trade))
            .Select(trade => (trade.Ticker, trade.ListedOn))
            .ToHashSet();
        (PaperTrade Trade, double? Result, string Version)[] earlier =
        [
            .. words
                .SelectMany(name => Of(rows.Where(row => row.Account == name || row.Account.StartsWith(name + ", from version ", StringComparison.Ordinal))))
                .Where(one => counted.Add((one.Item1.Ticker, one.Item1.ListedOn)))
                .Select(one => (one.Item1, one.Item2, VersionAt(lineage, one.Item1.ListedOn))),
        ];

        return earlier.Length == 0 ? account : account with { Earlier = [.. account.Earlier ?? [], .. earlier] };
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

    // A version's account with its reach against its live rule's registered settings, none where the family stands at no
    // registered rule.
    static PaperAccountSource Reached(PaperAccountSource version, string family, CandidateRow? live, CandidateRow row, IReadOnlyList<CandidateRow>? registrations = null) =>
        live is null
            ? version
            : version with
            {
                Reach = Core.Cards.VersionReach.Of(family, CandidateEvaluator.Read(live.Parameters), CandidateEvaluator.Read(row.Parameters)),
                Differences = Named(RuleSentences.Between(RuleRegister.ChipsOf(live), RuleRegister.ChipsOf(row)), BaseOf(registrations ?? [], row) is { } beside ? RuleSentences.Between(RuleRegister.ChipsOf(beside), RuleRegister.ChipsOf(row)) : []),
            };

    // The rule a variant was registered beside: its family's newest live registration on the index at or before the
    // variant's first, none where the register holds none.
    static CandidateRow? BaseOf(IReadOnlyList<CandidateRow> registrations, CandidateRow row)
    {
        var first = registrations.Where(one => one.Candidate == row.Candidate).Select(one => one.RegisteredAt).DefaultIfEmpty(row.RegisteredAt).Min();

        return registrations.Where(one => FamilyRecords.IsLive(one.Candidate) && one.RegisteredAt <= first).MaxBy(one => one.RegisteredAt);
    }

    // A variant's differences from its running revision with the one it is named by first: the setting it changed from the
    // rule it was registered beside, the plan aside, where the two read one; the rest as they fall.
    static IReadOnlyList<ChangeLine> Named(IReadOnlyList<ChangeLine> fromRunning, IReadOnlyList<ChangeLine> fromBase)
    {
        var named = fromBase.Where(change => change.Key != "hooks").Select(change => change.Key).ToHashSet(StringComparer.Ordinal);
        var first = fromRunning.FirstOrDefault(change => named.Contains(change.Key));

        return first is null ? fromRunning : [first, .. fromRunning.Where(change => !ReferenceEquals(change, first))];
    }

    // A variant's tab is its number and the settings it differs by: its short name is the number, and what it changes the
    // rest.
    const string TabJoint = " · ";

    public static string TabShort(string tab) => tab.IndexOf(TabJoint, StringComparison.Ordinal) is var at and > 0 ? tab[..at] : tab;

    public static string TabChanges(string tab) => tab.IndexOf(TabJoint, StringComparison.Ordinal) is var at and > 0 ? tab[(at + TabJoint.Length)..] : tab;

    // Every account on an index in the one family order, each family's live or provisional rule first and its variants
    // after it as its card numbers them, then the operator's own. A registered rule's account counts the trades of its
    // current version alone, from its version's first session; a provisional account and the operator's own count every
    // trade from their first. The register is read as the night's page reads it, as of the instant given where a night run
    // again read it later, and the rule a freeze keeps in the background has no account.
    // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
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
        using var reading = ReadingNight(night);
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
                var (liveDip, versions) = RuleRegister.Accounts(family, large, indexName, register, night, null, asOf);
                var dipChips = liveDip is not null
                    ? RuleRegister.ChipsOf(liveDip)
                    : [.. DipRule.Provisional.KeyedChipsOn(IndexCodeOf(indexName)).Select(chip => new RuleChip(chip.Key, chip.Words))];
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
                    liveDip?.Candidate ?? PageList + " " + family,
                    Short: liveDip is not null ? "Live rule" : "Provisional rule",
                    Changes: dipChips.Count > 0 ? string.Join(TabJoint, dipChips.Select(chip => chip.Words)) : null);
                var dipRegistrations = RuleRegister.Registrations(family, large, indexName, register);

                dipHead = dipHead with { Unfilled = Unfilled(rows.Where(dipHead.Keeps)) };
                IReadOnlyList<CandidateRow> dipLineage = [.. dipRegistrations.Where(row => FamilyRecords.IsLive(row.Candidate))];

                accounts.Add(liveDip is not null
                    ? WithEarlierWords(Started(dipHead, Of(rows.Where(dipHead.Keeps)), liveDip, [], dipLineage), rows, liveDip, dipLineage)
                    : dipHead with { Trades = Of(rows.Where(dipHead.Keeps)), Since = rows.Where(dipHead.Keeps).Select(row => (DateOnly?)row.Bought).Min(), Version = ProvisionalVersion, Earlier = [] });

                foreach (var (row, tab) in versions)
                {
                    var version = new PaperAccountSource(RuleRegister.Slug(row.Candidate), family, PaperAccountView.Variant, tab, row.Candidate, row.Candidate, false, [], null, row.Candidate, Short: TabShort(tab), Changes: TabChanges(tab));

                    accounts.Add(Reached(Started(version with { Unfilled = Unfilled(rows.Where(version.Keeps)) }, Of(rows.Where(version.Keeps)), row, [], [.. dipRegistrations.Where(one => one.Candidate == row.Candidate)]), family, liveDip, row, dipRegistrations));
                }

                continue;
            }

            var (live, variants) = RuleRegister.Accounts(family, large, indexName, register, night, large ? null : provisional, asOf);
            var book = family == HeavyweightRule.Name;
            var pageList = large && SetupFamilies.InPageOrder.Any(swing => swing.Name == family);
            var listed = pageList ? null : !large && live is not null ? live.Candidate : PageList + " " + family;
            var chips = live is not null
                ? RuleRegister.ChipsOf(live)
                : (!large || family == FundamentalsRule.Name) && provisional is not null ? RuleRegister.ProvisionalChips(family, provisional) : [];

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
            var registrations = RuleRegister.Registrations(family, large, indexName, register);

            head = head with { Unfilled = pageList ? PageUnfilled(cells) : Unfilled(rows.Where(head.Keeps)) };

            // A provisional account starts at the first night its list kept a trade, filled at the next open or not, so
            // pricing entries at the open moves no account's start.
            var first = pageList
                ? cells.Where(cell => cell.RepeatOf is null && cell.Status != PickStates.Missing && cell.Buy is not null).Select(cell => (DateOnly?)cell.Night).Min()
                : rows.Where(head.Keeps).Select(row => (DateOnly?)row.Bought).Min();

            IReadOnlyList<CandidateRow> lineage = [.. registrations.Where(row => FamilyRecords.IsLive(row.Candidate))];

            // The S&P 500's swing heads read the page list, which every version of the family keeps, and no earlier words.
            accounts.Add(live is not null
                ? pageList
                    ? Started(head, trades, live, replayed, lineage)
                    : WithEarlierWords(Started(head, trades, live, replayed, lineage), rows, live, lineage)
                : head with { Trades = trades, Since = first, Version = ProvisionalVersion, Earlier = [] });

            foreach (var (row, tab) in variants)
            {
                var variant = new PaperAccountSource(
                    RuleRegister.Slug(row.Candidate),
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

                accounts.Add(Reached(Started(variant with { Unfilled = Unfilled(rows.Where(variant.Keeps)) }, Of(rows.Where(variant.Keeps)), row, replayed, [.. registrations.Where(one => one.Candidate == row.Candidate)]), family, live, row, registrations));
            }
        }

        // From 22.11 Quality at a fair price's book after the setups', every index's: its live rule's holdings from its
        // freeze, or the provisional rule's from its first, and each version's beside it, named by what it changes.
        // see: A quality rule is registered as the family on its index and kept by the family's own step alone
        {
            var family = QualityRule.Name;
            var (live, versions) = RuleRegister.Accounts(family, large, indexName, register, night, null, asOf);
            var chips = live is not null ? RuleRegister.ChipsOf(live) : RuleChips.Of(RuleKind.Quality, QualityCandidate.ParametersOf(QualityRule.Starting));
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
                live?.Candidate ?? PageList + " " + family,
                Short: live is not null ? "Live rule" : "Provisional rule",
                Changes: chips.Count > 0 ? string.Join(TabJoint, chips.Select(chip => chip.Words)) : null);

            head = head with { Unfilled = Unfilled(rows.Where(head.Keeps)) };

            var registrations = RuleRegister.Registrations(family, large, indexName, register);
            IReadOnlyList<CandidateRow> lineage = [.. registrations.Where(row => FamilyRecords.IsLive(row.Candidate))];

            accounts.Add(live is not null
                ? WithEarlierWords(Started(head, Of(rows.Where(head.Keeps)), live, [], lineage), rows, live, lineage)
                : head with { Trades = Of(rows.Where(head.Keeps)), Since = rows.Where(head.Keeps).Select(row => (DateOnly?)row.Bought).Min(), Version = ProvisionalVersion, Earlier = [] });

            foreach (var (row, tab) in versions)
            {
                var version = new PaperAccountSource(RuleRegister.Slug(row.Candidate), family, PaperAccountView.Variant, tab, row.Candidate, row.Candidate, true, [], null, row.Candidate, Short: TabShort(tab), Changes: TabChanges(tab));

                accounts.Add(Reached(Started(version with { Unfilled = Unfilled(rows.Where(version.Keeps)) }, Of(rows.Where(version.Keeps)), row, [], [.. registrations.Where(one => one.Candidate == row.Candidate)]), family, live, row, registrations));
            }
        }

        // From 22.15 the Fundamentals family's trades after Quality at a fair price's, every index's: its live rule's from its
        // freeze, or the provisional rule's from its first night, and each version's beside it, named by what it changes.
        // see: A Fundamentals rule is registered as the family on its index and kept by the family's own step alone
        {
            var family = ImprovingRule.Name;
            var (live, versions) = RuleRegister.Accounts(family, large, indexName, register, night, null, asOf);
            var chips = live is not null ? RuleRegister.ChipsOf(live) : RuleChips.Of(RuleKind.Improving, ImprovingCandidate.ParametersOf(ImprovingGrid.Starting));
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
                live?.Candidate ?? PageList + " " + family,
                Short: live is not null ? "Live rule" : "Provisional rule",
                Changes: chips.Count > 0 ? string.Join(TabJoint, chips.Select(chip => chip.Words)) : null);

            head = head with { Unfilled = Unfilled(rows.Where(head.Keeps)) };

            var registrations = RuleRegister.Registrations(family, large, indexName, register);
            IReadOnlyList<CandidateRow> lineage = [.. registrations.Where(row => FamilyRecords.IsLive(row.Candidate))];

            accounts.Add(live is not null
                ? WithEarlierWords(Started(head, Of(rows.Where(head.Keeps)), live, [], lineage), rows, live, lineage)
                : head with { Trades = Of(rows.Where(head.Keeps)), Since = rows.Where(head.Keeps).Select(row => (DateOnly?)row.Bought).Min(), Version = ProvisionalVersion, Earlier = [] });

            foreach (var (row, tab) in versions)
            {
                var version = new PaperAccountSource(RuleRegister.Slug(row.Candidate), family, PaperAccountView.Variant, tab, row.Candidate, row.Candidate, true, [], null, row.Candidate, Short: TabShort(tab), Changes: TabChanges(tab));

                accounts.Add(Reached(Started(version with { Unfilled = Unfilled(rows.Where(version.Keeps)) }, Of(rows.Where(version.Keeps)), row, [], [.. registrations.Where(one => one.Candidate == row.Candidate)]), family, live, row, registrations));
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

        // A variant is a copy of its rule's running revision, so its account counts from that revision's first session where
        // its own registration is older, the trades before it left out as listed before that revision.
        // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
        return
        [
            .. accounts.Select(account => account.Standing == PaperAccountView.Variant
                && accounts.FirstOrDefault(head => head.Family == account.Family && head.Standing is PaperAccountView.Live or PaperAccountView.Provisional)?.Since is { } start
                ? FromRevision(account, start)
                : account),
        ];
    }

    public const string BeforeRevision = "before the running revision";

    static PaperAccountSource FromRevision(PaperAccountSource account, DateOnly start) =>
        account.Since is { } since && since >= start
            ? account
            : account with
            {
                Trades = [.. account.Trades.Where(one => one.Trade.ListedOn >= start)],
                Earlier = [.. account.Earlier ?? [], .. account.Trades.Where(one => one.Trade.ListedOn < start).Select(one => (one.Trade, one.Result, BeforeRevision))],
                Unfilled = [.. (account.Unfilled ?? []).Where(one => one.Listed >= start)],
                Since = start,
                VersionFrom = start,
            };

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

    // From 24.23 the records a promotion's swap carries: each carry hands the trades kept under one name and listed over its
    // sessions to the account kept under another, among that account's same trades, out of its own and its earlier ones, so
    // the promoted variant's trades count to the revision it became and the replaced revision's to the variant it became, each
    // stock a listing night counted once. An account no carry names is returned as it is.
    // see: A promotion swaps a running revision and its variant at the start of the next night that runs, each carrying its own record
    public static IReadOnlyList<PaperAccountSource> Carried(IReadOnlyList<PaperAccountSource> accounts, IReadOnlyList<PaperRow> rows, IReadOnlyList<PromotionCarry> carries)
    {
        if (carries.Count == 0)
        {
            return accounts;
        }

        return
        [
            .. accounts.Select(account =>
            {
                // A head names its live rule in its words where it lists the page's own trades under no name of its own.
                var into = carries.Where(carry => string.Equals(carry.To, account.Listed, StringComparison.Ordinal)
                    || (account.Standing == PaperAccountView.Live && string.Equals(carry.To, account.Words, StringComparison.Ordinal))).ToArray();

                if (into.Length == 0)
                {
                    return account;
                }

                bool Within(PromotionCarry carry, PaperTrade trade) => trade.ListedOn >= carry.Since && trade.ListedOn < carry.Until;

                // The trades kept under the name carried from, and where a head lists the page's own trades under its live rule,
                // the head's trades too, as the accounts stood before any carry.
                var carried = into
                    .SelectMany(carry => Of(rows.Where(row => row.Account == carry.From || row.Account.StartsWith(carry.From + ", from version ", StringComparison.Ordinal)))
                        .Concat(accounts.Where(from => from.Standing == PaperAccountView.Live && from.Listed is null && string.Equals(from.Words, carry.From, StringComparison.Ordinal))
                            .SelectMany(from => from.Trades.Concat((from.Earlier ?? []).Select(one => (one.Trade, one.Result)))))
                        .Where(one => Within(carry, one.Item1)))
                    .ToList();
                var seen = new HashSet<(string, DateOnly)>();
                (PaperTrade Trade, double? Result)[] same =
                [
                    .. account.Same,
                    .. carried.Where(one => !account.Same.Any(held => held.Trade.Ticker == one.Item1.Ticker && held.Trade.ListedOn == one.Item1.ListedOn)).Select(one => (one.Item1, one.Item2)),
                ];
                same = [.. same.Where(one => seen.Add((one.Trade.Ticker, one.Trade.ListedOn))).OrderBy(one => one.Trade.ListedOn).ThenBy(one => one.Trade.Ticker, StringComparer.Ordinal)];
                bool Carried(PaperTrade trade) => into.Any(carry => Within(carry, trade)) && same.Any(one => one.Trade.Ticker == trade.Ticker && one.Trade.ListedOn == trade.ListedOn);

                return account with
                {
                    Trades = [.. account.Trades.Where(one => !Carried(one.Trade))],
                    Earlier = account.Earlier is null ? null : [.. account.Earlier.Where(one => !Carried(one.Trade))],
                    Same = same,
                    SameSince = same.Length == 0 ? account.SameSince : new[] { account.SameSince, into.Min(carry => carry.Since) }.OfType<DateOnly>().Min(),
                };
            }),
        ];
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
            .Where(cell => cell.RepeatOf is null && cell.Status != PickStates.Missing && cell.Buy is not null && cell.FilledOn is not null)
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
                    PickStates.Target => PaperAccount.ReachedTarget,
                    PickStates.Stopped => cell.Trailing ? PaperAccount.UnderTrail : PaperAccount.UnderStop,
                    PickStates.Time => PaperAccount.TimeLimit,
                    _ => null,
                },
                FiredOn: cell.FiredOn,
                FiredWhy: cell.FiredWords), cell.Result))),
    ];

    // The S&P 500's page list's trades no fill bought, a repeat listing and one with no outcome stored left out.
    static IReadOnlyList<(string Ticker, DateOnly Listed, string Why)> PageUnfilled(IEnumerable<PickCell> cells) =>
    [
        .. cells
            .Where(cell => cell.RepeatOf is null && cell.Status != PickStates.Missing && cell.Buy is not null && cell.FilledOn is null)
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
                account.Book ? Points : "risks",
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
                Reach = account.Reach,
                Revisions = account.Revisions,
                Differences = account.Differences,
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

}
