using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Spending;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from stored rows to what the run page draws.
//
// It sits beside the other three screen projections and in the same seam. What
// it does is count and pair: the reason records are counts over the listings and
// the forward returns, and the base rate is read from the column the filler
// already wrote beside every return.
// see: A screen reads and renders, and each figure it works out has one function in the core
public static class RunScreen
{
    // The swing filter's funnel for a night, counted off the flags each member's row stores: each gate
    // in section 11's order passing that gate and every gate before it, the setup's two families, what
    // each exclusion removed of the members passing every gate, and how many pass. None where the night
    // stored no result.
    public static FunnelView? Funnel(IReadOnlyList<GateResultRow> rows, DateOnly night, string rule = ListRules.Reasons)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var flags = new Func<GateResultRow, bool>[] { row => row.Market, row => row.Trend, row => row.Setup, row => row.Trigger, row => row.Trade };
        var gates = new[] { "market", "trend and strength", "setup", "trigger", "trade" };
        var steps = new List<FunnelStep>();
        var before = rows.Count;

        for (var at = 0; at < flags.Length; at++)
        {
            var through = at;
            var passed = rows.Count(row => flags.Take(through + 1).All(flag => flag(row)));

            steps.Add(new FunnelStep(gates[at], passed, before - passed));
            before = passed;
        }

        var throughAll = rows.Where(row => flags.All(flag => flag(row))).ToArray();
        var exclusions = throughAll
            .SelectMany(row => row.Exclusions)
            .GroupBy(exclusion => exclusion, StringComparer.Ordinal)
            .Select(group => (group.Key, group.Count()))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToArray();

