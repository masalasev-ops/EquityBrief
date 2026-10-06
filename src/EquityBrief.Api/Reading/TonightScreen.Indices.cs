using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from an S&P 400's or 600's stored night to the cards its page draws. It evaluates no gate and draws no
// list: the index families read the night by the sweep's own code and stored each member's answer and the index's list,
// and this reads them back, family by family in the page's order, each card's rule written from the settings the night
// stored.
// see: A screen reads and renders, and computes nothing
// see: Every page reads one index at a time chosen under Universe, and every figure names its index
// see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
public static partial class TonightScreen
{
    // One card a swing family in the page's order, each provisional with its picks, the notes on what it passed and the
    // page holds back, and why it lists none on a night it lists none; the drift's card carries its line of evidence.
    public static IReadOnlyList<FamilyCardView> IndexCards(
        UniverseChoice universe,
        IndexNightRow night,
        IReadOnlyList<FamilyPickRow> picks,
        IReadOnlyList<IndexResultRow> results,
        IReadOnlyList<(string Ticker, string? Company, decimal? Close)> members,
        IReadOnlyList<ResearchedRow> researched,
        IReadOnlyDictionary<string, QueueState> queue)
    {
        var settings = IndexRuleSettings.Read(night.Settings);
        var memberBy = members.ToDictionary(member => member.Ticker, StringComparer.Ordinal);
        var writtenOn = researched.ToDictionary(row => row.Ticker, row => row.Written, StringComparer.Ordinal);
        var passedBy = results.Where(result => result.Passed).ToDictionary(result => (result.Family, result.Ticker));
        var listedUnder = picks
            .Where(pick => pick.State == FamilyList.Listed)
            .ToDictionary(pick => pick.Ticker, pick => pick.Family, StringComparer.Ordinal);
        var families = SetupFamilies.InPageOrder;
        var cards = new List<FamilyCardView>();

        foreach (var (family, at) in families.Select((family, at) => (family, at)))
        {
            var own = picks.Where(pick => pick.Family == family.Name).ToArray();

            FamilyPickCell[] listed =
            [
                .. own
                    .Where(pick => pick.State == FamilyList.Listed && passedBy.ContainsKey((family.Name, pick.Ticker)))
                    .OrderBy(pick => pick.Place ?? int.MaxValue)
                    .Select(pick =>
                    {
                        var answer = passedBy[(family.Name, pick.Ticker)];
                        var member = memberBy.GetValueOrDefault(pick.Ticker);
                        var row = new ListingCell(
                            pick.Ticker,
                            night.Session,
                            0,
                            0,
                            member.Close,
                            [],
                            Distance: new UniverseCell(pick.Ticker, string.Empty, member.Close, null, null, null, null, null, null, Name: member.Company),
                            ResearchedOn: writtenOn.TryGetValue(pick.Ticker, out var written) ? written : null,
                            Queue: queue.GetValueOrDefault(pick.Ticker));

                        return new FamilyPickCell(
                            row,
                            pick.Place ?? 0,
                            answer.Entry,
                            answer.Stop,
                            answer.Target,
                            family.Trails,
                            family.Name == SetupFamilies.Pullback && answer.OrderBy is { } ratio ? Statistic.ToPrice(ratio) : null,
                            IndexWhy(family, settings, universe.Name),
                            [.. pick.Also.Select(name => SetupFamilies.Named(name)?.Label ?? name)]);
                    }),
            ];

            IReadOnlyList<string> notes = family.Name == DriftRule.Name
                ? [RuleWords.DriftEvidence, .. Notes(family, own, listedUnder)]
                : Notes(family, own, listedUnder);

            cards.Add(new FamilyCardView(
                family.Name,
                family.Label,
                family.Heading,
                family.Eyebrow,
                family.Name switch
                {
                    SetupFamilies.Pullback => RuleWords.Pullback(settings, universe.Name),
                    BreakoutRule.Name => RuleWords.Breakout(settings, universe.Name),
                    _ => RuleWords.Drift(settings, universe.Name),
                },
                at + 1,
                families.Count,
                null,
                0,
                listed,
                notes,
                listed.Length > 0 ? null : IndexEmpty(universe, night, settings, own, [.. results.Where(result => result.Family == family.Name)])));
        }

        return cards;
    }

    // The instant the live rule of a family on an S&P 400 or 600 index was registered, as of a night whose list it drew,
    // none where no freeze stands or the night did not read it; the card's line saying its sweep found none goes from that
    // night on.
    // see: No family on any index is set aside or hidden by a test result without the operator's word
    public static DateTimeOffset? IndexFrozenAt(string index, string family, IReadOnlyList<CandidateRow> register, IndexNightRow night) =>
        ReadByLiveRule(night, family) ? StandingRows(IndexRuleCandidate.FamilyOn(family, index), register, night.Session).Live?.RegisteredAt : null;

