using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Shortlist;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from stored rows to what the run page draws.
//
// It sits beside the other three screen projections and in the same seam. What
// it does is count and pair: the reason records are counts over the listings and
// the forward returns, and the base rate is read from the column the filler
// already wrote beside every return.
// see: A screen reads and renders, and computes nothing
public static class RunScreen
{
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
            var (counting, fired) = Counting(listing.Reasons);

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
                // see: A screen reads and renders, and computes nothing
                // see: The record column stays empty until it has earned a number
                // see: A reason's record is displayed, beside the reason and never beside the name
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
    // one's record is withheld until it is promoted. It shows no evaluation of a
    // name, here or anywhere else: seeing a candidate's record before it is
    // promoted is the thing the shadow exists to prevent, and a page that drew
    // one would make the register a formality.
    //
    // The instant is the one the page is read at rather than the night's, because
    // what the region states is how hard the correction is now, which is what a
    // reader comparing it against a verdict needs.
    // see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
    public static ShadowRegion Shadow(IReadOnlyList<CandidateRow> rows, DateTimeOffset at)
    {
        var register = rows
            .Select(row => new RegisterRow(
                row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator,
                "{}", string.Empty, row.Event, row.Retires, row.RegisteredAt, null))
            .ToArray();

        // The count is the set a night evaluates and the divisor the family's own figure, taken apart so a disagreement shows.
        return new ShadowRegion(
            ShadowColumn.StandingAt(register, at).Count,
            CandidateFamily.Divisor(register, at),
            CandidateFamily.Maximum);
    }