        return new FunnelView(
            night,
            rows[0].Version,
            rows.Count,
            steps,
            rows.Count(row => row.Market && row.Trend && row.Family == "pullback"),
            rows.Count(row => row.Market && row.Trend && row.Family == "breakout"),
            exclusions,
            throughAll.Count(row => row.Exclusions.Count > 0),
            rows.Count(row => row.Passed),
            rule);
    }

    // The night's market reading as the run page and tonight's header draw it, as the swing reader stored it.
    public static MarketView? Market(MarketReadingRow? row) =>
        row is null
            ? null
            : new MarketView(row.SessionDate, row.Members, row.Counted, row.Above, row.Breadth, row.CountedContext,
                row.AboveContext, row.BreadthContext, row.VolumeCounted, row.MedianVolumeRatio);

    // One row per reason, in section 11's order, whether or not it has earned a
    // number. A reason absent from the page is a reason nobody can ask about.
    //
    // The row's resolved count is every win and loss; the share, the bar, both floors and the
    // verdict are over the ones that set a bar, and the record carries that count apart.
    // see: A reason's share, verdict and both floors are counted over the resolved setups that set a bar
    public static IReadOnlyList<ReasonRecord> Records(
        IReadOnlyList<ListingRow> listings,
        IReadOnlyList<ResolvedSetup> resolved)
    {
        var byReason = ShortlistSeries.Reasons.ToDictionary(
            reason => reason,
            _ => (Fired: 0, Won: 0, Lost: 0, Unresolved: 0, NeverEntered: 0),
            StringComparer.Ordinal);

        // Each reason's setups with the session each was listed on, handed to the arithmetic as a
        // population; which of them a reason is scored over is that arithmetic's question.
        var setupsByReason = ShortlistSeries.Reasons.ToDictionary(
            reason => reason,
            _ => new List<ReasonVerdict.ScoredSetup>(),
            StringComparer.Ordinal);

        var listedOn = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // The sessions each reason's record stands on, which for the two reasons
        // whose rule was corrected leaves out the sessions written under the old one.
        var nights = ShortlistSeries.Reasons.ToDictionary(reason => reason, _ => new HashSet<DateOnly>(), StringComparer.Ordinal);

        foreach (var listing in listings)
        {
            var (counting, fired) = NightFirings.Counting(listing.Reasons);

            foreach (var reason in counting)
            {
                nights.GetValueOrDefault(reason)?.Add(listing.SessionDate);
            }

            listedOn[Key(listing.Ticker, listing.SessionDate)] = [.. fired];

            foreach (var reason in fired)
            {
                // A stored reason the roster does not carry refuses rather than
                // being counted into whichever row is read first. A retired
                // reason or a renamed one reaches this table before it reaches
                // any code that knows about it, and a record quietly missing a
                // reason's nights is a record nobody can tell from a reason that
                // did not fire.
                if (!byReason.TryGetValue(reason, out var counted))
                {
                    throw new InvalidOperationException(
                        FormattableString.Invariant($"The listing for {listing.Ticker} on {listing.SessionDate:yyyy-MM-dd} carries the ") +
                        $"reason '{reason}', which section 11's list does not hold. A record counted over " +
                        "reasons this build does not know about would be a record about a different set of " +
                        "reasons from the one the page names.");
                }

                byReason[reason] = counted with { Fired = counted.Fired + 1 };
            }
        }

        // A setup belongs to every reason that fired on the night it was listed,
        // which is what a reason's own record is over. A setup counted against
        // one reason alone would be a record about whichever reason happened to
        // be read first.
        foreach (var setup in resolved)
        {
            if (!listedOn.TryGetValue(Key(setup.Ticker, setup.SessionDate), out var reasons))
            {
                continue;
            }

            foreach (var reason in reasons)
            {
                var counted = byReason[reason];

                // see: A condition is judged against the break-even its own plan demands
                setupsByReason[reason].Add(new ReasonVerdict.ScoredSetup(setup.Outcome, setup.BreakEven, setup.SessionDate));

                byReason[reason] = setup.Outcome switch
                {
                    ForwardReturnSeries.Win => counted with { Won = counted.Won + 1 },
                    ForwardReturnSeries.Loss => counted with { Lost = counted.Lost + 1 },

                    // A setup whose price never reached the entry the plan named.
                    // Its own column, because it is not a trade that went badly
                    // but a trade that never happened.
                    // see: A setup is scored from its entry, and a target reached before the entry is never a win
                    ForwardReturnSeries.NeverEntered => counted with { NeverEntered = counted.NeverEntered + 1 },

                    // Matured and neither, or not yet matured. Its own state and
                    // never a smaller amount of losing.
                    _ => counted with { Unresolved = counted.Unresolved + 1 },
                };
            }
        }

        return
        [
            .. ShortlistSeries.Reasons.Select(reason =>
            {
                var counted = byReason[reason];
                var setups = setupsByReason[reason];
                var scored = ForwardReturnSeries.Record([.. setups.Select(setup => ((string?)setup.Outcome, setup.BreakEven))]);

                // The family is the six live reasons, registered by section 11
                // before the first listing night; a candidate promoted to live
                // would join them and restart the window, which is why the
                // divisor is the family and not a count of anything this page holds.
                // see: Adding a candidate later restarts the clock
                var tested = ReasonVerdict.For(setups, ReasonVerdict.LiveFamily);

                var record = new ReasonRecord(
                    reason,
                    counted.Fired,
                    counted.Won,
                    counted.Lost,
                    counted.Unresolved,
                    ReasonVerdict.MinimumResolved,
                    counted.NeverEntered,
                    tested.Scored,
                    Sessions: tested.Sessions,
                    SessionMinimum: ReasonVerdict.MinimumSessions,
                    Threshold: tested.Threshold,
                    Divisor: tested.Divisor,
                    Nights: nights[reason].Count,
                    Withheld: tested.Withheld,
                    Significance: ReasonVerdict.Significance);

                // Withheld here rather than at the page, so no surface can draw a share, a bar or a
                // verdict for a reason below either floor by forgetting to ask.
                // see: A screen reads and renders, and each figure it works out has one function in the core
                // see: The record column stays empty until it has earned a number
                // see: A rule's record is drawn under the rule's own heading, beside its reason or on a pick's card and never as the stock's own
                return record.HasEarnedAVerdict
                    ? record with
                    {
                        Share = scored.Share,
                        BreakEven = scored.BreakEven,
                        Cleared = tested.Cleared,
                        PValue = tested.PValue,
                    }
                    : record;
            }),
        ];
    }

    // The track the run page draws beside each record, which is 15.5's mark over
    // the counts the record already carries.
    //
    // Below the minimum the win and loss counts are handed over as one resolved
    // segment rather than as two. 15.11 gates the record column on the minimum,
    // and a win-loss split drawn beside a reason with eleven resolved setups is
    // the same figure through a second channel: a reader reads the ratio off the
    // picture, which is the thing the gate exists to prevent. The mark keeps its
    // three states as section 15.5 defines them and draws what it is given.
    // see: The record column stays empty until it has earned a number
    // The shadow region, which is a count and a divisor and never a name.
    //
    // A screen shows how many candidate conditions are registered and that each
    // one's record is withheld until it is promoted. It shows no candidate's pick
    // of a name, which the comparison of tonight's picks alone draws: seeing a candidate's record before it is
    // promoted is the thing the shadow exists to prevent, and a page that drew
    // one would make the register a formality.
    //
    // The instant is the one the page is read at rather than the night's, because
    // what the region states is how hard the correction is now, which is what a
    // reader comparing it against a verdict needs.
    //
    // Beside the family's divisor stand the distinct trials the level is shared across and
    // the level each starts at, with what a candidate's looks release of it, so the bar a
    // verdict is read against is on the page wherever the divisor is.
    // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
    // see: Holm's level passes between the candidates by a graph fixed when they are registered, and its first step is 0.05 over the distinct trials read at a look or still running
    public static ShadowRegion Shadow(
        IReadOnlyList<CandidateRow> rows,
        IReadOnlyList<CandidateNightRow> nights,
        IReadOnlyList<CandidateSetupRow> setups,
        DateOnly night,
        DateTimeOffset at)
    {
        var register = Register(rows);
        var trials = TrialsCounted(register, nights, Fired(setups), night, at).Now;
        double? level = trials > 0 ? ReasonVerdict.Significance / trials : null;

        // The count is the set a night evaluates and the divisor the family's own figure, taken apart so a disagreement shows.
        return new ShadowRegion(
            ShadowColumn.StandingAt(register, at).Count,
            CandidateFamily.Divisor(register, at),
            CandidateFamily.Maximum,
            trials,
            level,
            level is { } starts ? [.. Looks.At.Select((_, look) => Looks.Spent(starts, Looks.Fraction(look)))] : [],
            level is { } first && Looks.Spent(first, Looks.Fraction(0)) >= 1d / (1 << Looks.At[0]));
    }

    // The pullback family's rows, which this region's divisor, trials and graph are read over: every other
    // setup family's correction is its own.
    // see: Each setup family's correction for luck counts its own rules alone, at most nine a family
    static RegisterRow[] Register(IReadOnlyList<CandidateRow> rows) =>
    [
        .. CandidateFamily.In(
            rows.Select(row => new RegisterRow(
                row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator,
                row.Parameters, string.Empty, row.Event, row.Retires, row.RegisteredAt, row.Evidence)),
            EquityBrief.Core.Families.SetupFamilies.Pullback),
    ];

    static Dictionary<string, IReadOnlyList<CandidateSetup>> Fired(IReadOnlyList<CandidateSetupRow> setups) =>
        setups
            .GroupBy(row => row.Candidate, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CandidateSetup>)
                [
                    .. group.Select(row => new CandidateSetup(
                        row.SessionDate, row.Outcome ?? string.Empty, row.Null, row.NullAtSensitivity,
                        row.BreakEven, row.ReturnPct, row.PlannedRisk, row.OnEarnings)),
                ],
                StringComparer.Ordinal);

    // The nights each registered candidate's looks were read on, the count of distinct trials as of any
    // night, and the count as the page is read.
    //
    // A night's count is the rules that night evaluated, which are the ones running when it started, and
    // the rules a look had read by it. A candidate retired with no result of its own read is counted in
    // neither, and one a look has read is counted from that night for the life of the system.
    sealed record Counted(IReadOnlyDictionary<string, IReadOnlyList<DateOnly>> LookNights, Func<DateOnly, int> On, int Now);

    static Counted TrialsCounted(
        IReadOnlyList<RegisterRow> rows,
        IReadOnlyList<CandidateNightRow> nights,
        IReadOnlyDictionary<string, IReadOnlyList<CandidateSetup>> fired,
        DateOnly night,
        DateTimeOffset at)
    {
        var standing = CandidateFamily.Standing(rows, at).Select(row => row.Candidate).ToArray();

        var evaluatedOn = nights
            .GroupBy(row => row.SessionDate)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Candidate).ToArray());

        var opened = nights
            .GroupBy(row => row.Candidate, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Min(row => row.SessionDate), StringComparer.Ordinal);

        DateOnly[] evaluated = [.. evaluatedOn.Keys];

        var lookNights = rows
            .Where(row => row.Event == CandidateFamily.Registered)
            .Select(row => row.Candidate)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                candidate => candidate,
                candidate => CandidateRecord.LookNights(
                    fired.GetValueOrDefault(candidate, []),
                    opened.GetValueOrDefault(candidate, night),
                    evaluated,
                    RecordUntil(rows, standing.Contains(candidate, StringComparer.Ordinal), candidate, night)),
                StringComparer.Ordinal);

        // A promotion is written after a look crossed, so a promoted candidate is one a look has read
        // whatever the setups handed here hold.
        IEnumerable<string> ReadBy(DateOnly on, DateTimeOffset promotedBy) =>
            lookNights
                .Where(entry => entry.Value.Count > 0 && entry.Value[0] <= on
                    || PromotionOf(rows, entry.Key) is { } promotion && promotion.RegisteredAt <= promotedBy)
                .Select(entry => entry.Key);

        // Running on a night: the candidates it evaluated, and those the register held standing through
        // its day, so a night whose rows were not written still counts what was running.
        int On(DateOnly on)
        {
            var through = new DateTimeOffset(on.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

            return CandidateFamily.Trials(
                rows,
                evaluatedOn.GetValueOrDefault(on, [])
                    .Concat(CandidateFamily.StandingBefore(rows, through).Select(row => row.Candidate))
                    .Concat(ReadBy(on, through)));
        }

        return new Counted(lookNights, On, CandidateFamily.Trials(rows, standing.Concat(ReadBy(night, at))));
    }

    // The night a candidate's record runs to: tonight while it stands, and the night it was first retired
    // on once it is not. Its blocks would otherwise go on completing over setups it stopped producing, and a
    // look read after a candidate left the family would be a look nobody registered.
    static DateOnly RecordUntil(IReadOnlyList<RegisterRow> rows, bool stands, string candidate, DateOnly night) =>
        stands
            ? night
            : rows.Where(row => row.Event == CandidateFamily.Retired && row.Retires == candidate)
                .Select(row => DateOnly.FromDateTime(row.RegisteredAt.UtcDateTime))
                .DefaultIfEmpty(night)
                .Min();

    // What each registered candidate's setups have come to, read at the looks it was registered
    // with and at the level the graph gives it now.
    //
    // A candidate's window opens on the first night that evaluated it, and the candidates that
    // night evaluated are the family its graph passes a level among: a candidate registered after a
    // window opened was not among the things being tried over the evidence that window holds. The
    // level at the graph's first step is the significance over the distinct trials, and each look is
    // read at the level of the count as of the night it was read on, so a trial registered since moves
    // only the looks not yet read.
    // Nothing here names a ticker, and nothing it hands the page could: a record is a count of
    // setups and the sessions they were listed on.
    // see: A candidate is judged by a sign-flip test over blocks of 63 sessions, with at least eight blocks
    // see: Holm's level passes between the candidates by a graph fixed when they are registered, and its first step is 0.05 over the distinct trials read at a look or still running
    public static CandidateRegion Candidates(
        IReadOnlyList<CandidateRow> register,
        IReadOnlyList<CandidateNightRow> nights,
        IReadOnlyList<CandidateSetupRow> setups,
        DateOnly night,
        DateTimeOffset at)
    {
        var rows = Register(register);

        var standing = CandidateFamily.Standing(rows, at).ToDictionary(row => row.Candidate, StringComparer.Ordinal);

        var registered = rows
            .Where(row => row.Event == CandidateFamily.Registered)
            .Select(row => row.Candidate)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var opened = nights
            .GroupBy(row => row.Candidate, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Min(row => row.SessionDate), StringComparer.Ordinal);

        var evaluatedOn = nights
            .GroupBy(row => row.SessionDate)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Candidate).ToArray());

        var fired = Fired(setups);
        var trials = TrialsCounted(rows, nights, fired, night, at);

        // A family whose every candidate was retired unread counts no trial, and its records are still
        // drawn at the level a single trial would hold.
        var now = Math.Max(1, trials.Now);

        // The graph's levels are over the count as the page is read, and a look already read takes its
        // own count's instead: the level is the same share of the significance either way, so it is
        // carried from one count to the other by their ratio.
        Measured Read(string candidate, double level) =>
            CandidateRecord.For(
                fired.GetValueOrDefault(candidate, []),
                opened.GetValueOrDefault(candidate, night),
                RecordUntil(rows, standing.ContainsKey(candidate), candidate, night),
                look => trials.LookNights.TryGetValue(candidate, out var read) && look < read.Count
                    ? level * now / Math.Max(1, trials.On(read[look]))
                    : level);

        // A retirement the operator wrote after a promotion says so in its evidence, which is what
        // tells a candidate that left the family having been shown from one that left having not.
        bool Promoted(string candidate) => PromotionOf(rows, candidate) is not null;

        // The instant of the last retirement naming a candidate by the page's instant, or the page's
        // own where there is none.
        DateTimeOffset RetiredAt(string candidate) =>
            rows.Where(row => row.Event == CandidateFamily.Retired && row.Retires == candidate && row.RegisteredAt <= at)
                .Select(row => row.RegisteredAt)
                .DefaultIfEmpty(at)
                .Max();

        IReadOnlyList<GraphLevel> Graph(IEnumerable<string> candidates) =>
            HolmGraph.Levels(
                [
                    .. StepOrder(rows, candidates.Where(candidate => registered.Contains(candidate, StringComparer.Ordinal)))
                        .Select(candidate => new GraphMember(
                            candidate,
                            Promoted(candidate),
                            !standing.ContainsKey(candidate),
                            level => Read(candidate, level).Verdict == CandidateRecord.Crossed)),
                ],
                ReasonVerdict.Significance,
                now);

        var levels = new Dictionary<string, GraphLevel>(StringComparer.Ordinal);

        // Each candidate reads the graph of the window it opened with, and never a later window that
        // holds it beside a candidate registered since.
        foreach (var family in registered.Where(opened.ContainsKey).GroupBy(candidate => opened[candidate]))
        {
            foreach (var level in Graph(evaluatedOn[family.Key]).Where(level => family.Contains(level.Candidate, StringComparer.Ordinal)))
            {
                levels[level.Candidate] = level;
            }
        }

        // A candidate no night has evaluated has opened no window, and it reads the graph over the
        // candidates standing beside it: while it stands, the ones the next night evaluates with it,
        // and retired first, the ones standing when it was retired.
        foreach (var candidate in registered.Where(candidate => !opened.ContainsKey(candidate)))
        {
            IEnumerable<string> beside = standing.ContainsKey(candidate)
                ? standing.Keys
                : CandidateFamily.StandingBefore(rows, RetiredAt(candidate)).Select(row => row.Candidate);

            levels[candidate] = Graph(beside.Append(candidate)).Single(level => level.Candidate == candidate);
        }

        return new CandidateRegion(
            [
                .. registered
                    .OrderBy(candidate => candidate, StringComparer.Ordinal)
                    .Select(candidate =>
                    {
                        var level = levels.GetValueOrDefault(
                            candidate,
                            new GraphLevel(candidate, ReasonVerdict.Significance / now, 1, false));

                        return new CandidateRecordRow(
                            candidate,
                            standing.GetValueOrDefault(candidate)?.Parameters ?? "{}",
                            standing.ContainsKey(candidate),
                            level.Crossed,
                            level.Level,
                            level.Step,
                            Read(candidate, level.Level));
                    }),
            ],
            registered.Length,
            standing.Count,
            trials.Now,
            ReasonVerdict.Significance,
            Blocks.Sessions,
            Blocks.Floor,
            Looks.At,
            NullWin.CostBasisPoints,
            NullWin.SensitivityBasisPoints);
    }

    // The order the graph steps its members in: the promoted first, in the order their promotions
    // were written, which is the order their levels passed in, and then the rest in the order they
    // were first registered, which decides which of two crossing on one read steps first.
    // see: The graph steps promoted candidates in the order their promotions were written, and candidates crossing on one read in the order they were registered
    public static IReadOnlyList<string> StepOrder(IReadOnlyList<RegisterRow> rows, IEnumerable<string> candidates) =>
    [
        .. candidates
            .Distinct(StringComparer.Ordinal)
            .OrderBy(candidate => PromotionOf(rows, candidate)?.RegisteredAt ?? DateTimeOffset.MaxValue)
            .ThenBy(candidate => PromotionOf(rows, candidate)?.Id ?? long.MaxValue)
            .ThenBy(candidate => rows
                .Where(row => row.Event == CandidateFamily.Registered && row.Candidate == candidate)
                .Select(row => row.Id)
                .DefaultIfEmpty(long.MaxValue)
                .Min()),
    ];

    // The retirement that promoted a candidate, the first written where there is one: a retirement the
    // operator wrote after a promotion says so in its evidence.
    static RegisterRow? PromotionOf(IReadOnlyList<RegisterRow> rows, string candidate) =>
        rows.Where(row => row.Event == CandidateFamily.Retired
                && row.Retires == candidate
                && row.Evidence is { } evidence
                && evidence.StartsWith(CandidateFamily.PromotedBy, StringComparison.Ordinal))
            .OrderBy(row => row.RegisteredAt)
            .ThenBy(row => row.Id)
            .FirstOrDefault();

    // The trend rule's open versions, what each labelled the night, and the flip-backs the stored
    // labels show.
    //
    // A version of this rule only ever takes a setup away, because the label it writes is the one
    // that carries no tranche at all, so its own setups are the live rule's less the ones its
    // label removes and every outcome is one the store already holds. That is what lets a
    // difference be measured at all, and the measuring happens on the night a block completes:
    // what arrives here is the frozen block, because the scores and labels behind it are dropped
    // a year back and a record is read over about four years of nights.
    //
    // Every row here is keyed on the window and never on the version's name. A window is closed
    // and opened again under the same name when a pinned source moves, so one name can carry rows
    // of two windows, and a session scored under both would otherwise be counted twice in the
    // labels and fed twice into the blocks.
    // owes: The trend confirmation's nights settled from flip-backs
    // see: A trend version is judged by the candidates' test on its difference from the live rule
    // see: A version's record is read from the blocks frozen as each completed
    // see: A version's record belongs to the window its scores were written under and never to the version's name
    public static TrendVersionRegion TrendVersions(
        IReadOnlyList<OpenVersionRow> open,
        IReadOnlyList<VersionLabelRow> labels,
        IReadOnlyList<LiveLabelRow> live,
        IReadOnlyList<VersionBlockRow> blocks,
        LabelReturns returns,
        int nights,
        DateOnly night)
    {
        var byWindow = blocks
            .GroupBy(row => new Window(row.Version, row.OpenedAt))
            .ToDictionary(group => group.Key, group => group.Select(row => row.Block).ToArray());

        var measured = new List<VersionMeasured>();

        var rows = open
            .Where(row => !string.Equals(row.Version, RuleVersions.Live, StringComparison.Ordinal))
            .Select(row =>
            {
                var window = new Window(row.Version, row.OpenedAt);
                var mine = byWindow.GetValueOrDefault(window, []);

                var record = mine.Length == 0
                    ? null
                    : VersionRecord.For(row.Version, row.OpenedAt, mine, ReasonVerdict.Significance);

                if (record is not null)
                {
                    measured.Add(record);
                }

                var drawn = labels
                    .Where(label => new Window(label.Version, label.OpenedAt) == window)
                    .ToArray();

                return new TrendVersionRow(
                    row.Version,
                    row.Parameters,
                    row.OpenedAt,
                    [.. drawn.Select(label => new LabelCount(label.Label, label.Names))],
                    drawn.Sum(label => label.Moved),
                    record);
            })
            .ToArray();

        return new TrendVersionRegion(
            rows,
            [.. live.Select(label => new LabelCount(label.Label, label.Names))],
            returns,
            night,
            nights,
            RuleVersions.MostAtOnce,
            open.Count,
            VersionRecord.MarginInPoints,
            RealityCheck.Over(measured));
    }

    // One window's key, being the whole of the version's name and the whole of the instant its
    // window opened at. The name alone is the opening of the key and not the key, and a matcher
    // keyed on it answers about every window sharing it.
    readonly record struct Window(string Version, DateTimeOffset OpenedAt);

    // The three orders of tonight's list over the nights whose listings record what each order
    // reads, up to the night shown: on each night the first rows each order would have drawn, the
    // setups among them, how many have had their whole outcome window by the night shown, and the
    // blocks those fall in, counted from the first night that recorded it.
    //
    // A drawn row whose plan computes no reward to risk has no stop or no traded target, so it is
    // no setup and adds nothing, which is part of what the orders differ in: the old order draws
    // such a row wherever its band strength puts it. The benchmark comes first because the other
    // two are read against it.
    // see: The order tonight's list is drawn in is compared against the order it replaces, declared before any record is read
    // see: A listing records the band strength the old order read, and the three orders are compared over the nights that recorded it
    public static OrderComparison Orders(IReadOnlyList<ListingRow> listings, DateOnly night)
    {
        var recorded = listings
            .Where(listing => listing.BandStrength is not null && listing.SessionDate <= night)
            .GroupBy(listing => listing.SessionDate)
            .OrderBy(group => group.Key)
            .Select(group => (Night: group.Key, Fired: group.Where(listing => listing.FiredCount > 0).Select(TonightScreen.Ranked).ToArray()))
            .ToArray();

        var first = recorded.Length == 0 ? (DateOnly?)null : recorded[0].Night;

        OrderMeasured Measured(TonightScreen.Order order, string key)
        {
            var (setups, closed) = (0, 0);
            var blocks = new HashSet<int>();

            foreach (var (on, fired) in recorded)
            {
                var drawn = TonightScreen.Ordered(fired, order)
                    .Take(SinglePageApp.TonightDrawn)
                    .Count(row => row.RewardToRisk is not null);

                setups += drawn;

                if (drawn > 0 && Blocks.Closed(on, night))
                {
                    closed += drawn;
                    blocks.Add(Blocks.Of(first!.Value, on));
                }
            }

            return new OrderMeasured(key, TonightScreen.Named(order), order == TonightScreen.Order.FiredThenBandStrength, setups, closed, blocks.Count);
        }

        return new OrderComparison(
            [
                Measured(TonightScreen.Order.FiredThenBandStrength, "fired-then-band-strength"),
                Measured(TonightScreen.Order.FiredThenRewardToRisk, "fired-then-reward-to-risk"),
                Measured(TonightScreen.Order.RewardToRiskAlone, "reward-to-risk-alone"),
            ],
            first,
            recorded.Length,
            SinglePageApp.TonightDrawn,
            Blocks.Sessions,
            Blocks.Floor);
    }

    public static IReadOnlyList<ReasonTrackRow> Tracks(IReadOnlyList<ReasonRecord> records) =>
    [
        // Gated on the record's own answer rather than on the count, from 8.5.
        //
        // It read `Resolved >= Minimum` here and `HasEarnedAVerdict` in the
        // column, which was one gate written twice and agreed only while there
        // was one floor. The night floor arrived and the two came apart at once:
        // a reason with 280 resolved over 12 sessions had its verdict withheld
        // from the column and its win-loss split drawn in the picture, which is
        // the same figure through the second channel this split exists to close.
        .. records.Select(record => record.HasEarnedAVerdict
            ? new ReasonTrackRow(record.Reason, record.Won, record.Lost, record.Unresolved)
            : new ReasonTrackRow(record.Reason, 0, 0, record.Unresolved, record.Resolved)),
    ];

    // Tonight's own track, section 15.7's reason totals. Every name on tonight's
    // list is a setup nothing has scored yet, so each reason's whole count is
    // the unresolved state, and what the mark says is how wide each reason's bar
    // is against the busiest: one thing happening to many names, or many things
    // happening to a few.
    public static IReadOnlyList<ReasonTrackRow> Tracks(IReadOnlyList<ReasonTotal> totals) =>
    [
        .. totals.Select(total => new ReasonTrackRow(total.Reason, 0, 0, total.Names)),
    ];

    // The setups a reason's record counts: the setup horizon's rows that reached
    // an outcome. The five and twenty-one session horizons are the universe's
    // own windows and are what the base rate is over, and they are not what a
    // reason is scored on.
    public static IReadOnlyList<ResolvedSetup> Resolved(IReadOnlyList<ForwardReturnRow> returns) =>
    [
        .. returns
            .Where(row => row.Horizon == ForwardReturnSeries.Setup && row.Outcome is not null)
            .Select(row => new ResolvedSetup(row.Ticker, row.SessionDate, row.Outcome!, row.BreakEven)),
    ];

    // The universe base rate per window, read off the column the filler wrote
    // beside every return rather than counted here. A window whose rows carry no
    // rate has none, which is a night before the first fill rather than a rate
    // of zero.
    //
    // One line per window that has one, which is the two session horizons. The
    // setup horizon is not among them by rule rather than by absence, and the
    // page states that rather than leaving a gap.
    // see: Every forward-return figure is shown against the universe base rate
    // see: The `setup` horizon has no universe base rate, and the column is null for it
    public static IReadOnlyList<BaseRateLine> BaseRates(IReadOnlyList<ForwardReturnRow> returns) =>
    [
        .. ForwardReturnSeries.Horizons
            .Where(horizon => horizon != ForwardReturnSeries.Setup)
            .Select(horizon => new BaseRateLine(
                horizon,
                returns.FirstOrDefault(row => row.Horizon == horizon && row.BaseRate is not null)?.BaseRate)),
    ];

    // The list from night to night: of the names listed on the night, how many were listed on the evening
    // before it and at least once over the five and the twenty evenings before it, the evenings being the
    // ones the listings hold and each read by the rule that listed it. None where the night holds no listing.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    public static OverlapView? Overlap(IReadOnlyList<ListingRow> listings, DateOnly night)
    {
        var tonight = listings.Where(listing => listing.SessionDate == night).ToArray();

        if (tonight.Length == 0)
        {
            return null;
        }

        var names = tonight.Where(listing => listing.IsListed).Select(listing => listing.Ticker).ToHashSet(StringComparer.Ordinal);
        var before = listings
            .Select(listing => listing.SessionDate)
            .Where(session => session < night)
            .Distinct()
            .OrderByDescending(session => session)
            .ToArray();

        (int Held, int On) Over(int evenings)
        {
            var window = before.Take(evenings).ToHashSet();
            var listed = listings
                .Where(listing => window.Contains(listing.SessionDate) && listing.IsListed)
                .Select(listing => listing.Ticker)
                .ToHashSet(StringComparer.Ordinal);

            return (window.Count, names.Count(listed.Contains));
        }

        var (_, onLast) = Over(1);
        var (fiveHeld, onFive) = Over(5);
        var (twentyHeld, onTwenty) = Over(20);

        return new OverlapView(night, names.Count, before.Length > 0 ? before[0] : null, onLast, fiveHeld, onFive, twentyHeld, onTwenty);
    }

    // How many nights the record stands on, which is what makes the count
    // against the minimum readable as a distance rather than as a small number.
    // Three operating obligations are read on this page, and each of them is a
    // count of nights or of resolved setups.
    // owes: The six reason thresholds calibrated from the nights they fired on
    public static int Nights(IReadOnlyList<ListingRow> listings) =>
        listings.Select(listing => listing.SessionDate).Distinct().Count();

    // The stages of a night, in the order they ran, with each one's own elapsed
    // time. Per stage rather than in one total, because a night that landed
    // inside its limit by one step doing nothing is legible only if the steps
    // are apart.
    //
    // The subtraction is the same one the night header's duration makes: two
    // stored instants, and nothing derived from anything else.
    public static IReadOnlyList<StageRow> Stages(IReadOnlyList<RunStageRow> log) =>
    [
        .. log
            .OrderBy(row => row.StartedAt)
            .ThenBy(row => row.Stage, StringComparer.Ordinal)
            .Select(row => new StageRow(
                row.Stage,
                row.StartedAt,
                (row.EndedAt - row.StartedAt).TotalSeconds,
                row.RowsWritten,
                row.ModelCalls,
                row.NetworkRequests,
                row.Spend,
                row.Outcome,
                row.Detail,
                IsByHand(row.RunId))),
    ];

    // The run ids a person's command writes, stated here because the read surface holds no
    // reference to the worker; `read-surface` asserts they are the verbs' own. Their rows are
    // drawn as run by hand, apart from the night's stages.
    public static IReadOnlyList<string> RunsByHand { get; } = ["version-", "register-", "filter-history-", "history-pull-", "history-purge-", "quarters-by-hand-", "index-families-by-hand-", "loop-apply-by-hand-", "loop-month-"];

    public static bool IsByHand(string runId) => RunsByHand.Any(prefix => runId.StartsWith(prefix, StringComparison.Ordinal));

    // What failed, in which component, which is the second half of 15.10's stale
    // and failed region. A stage that did not end with the word its own writer
    // uses for success is one a person has to look at.
    public const string Ok = "ok";

    // A night on a day the exchange did not trade, which did what it should by
    // doing nothing and is not a failure a person has to look at. The worker's
    // own constant cannot be referenced from here, since the read surface holds
    // no reference to the worker, so the word is stated and `nightly-run`
    // asserts the two agree.
    // see: A night on a day the exchange did not trade fetches nothing and exits clean
    public const string NoSession = "no session";

    //
    // A stage whose detail is a record carrying the reason it came to, as a research pass writes
    // it, is drawn with that reason, so the line is the pass's own words rather than its record:
    // a pass the research job's profile could not be used for says which profile and which key.
    // see: A paid model is one interface with an implementation per wire format, and a job never falls back from the profile it names
    public static IReadOnlyList<StageRow> Failed(IReadOnlyList<StageRow> stages) =>
    [
        .. stages
            .Where(stage => !stage.ByHand
                && !string.Equals(stage.Outcome, Ok, StringComparison.Ordinal)
                && !string.Equals(stage.Outcome, "started", StringComparison.Ordinal)
                && !string.Equals(stage.Outcome, NoSession, StringComparison.Ordinal)
                && !(string.Equals(stage.Stage, QueueStage, StringComparison.Ordinal) && string.Equals(stage.Outcome, QueueAtItsLimit, StringComparison.Ordinal)))
            .Select(stage => ReasonOf(stage.Detail) is { } reason ? stage with { Detail = reason } : stage),
    ];

    // The reason a detail written as a record carries, or none where it is not one or carries none.
    static string? ReasonOf(string detail)
    {
        if (!detail.StartsWith('{'))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(detail);

            return document.RootElement.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
                && reason.GetString() is { Length: > 0 } said
                    ? said
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The stage the overnight queue wrote on the nights it ran, and the outcome it wrote where it
    // stopped at its limit with names left, kept so a page for one of those nights reads its rows as
    // it did: a queue at its limit did what its limit was for, so it is not a stage that failed, and
    // its time sits after the close.
    public const string QueueStage = "overnight queue";

    public const string QueueAtItsLimit = "limit";

    // The documents a night's passes refused, grouped by the category that
    // refused each.
    //
    // Listed rather than counted, for the reason the stale names are: a page that
    // says four documents were refused and does not say which is a page nobody
    // can act on, and the thing a person acts on is the address. Grouped by
    // category and then ordered by the address inside a group, so four refusals
    // of one kind read as a search returning marketing rather than as four
    // unrelated events.
    //
    // No date is drawn beside a row. One class of refusal is that the document
    // carried no publish date, so a column of dates would be blank for exactly
    // the rows whose reason is the blank.
    public static IReadOnlyList<RefusedDocument> Refused(IReadOnlyList<RefusedDocumentRow> rows) =>
    [
        .. rows
            .OrderBy(row => row.Category, StringComparer.Ordinal)
            .ThenBy(row => row.Url, StringComparer.Ordinal)
            .Select(row => new RefusedDocument(row.Category, row.Title, row.Url)),
    ];

    // The sections that fell back on a night, a name's and a theme's, with the
    // reason each stored, in the order the store handed them over.
    public static IReadOnlyList<LeftOutSection> FellBack(IReadOnlyList<FellBackRow> rows) =>
    [
        .. rows.Select(row => new LeftOutSection(row.Subject, row.Section, row.Reason ?? string.Empty)),
    ];

    // The verdict counts from the last phase report, read out of the report the
    // harness wrote rather than counted here.
    //
    // The text is handed in rather than opened here, so nothing on the read
    // surface reaches the filesystem for it, and a machine with no report says
    // so instead of showing four zeros. Out of scope is carried separately from
    // unexamined for the reason .claude/rules/checks.md states: only one of them is a defect,
    // and a page that summed them would report a build that has not reached a
    // claim as one that failed to check it.
    public static HarnessCounts? Harness(string? report)
    {
        if (report is not { Length: > 0 })
        {
            return null;
        }

        using var document = JsonDocument.Parse(report);

        if (!document.RootElement.TryGetProperty("summary", out var summary))
        {
            return null;
        }

        return new HarnessCounts(
            summary.GetProperty("pass").GetInt32(),
            summary.GetProperty("fail").GetInt32(),
            summary.GetProperty("unexamined").GetInt32(),
            summary.GetProperty("outOfScope").GetInt32());
    }

    static string Key(string ticker, DateOnly sessionDate) =>
        $"{ticker}|{sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    // The reasons on a row that count toward their own records, and which of those fired.
    //
    // A reason the 5.4 correction changed counts only where the row was written
    // under the corrected rule, read off the value that rule writes and the old
    // one did not. Earnings soon written before it fired on every future print,
    // so the setups those rows seeded are not earnings soon's; breakout on volume
    // written before it could not fire, so its rows remove nothing.
    // see: Sessions to a dated event are counted on the exchange calendar and never on stored bars
    //
    // A reason counts only where the row stored the threshold the code carries now.
    // see: A reason's record reads only the rows written under the threshold the code carries, and the rows written under another are kept
    // Each night's firing, read off every listing the store holds by the rule the records count by.
    public static IReadOnlyList<NightFiring> Firings(IReadOnlyList<ListingRow> listings) =>
        NightFirings.Of(listings.Select(listing => (listing.SessionDate, listing.Reasons)));

    // The shape half of the calibration for the night the page is drawn for, over every night's stored
    // filter results and the listings' firing, in the window the open filter version names.
    // see: The swing filter's shape is calibrated over its ordinary nights, a night one cause pushes past a quarter and twice its usual share is left out, and each band spans a third to three times what the ruled filter passes
    public static ShapeState Calibration(
        IReadOnlyList<GateNightRow> nights,
        IReadOnlyDictionary<DateOnly, double?> ratios,
        IReadOnlyList<NightFiring> firings,
        string? openVersion,
        DateOnly night) =>
        ShapeClock.For(
            [
                .. nights.Select(row => new NightShape(
                    row.Session,
                    row.Version,
                    row.Members,
                    [row.Trend, row.Setup, row.Trigger, row.Trade],
                    row.Listed,
                    ratios.TryGetValue(row.Session, out var ratio) ? ratio : null)),
            ],
            firings,
            openVersion ?? ShapeClock.NoVersion,
            night);

    // The newest shape proposal as the page draws it, with what accepting it would restart now: the live
    // filter's candidate standing as the page is read, the acceptances already taken while one stood,
    // and the non-empty blocks its clock has run by the newest night the listings hold, counted by the
    // arithmetic the shape command holds an acceptance to.
    // see: A shape acceptance restarts the live filter's edge clock, and after one acceptance while the list is live each further one states the blocks it restarts
    public static ProposalView? Proposal(
        ShapeProposalRow? latest,
        IReadOnlyList<CandidateRow> register,
        IReadOnlyList<CandidateNightRow> nights,
        IReadOnlyList<CandidateSetupRow> setups,
        IReadOnlyList<ListingRow> listings,
        DateTimeOffset at)
    {
        if (latest is null)
        {
            return null;
        }

        var rows = register
            .Select(row => new RegisterRow(
                row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator,
                row.Parameters, string.Empty, row.Event, row.Retires, row.RegisteredAt, row.Evidence))
            .ToArray();

        var live = SwingFamily.Standing(rows, at);
        DateOnly? newest = listings.Count == 0 ? null : listings.Max(listing => listing.SessionDate);

        var blocks = live is null || newest is not { } night
            ? 0
            : SwingFamily.Blocks(
                [
                    .. setups
                        .Where(setup => string.Equals(setup.Candidate, live.Candidate, StringComparison.Ordinal))
                        .Select(setup => new CandidateSetup(
                            setup.SessionDate, setup.Outcome ?? string.Empty, setup.Null, setup.NullAtSensitivity,
                            setup.BreakEven, setup.ReturnPct, setup.PlannedRisk, setup.OnEarnings)),
                ],
                nights.Where(evaluated => string.Equals(evaluated.Candidate, live.Candidate, StringComparison.Ordinal))
                    .Select(evaluated => (DateOnly?)evaluated.SessionDate)
                    .Min(),
                night);

        return new ProposalView(
            latest.Id,
            latest.Session,
            latest.Version,
            latest.Ordinary,
            latest.Levers,
            latest.ListNow,
            latest.ListProposed,
            latest.Findings,
            latest.Decision,
            latest.Reason,
            latest.Opened,
            live?.Candidate,
            SwingFamily.AcceptedWhileLive(rows),
            blocks);
    }

    // The count each of three operating obligations waits on, against its trigger, where no other surface
    // draws one: the research passes carrying a recorded cost, the event book's resolved setups, which
    // nothing scores yet, and the nights of trend labels under the version holding a new label for more
    // than one night. The night's wall clock and the bound on versions scored at once were settled by the
    // operator's ruling of 2026-09-28 and wait on no count.
    // owes: The spend cap set from the passes the ledger has priced
    // owes: The event setups' triggers calibrated from resolved setups
    // owes: The trend confirmation's nights settled from flip-backs
    // From 15.2 the S&P 400's and 600's members holding four dated rating counts, against nine in ten of them.
    // owes: Analyst coverage tested as a dial once dated counts exist
    public static IReadOnlyList<TriggerLine> Triggers(TriggerReads reads, PricedCalls priced, TriggerLine? labeller = null) =>
    [
        new(
            "spend cap",
            priced.Passes,
            20,
            "research pass(es) carry a recorded cost, against the twenty the spend cap is set from"),
        new(
            "event setups",
            0,
            250,
            "resolved event-book setups, against the 250 their triggers are calibrated from: no stage scores an event-book setup yet, so nothing counts toward it"),
        new(
            "trend confirmation",
            reads.ConfirmationNights ?? 0,
            60,
            reads.ConfirmationVersion is { } version
                ? $"night(s) of trend labels scored under '{version}' since its window opened, against the sixty its nights are settled from"
                : "night(s) of trend labels: no version holding a new label for more than one night is open, so nothing counts toward the sixty"),
        new(
            "analyst coverage",
            reads.RatedFourTimes,
            CoverageOf(reads.WiderMembers),
            FormattableString.Invariant($"of the S&P 400's and 600's {reads.WiderMembers} member(s) on the newest night hold four dated rating counts, against the nine in ten analyst coverage is tested as a dial from")),
        .. labeller is null ? Array.Empty<TriggerLine>() : new[] { labeller },
    ];

    // Nine in ten of the members, rounded up, the share of them that must hold four dated counts.
    public static int CoverageOf(int members) => ((members * 9) + 9) / 10;

    // The paid calls the log carries a recorded cost for, counted, their passes counted
    // by the run each was made under, and summed, off the rows the read surface handed back,
    // with how many came back inside a peak window, which is a pass that outlasted the bound
    // it was started under.
    // see: A pass starts only where the longest pass the store holds would end before a peak window opens
    public static PricedCalls Priced(IReadOnlyList<(string RunId, decimal Spend)> spends, int atPeak = 0) =>
        new(spends.Count, spends.Select(call => call.RunId).Distinct(StringComparer.Ordinal).Count(), spends.Sum(call => call.Spend), atPeak);

    // The night's steps in six groups, in the order the night runs them, which the Run page's time bar
    // draws. Every stage a night writes is in exactly one group, which `read-surface` asserts against the
    // worker's own steps, so a step added to the night without a group fails there.
    public static IReadOnlyList<(string Name, IReadOnlyList<string> Stages)> StepGroups { get; } =
    [
        ("Prices and calendar", ["migrate", "membership", "backfill", "fetch", "market-series", "actions", "calendar"]),
        ("Indicators and levels", ["indicators", "chart-averages", "swings", "volume-profile", "levels"]),
        ("Plans and moves", ["ladders", "moves"]),
        ("Readings and the list", ["swing-readings", "fundamental-readings", "member-readings", "listings", "swing-filter", "estimates", "family-rules", "families", "family-records", "heavyweights", "loop-apply", "index-families", "decision-cards", "rule-cards", "live-alarm", "taken-follower", "ledger", "shape-proposal"]),
        ("Records", ["facts", "changes", "forward-returns", "news-pulse", "rule-versions", "close"]),
        ("After the close", ["quarters", "filings", QueueStage, "report", "label-news", "backup"]),
    ];

    // The words a night's stop is written with, beside `ok`. The read surface holds no reference to the
    // worker, so they are stated here and `nightly-run` asserts they are the night close's own.
    public static IReadOnlyList<string> StopOutcomes { get; } = ["failed", "stopped", "refused"];

    // The stage the night closes its arithmetic on.
    public const string CloseStage = "close";

    // The run id every night the scheduler or a person starts carries, and the part a queue's pass adds to
    // it, whose rows are that pass's and not the night's.
    const string NightPrefix = "night-";
    const string QueuePass = "-queue-";

    // The part a night's try again adds to the id of the night's first try, before its number, the stage
    // and outcome of the row a stopped try writes beside its stop saying when the next starts, and the words
    // that row ends on. The read surface holds no reference to the worker, so they are stated here and
    // `nightly-run` asserts they are the night's own.
    public const string TryMark = "-try-";
    public const string TryAgainStage = "try again";
    public const string Waiting = "waiting";
    static readonly Regex NextTryAt = new(@" at (?<at>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z)$", RegexOptions.Compiled);

    // The instant a session's night is first read as never ran where no run of it is stored: 00:30 UTC on
    // the day after the session, an hour after the scheduled night starts.
    public static readonly TimeSpan NeverRanFrom = TimeSpan.FromHours(24.5);

    // How a night went, from its own run log rows alone. The night's runs are those whose id is a night's;
    // a try the night made again, or a run of its rest, carries the id of the run it continues with the try
    // mark and its number, and the newest run by the instant its id carries is read with its tries as one
    // night, each stage as the newest try that wrote it. A night run again whole for its session is a run of
    // its own. Its state: finished where the arithmetic closed; waiting to try again where the newest try
    // stopped before the close and wrote when the next starts, until that instant and the deadline after it
    // have passed; running where the newest try wrote neither a stop nor a close and its last row is inside
    // the night's deadline; left unfinished otherwise, with the step and the reason where a try stopped; and
    // where no run of the night is stored, not yet run until 00:30 UTC on the day after the session and never
    // ran from then, or no session on a day the exchange did not trade. A night whose first try holds the
    // night's lock and that wrote neither a stop nor a close is running whatever its last row's instant,
    // since a run of the rest of an earlier session's night stamps its rows on that session's evening. The
    // notice on tonight's page is handed this same view.
    // see: A night's state is read off its own run log rows and its tries, and the pages that state it read that one state
    public static NightView Night(IReadOnlyList<RunStageRow> log, DateOnly session, DateTimeOffset now, TimeSpan deadline, string? heldBy = null, string? refusal = null)
    {
        var runs = log
            .Where(row => row.RunId.StartsWith(NightPrefix, StringComparison.Ordinal) && !row.RunId.Contains(QueuePass, StringComparison.Ordinal))
            .GroupBy(row => FirstTry(row.RunId), StringComparer.Ordinal)
            .OrderBy(run => RunInstant(run.Key) ?? run.Min(row => row.StartedAt))
            .ToArray();

        var spend = log.Sum(row => decimal.TryParse(row.Spend, NumberStyles.Number, CultureInfo.InvariantCulture, out var paid) ? paid : 0m);
        var groups = StepGroups;

        if (runs.Length == 0)
        {
            // A refusal the night's script wrote for this session, before any worker existed, is the night's
            // state where no run of it is stored, with the reason the script gave.
            // see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
            var state = refusal is not null ? NightStates.Refused
                : !Traded(session) ? NightStates.NoSession
                : now < new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) + NeverRanFrom ? NightStates.NotYet
                : NightStates.NeverRan;

            return new NightView(
                session, state, null, refusal, null, null, null, deadline.TotalMinutes, null, 0, spend, 0, null,
                [.. groups.Select(group => new StepGroupView(group.Name, group.Stages.Count, 0, 0, false))]);
        }

        // The night's tries in order, the first under the id the night started with.
        var tries = runs[^1]
            .GroupBy(row => row.RunId, StringComparer.Ordinal)
            .OrderBy(attempt => TryNumber(attempt.Key))
            .Select(attempt => attempt.ToArray())
            .ToArray();

        var afterTheClose = groups[^1].Stages;
        var latest = tries[^1];

        // Each stage as the newest try that wrote it, so a try that ran from a step keeps what an earlier
        // try stored before it.
        var merged = tries
            .SelectMany(attempt => attempt)
            .Where(row => row.Stage != TryAgainStage)
            .GroupBy(row => row.Stage, StringComparer.Ordinal)
            .Select(stage => stage.Last())
            .ToArray();

        var close = merged.FirstOrDefault(row => row.Stage == CloseStage && row.Outcome == Ok);
        var stop = latest.FirstOrDefault(row => StopOutcomes.Contains(row.Outcome) && !afterTheClose.Contains(row.Stage));
        var afterStop = merged.FirstOrDefault(row => StopOutcomes.Contains(row.Outcome) && afterTheClose.Contains(row.Stage));
        var waiting = latest.FirstOrDefault(row => row.Stage == TryAgainStage && row.Outcome == Waiting);
        var nextTry = waiting is not null && NextTryAt.Match(waiting.Detail) is { Success: true } named
            && DateTimeOffset.TryParse(named.Groups["at"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var due)
                ? due
                : (DateTimeOffset?)null;
        var started = tries[0].Min(row => row.StartedAt);
        var written = latest.Max(row => row.EndedAt);
        var arithmetic = merged.Where(row => !afterTheClose.Contains(row.Stage)).ToArray();

        var nightState =
            merged.Any(row => row.Outcome == NoSession) ? NightStates.NoSession
            : close is not null ? NightStates.Finished
            : stop is not null && nextTry is { } next && now <= next + deadline ? NightStates.Waiting
            : stop is not null ? NightStates.Unfinished
            : string.Equals(heldBy, runs[^1].Key, StringComparison.Ordinal) || now - written <= deadline ? NightStates.Running
            : NightStates.Unfinished;

        var stoppedAt = nightState switch
        {
            NightStates.Waiting => stop!.Stage,
            NightStates.Unfinished when stop is not null => stop.Stage,
            NightStates.Running or NightStates.Unfinished => latest.Where(row => row.Stage != TryAgainStage).MaxBy(row => row.EndedAt)?.Stage,
            _ => null,
        };

        var madeTries = tries
            .Select((attempt, at) =>
            {
                var stopped = attempt.FirstOrDefault(row => StopOutcomes.Contains(row.Outcome) && !afterTheClose.Contains(row.Stage));

                return new NightTry(at + 1, attempt[0].RunId, stopped?.Stage, stopped?.Detail);
            })
            .ToArray();

        return new NightView(
            session,
            nightState,
            stoppedAt,
            stop is not null && nightState is NightStates.Waiting or NightStates.Unfinished ? stop.Detail : null,
            started,
            written,
            arithmetic.Length == 0 ? null : (arithmetic.Max(row => row.EndedAt) - started).TotalSeconds,
            deadline.TotalMinutes,
            close is not null && Regex.Match(close.Detail, @"^(\d+) name\(s\) computed") is { Success: true } computed
                ? int.Parse(computed.Groups[1].Value, CultureInfo.InvariantCulture)
                : null,
            runs.Sum(run => run.Sum(row => row.NetworkRequests)),
            spend,
            (tries.Length - 1) + runs[..^1].Sum(run => run.Count(row => StopOutcomes.Contains(row.Outcome))),
            nightState == NightStates.Finished && afterStop is not null ? $"{afterStop.Stage} {afterStop.Outcome}: {afterStop.Detail}" : null,
            [
                .. groups.Select(group =>
                {
                    var reached = merged.Where(row => group.Stages.Contains(row.Stage)).ToArray();

                    return new StepGroupView(
                        group.Name,
                        group.Stages.Count,
                        reached.Select(row => row.Stage).Distinct(StringComparer.Ordinal).Count(),
                        reached.Sum(row => (row.EndedAt - row.StartedAt).TotalSeconds),
                        stop is not null && nightState is NightStates.Waiting or NightStates.Unfinished && group.Stages.Contains(stop.Stage));
                }),
            ],
            madeTries,
            nightState == NightStates.Waiting ? nextTry : null,
            merged.FirstOrDefault(row => row.Stage == EquityBrief.Core.Configuration.NightBuild.Stage)?.Detail,
            close is null ? null : MembersOf(close.Detail));

        static string FirstTry(string runId)
        {
            var at = runId.IndexOf(TryMark, StringComparison.Ordinal);

            return at < 0 ? runId : runId[..at];
        }

        // Each index's members as the night's close counted them, on a night that read more than one index.
        static IReadOnlyList<IndexMembers>? MembersOf(string detail) =>
            Regex.Match(detail, @"; members on the session: (?<members>[A-Z]+ \d+(?:, [A-Z]+ \d+)*)") is { Success: true } counted
                ? [.. counted.Groups["members"].Value.Split(", ").Select(index => index.Split(' ')).Select(parts => new IndexMembers(parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture)))]
                : null;

        static int TryNumber(string runId)
        {
            var at = runId.IndexOf(TryMark, StringComparison.Ordinal);

            return at >= 0 && int.TryParse(runId[(at + TryMark.Length)..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 1;
        }

        static DateTimeOffset? RunInstant(string runId) =>
            runId.Length >= NightPrefix.Length + 16
                && DateTimeOffset.TryParseExact(runId.Substring(NightPrefix.Length, 16), "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                ? at
                : null;

        static bool Traded(DateOnly day)
        {
            try
            {
                return EquityBrief.Core.Bars.ExchangeClosures.IsSession(day);
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    // The market as the Run page pictures it: the night's stored reading, the floor of the market gate the
    // version the night ran under holds, and the breadth line over the sessions up to the night.
    // see: The market on the Run page is named in one word by a stated rule that moves no gate
    public static MarketPicture Pictured(DateOnly session, MarketView? night, string? settings, IReadOnlyList<BreadthPoint> line) =>
        new(session, night, (settings is null ? FilterSettings.Proposed : FilterSettings.Read(settings)).BreadthFloor, line);

    // How many evenings the freshness bars draw at most.
    public const int FreshEvenings = 20;

    // Whether the list finds new stocks: each of the evenings up to the night that the dated screens draw, the
    // newest twenty, with the names it listed and how many of them the evening before listed, each evening
    // read by the rule that listed it, as the list from night to night reads them.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    public static IReadOnlyList<FreshNight> Freshness(IReadOnlyList<ListingRow> listings, DateOnly night, DateOnly? first)
    {
        var sessions = listings.Select(listing => listing.SessionDate).Where(session => session <= night).Distinct().Order().ToArray();
        var listed = listings
            .Where(listing => listing.IsListed)
            .GroupBy(listing => listing.SessionDate)
            .ToDictionary(evening => evening.Key, evening => evening.Select(listing => listing.Ticker).ToHashSet(StringComparer.Ordinal));

        IReadOnlySet<string> On(DateOnly session) => listed.TryGetValue(session, out var names) ? names : new HashSet<string>(StringComparer.Ordinal);

        return
        [
            .. sessions
                .Select((session, at) => (Session: session, Before: at > 0 ? sessions[at - 1] : (DateOnly?)null))
                .Where(evening => first is not { } from || evening.Session >= from)
                .TakeLast(FreshEvenings)
                .Select(evening => new FreshNight(
                    evening.Session,
                    On(evening.Session).Count,
                    evening.Before is { } before ? On(evening.Session).Count(On(before).Contains) : 0)),
        ];
    }

    // Research over the seven nights up to the night: on each, the passes the paid model wrote, being the
    // research runs that made a paid call that answered.
    public static IReadOnlyList<ResearchNight> Research(IReadOnlyList<(DateOnly Night, IReadOnlyList<RunStageRow> Log)> nights) =>
    [
        .. nights.Select(night => new ResearchNight(
            night.Night,
            night.Log
                .Where(row => row.RunId.StartsWith(EquityBrief.Core.Research.PassRun.Prefix, StringComparison.Ordinal)
                    && row.Stage.StartsWith("research call", StringComparison.Ordinal)
                    && !EquityBrief.Core.Research.TrialCalls.Is(row.Stage)
                    && row.Outcome == Ok)
                .Select(row => row.RunId)
                .Distinct(StringComparer.Ordinal)
                .Count())),
    ];

    // How each report came out. A report is a research pass whose paid model answered at least once, dated by the
    // day its own row names; each is read off its own rows and the versions it wrote, as the checker left them:
    //
    // - a section the pass did not warrant stood from an earlier day;
    // - one whose accepted version was the pass's first draft passed first time, and one whose accepted version
    //   was its retry passed on retry;
    // - any other warranted section was left out, with the line the pass's row names it by, or the checker's
    //   refusal as the name page words it, or that it was not written.
    //
    // The industry cycle is read off the theme pass under the same run. A section's cost is its own calls under the
    // run, every round, and a report's is every call; a trial's calls are neither. The two cases' cell is marked
    // where any draft of the pass carried a figure on both sides, the first as well as the retry, since the count
    // measures the ask and not what the checker let through.
    //
    // The reports drawn are those dated on or after the first of the seven nights and on or before the night;
    // each section's rates are read over the newest twenty reports up to the night that warranted it, and the
    // both-sides count over the newest twenty that drafted the two cases, with the count read where fewer exist.
    // see: The run page draws how each report's sections came out and each section's rates over the newest twenty reports, and no trial's drafts
    public static ReportsView Reports(IReadOnlyList<RunStageRow> rows, IReadOnlyList<WrittenVersion> versions, DateOnly from, DateOnly night)
    {
        var written = versions
            .GroupBy(version => (version.Owner, version.Section, version.Version))
            .ToDictionary(group => group.Key, group => group.First());

        var reports = new List<ReportRow>();
        var passes = new Dictionary<string, (ReportRow Report, PassDetail Pass)>(StringComparer.Ordinal);

        foreach (var run in rows.GroupBy(row => row.RunId, StringComparer.Ordinal))
        {
            if (run.FirstOrDefault(row => row.Stage == ReadApi.ResearchStage) is not { } own
                || PassDetail.Read(own.Detail) is not { } pass
                || pass.Day > night)
            {
                continue;
            }

            var calls = run.Where(row => row.Stage.StartsWith(ReadApi.PaidCallStage + ":", StringComparison.Ordinal) && !TrialCalls.Is(row.Stage)).ToArray();

            if (!calls.Any(row => row.Outcome == Ok))
            {
                continue;
            }

            var theme = run.FirstOrDefault(row => row.Stage == ReadApi.ThemeStage) is { } themed ? PassDetail.Read(themed.Detail) : null;

            var report = new ReportRow(
                run.Key,
                pass.Ticker,
                pass.Day,
                calls.Sum(row => Money(row.Spend)),
                [.. ClaimRules.Sections.Select(section => Cell(section, pass, theme, calls, written))]);

            reports.Add(report);
            passes[run.Key] = (report, pass);
        }

        var newest = reports.OrderByDescending(report => report.Day).ThenByDescending(report => report.RunId, StringComparer.Ordinal).ToArray();
        var (bothSides, drafted) = BothSidesOver(newest, ReportsView.RateWindow);

        // A trial's row and a review's for one section of one report are one entry, the trial's first.
        var trials = rows
            .Where(row => passes.ContainsKey(row.RunId)
                && (row.Stage.StartsWith(ReadApi.TrialStage + ":", StringComparison.Ordinal) || row.Stage.StartsWith(ReadApi.ReviewStage + ":", StringComparison.Ordinal)))
            .Select(row => (Row: row, Side: Asked(row)))
            .Where(one => one.Side is not null)
            .GroupBy(one => (one.Row.RunId, one.Side!.Value.Section))
            .Select(group => Trial(group.Key.RunId, group.Key.Section, passes[group.Key.RunId].Report, passes[group.Key.RunId].Pass, written, [.. group.Select(one => one.Side!.Value)]))
            .ToArray();

        return new ReportsView(
            [.. newest.Where(report => report.Day >= from)],
            newest.Length,
            RatesOver(newest, ReportsView.RateWindow),
            bothSides,
            drafted,
            trials);
    }

    // Each section's share passed first time and share left out over the reports handed in, newest first, read over
    // at most the window's number of those that warranted it.
    public static IReadOnlyList<SectionRate> RatesOver(IReadOnlyList<ReportRow> newest, int window) =>
    [
        .. ClaimRules.Sections.Select(section =>
        {
            var read = newest.Select(report => CellOf(report, section)).Where(cell => cell.Outcome != ReportsView.NotWarranted).Take(window).ToArray();

            return new SectionRate(section, read.Length, read.Count(cell => cell.Outcome == ReportsView.FirstTime), read.Count(cell => cell.Outcome == ReportsView.LeftOut));
        }),
    ];

    // Of the reports handed in, newest first, the first window's number that drafted the two cases, and how many of those
    // carried a figure on both sides in any draft.
    public static (int BothSides, int Drafted) BothSidesOver(IReadOnlyList<ReportRow> newest, int window)
    {
        var drafted = newest.Select(report => CellOf(report, ClaimRules.TwoCasesSection)).Where(cell => cell.Drafts > 0).Take(window).ToArray();

        return (drafted.Count(cell => cell.BothSides), drafted.Length);
    }

    static ReportCell CellOf(ReportRow report, string section) => report.Cells.Single(cell => cell.Section == section);

    // What the comparison command writes, read over every report the store holds: each section a trial or a review
    // asked for beside a report; each section's rates over the ten reports whose passes started before the research
    // prompt's addendum merged and the ten that started after it; and, over the reports started after the two cases
    // were asked to argue each fact on one side, the first twenty that drafted the two cases and how many carried a
    // figure on both sides. A report is placed by the instant its run is named for, since a day holds several.
    // see: The drafts compared beside a report and the research template's before and after counts are written to files by a command, and drawn on no page
    public static ComparisonView Comparisons(IReadOnlyList<RunStageRow> rows, IReadOnlyList<WrittenVersion> versions)
    {
        var all = Reports(rows, versions, DateOnly.MinValue, DateOnly.MaxValue);

        var started = all.Reports
            .Select(report => (Report: report, At: PassRun.StartedAt(report.RunId)))
            .Where(one => one.At is not null)
            .Select(one => (one.Report, At: one.At!.Value))
            .ToArray();

        ReportRow[] Newest(IEnumerable<(ReportRow Report, DateTimeOffset At)> reports) =>
            [.. reports.OrderByDescending(one => one.At).Select(one => one.Report)];

        var before = Newest(started.Where(one => one.At < ComparisonView.AddendumMergedAt).OrderByDescending(one => one.At).Take(ComparisonView.RatesWindow));
        var after = Newest(started.Where(one => one.At >= ComparisonView.AddendumMergedAt).OrderBy(one => one.At).Take(ComparisonView.RatesWindow));

        var sinceTheAsk = Newest(started
            .Where(one => one.At >= ComparisonView.AskChangedAt && CellOf(one.Report, ClaimRules.TwoCasesSection).Drafts > 0)
            .OrderBy(one => one.At)
            .Take(ComparisonView.BothSidesWindow));

        return new ComparisonView(
            all.Trials,
            before,
            after,
            RatesOver(before, ComparisonView.RatesWindow),
            RatesOver(after, ComparisonView.RatesWindow),
            sinceTheAsk,
            BothSidesOver(sinceTheAsk, ComparisonView.BothSidesWindow).BothSides);
    }

    static ReportCell Cell(
        string section,
        PassDetail pass,
        PassDetail? theme,
        IReadOnlyList<RunStageRow> calls,
        IReadOnlyDictionary<(string Owner, string Section, int Version), WrittenVersion> versions)
    {
        var cost = calls.Where(row => CallFor(row.Stage, section)).Sum(row => Money(row.Spend));

        if (!pass.Warranted.Contains(section, StringComparer.Ordinal))
        {
            return new ReportCell(section, ReportsView.NotWarranted, null, cost, 0, false);
        }

        var (owner, drafts) = section == ClaimRules.CycleSection
            ? (theme?.Owner ?? string.Empty, theme?.Written.Where(one => one.Section == section).ToArray() ?? [])
            : (pass.Owner, pass.Written.Where(one => one.Section == section).ToArray());

        var held = drafts.Select(one => (one.Retry, Version: versions.GetValueOrDefault((owner, section, one.Version)))).ToArray();

        var bothSides = section == ClaimRules.TwoCasesSection
            && held.Any(one => one.Version is { Prose.Length: > 0 } version && ClaimRules.FiguresOnBothSides(version.Prose) is { Count: > 0 });

        if (held.FirstOrDefault(one => one.Version is { Status: Accepted }) is { Version: not null } passed)
        {
            return new ReportCell(section, passed.Retry ? ReportsView.OnRetry : ReportsView.FirstTime, null, cost, drafts.Length, bothSides);
        }

        var refused = held.Select(one => one.Version?.RejectReason).LastOrDefault(reason => reason is not null);
        var why = pass.NotWritten.FirstOrDefault(one => one.Section == section).Reason
            ?? (refused is null ? NotWrittenLine : NameScreen.Refused(refused));

        return new ReportCell(section, ReportsView.LeftOut, why, cost, drafts.Length, bothSides);
    }

    // A section's own calls: its stage names the section, alone or with the round it was asked in.
    static bool CallFor(string stage, string section)
    {
        var named = ReadApi.PaidCallStage + ": " + section;

        return stage == named || stage.StartsWith(named + ", ", StringComparison.Ordinal);
    }

    // The pass's own side read off its cell and the versions it wrote, first draft first, beside the sides asked.
    static TrialRow Trial(
        string runId,
        string section,
        ReportRow report,
        PassDetail pass,
        IReadOnlyDictionary<(string Owner, string Section, int Version), WrittenVersion> versions,
        IReadOnlyList<(string Section, string Compared, TrialSide Side)> asked)
    {
        var own = report.Cells.Single(cell => cell.Section == section);

        var drafts = pass.Written
            .Where(one => one.Section == section)
            .Select(one => versions.GetValueOrDefault((pass.Owner, section, one.Version))?.Prose ?? string.Empty)
            .ToArray();

        return new TrialRow(
            runId,
            report.Ticker,
            report.Day,
            section,
            new TrialSide(asked[0].Compared, own.Outcome, drafts.Length, own.Cost, drafts, TrialSide.OfPass),
            [.. asked.Select(one => one.Side).OrderBy(side => side.Side == TrialSide.OfTrial ? 0 : 1)]);
    }

    // One trial's or review's row read as its side: the model asked, how it came out, its rounds, cost and drafts, and
    // the model of the pass's draft it was asked beside. None where the row cannot be read.
    static (string Section, string Compared, TrialSide Side)? Asked(RunStageRow row)
    {
        try
        {
            using var detail = JsonDocument.Parse(row.Detail);
            var root = detail.RootElement;
            var rounds = root.GetProperty("rounds").EnumerateArray().ToArray();

            return (
                root.GetProperty("section").GetString()!,
                root.GetProperty("compared").GetProperty("model").GetString() ?? string.Empty,
                new TrialSide(
                    root.GetProperty("model").GetString() ?? string.Empty,
                    root.GetProperty("outcome").GetString() ?? string.Empty,
                    rounds.Length,
                    Money(root.GetProperty("cost").GetString() ?? NothingSpentText),
                    [.. rounds.Select(round => round.TryGetProperty("prose", out var prose) && prose.ValueKind == JsonValueKind.String ? prose.GetString()! : string.Empty)],
                    row.Stage.StartsWith(ReadApi.ReviewStage + ":", StringComparison.Ordinal) ? TrialSide.OfReview : TrialSide.OfTrial));
        }
        catch (Exception unread) when (unread is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    const string Accepted = "accepted";
    const string NotWrittenLine = "not written";
    const string NothingSpentText = "0";

    static decimal Money(string spend) =>
        decimal.TryParse(spend, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount) ? amount : 0m;

    // What a pass's or a theme pass's row says it did: whose report it was and the day it wrote for, the sections it
    // warranted, each version it wrote with whether that was a retry, and each section it did not write with why.
    sealed record PassDetail(
        string Owner,
        string Ticker,
        DateOnly Day,
        IReadOnlyList<string> Warranted,
        IReadOnlyList<(string Section, int Version, bool Retry)> Written,
        IReadOnlyList<(string Section, string? Reason)> NotWritten)
    {
        public static PassDetail? Read(string detail)
        {
            try
            {
                using var parsed = JsonDocument.Parse(detail);
                var root = parsed.RootElement;

                string Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

                IEnumerable<JsonElement> List(string name) =>
                    root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

                if (!DateOnly.TryParseExact(Text("asOf"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    return null;
                }

                var ticker = Text("ticker");

                return new PassDetail(
                    ticker.Length > 0 ? ticker : Text("theme"),
                    ticker,
                    day,
                    [.. List("warranted").Where(one => one.ValueKind == JsonValueKind.String).Select(one => one.GetString()!)],
                    [.. List("written").Select(one => (one.GetProperty("section").GetString()!, one.GetProperty("version").GetInt32(), one.GetProperty("retry").GetBoolean()))],
                    [.. List("notWritten").Select(one => (one.GetProperty("section").GetString()!, one.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null))]);
            }
            catch (Exception unread) when (unread is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return null;
            }
        }
    }

    // A version's name as a link carries it: its words in lower case with every run of anything else one dash.
    public static string Slug(string candidate) =>
        Regex.Replace(candidate.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    // The settings a candidate was registered with, read from its register row.
    static IReadOnlyDictionary<string, double> Settings(string parameters)
    {
        using var document = JsonDocument.Parse(parameters);

        return document.RootElement.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.Number)
            .ToDictionary(property => property.Name, property => property.Value.GetDouble(), StringComparer.Ordinal);
    }

    // What a version changes against the live list, in plain words, read off the two registered settings.
    public static string Changes(IReadOnlyDictionary<string, double> version, IReadOnlyDictionary<string, double> live)
    {
        double Of(IReadOnlyDictionary<string, double> settings, string key) => settings.TryGetValue(key, out var held) ? held : double.NaN;
        bool Moved(string key) => !Of(version, key).Equals(Of(live, key));

        static string Plan(double trade) => trade switch
        {
            0 => "the ladder's first tranche",
            1 => "a stop and target at the nearest bands",
            _ => "a stop and target clear of the noise",
        };

        static string Strength(double floor) => Math.Abs(floor - (2.0 / 3)) < 0.001 ? "the top third" : Math.Abs(floor - 0.5) < 0.001 ? "the top half" : floor.ToString("0.00", CultureInfo.InvariantCulture);

        static string Share(double of) => of switch
        {
            2 => "half",
            3 => "third",
            4 => "quarter",
            _ => FormattableString.Invariant($"1 in {of:0}"),
        };

        // A registration that states no count a night keeps every name it passes, as one stating none does.
        static double BestOf(IReadOnlyDictionary<string, double> settings) => settings.TryGetValue("bestOf", out var count) ? count : 0;

        // A registration written before the estimates were a setting reads none, as one stating nought does.
        static double RaisedEstimates(IReadOnlyDictionary<string, double> settings) => settings.TryGetValue("raisedEstimates", out var reads) ? reads : 0;

        var said = new List<string>();
        var named = new HashSet<string>(StringComparer.Ordinal) { "marketGate", "strengthFloor", "depthLow", "depthHigh", "arrivalSessions", "trade", "rewardToRiskFloor", "skipDeteriorating", "leaderSectors", "leaderShareOf", "bestOf", "raisedEstimates" };

        if (Moved("marketGate"))
        {
            said.Add(Of(version, "marketGate") == 0 ? "lists on every night, the market gate off" : "reads the market gate, which the live list does not");
        }

        // The seventh candidate, which leaves off a business whose reported quarters read deteriorating.
        // see: The seventh swing family candidate leaves off a member whose reported quarters read deteriorating, and no live rule removes a stock for its state
        if (Moved("skipDeteriorating"))
        {
            said.Add(Of(version, "skipDeteriorating") == 1
                ? "leaves off a business whose reported quarters read deteriorating, which the live list keeps"
                : "keeps a business whose reported quarters read deteriorating, which the live list leaves off");
        }

        // The eighth, which reads sector leadership in place of the trend and strength gate.
        // see: The sector leaders are a variant of the pullback's starting point and not a family of their own
        if (Moved("leaderSectors") || Moved("leaderShareOf"))
        {
            said.Add(Of(version, "leaderSectors") > 0 && Of(version, "leaderShareOf") > 0
                ? FormattableString.Invariant($"only the top {Share(Of(version, "leaderShareOf"))} of one of the {Of(version, "leaderSectors"):0} strongest sectors, in place of an uptrend's strength")
                : "an uptrend's strength in place of sector leadership");
        }

        // The analysts' revisions, which lists only a member whose estimates were raised.
        // see: The pullback's sector leaders' rule is retired and the analysts' revisions variant registered in its place
        if (!RaisedEstimates(version).Equals(RaisedEstimates(live)))
        {
            said.Add(RaisedEstimates(version) == 1
                ? "only a stock whose analysts raised their estimate for its year over the last 30 days, which the live list does not ask"
                : "whatever its analysts' estimates did, where the live list asks that they were raised over the last 30 days");
        }

        // The ninth, which keeps the night's first few in the list's own order.
        // see: The pullback's ninth rule keeps the night's best three in the list's own order, and the family is registered again whole to add it
        if (!BestOf(version).Equals(BestOf(live)))
        {
            said.Add(BestOf(version) > 0
                ? FormattableString.Invariant($"keeps only the night's first {BestOf(version):0} in the list's own order, where the live list keeps every name it passes")
                : FormattableString.Invariant($"keeps every name it passes, where the live list keeps only the night's first {BestOf(live):0}"));
        }

        if (Moved("strengthFloor"))
        {
            said.Add($"only strength in {Strength(Of(version, "strengthFloor"))}, not {Strength(Of(live, "strengthFloor"))}");
        }

        if (Moved("depthLow") || Moved("depthHigh"))
        {
            said.Add(FormattableString.Invariant($"only dips of {Of(version, "depthLow"):0.#} to {Of(version, "depthHigh"):0.#} typical days, not {Of(live, "depthLow"):0.#} to {Of(live, "depthHigh"):0.#}"));
        }

        if (Moved("arrivalSessions"))
        {
            said.Add(Of(version, "arrivalSessions") == 1
                ? FormattableString.Invariant($"the buy signal must arrive tonight, not within {Of(live, "arrivalSessions"):0} sessions")
                : FormattableString.Invariant($"the buy signal may arrive within {Of(version, "arrivalSessions"):0} sessions, not {Of(live, "arrivalSessions"):0}"));
        }

        if (Moved("trade"))
        {
            said.Add($"{Plan(Of(version, "trade"))}, not {Plan(Of(live, "trade"))}");
        }

        if (Moved("rewardToRiskFloor"))
        {
            said.Add(FormattableString.Invariant($"a reward to risk of {Of(version, "rewardToRiskFloor"):0.##} or more, not {Of(live, "rewardToRiskFloor"):0.##}"));
        }

        said.AddRange(version.Keys.Union(live.Keys).Where(key => !named.Contains(key) && Moved(key)).Order(StringComparer.Ordinal)
            .Select(key => FormattableString.Invariant($"{key} at {Of(version, key):0.###} where the live list has {Of(live, key):0.###}")));

        var sentence = said.Count == 0 ? "the same settings as the live list" : string.Join("; ", said);

        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    // The versions running beside the live list, the live one first, each with what it changes, the stocks it
    // has picked over the nights up to the night, the share of those the live list also picked, and the
    // blocks its edge clock holds against the floor of its first look. Picks only: no figure here is read from
    // how a trade turned out.
    // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
    public static IReadOnlyList<VersionLine> Versions(IReadOnlyList<CandidateRow> register, EdgeView edge, IReadOnlyList<ShadowPickRow> picks)
    {
        var standing = edge.Candidates;
        var settings = register
            .Where(row => row.Retires is null)
            .GroupBy(row => row.Candidate, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Settings(group.OrderBy(row => row.RegisteredAt).Last().Parameters), StringComparer.Ordinal);
        var live = standing.FirstOrDefault(candidate => candidate.Live);
        var liveSettings = live is not null && settings.TryGetValue(live.Candidate, out var held) ? held : new Dictionary<string, double>(StringComparer.Ordinal);

        return
        [
            .. standing
                .OrderByDescending(candidate => candidate.Live)
                .Select(candidate =>
                {
                    var fired = picks.Where(pick => pick.Fired && pick.Candidate == candidate.Candidate).ToArray();

                    return new VersionLine(
                        candidate.Candidate,
                        Slug(candidate.Candidate),
                        candidate.Live ? "The rules that pick tonight's stocks" : Changes(settings.GetValueOrDefault(candidate.Candidate) ?? liveSettings, liveSettings),
                        fired.Length,
                        fired.Length == 0 ? null : 1.0 * fired.Count(pick => pick.LivePassed) / fired.Length,
                        candidate.Record.Blocks,
                        candidate.Record.Floor,
                        candidate.Live);
                }),
        ];
    }

    // Tonight's picks beside one background version's: the version the link names or the first after the
    // live list, the names only the live list picked with the setting the version read them against, the
    // names both picked, the names only the version picked with the gate the live list stopped them at, and
    // over the last twenty evenings the version's picks, the share of them the live list also picked and the
    // evenings it picked a name the live list did not. Picks only.
    // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
    public static CompareView Compare(
        DateOnly night,
        IReadOnlyList<VersionLine> versions,
        IReadOnlyList<CandidateRow> register,
        string? asked,
        IReadOnlyList<ShadowPickRow> picks,
        IReadOnlySet<string> evaluated,
        IReadOnlyList<GateResultRow> tonight)
    {
        var chosen = versions.FirstOrDefault(version => !version.Live && version.Slug == asked) ?? versions.FirstOrDefault(version => !version.Live);

        if (chosen is null)
        {
            return new CompareView(night, versions, null, false, [], [], [], 0, 0, null, 0);
        }

        var settings = register
            .Where(row => row.Retires is null && row.Candidate == chosen.Candidate)
            .OrderBy(row => row.RegisteredAt)
            .Select(row => Settings(row.Parameters))
            .LastOrDefault() ?? new Dictionary<string, double>(StringComparer.Ordinal);

        var mine = picks.Where(pick => pick.Candidate == chosen.Candidate).ToArray();
        var live = tonight.Where(row => row.Passed).Select(row => row.Ticker).ToHashSet(StringComparer.Ordinal);
        var version = mine.Where(pick => pick.Session == night && pick.Fired).Select(pick => pick.Ticker).ToHashSet(StringComparer.Ordinal);
        var rows = tonight.ToDictionary(row => row.Ticker, StringComparer.Ordinal);
        var answers = mine.Where(pick => pick.Session == night).ToDictionary(pick => pick.Ticker, pick => pick.Values, StringComparer.Ordinal);

        var sessions = picks.Select(pick => pick.Session).Where(session => session <= night).Distinct().OrderDescending().Take(FreshEvenings).ToHashSet();
        var window = mine.Where(pick => pick.Fired && sessions.Contains(pick.Session)).ToArray();

        return new CompareView(
            night,
            versions,
            chosen,
            evaluated.Contains(chosen.Candidate),
            [.. live.Except(version).Order(StringComparer.Ordinal).Select(ticker => new ComparedName(ticker, rows.TryGetValue(ticker, out var row) && answers.TryGetValue(ticker, out var said) ? WhyNotTheVersion(row, said, settings) : null))],
            [.. live.Intersect(version).Order(StringComparer.Ordinal)],
            [.. version.Except(live).Order(StringComparer.Ordinal).Select(ticker => new ComparedName(ticker, rows.TryGetValue(ticker, out var row) ? WhyNotTheLiveList(row) : null))],
            sessions.Count,
            window.Length,
            window.Length == 0 ? null : 1.0 * window.Count(pick => pick.LivePassed) / window.Length,
            window.Where(pick => !pick.LivePassed).Select(pick => pick.Session).Distinct().Count());
    }

    // The gates in the order the filter reads them.
    static readonly string[] GateOrder = [SwingGates.Market, SwingGates.Trend, SwingGates.Setup, SwingGates.Trigger, SwingGates.Trade];

    // Why the live list stopped a name the version picked: the first gate the live row failed, in the words
    // the live row stored for it, or the exclusion that took it.
    static string WhyNotTheLiveList(GateResultRow row)
    {
        var gates = Gates(row.Gates);

        foreach (var gate in GateOrder)
        {
            if (gates.TryGetValue(gate, out var read) && !read.Passed)
            {
                return $"the live list's {gate} gate: {read.Reason}";
            }
        }

        return row.Exclusions.Count > 0 ? "excluded from the live list: " + string.Join(", ", row.Exclusions) : "the live list did not pick it";
    }

    // Why the version did not pick a name the live list picked: the first gate the version's answer failed,
    // said with the live row's stored reading for that gate and the version's own setting.
    static string WhyNotTheVersion(GateResultRow row, string answer, IReadOnlyDictionary<string, double> settings)
    {
        using var document = JsonDocument.Parse(answer);

        var values = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.ToString(), StringComparer.Ordinal);
        var gates = Gates(row.Gates);

        string Value(string gate, string key) => gates.TryGetValue(gate, out var read) && read.Values.TryGetValue(key, out var held) ? held : "none";

        double? Number(string gate, string key) =>
            double.TryParse(Value(gate, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

        double Setting(string key) => settings.TryGetValue(key, out var held) ? held : double.NaN;

        foreach (var gate in GateOrder)
        {
            if (!values.TryGetValue(gate, out var answered) || answered != "failed")
            {
                continue;
            }

            return gate switch
            {
                SwingGates.Market => FormattableString.Invariant($"the market's breadth of {Number(gate, "breadth") * 100:0.0}% is below this version's floor of {Setting("breadthFloor") * 100:0}%"),
                SwingGates.Trend => FormattableString.Invariant($"strength {Number(gate, "strength"):0.000} is below this version's floor of {Setting("strengthFloor"):0.000}"),
                SwingGates.Setup => FormattableString.Invariant($"dipped {Number(gate, "depth"):0.0} typical days, outside this version's {Setting("depthLow"):0.#} to {Setting("depthHigh"):0.#}"),
                SwingGates.Trigger => FormattableString.Invariant($"its buy signal arrived {Value(gate, "arrived")}, outside this version's window of {Setting("arrivalSessions"):0} session(s)"),
                _ => FormattableString.Invariant($"its reward to risk on this version's plan is {(Setting("trade") == 1 ? row.SwingRewardToRisk : Setting("trade") == 0 ? row.LadderRewardToRisk : row.ClearRewardToRisk):0.00}, against a floor of {Setting("rewardToRiskFloor"):0.##}"),
            };
        }

        return values.TryGetValue("exclusions", out var excluded) && excluded != "none" ? "excluded by this version: " + excluded : "this version did not pick it";
    }

    // Each gate a live row stored, with whether it passed, the reason it gave and the values it was read over.
    static Dictionary<string, (bool Passed, string Reason, IReadOnlyDictionary<string, string> Values)> Gates(string stored)
    {
        using var document = JsonDocument.Parse(string.IsNullOrEmpty(stored) ? "{}" : stored);

        var gates = new Dictionary<string, (bool, string, IReadOnlyDictionary<string, string>)>(StringComparer.Ordinal);

        if (document.RootElement.TryGetProperty("gates", out var list))
        {
            foreach (var gate in list.EnumerateArray())
            {
                gates[gate.GetProperty("gate").GetString() ?? string.Empty] = (
                    gate.GetProperty("passed").GetBoolean(),
                    gate.TryGetProperty("reason", out var reason) ? reason.GetString() ?? string.Empty : string.Empty,
                    gate.TryGetProperty("values", out var read)
                        ? read.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
                        : new Dictionary<string, string>(StringComparer.Ordinal));
            }
        }

        return gates;
    }

    // Each version at a checkpoint: locked with its setups and blocks until its first look is read, and from
    // then the last look's share, the break-even its plans needed, what no skill scored from the same starts,
    // the smallest excess luck alone could not explain, and the verdict in words.
    // see: A candidate's verdict is read only at looks fixed when it is registered, with each look's boundary found over every sign vector its blocks allow
    public static IReadOnlyList<CheckpointRow> Checkpoints(EdgeView edge) =>
    [
        .. edge.Candidates
            .OrderByDescending(candidate => candidate.Live)
            .Select(candidate =>
            {
                var record = candidate.Record;
                var look = record.Looks.Count > 0 ? record.Looks[^1] : null;

                return new CheckpointRow(
                    candidate.Candidate,
                    candidate.Live,
                    record.Setups,
                    record.Blocks,
                    record.Floor,
                    look?.Share,
                    look is null ? null : record.PlannedBreakEven,
                    look?.NullShare,
                    look?.SmallestExcess,
                    record.Verdict);
            }),
    ];

    // The checklist: each item held, failed with why, or not read where the night stored nothing it could be
    // read from. Whether every step finished is the night's own state, the one region 1 draws, so a command
    // run by hand that day is none of the night's steps. The quarters are read off the night's own quarters
    // step: asked on schedule where it ran and neither refused an ask nor left one at its limit or the day's
    // allowance.
    // see: A night's state is read off its own run log rows and its tries, and the pages that state it read that one state
    //
    // Two items follow them. A paid job's profile whose provider publishes a retirement date within thirty
    // days of the night is named with the job using it and the date, so a switch is made before the model
    // stops answering. And a report whose pass ran on the night's session and cost more than the amount
    // section 17 names is named with its cost, so an expensive report is seen the morning after it was
    // written rather than in the month's spend.
    // see: A profile carries its provider's earliest retirement date, and the run page names it from thirty days before
    //
    // Two more follow where the page was handed the store's copies as read: the store copied after the night
    // began, held by a copy made since it began or by one started since that is waiting or copying still, and
    // failed by one that was ended before it finished, by one that made none, or by none started since; and
    // every copy the newest copy's row before it kept still in the folder, failed where the newest copy found
    // any gone that no copy removed, each named. Neither is read where the store holds no copy's row.
    // see: The store is copied once the night and every process it started have finished and the newest three copies are kept after each is opened and read, and the copy writes a row as it starts and one as it ends
    public static IReadOnlyList<WorryItem> Worries(
        IReadOnlyList<string> stale,
        NightView night,
        IReadOnlyList<RefusedDocument> refused,
        IReadOnlyList<LeftOutSection> fellBack,
        IReadOnlyList<RunStageRow> log,
        IReadOnlyList<ModelProfile>? profiles = null,
        StoreCopyRead? storeCopy = null,
        IReadOnlyList<(string Family, string Rule, int Stretch, int Mark)>? pastTheirMark = null)
    {
        var quarters = log
            .Where(row => row.RunId.StartsWith(NightPrefix, StringComparison.Ordinal) && row.Stage == "quarters")
            .OrderBy(row => row.StartedAt)
            .LastOrDefault();

        WorryItem Item(string item, bool held, string why) => new(item, held ? WorryItem.Held : WorryItem.Failed, held ? null : why);

        var refusedAsks = quarters is null ? null : Regex.Match(quarters.Detail, @"(\d+) refused;");
        var leftAt = quarters is null ? null : Regex.Match(quarters.Detail, @"(\d+) left at the (step's limit|day's allowance)");

        var retiring = (profiles ?? []).Where(profile => ModelProfiles.RetiresSoon(profile, night.Session)).ToArray();

        var dear = log
            .Where(row => row.RunId.StartsWith(PassRun.Prefix, StringComparison.Ordinal))
            .GroupBy(row => row.RunId, StringComparer.Ordinal)
            .Select(run => (Ticker: run.Key[(run.Key.LastIndexOf('-') + 1)..], Spent: run.Sum(row => Spent(row.Spend))))
            .Where(run => run.Spent > SpendCaps.ReportNamedAbove)
            .ToArray();

        // A drain that stopped on an error outside a pass, on the night its row fell on.
        // see: A drain that stops on an error writes a row of its own, and the queue page states it until a pass starts after it
        var stops = log
            .Where(row => DrainStops.IsStop(row.RunId, row.Stage, row.Outcome))
            .OrderBy(row => row.StartedAt)
            .ToArray();

        return
        [
            Item("Every stock has the night's prices", stale.Count == 0, FormattableString.Invariant($"{stale.Count} carry an earlier session's bars: {string.Join(", ", stale)}")),
            Item(
                "Every step of the night finished",
                night.State is NightStates.Finished or NightStates.NoSession && night.AfterTheClose is null,
                night.State switch
                {
                    NightStates.Finished => "after the close, " + night.AfterTheClose,
                    NightStates.Waiting => $"it stopped at {night.StoppedAt} and is waiting to try again: {night.Reason}",
                    NightStates.Running => $"it is still running, last at {night.StoppedAt}",
                    NightStates.Unfinished when night.Reason is { } reason => $"it was left unfinished at {night.StoppedAt}: {reason}",
                    NightStates.Unfinished => $"it was left unfinished at {night.StoppedAt}",
                    _ => "no night ran for this session",
                }),
            Item("No research document was refused", refused.Count == 0, FormattableString.Invariant($"{refused.Count} refused by admissibility")),
            Item("No report section fell back", fellBack.Count == 0, FormattableString.Invariant($"{fellBack.Count} fell back: {string.Join(", ", fellBack.Select(section => section.Subject + " " + section.Section))}")),
            quarters is null
                ? new WorryItem("Every company awaiting a new quarter was asked on schedule", WorryItem.NotRead, "the night ran no quarters step")
                : Item(
                    "Every company awaiting a new quarter was asked on schedule",
                    quarters.Outcome == Ok && refusedAsks is { Success: true } && refusedAsks.Groups[1].Value == "0" && leftAt is { Success: false },
                    quarters.Outcome != Ok
                        ? $"the quarters step {quarters.Outcome}: {quarters.Detail}"
                        : leftAt is { Success: true }
                            ? $"{leftAt.Groups[1].Value} left at the {leftAt.Groups[2].Value}"
                            : $"{refusedAsks!.Groups[1].Value} ask(s) refused"),
            Item(
                "No paid model a job uses is within thirty days of its retirement date",
                retiring.Length == 0,
                string.Join("; ", retiring.Select(profile =>
                    FormattableString.Invariant($"{profile.Name}, used by the {profile.Job.ToLowerInvariant()} job, may be retired from {profile.Retires:yyyy-MM-dd}")
                    + (profile.RetiresReadOn is { } read ? FormattableString.Invariant($", as its provider stated on {read:yyyy-MM-dd}") : string.Empty)))),
            Item(
                "No report cost more than " + SpendVerdict.Money(SpendCaps.ReportNamedAbove),
                dear.Length == 0,
                string.Join("; ", dear.Select(run => $"{run.Ticker}'s report cost {SpendVerdict.Money(run.Spent)}"))),
            Item(
                "No drain stopped on an error",
                stops.Length == 0,
                string.Join("; ", stops.Select(stop => FormattableString.Invariant($"the drain that started at {stop.StartedAt.UtcDateTime:HH:mm} UTC stopped on an error: {stop.Detail}")))),
            .. storeCopy is null ? [] : CopyItems(night, storeCopy),
            .. pastTheirMark is null ? Array.Empty<WorryItem>() : [StretchItem(pastTheirMark)],
        ];
    }

    // The live rules whose empty stretch is past the mark their own past empty nights set, each named with its stretch
    // against its mark; held where none is.
    // see: A card's stretch line counts its mark over past empty nights and draws none under thirty completed stretches
    public static WorryItem StretchItem(IReadOnlyList<(string Family, string Rule, int Stretch, int Mark)> pastTheirMark) =>
        pastTheirMark.Count == 0
            ? new WorryItem(StretchWorry, WorryItem.Held, null)
            : new WorryItem(StretchWorry, WorryItem.Failed, string.Join("; ", pastTheirMark.Select(rule => FormattableString.Invariant($"the {rule.Family}'s live rule has listed nothing for {rule.Stretch} nights, past its mark of {rule.Mark}"))));

    public const string StretchWorry = "No live rule has gone longer without a pick than its own history says it does";

    // The checklist's two items on the store's copies, read off the copies' rows as the page read them.
    static WorryItem[] CopyItems(NightView night, StoreCopyRead storeCopy)
    {
        const string copied = "The store was copied after the last night";
        const string stillThere = "Every copy the last copy kept is still in its folder";

        if (storeCopy.Newest is not { } copy)
        {
            return
            [
                new WorryItem(copied, WorryItem.NotRead, "no copy of the store is recorded yet"),
                new WorryItem(stillThere, WorryItem.NotRead, "no copy of the store is recorded yet"),
            ];
        }

        static string At(DateTimeOffset instant) => FormattableString.Invariant($"{instant.UtcDateTime:HH:mm} UTC on {instant.UtcDateTime:yyyy-MM-dd}");

        var began = night.Started ?? DateTimeOffset.MinValue;
        var since = copy.MadeAt is { } made && made >= began
            ? new WorryItem(copied, WorryItem.Held, null)
            : copy.StartedAt is { } started && started >= began
                ? copy.Gone
                    ? new WorryItem(copied, WorryItem.Failed, $"the copy started at {At(started)} was ended before it finished, and made none")
                    : new WorryItem(copied, WorryItem.Held, $"a copy started at {At(started)} is waiting or copying")
                : copy.FailedAt is { } tried && tried >= began
                    ? new WorryItem(copied, WorryItem.Failed, $"the copy tried at {At(tried)} was not made: {copy.Failed ?? "no reason was recorded"}")
                    : new WorryItem(copied, WorryItem.Failed, night.Started is { } start ? $"no copy has started since the night began at {At(start)}" : "no copy has started since the night");

        var missing = copy.Missing ?? [];
        var kept = copy.MadeAt is null
            ? new WorryItem(stillThere, WorryItem.NotRead, "no copy of the store has been made yet")
            : missing.Count == 0
                ? new WorryItem(stillThere, WorryItem.Held, null)
                : new WorryItem(stillThere, WorryItem.Failed, FormattableString.Invariant($"{missing.Count} gone from the folder, removed by no copy: {string.Join(", ", missing)}"));

        return [since, kept];
    }

    // The outcome of the row a copy writes as it starts, as the worker writes it.
    public const string CopyStarted = "started";

    // The store's newest copy off the copies' own rows, newest first: the newest ending row that made one, its
    // folder read against this surface's data root or named alone where its row could carry no path to it, the
    // newest attempt after it that made none, with why, the newest copy started after it that has written no end,
    // waiting or copying still or ended before it finished where its start is older than a copy's longest wait
    // and an hour, and the copies the newest copy found gone from the folder that no copy removed.
    // see: The store is copied once the night and every process it started have finished and the newest three copies are kept after each is opened and read, and the copy writes a row as it starts and one as it ends
    public static StoreCopyRead StoreCopy(IReadOnlyList<StoreBackupRow> rows, string dataRoot, DateTimeOffset now)
    {
        var placed = rows.Select((row, place) => (Row: row, Place: place)).ToArray();
        var made = placed.FirstOrDefault(one => one.Row.Outcome == Ok);
        var failed = placed.FirstOrDefault(one => one.Row.Outcome != Ok && one.Row.Outcome != CopyStarted && one.Row.RunId.Length > 0);

        // A start whose run wrote no end: its ending row, written later, would sit before it among rows newest
        // first, so a start with no earlier row of its run is one still open.
        var open = placed.FirstOrDefault(one =>
            one.Row.Outcome == CopyStarted
            && !placed.Take(one.Place).Any(earlier => string.Equals(earlier.Row.RunId, one.Row.RunId, StringComparison.Ordinal)));

        if (made.Row is null && failed.Row is null && open.Row is null)
        {
            return new StoreCopyRead(null);
        }

        static JsonElement Detail(StoreBackupRow row)
        {
            try
            {
                using var document = JsonDocument.Parse(row.Detail);

                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return default;
            }
        }

        static string? Text(JsonElement detail, string field) =>
            detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        static string[] Listed(JsonElement detail, string field) =>
            detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty(field, out var list) && list.ValueKind == JsonValueKind.Array
                ? [.. list.EnumerateArray().Where(one => one.ValueKind == JsonValueKind.String).Select(one => one.GetString()!)]
                : [];

        static DateTimeOffset? Stamp(string at) =>
            DateTimeOffset.TryParseExact(at, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant) ? instant : null;

        DateTimeOffset? madeAt = null;
        string? copy = null;
        string? folder = null;
        var kept = 0;
        string[] missing = [];

        if (made.Row is { } row)
        {
            var detail = Detail(row);

            copy = Text(detail, "copy");
            madeAt = copy is null ? null : EquityBrief.Core.Configuration.StoreCopies.MadeAt(copy);
            folder = Text(detail, "folder") is { } stored
                ? EquityBrief.Core.Configuration.StoreCopies.FromStored(stored, dataRoot)
                : Text(detail, "elsewhere") is { } named ? $"a folder named {named} on a drive the store's rows do not name" : null;
            kept = Listed(detail, "kept").Length;
            missing = Listed(detail, "missing");
        }

        // An attempt that made none is drawn only where it is newer than the copy drawn.
        var notMade = failed.Row is { } attempt && (made.Row is null || failed.Place < made.Place) ? attempt : null;
        var failedAt = notMade is null ? null : Stamp(notMade.EndedAt);

        // A start with no end is drawn only where it is newer than the copy drawn and the attempt drawn.
        var unfinished = open.Row is { } start && (made.Row is null || open.Place < made.Place) && (notMade is null || open.Place < failed.Place) ? start : null;
        var startedAt = unfinished is null ? null : Stamp(unfinished.StartedAt);
        var gone = startedAt is { } since && now - since > EquityBrief.Core.Configuration.StoreCopies.EndsBy;

        return new StoreCopyRead(new StoreCopyView(
            madeAt,
            copy,
            folder,
            kept,
            failedAt,
            notMade is null ? null : Text(Detail(notMade), "reason") ?? "no reason was recorded",
            startedAt,
            gone,
            missing));
    }

    static decimal Spent(string spend) =>
        decimal.TryParse(spend, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount) ? amount : 0m;
}

// One resolved setup, as the record counts it.
//
// The break-even is the bar this setup's own plan set, and it is nullable because
// a setup that entered and stopped on one session has no entry close to measure
// one from. A record's share and its mean break-even are over the setups that
// carry one, which is the population those two figures share.
// see: A condition is judged against the break-even its own plan demands
public sealed record ResolvedSetup(string Ticker, DateOnly SessionDate, string Outcome, double? BreakEven = null);