    // Each card of a family whose live rule stands on the index as of the night and drew its list that night: live since
    // the day it was registered, the variants standing beside it, and its rule in the words its registration's name
    // carries, which the code wrote from its settings. A family whose live rule the night did not read, one whose
    // evaluator moved among them, keeps the provisional card its list was drawn by.
    // see: A family lists on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
    // see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
    public static IReadOnlyList<FamilyCardView> WithIndexFreezes(IReadOnlyList<FamilyCardView> cards, UniverseChoice universe, IReadOnlyList<CandidateRow> register, IndexNightRow night) =>
    [
        .. cards.Select(card => ReadByLiveRule(night, card.Family) && StandingRows(IndexRuleCandidate.FamilyOn(card.Family, universe.Code), register, night.Session) is { Live: { } live } standing
            ? card with
            {
                LiveSince = DateOnly.FromDateTime(live.RegisteredAt.UtcDateTime),
                Variants = standing.Variants,
                Rule = "The " + live.Candidate[FamilyRecords.LivePrefix.Length..] + ", each trade paying the published spread, half at each end.",
            }
            : card),
    ];

    // Each setup row of the index's Run page whose family's live rule stands as of the night and drew its list that night:
    // live since the day it was registered, the variants standing beside it and its live rule's record in words, as the
    // records card reads it.
    public static IReadOnlyList<FamilyRunRow> WithIndexFreezes(IReadOnlyList<FamilyRunRow> rows, UniverseChoice universe, IReadOnlyList<CandidateRow> register, IReadOnlyList<FamilyRecordRow> records, IndexNightRow? night, DateOnly on) =>
    [
        .. rows.Select(row => night is not null && ReadByLiveRule(night, row.Family) && StandingRows(IndexRuleCandidate.FamilyOn(row.Family, universe.Code), register, on) is { Live: { } live } standing
            ? row with
            {
                LiveSince = DateOnly.FromDateTime(live.RegisteredAt.UtcDateTime),
                Variants = standing.Variants,
                Record = records.FirstOrDefault(record => record.Family == row.Family && record.Live)?.Record ?? row.Record,
            }
            : row),
    ];

    // Whether the night read a family's list by its registered live rule, as the settings it stored say.
    static bool ReadByLiveRule(IndexNightRow night, string family) =>
        IndexRuleSettings.Read(night.Settings).ReadByLiveRule?.Contains(family, StringComparer.Ordinal) == true;

    // Each registered rule of the index's swing families standing at the night's end, read over its own trades after each
    // one's round trip, its correction its family's own on the index, each family's live rule first.
    // see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
    public static IReadOnlyList<FamilyRecordRow> IndexRecordRows(UniverseChoice universe, IReadOnlyList<CandidateRow> register, IReadOnlyList<FamilyTradeRow> trades, DateOnly night)
    {
        RegisterRow[] rows =
        [
            .. register.Select(row => new RegisterRow(
                row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator,
                row.Parameters, string.Empty, row.Event, row.Retires, row.RegisteredAt, row.Evidence)),
        ];
        var standing = CandidateFamily.Standing(rows, PastTheNight(night));

        return
        [
            .. SetupFamilies.InPageOrder.SelectMany(family =>
            {
                var name = IndexRuleCandidate.FamilyOn(family.Name, universe.Code);
                var rules = CandidateFamily.In(standing, name);
                var trials = CandidateFamily.Trials(CandidateFamily.In(rows, name), rules.Select(rule => rule.Candidate));

                return FamilyRecords.Family(rules, trades, night, family.CapSessions, trials)
                    .OrderBy(view => view.Live ? 0 : 1)
                    .ThenBy(view => view.Candidate, StringComparer.Ordinal)
                    .Select(view => new FamilyRecordRow(
                        family.Name,
                        family.Heading,
                        view.Candidate,
                        view.Live,
                        view.Trades,
                        view.Decided,
                        view.Edge,
                        view.Blocks,
                        view.NextLook,
                        view.Level,
                        RecordWords(view).Replace(", an edge of ", ", an edge after each trade's own round trip of ", StringComparison.Ordinal),
                        view.First,
                        view.Restarted));
            }),
        ];
    }