    // What each registered candidate's setups have come to, read at the looks it was registered
    // with and at the level the graph gives it now.
    //
    // A candidate's window opens on the first night that evaluated it, and the candidates that
    // night evaluated are the family its level is divided by: a candidate registered after a
    // window opened was not among the things being tried over the evidence that window holds.
    // Nothing here names a ticker, and nothing it hands the page could: a record is a count of
    // setups and the sessions they were listed on.
    // see: A candidate is judged by a sign-flip test over blocks of 63 sessions, with at least eight blocks
    // see: Holm's level passes between the candidates by a graph fixed when they are registered, and every verdict shows the lifetime count
    public static CandidateRegion Candidates(
        IReadOnlyList<CandidateRow> register,
        IReadOnlyList<CandidateNightRow> nights,
        IReadOnlyList<CandidateSetupRow> setups,
        DateOnly night,
        DateTimeOffset at)
    {
        var rows = register
            .Select(row => new RegisterRow(
                row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator,
                row.Parameters, string.Empty, row.Event, row.Retires, row.RegisteredAt, row.Evidence))
            .ToArray();

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

        var fired = setups
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

        // A retired candidate's record stops at the night it was retired on. Its blocks would
        // otherwise go on completing over setups it stopped producing, and a look read after a
        // candidate left the family would be a look nobody registered.
        DateOnly Until(string candidate) =>
            standing.ContainsKey(candidate)
                ? night
                : rows.Where(row => row.Event == CandidateFamily.Retired && row.Retires == candidate)
                    .Select(row => DateOnly.FromDateTime(row.RegisteredAt.UtcDateTime))
                    .DefaultIfEmpty(night)
                    .Min();

        Measured Read(string candidate, double level) =>
            CandidateRecord.For(
                fired.GetValueOrDefault(candidate, []),
                opened.GetValueOrDefault(candidate, night),
                Until(candidate),
                level);

        // A retirement the operator wrote after a promotion says so in its evidence, which is what
        // tells a candidate that left the family having been shown from one that left having not.
        bool Promoted(string candidate) =>
            rows.Any(row => row.Event == CandidateFamily.Retired
                && row.Retires == candidate
                && row.Evidence is { } evidence
                && evidence.StartsWith(CandidateFamily.PromotedBy, StringComparison.Ordinal));

        var levels = new Dictionary<string, GraphLevel>(StringComparer.Ordinal);

        foreach (var family in registered.GroupBy(candidate => opened.GetValueOrDefault(candidate, night)))
        {
            var members = (evaluatedOn.GetValueOrDefault(family.Key, [.. family])
                .Where(candidate => registered.Contains(candidate, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(candidate => candidate, StringComparer.Ordinal)
                .Select(candidate => new GraphMember(
                    candidate,
                    Promoted(candidate),
                    !standing.ContainsKey(candidate),
                    level => Read(candidate, level).Verdict == CandidateRecord.Crossed)))
                .ToArray();

            foreach (var level in HolmGraph.Levels(members, ReasonVerdict.Significance))
            {
                levels[level.Candidate] = level;
            }
        }

        return new CandidateRegion(
            [
                .. registered
                    .OrderBy(candidate => candidate, StringComparer.Ordinal)
                    .Select(candidate =>
                    {
                        var level = levels.GetValueOrDefault(
                            candidate,
                            new GraphLevel(candidate, ReasonVerdict.Significance, 1, false));

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
            CandidateFamily.Maximum,
            ReasonVerdict.Significance,
            Blocks.Sessions,
            Blocks.Floor,
            Looks.At,
            NullWin.CostBasisPoints,
            NullWin.SensitivityBasisPoints);
    }

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
    public static IReadOnlyList<string> RunsByHand { get; } = ["version-", "register-"];

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

    public static IReadOnlyList<StageRow> Failed(IReadOnlyList<StageRow> stages) =>
    [
        .. stages.Where(stage => !stage.ByHand
            && !string.Equals(stage.Outcome, Ok, StringComparison.Ordinal)
            && !string.Equals(stage.Outcome, "started", StringComparison.Ordinal)
            && !string.Equals(stage.Outcome, NoSession, StringComparison.Ordinal)
            && !(string.Equals(stage.Stage, QueueStage, StringComparison.Ordinal) && string.Equals(stage.Outcome, QueueAtItsLimit, StringComparison.Ordinal))),
    ];

    // The overnight queue's own stage, and the outcome it writes where it stopped at its
    // limit with names left. Stated here for the reason the no-session word is, the read
    // surface holding no reference to the worker, and `nightly-run` asserts both agree. A
    // queue at its limit did what its limit is for, so it is not a stage that failed; a
    // queue the local model stopped is, and stays on the list.
    public const string QueueStage = "overnight queue";

    public const string QueueAtItsLimit = "limit";

    // The night a queue row's detail names, or none where it names none or is not JSON.
    public static DateOnly? QueueNightOf(string detail)
    {
        try
        {
            using var document = JsonDocument.Parse(detail);

            return document.RootElement.TryGetProperty("night", out var night) && night.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(night.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var on)
                    ? on
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The overnight queue for one night, read off the queue's rows and the exchange's
    // calendar: what the newest row for the night came to, with its counts, and every
    // traded session with no row from the one after the newest earlier night the queue ran
    // on, up to and including this night. A store with no row on or before the night says
    // the queue never ran rather than naming every night it holds from before the queue
    // existed.
    // see: A night the overnight queue did not run is a traded session with no queue row, read on the run page against the exchange calendar
    public static QueueNight Queue(IReadOnlyList<QueueRow> rows, DateOnly night, Func<DateOnly, bool> traded)
    {
        // The night's row written last, which is what the night came to: the rows arrive in the
        // order they were written, and a night run again for its session writes a later row with an
        // earlier instant than the one it replaces.
        var tonight = rows
            .Where(row => row.Night == night)
            .LastOrDefault();

        var earlier = rows.Where(row => row.Night < night).Select(row => (DateOnly?)row.Night).Max();

        if (tonight is null && earlier is null)
        {
            return new QueueNight(night, null, 0, 0, 0, 0, null, null, [], NeverRan: true);
        }

        var notRun = new List<DateOnly>();

        if (earlier is { } since)
        {
            for (var day = since.AddDays(1); day < night; day = day.AddDays(1))
            {
                if (traded(day))
                {
                    notRun.Add(day);
                }
            }
        }

        if (tonight is null && traded(night))
        {
            notRun.Add(night);
        }

        if (tonight is null)
        {
            return new QueueNight(night, null, 0, 0, 0, 0, null, null, notRun, NeverRan: false);
        }

        // A step that failed writes its message rather than the queue's record, and the page says the
        // queue failed with that message rather than failing to draw.
        if (!IsRecord(tonight.Detail))
        {
            return new QueueNight(night, tonight.Outcome, 0, 0, 0, 0, tonight.Detail, null, notRun, NeverRan: false);
        }

        using var detail = JsonDocument.Parse(tonight.Detail);
        var root = detail.RootElement;

        int Count(string name) =>
            root.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : 0;

        string? Text(string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        return new QueueNight(
            night,
            tonight.Outcome,
            Count("queued"),
            Count("completed"),
            Count("left"),
            root.TryGetProperty("limitHours", out var hours) && hours.ValueKind == JsonValueKind.Number ? hours.GetDouble() : 0,
            Text("reason"),
            Text("awake"),
            notRun,
            NeverRan: false);
    }

    // Whether a queue row's detail is the queue's own record rather than a message.
    static bool IsRecord(string detail)
    {
        try
        {
            using var document = JsonDocument.Parse(detail);

            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
    static (IReadOnlyList<string> Counted, IReadOnlyList<string> Fired) Counting(string reasons)
    {
        using var document = JsonDocument.Parse(reasons);

        var counted = document.RootElement.EnumerateArray()
            .Where(reason => !ShortlistSeries.WrittenBeforeTheCorrection(
                reason.GetProperty("name").GetString()!,
                value => reason.GetProperty("values").TryGetProperty(value, out _)))
            .Where(reason => !ShortlistSeries.MeasuredUnderAnotherThreshold(
                reason.GetProperty("name").GetString()!,
                value => reason.GetProperty("values").TryGetProperty(value, out var stored) ? stored.GetString() : null))
            .ToArray();

        return (
            [.. counted.Select(reason => reason.GetProperty("name").GetString()!)],
            [.. counted.Where(reason => reason.GetProperty("fired").GetBoolean()).Select(reason => reason.GetProperty("name").GetString()!)]);
    }

    // The paid calls the log carries a recorded cost for, counted, their passes counted
    // by the run each was made under, and summed, off the rows the read surface handed back.
    public static PricedCalls Priced(IReadOnlyList<(string RunId, decimal Spend)> spends) =>
        new(spends.Count, spends.Select(call => call.RunId).Distinct(StringComparer.Ordinal).Count(), spends.Sum(call => call.Spend));
}

// One resolved setup, as the record counts it.
//
// The break-even is the bar this setup's own plan set, and it is nullable because
// a setup that entered and stopped on one session has no entry close to measure
// one from. A record's share and its mean break-even are over the setups that
// carry one, which is the population those two figures share.
// see: A condition is judged against the break-even its own plan demands
public sealed record ResolvedSetup(string Ticker, DateOnly SessionDate, string Outcome, double? BreakEven = null);