    // Why an index's family lists a stock tonight: the night stores the trade of each member it passed and not its gates'
    // figures, so the words say which rule passed it and on what settings.
    static string IndexWhy(SetupFamily family, IndexRuleSettings settings, string index) =>
        family.Name switch
        {
            SetupFamilies.Pullback => $"Passed every gate of the pullback on the {index}'s base setting tonight.",
            BreakoutRule.Name => $"Closed above its high of the {settings.Breakout.GetValueOrDefault("high", "stored")} sessions before on heavy volume tonight, every gate of the breakout passing on the {index}'s provisional settings.",
            _ => $"Its report beat its estimate and the reaction held, every gate of the drift passing on the {index}'s provisional settings.",
        };

    // What an index's cards and its line say on a night its part of the night failed, whose rows hold no answer of it.
    // see: A failure in the S&P 400's or 600's part of the night is caught and named, and the S&P 500's night is built regardless
    public static string NotComputed(UniverseChoice universe) =>
        $"Not computed tonight: the {universe.Possessive} part of the night failed, and the Run page names its cause.";

    // Why an index's family lists nothing on a night it lists none: its part of the night failed, the index's own market
    // check closed every list, every stock it passed is held back, or none passed, with how many of the index's members
    // stopped at each part of the rule.
    static string IndexEmpty(UniverseChoice universe, IndexNightRow night, IndexRuleSettings settings, IReadOnlyList<FamilyPickRow> own, IReadOnlyList<IndexResultRow> results)
    {
        if (night.Fault is not null)
        {
            return NotComputed(universe);
        }

        if (own.Count > 0)
        {
            return "Every stock this setup passed tonight is held back, as the notes beneath say.";
        }

        if (!night.MarketOpen)
        {
            return night.Breadth is { } breadth
                ? FormattableString.Invariant($"The market check closed every list of the {universe.Name} tonight: the {universe.Possessive} breadth was {breadth * 100:0.0}% of its members closing above their 200-day average, below its floor of {settings.MarketFloor * 100:0.#}%.")
                : $"The market check closed every list of the {universe.Name} tonight: the {universe.Possessive} breadth is not available.";
        }

        if (results.Count == 0)
        {
            return "No answer of this setup is stored for the night.";
        }

        var stopped = results
            .Where(result => !result.Passed)
            .GroupBy(result => result.Reason ?? "no reason stored", StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => FormattableString.Invariant($"{group.Count()} {group.Key}"));

        return FormattableString.Invariant($"No stock passed this setup tonight: of the {results.Count} {universe.Name} members read, ") + string.Join(", ", stopped) + ".";
    }

    // An index's sector heavyweights' card: the holdings open at the night's close in the order of their sectors, each
    // with its close against its 200-session average, what the last rebalance bought, what ended at it or since, and the
    // next rebalance, its rule written from the settings the night stored with its design and its sector comparison.
    public static HeavyweightCardView IndexHeavyweights(
        UniverseChoice universe,
        IndexNightRow night,
        IReadOnlyList<IndexHoldingRow> holdings,
        DateOnly? last,
        IReadOnlyList<HeavyweightCloseRow> closes,
        IReadOnlyList<(string Ticker, string? Company, decimal? Close)> members)
    {
        var words = SetupFamilies.SectorHeavyweights;
        var settings = IndexRuleSettings.Read(night.Settings);
        var companies = members.ToDictionary(member => member.Ticker, member => member.Company, StringComparer.Ordinal);
        var closeOf = closes.ToDictionary(close => close.Ticker, StringComparer.Ordinal);
        var fund = FundHoldings.Funds.TryGetValue(universe.Code, out var held) ? held.Fund : "the index's fund";
        var lookBack = int.TryParse(settings.Heavyweights.GetValueOrDefault("look-back"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sessions) ? sessions : HeavyweightRule.LookBack;

        // A night whose part failed carried nothing, so the card lists none of the book's holdings for it.
        var computed = night.Fault is null;

        HeavyweightHoldingCell[] cells =
        [
            .. holdings
                .Where(holding => computed && holding.EndedOn is null)
                .OrderBy(holding => holding.Sector, StringComparer.Ordinal)
                .ThenBy(holding => holding.Ticker, StringComparer.Ordinal)
                .Select(holding =>
                {
                    var close = closeOf.GetValueOrDefault(holding.Ticker);

                    return new HeavyweightHoldingCell(
                        holding.Ticker,
                        companies.GetValueOrDefault(holding.Ticker),
                        holding.Sector,
                        holding.EnteredOn,
                        null,
                        null,
                        close?.Close,
                        close?.Average200,
                        close is { Close: { } shut, Average200: { } average } && HeavyweightRule.Broken(Statistic.FromPrice(shut), average));
                }),
        ];

        string[] entered = computed && last is { } on
            ? [.. holdings.Where(holding => holding.EnteredOn == on).OrderBy(holding => holding.Sector, StringComparer.Ordinal).ThenBy(holding => holding.Ticker, StringComparer.Ordinal).Select(holding => holding.Ticker)]
            : [];

        HeavyweightEndedCell[] ended = computed && last is { } since
            ? [.. holdings
                .Where(holding => holding.EndedOn is { } end && end >= since)
                .OrderBy(holding => holding.EndedOn)
                .ThenBy(holding => holding.Ticker, StringComparer.Ordinal)
                .Select(holding => new HeavyweightEndedCell(holding.Ticker, holding.EndedOn!.Value, holding.Reason ?? "ended"))]
            : [];

        var empty = cells.Length > 0
            ? null
            : !computed
                ? NotComputed(universe)
            : last is not { } rebalance
                ? $"No rebalance has been read yet. The {universe.Possessive} book reads its first on the first night of a month the night reads the {universe.Possessive} members."
                : entered.Length == 0
                    ? FormattableString.Invariant($"No sector's largest companies of the {universe.Name} led their sector at the rebalance of {rebalance:yyyy-MM-dd} while passing the floors, the profit check, the trend gate and the beta, so the book holds nothing until the next.")
                    : FormattableString.Invariant($"Every holding the rebalance of {rebalance:yyyy-MM-dd} bought has been sold since, as the notes beneath say.");

        return new HeavyweightCardView(
            words.Heading,
            words.Eyebrow,
            RuleWords.Heavyweights(settings, universe.Name, fund),
            lookBack,
            last,
            NextRebalance(night.Session, last),
            cells,
            entered,
            ended,
            empty);
    }

    // How an index's night went, read off the rows its families stored for it.
    public static IndexRunView IndexRun(IndexNightRow night, IReadOnlyList<FamilyPickRow> picks, IReadOnlyList<IndexResultRow> results, IReadOnlyList<IndexTradeRow> trades, IReadOnlyList<IndexHoldingRow> holdings) =>
        new(
            night.Members,
            night.Breadth,
            IndexRuleSettings.Read(night.Settings).MarketFloor,
            night.MarketOpen,
            results.Where(result => result.Passed).Select(result => result.Ticker).Distinct(StringComparer.Ordinal).Count(),
            picks.Count(pick => pick.State == FamilyList.Listed),
            picks.Count(pick => pick.State == FamilyList.OpenTrade),
            trades.Count(trade => trade.Listed == night.Session),
            trades.Count(trade => trade.EndedOn == night.Session),
            night.Rebalanced,
            holdings.Count(holding => holding.EndedOn is null),
            night.Fault);

    // An index's setups on its Run page: each swing family with what it listed on the night and every trade its list has
    // kept, and the sector heavyweights with their holdings, each provisional with its record waiting for its freeze.
    public static IReadOnlyList<FamilyRunRow> IndexFamilyRun(IReadOnlyList<FamilyPickRow> picks, IReadOnlyList<IndexTradeRow> trades, IReadOnlyList<IndexHoldingRow> holdings) =>
    [
        .. SetupFamilies.InPageOrder.Select(family =>
        {
            var own = trades.Where(trade => trade.Family == family.Name).ToArray();

            return new FamilyRunRow(
                family.Name,
                family.Heading,
                null,
                0,
                picks.Count(pick => pick.Family == family.Name && pick.State == FamilyList.Listed),
                own.Length,
                own.Count(trade => trade.EndedOn is null),
                own.Count(trade => trade.EndedOn is not null),
                SetupFamilies.Provisional);
        }),
        new FamilyRunRow(
            HeavyweightRule.Name,
            SetupFamilies.SectorHeavyweights.Heading,
            null,
            0,
            0,
            holdings.Count,
            holdings.Count(holding => holding.EndedOn is null),
            holdings.Count(holding => holding.EndedOn is not null),
            SetupFamilies.Provisional),
    ];

    // The line an index's page opens its setups on: the index's own market check with its figures, how many of its
    // members a setup passed, the buy points its cards list and how many setups list one, and its trades still open.
    public static MarketLineView IndexLine(UniverseChoice universe, IndexNightRow night, IReadOnlyList<FamilyCardView> cards, IReadOnlyList<IndexResultRow> results, int openTrades) =>
        new(
            night.MarketOpen,
            night.Breadth,
            IndexRuleSettings.Read(night.Settings).MarketFloor,
            cards.Sum(card => card.Picks.Count),
            cards.Count(card => card.Picks.Count > 0),
            cards.Count,
            0,
            openTrades,
            universe.Name,
            results.Where(result => result.Passed).Select(result => result.Ticker).Distinct(StringComparer.Ordinal).Count(),
            night.Members,
            CloseRead: false,
            Universe: universe.Word);
}
