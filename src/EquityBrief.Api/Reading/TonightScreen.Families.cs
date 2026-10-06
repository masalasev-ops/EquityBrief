using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Sweep;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from the page's stored list to the cards a night the families drew is read as. It
// evaluates no gate and draws no list: the lister drew it and stored it, and this reads it back, family by
// family in the page's order, each pick with the trade and the words its own family's stored answer holds.
// see: A screen reads and renders, and computes only the plan in the operator's money and a pick's open trades in its sector
// see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
public static partial class TonightScreen
{
    // The stocks the page lists on the night, in the page's order, each drawn as a row of the list draws a
    // name, which is what a selected row and the walk from name to name are read from.
    public static IReadOnlyList<ListingCell> ListedByFamilies(
        DateOnly night,
        IReadOnlyList<FamilyPickRow> picks,
        IReadOnlyList<ListingRow> listings,
        IReadOnlyDictionary<string, UniverseCell> cellByTicker,
        IReadOnlyList<CloseRow> closesToTheNight,
        IReadOnlyList<SuspectSeriesRow>? suspects = null,
        IReadOnlyList<ResearchedRow>? researched = null,
        IReadOnlyList<GateResultRow>? gates = null,
        IReadOnlyList<FundamentalReadingRow>? readings = null,
        IReadOnlyDictionary<string, (int Positive, int Negative)>? news = null)
    {
        var (drawn, _) = Drawer(night, cellByTicker, closesToTheNight, suspects, researched, readings);
        var byTicker = listings.ToDictionary(listing => listing.Ticker, StringComparer.Ordinal);
        var gateByTicker = (gates ?? []).ToDictionary(gate => gate.Ticker, StringComparer.Ordinal);

        return
        [
            .. picks
                .Where(pick => pick.State == FamilyList.Listed && byTicker.ContainsKey(pick.Ticker))
                .OrderBy(pick => pick.Place ?? int.MaxValue)
                .ThenBy(pick => pick.Ticker, StringComparer.Ordinal)
                .Select(pick =>
                {
                    var cell = drawn(byTicker[pick.Ticker]);

                    // The swing filter's answer is the trade of a stock the pullback lists, and of no other
                    // family's pick, whose trade its own card states.
                    if (pick.Family == SetupFamilies.Pullback && gateByTicker.TryGetValue(pick.Ticker, out var gate))
                    {
                        var filter = FilterRowOf(gate);

                        cell = cell with
                        {
                            Filter = filter,
                            RewardToRisk = decimal.TryParse(filter.RewardToRisk, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) ? ratio : null,
                            NoRewardToRisk = null,
                        };
                    }

                    return news is not null && news.TryGetValue(pick.Ticker, out var counts)
                        ? cell with { NewsPositive = counts.Positive, NewsNegative = counts.Negative }
                        : cell;
                }),
        ];
    }

    // One card a family, in the page's order: its picks as the lister placed them, each with its trade and
    // why its family lists it, the notes on the stocks it passed and the page holds back, and why it lists
    // none on a night it lists none. The rows are the ones `ListedByFamilies` drew, carrying what the queue
    // holds for each.
    public static IReadOnlyList<FamilyCardView> Families(
        DateOnly night,
        IReadOnlyList<string> onThePage,
        IReadOnlyList<FamilyPickRow> picks,
        IReadOnlyList<ListingCell> rows,
        IReadOnlyList<GateResultRow> gates,
        IReadOnlyList<CandidateRow> register,
        ListRuleView rule,
        IReadOnlyList<FamilyResultRow>? results = null,
        FilterSettings? pullback = null)
    {
        var rowByTicker = rows.ToDictionary(row => row.Ticker, StringComparer.Ordinal);
        var gateByTicker = gates.ToDictionary(gate => gate.Ticker, StringComparer.Ordinal);
        var resultBy = (results ?? []).ToDictionary(result => (result.Family, result.Ticker));
        var listedUnder = picks
            .Where(pick => pick.State == FamilyList.Listed)
            .ToDictionary(pick => pick.Ticker, pick => pick.Family, StringComparer.Ordinal);

        // The families the night's page held, in the order it held them, which an earlier night's page is
        // drawn with whatever families have joined the page since.
        var families = onThePage.Select(SetupFamilies.Named).OfType<SetupFamily>().ToArray();
        var cards = new List<FamilyCardView>();

        foreach (var (family, at) in families.Select((family, at) => (family, at)))
        {
            var own = picks.Where(pick => pick.Family == family.Name).ToArray();

            var listed = own
                .Where(pick => pick.State == FamilyList.Listed && rowByTicker.ContainsKey(pick.Ticker))
                .OrderBy(pick => pick.Place ?? int.MaxValue)
                .Select(pick => PickOf(family, pick, rowByTicker[pick.Ticker], gateByTicker.GetValueOrDefault(pick.Ticker), resultBy.GetValueOrDefault((family.Name, pick.Ticker))))
                .ToArray();

            var (liveSince, variants) = Standing(family, register, night);

            cards.Add(new FamilyCardView(
                family.Name,
                family.Label,
                family.Heading,
                family.Eyebrow,
                RuleOf(family, pullback),
                at + 1,
                families.Length,
                liveSince,
                variants,
                listed,
                Notes(family, own, listedUnder),
                listed.Length > 0 ? null : Empty(family, own, rule, [.. (results ?? []).Where(result => result.Family == family.Name)])));
        }

        return cards;
    }

    // An S&P 500 card's rule in words, written from the settings its live rule runs at: the pullback's from the swing
    // filter version its night ran under, and the proposed settings where none is named.
    // see: Every page reads one index at a time chosen under Universe, and every figure names its index
    static string RuleOf(SetupFamily family, FilterSettings? pullback) => family.Name switch
    {
        SetupFamilies.Pullback => RuleWords.Pullback(pullback ?? FilterSettings.Proposed),
        BreakoutRule.Name => RuleWords.Breakout(BreakoutRule.Live),
        DriftRule.Name => RuleWords.Drift(DriftRule.Live),
        _ => family.Rule,
    };

    // A pick as its family's card draws it. A pullback's trade is the plan its night's trade gate read, off
    // its stored gate result, and its words are the figures its setup and trigger gates stored.
    // Another family's is the trade its evaluator stored that night, a trailing family's with no target, and
    // its words are the figures its own gates stored.
    static FamilyPickCell PickOf(SetupFamily family, FamilyPickRow pick, ListingCell row, GateResultRow? gate, FamilyResultRow? result)
    {
        var also = pick.Also.Select(name => SetupFamilies.Named(name)?.Label ?? name).ToArray();

        if (family.Name != SetupFamilies.Pullback && result is not null)
        {
            return new FamilyPickCell(
                row,
                pick.Place ?? 0,
                result.Entry,
                result.Stop,
                result.Target,
                family.Trails,
                Stored(result.Gates, FamilyRule.Trade, FamilyRule.RewardToRiskValue) is { } stated && decimal.TryParse(stated, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) ? ratio : null,
                family.Name switch
                {
                    BreakoutRule.Name => BreakoutWhy(result),
                    DriftRule.Name => DriftWhy(result),
                    LeaderRule.Name => LeaderWhy(result),
                    _ => "its setup's gates all passed tonight",
                },
                also);
        }

        if (family.Name != SetupFamilies.Pullback || gate is null || row.Filter is not { } filter)
        {
            return new FamilyPickCell(row, pick.Place ?? 0, null, null, null, false, null, "no stored answer of this setup is held for the night", also);
        }

        var (stop, target) = filter.Input == FilterSettings.ClearWord
            ? (gate.ClearStop, gate.ClearTarget)
            : filter.Input == FilterSettings.SwingWord
                ? (gate.SwingStop, gate.SwingTarget)
                : (null, null);

        return new FamilyPickCell(row, pick.Place ?? 0, gate.SwingEntry, stop, target, false, row.RewardToRisk, PullbackWhy(gate), also);
    }

    // Why the pullback lists a stock tonight, in the figures its setup and trigger gates stored: how far it
    // came down, the band it came down into, how its volume ran on the way, and the session it turned up on.
    public static string PullbackWhy(GateResultRow gate)
    {
        using var document = JsonDocument.Parse(gate.Gates);

        string? Value(string name, string key) =>
            document.RootElement.GetProperty("gates").EnumerateArray()
                .Where(one => one.GetProperty("gate").GetString() == name)
                .Select(one => one.GetProperty("values").TryGetProperty(key, out var value) ? value.GetString() : null)
                .FirstOrDefault();

        double? Figure(string name, string key) =>
            double.TryParse(Value(name, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var figure) ? figure : null;

        var depth = Figure(SwingGates.Setup, "depth");
        var dryUp = Figure(SwingGates.Setup, "dry-up");
        var low = Value(SwingGates.Setup, "band low");
        var high = Value(SwingGates.Setup, "band high");
        var arrived = Value(SwingGates.Trigger, SwingGates.ArrivedValue);

        var fell = depth is { } moves
            ? FormattableString.Invariant($"Fell {moves:0.0} typical moves")
            : "Fell";
        var band = low is { Length: > 0 } and not "none" && high is { Length: > 0 } and not "none"
            ? FormattableString.Invariant($" into its support band at {Price(low)} to {Price(high)}")
            : " into a support band";
        var volume = dryUp is { } ratio
            ? FormattableString.Invariant($" on volume {ratio:0.00} times its average")
            : string.Empty;
        var turned = arrived switch
        {
            null or "none" => string.Empty,
            "tonight" => ", and turned up tonight",
            var on when DateOnly.TryParseExact(on, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) => $", and turned up on {on}",
            var worded => $", and turned up on {worded}",
        };

        return fell + band + volume + turned + ".";
    }

    static string Price(string stored) =>
        decimal.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var price) ? Figures.Price(price) : stored;

    // One value a stored gate holds, and none where the gate or the value is not there.
    static string? Stored(string gates, string gate, string key) =>
        FamilyRule.GatesOf(gates).FirstOrDefault(one => one.Name == gate)?.Values.GetValueOrDefault(key);

    static double? StoredFigure(string gates, string gate, string key) =>
        double.TryParse(Stored(gates, gate, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var figure) ? figure : null;

    // Why the breakout lists a stock tonight, in the figures its gates stored: the high it closed above,
    // its volume against its average, and how its daily ranges ran before.
    public static string BreakoutWhy(FamilyResultRow result)
    {
        var close = Stored(result.Gates, BreakoutRule.NewHigh, "close");
        var high = Stored(result.Gates, BreakoutRule.NewHigh, "high");
        var multiple = StoredFigure(result.Gates, BreakoutRule.Volume, "multiple");
        var ratio = StoredFigure(result.Gates, BreakoutRule.Tightened, "ratio");

        // The sessions its night read the close against, and the provisional setting's for a row stored
        // before the freeze, which names none.
        var window = Stored(result.Gates, BreakoutRule.NewHigh, BreakoutRule.WindowValue)
            ?? BreakoutRule.ProvisionalHighSessions.ToString(CultureInfo.InvariantCulture);

        var closed = close is { Length: > 0 } and not "none" && high is { Length: > 0 } and not "none"
            ? FormattableString.Invariant($"Closed at {Price(close)}, above its high of {Price(high)} over the {window} sessions before")
            : "Closed above its high of the year before";
        var volume = multiple is { } times
            ? FormattableString.Invariant($", on volume {times:0.00} times its average")
            : string.Empty;
        var ranges = ratio is { } narrowed
            ? FormattableString.Invariant($", after its daily ranges ran at {narrowed:0.00} of the {BreakoutRule.RangeSessions} sessions before them")
            : string.Empty;

        return closed + volume + ranges + ".";
    }

    // Why the sector leaders list a stock tonight, in the figures its gates stored: where its sector ranks,
    // where its own return stands inside it, and that it is at the pullback's buy point.
    public static string LeaderWhy(FamilyResultRow result)
    {
        var sector = Stored(result.Gates, LeaderRule.Sector, "sector");
        var rank = Stored(result.Gates, LeaderRule.Sector, "rank");
        var ranked = Stored(result.Gates, LeaderRule.Sector, "ranked");
        var own = StoredFigure(result.Gates, LeaderRule.Leader, "return");
        var place = Stored(result.Gates, LeaderRule.Leader, "place");
        var of = Stored(result.Gates, LeaderRule.Leader, "of");

        var standing = sector is { Length: > 0 } && rank is { Length: > 0 } && ranked is { Length: > 0 }
            ? $"Its sector, {sector}, ranks {rank} of {ranked} by its members' return"
            : "Its sector is among the strongest";
        var leading = own is { } over && place is { Length: > 0 } && of is { Length: > 0 }
            ? FormattableString.Invariant($", and its own return of {over * 100:0.0}% is {place} of the {of} in it")
            : string.Empty;

        return standing + leading + ". It is at a pullback's buy point tonight.";
    }

    // Why the earnings drift lists a stock tonight, in the figures its gates stored: the print and how far
    // it beat its estimate, how the reaction session closed and on what volume, and the low the close holds
    // above.
    public static string DriftWhy(FamilyResultRow result)
    {
        var report = Stored(result.Gates, DriftRule.Print, "report");
        var back = StoredFigure(result.Gates, DriftRule.Print, "back");
        var surprise = StoredFigure(result.Gates, DriftRule.Beat, "surprise");
        var moves = StoredFigure(result.Gates, DriftRule.Reaction, "moves");
        var multiple = StoredFigure(result.Gates, DriftRule.Volume, "multiple");
        var low = Stored(result.Gates, DriftRule.Held, "low");

        var beat = surprise is { } by
            ? FormattableString.Invariant($"Beat its estimate by {by:0.0}%")
            : "Beat its estimate";
        var reported = report is { Length: > 0 } ? $" in its report of {report}" : string.Empty;
        var reacted = moves is { } up
            ? FormattableString.Invariant($", and closed up {up:0.0} typical moves on the reaction")
            : string.Empty;
        var volume = multiple is { } times
            ? FormattableString.Invariant($" on volume {times:0.00} times its average")
            : string.Empty;
        var since = back switch
        {
            null => string.Empty,
            0 => ", which was tonight",
            1 => ", one session ago",
            var sessions => FormattableString.Invariant($", {sessions:0} sessions ago"),
        };
        var held = low is { Length: > 0 } and not "none"
            ? FormattableString.Invariant($". It holds above that session's low of {Price(low)}")
            : string.Empty;

        return beat + reported + reacted + volume + since + held + ".";
    }

    // The day a family's rule went live and the variants scored beside it, read off the register as it
    // stood at the night's end: the family's live candidate and the other candidates registered under its
    // evaluator, the pullback's the swing filter's. A family no registration stands for runs on provisional
    // settings and reads no day.
    // see: A family lists on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
    // see: The new families freeze at their sweeps' proposals, the breakout's provisional setting and the drift's wider stop registered beside them as variants
    static (DateOnly? LiveSince, int Variants) Standing(SetupFamily family, IReadOnlyList<CandidateRow> register, DateOnly night) =>
        Standing(family.Name, register, night);

    // A family's standing by its name, the sector heavyweights' among them: the day its live rule registered, and
    // the variants standing beside it.
    static (DateOnly? LiveSince, int Variants) Standing(string name, IReadOnlyList<CandidateRow> register, DateOnly night)
    {
        var (live, variants) = StandingRows(name, register, night);

        return (live is null ? null : DateOnly.FromDateTime(live.RegisteredAt.UtcDateTime), variants);
    }

    // The instant a family's live rule standing on the night was registered, read as its standing is, and none where no
    // freeze has registered one; what a recorded sweep answer is read against.
    public static DateTimeOffset? FrozenAt(string name, IReadOnlyList<CandidateRow> register, DateOnly night) =>
        StandingRows(name, register, night).Live?.RegisteredAt;

    // The instant past a night from which nothing recorded counts on its page: a freeze or an answer written after the
    // night is read on no night before it.
    public static DateTimeOffset PastTheNight(DateOnly night) => new(night.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    // Each card with the line its family's recorded sweep answers draw on the night, the answers recorded before the
    // night was past and its freeze read off the register as of the night.
    // see: No family on any index is set aside or hidden by a test result without the operator's word
    public static IReadOnlyList<FamilyCardView> WithSweepLines(IReadOnlyList<FamilyCardView> cards, IReadOnlyList<RecordedAnswer> answers, Func<string, DateTimeOffset?> frozenAt) =>
    [
        .. cards.Select(card => card with
        {
            SweepFoundNone = SweepLine.Drawn([.. answers.Where(answer => answer.Family == card.Family)], frozenAt(card.Family)),
        }),
    ];

    static (RegisterRow? Live, int Variants) StandingRows(string name, IReadOnlyList<CandidateRow> register, DateOnly night)
    {
        var at = PastTheNight(night);
        var standing = CandidateFamily.In(
                CandidateFamily.Standing(
                    [
                        .. register.Select(row => new RegisterRow(
                            row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator,
                            row.Parameters, string.Empty, row.Event, row.Retires, row.RegisteredAt, row.Evidence)),
                    ],
                    at),
                name)
            .Where(row => name != SetupFamilies.Pullback || row.Evaluator == SwingFilterRule.EvaluatorName)
            .ToArray();

        bool IsLive(string candidate) => name == SetupFamilies.Pullback ? SwingFamily.IsLive(candidate) : FamilyRecords.IsLive(candidate);

        return (standing.LastOrDefault(row => IsLive(row.Candidate)), standing.Count(row => !IsLive(row.Candidate)));
    }

    // The notes beneath a family's picks: each stock it passed that a trade still open holds back, each one
    // the page lists under an earlier family, and how many it passed beyond the five it lists.
    // see: A stock holds one trade across every swing family, and one qualifying under two is listed once under the first in the page's order
    static IReadOnlyList<string> Notes(SetupFamily family, IReadOnlyList<FamilyPickRow> own, IReadOnlyDictionary<string, string> listedUnder)
    {
        var notes = new List<string>();

        foreach (var held in own.Where(pick => pick.State == FamilyList.OpenTrade).OrderBy(pick => pick.Ticker, StringComparer.Ordinal))
        {
            var from = held.HeldNight is { } listed ? listed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "an earlier night";
            var where = held.HeldIndex is { } code ? $" on the {Universes.ByCode(code)?.Possessive ?? code} list" : string.Empty;
            var whose = held.HeldFamily is { } holder && holder != family.Name
                ? $"its {(SetupFamilies.Named(holder)?.Label ?? holder).ToLowerInvariant()} trade from {from}{where}"
                : $"its trade from {from}{where}";

            notes.Add($"{held.Ticker} qualified again tonight but {whose} is still open, so it is not listed. One stock, one trade.");
        }

        foreach (var shared in own.Where(pick => pick.State == FamilyList.UnderAnother).OrderBy(pick => pick.Ticker, StringComparer.Ordinal))
        {
            var under = listedUnder.TryGetValue(shared.Ticker, out var first) ? SetupFamilies.Named(first)?.Heading ?? first : "another setup";

            notes.Add($"{shared.Ticker} also qualified here tonight and is shown once, under {under}, with both labels. One stock is one trade.");
        }

        var past = own.Where(pick => pick.State == FamilyList.PastFive).Select(pick => pick.Ticker).Order(StringComparer.Ordinal).ToArray();

        if (past.Length > 0)
        {
            notes.Add(FormattableString.Invariant($"{past.Length} more qualified tonight and {(past.Length == 1 ? "is" : "are")} not listed here, a setup listing at most {SetupFamilies.ListedANight} a night: {string.Join(", ", past)}."));
        }

        return notes;
    }

    // Why a family lists nothing on a night it lists none: the market check closed every list, no stock
    // passed its gates, or every stock it passed is held back, which its notes say.
    // see: The market check closes every swing family's list together, and the sector heavyweights read none
    static string Empty(SetupFamily family, IReadOnlyList<FamilyPickRow> own, ListRuleView rule, IReadOnlyList<FamilyResultRow> results)
    {
        if (own.Count > 0)
        {
            return "Every stock this setup passed tonight is held back, as the notes beneath say.";
        }

        if (!rule.MarketOpen)
        {
            return rule.Breadth is { } breadth && rule.Floor is { } floor
                ? FormattableString.Invariant($"The market check closed every list tonight: {breadth * 100:0.0}% of the members closed above their 200-day average, below its floor of {floor * 100:0.#}%.")
                : "The market check closed every list tonight: the night's breadth is not available.";
        }

        if (family.Name == SetupFamilies.Pullback)
        {
            return rule.Reached.Count == 4
                ? FormattableString.Invariant($"No stock passed this setup tonight: {rule.Reached[0]} passed the trend and strength gate, {rule.Reached[1]} the setup, {rule.Reached[2]} the trigger and {rule.Reached[3]} the trade, and none of those past the exclusions.")
                : "No stock passed this setup tonight.";
        }

        return results.Count == 0
            ? "No answer of this setup is stored for the night."
            : "No stock passed this setup tonight: " + Funnel(results) + ".";
    }

    // The shared list of stocks close to a buy point on a night the families drew: the pullback's, the rows
    // the swing filter's results give in their own order, each with the gate it missed in that gate's words,
    // then every other family's in the page's order, each member its family stored as missing exactly one
    // gate with nothing excluding it, by ticker, with that gate's stored reason.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    public static IReadOnlyList<CloseToBuyCell> CloseAcross(
        IReadOnlyList<string> onThePage,
        IReadOnlyList<ListingCell>? near,
        IReadOnlyList<FamilyResultRow> results,
        IReadOnlyDictionary<string, UniverseCell> cellByTicker)
    {
        var rows = new List<CloseToBuyCell>();

        foreach (var family in onThePage.Select(SetupFamilies.Named).OfType<SetupFamily>())
        {
            if (family.Name == SetupFamilies.Pullback)
            {
                rows.AddRange((near ?? [])
                    .Where(row => row.Missed is not null)
                    .Select(row => new CloseToBuyCell(row.Ticker, row.Distance?.Name, family.Name, family.Label, row.Missed!.Gate, row.Missed.Words)));

                continue;
            }

            rows.AddRange(results
                .Where(result => result.Family == family.Name && !result.Passed && result.Missed == 1 && result.Exclusions.Count == 0)
                .OrderBy(result => result.Ticker, StringComparer.Ordinal)
                .Select(result =>
                {
                    var missed = FamilyRule.GatesOf(result.Gates).First(gate => !gate.Passed);

                    return new CloseToBuyCell(
                        result.Ticker,
                        cellByTicker.TryGetValue(result.Ticker, out var cell) ? cell.Name : null,
                        family.Name,
                        family.Label,
                        missed.Name,
                        missed.Reason);
                }));
        }

        return rows;
    }

    // The setups whose trades are in no share as of a night: every one but the pullback that no standing
    // registration makes live. The pullback's trades are the live list's, counted from the swing filter's
    // first night whatever the register holds, as Past picks has always counted them.
    // see: A family lists on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
    public static IReadOnlySet<string> ProvisionalSetups(IReadOnlyList<CandidateRow> register, DateOnly night) =>
        SetupFamilies.InPageOrder
            .Where(family => family.Name != SetupFamilies.Pullback && Standing(family, register, night).LiveSince is null)
            .Select(family => family.Name)
            .ToHashSet(StringComparer.Ordinal);

    // The run page's setups: each family the page draws with its rule's standing, the variants scored
    // beside it, what it lists on the night, the trades the page has listed under it and how they stand,
    // and its record, which for a family no freeze has registered is the words saying it starts at the freeze,
    // for the pullback the live list's, and for a registered new family its live rule's own.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    public static IReadOnlyList<FamilyRunRow> FamilyRun(
        DateOnly night,
        IReadOnlyList<FamilyPickRow> picksTonight,
        IReadOnlyList<PickCell> trades,
        IReadOnlyList<CandidateRow> register,
        IReadOnlyList<FamilyTradeRow>? familyTrades = null,
        IReadOnlyList<FamilyReplayRow>? replays = null)
    {
        var records = FamilyRecordViews(register, familyTrades ?? [], night, replays);

        return
        [
            .. SetupFamilies.InPageOrder.Select(family =>
            {
                var (liveSince, variants) = Standing(family, register, night);
                var own = trades.Where(trade => trade.Family == family.Name && trade.RepeatOf is null).ToArray();
                var summary = PicksScreen.Summary(own);
                var live = records.FirstOrDefault(record => record.Family == family.Name && record.View.Live).View;

                return new FamilyRunRow(
                    family.Name,
                    family.Heading,
                    liveSince,
                    variants,
                    picksTonight.Count(pick => pick.Family == family.Name && pick.State == FamilyList.Listed),
                    own.Length,
                    summary.Open,
                    summary.Finished,
                    liveSince is null
                        ? SetupFamilies.Provisional
                        : family.Name != SetupFamilies.Pullback && live is not null
                            ? RecordWords(live)
                            : FormattableString.Invariant($"{summary.Decided} decided of the {summary.MinimumDecided} its record waits for, on {summary.DecidedNights} of {summary.MinimumNights} nights"));
            }),
        ];
    }

    // Each registered rule of each new family standing at the night's end, read over its own trades from where its
    // record counts, its correction the family's own.
    // see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
    // see: Each setup family's correction for luck counts its own rules alone, at most nine a family
    // see: A family rule registered again keeps its record from its first registration where a replay of its stored nights reproduces every trade, and restarts at the change otherwise
    public static IReadOnlyList<(string Family, FamilyRecordView View)> FamilyRecordViews(
        IReadOnlyList<CandidateRow> register,
        IReadOnlyList<FamilyTradeRow> trades,
        DateOnly night,
        IReadOnlyList<FamilyReplayRow>? replays = null)
    {
        var at = new DateTimeOffset(night.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        RegisterRow[] rows =
        [
            .. register.Select(row => new RegisterRow(
                row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator,
                row.Parameters, row.EvaluatorVersion, row.Event, row.Retires, row.RegisteredAt, row.Evidence)),
        ];
        var standing = CandidateFamily.Standing(rows, at);

        // Each new swing family under its own cap, and the sector heavyweights under none, a holding counting in the
        // block it ends in.
        // see: Each registered sector heavyweights rule keeps a book of its own beside the page's, its holdings scored in percent against their size cut
        return
        [
            .. SetupFamilies.Evaluated.Select(family => (family.Name, Cap: family.CapSessions))
                .Append((HeavyweightRule.Name, Cap: 0))
                .SelectMany(family =>
                {
                    var rules = CandidateFamily.In(standing, family.Name);
                    var trials = CandidateFamily.Trials(CandidateFamily.In(rows, family.Name), rules.Select(rule => rule.Candidate));

                    return FamilyRecords.Family(rules, trades, night, family.Cap, trials, replays).Select(view => (family.Name, view));
                }),
        ];
    }

    // The run page's rows of the new setups' registered rules, each family's live rule first.
    public static IReadOnlyList<FamilyRecordRow> FamilyRecordRows(
        IReadOnlyList<CandidateRow> register,
        IReadOnlyList<FamilyTradeRow> trades,
        DateOnly night,
        IReadOnlyList<FamilyReplayRow>? replays = null) =>
    [
        .. FamilyRecordViews(register, trades, night, replays)
            .OrderBy(record => SetupFamilies.PlaceOf(record.Family))
            .ThenBy(record => record.View.Live ? 0 : 1)
            .ThenBy(record => record.View.Candidate, StringComparer.Ordinal)
            .Select(record => new FamilyRecordRow(
                record.Family,
                SetupFamilies.Named(record.Family)?.Heading ?? (record.Family == HeavyweightRule.Name ? SetupFamilies.SectorHeavyweights.Heading : record.Family),
                record.View.Candidate,
                record.View.Live,
                record.View.Trades,
                record.View.Decided,
                record.View.Edge,
                record.View.Blocks,
                record.View.NextLook,
                record.View.Level,
                RecordWords(record.View),
                record.View.First,
                record.View.Restarted)),
    ];

    // A registered rule's record in words: its trades, the decided ones and their edge, and the whole blocks
    // against the look they wait for, or what its last look read.
    public static string RecordWords(FamilyRecordView view) =>
        view.Trades == 0
            ? FormattableString.Invariant($"no trade kept yet; its first look reads {view.NextLook} whole blocks of {EquityBrief.Core.Returns.Blocks.Sessions} sessions")
            : FormattableString.Invariant($"{view.Decided} of {view.Trades} trades decided")
                + (view.Edge is { } edge ? FormattableString.Invariant($", an edge of {edge:0.000}") : string.Empty)
                + (view.EdgeAfterCosts is { } after ? FormattableString.Invariant($" and {after:0.000} after each trade's own round trip over the {view.Priced} priced") : string.Empty)
                + (view.LooksTaken == 0
                    ? FormattableString.Invariant($"; {view.Blocks} whole blocks of the {view.NextLook} its first look reads")
                    : FormattableString.Invariant($"; its last look read {view.PValue:0.0000} against {view.Level:0.0000}{(view.Crossed ? ", crossed" : ", not crossed")}"));

    // What a name's page says of the page's list on its night, from the name's own rows on it: the setup that
    // lists it, its place and the labels of the other setups it qualified under, and a sentence for a setup
    // that passed it while a trade for it is still open or past that setup's five. Nothing where the
    // families drew no row for the name.
    // see: A stock holds one trade across every swing family, and one qualifying under two is listed once under the first in the page's order
    public static ListedUnderView? ListedUnder(string ticker, IReadOnlyList<FamilyPickRow> picks)
    {
        var own = picks.Where(pick => pick.Ticker == ticker).ToArray();

        if (own.Length == 0)
        {
            return null;
        }

        var listed = own.FirstOrDefault(pick => pick.State == FamilyList.Listed);
        var held = new List<string>();

        foreach (var pick in own.Where(pick => pick.State is FamilyList.OpenTrade or FamilyList.PastFive).OrderBy(pick => SetupFamilies.PlaceOf(pick.Family)))
        {
            var heading = SetupFamilies.Named(pick.Family)?.Heading ?? pick.Family;

            held.Add(pick.State == FamilyList.OpenTrade
                ? $"{heading} passed it on {pick.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} and does not list it: its trade from {(pick.HeldNight is { } from ? from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "an earlier night")} is still open."
                : FormattableString.Invariant($"{heading} passed it on {pick.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} and does not list it: it is past that setup's {SetupFamilies.ListedANight} for the night."));
        }

        return new ListedUnderView(
            own[0].SessionDate,
            listed?.Family,
            listed is null ? null : SetupFamilies.Named(listed.Family)?.Heading ?? listed.Family,
            listed?.Place,
            listed is null ? [] : [.. listed.Also.Select(name => SetupFamilies.Named(name)?.Label ?? name)],
            held);
    }

    // The line the page opens its setups on: the market check's answer with its figures, how many of the index's
    // members a setup passed where the night's answers are handed in, the buy points the cards list and how many
    // setups list one, the stocks close to one, and the trades still open on the night.
    // see: The market check closes every swing family's list together, and the sector heavyweights read none
    public static MarketLineView Line(ListRuleView rule, IReadOnlyList<FamilyCardView> cards, int close, int openTrades, int? passed = null, int? members = null) =>
        new(
            rule.MarketOpen,
            rule.Breadth,
            rule.Floor,
            cards.Sum(card => card.Picks.Count),
            cards.Count(card => card.Picks.Count > 0),
            cards.Count,
            close,
            openTrades,
            Universes.Large.Name,
            passed,
            members);

    // How far the members got down a family's gates, each count the members passing that gate and every
    // gate before it, the market check left out since it is one answer for every member.
    public static string Funnel(IReadOnlyList<FamilyResultRow> results)
    {
        var gates = results.Select(result => FamilyRule.GatesOf(result.Gates)).ToArray();
        var names = gates[0].Select(gate => gate.Name).Where(name => name != FamilyRule.Market).ToArray();
        var reached = names
            .Select((name, at) => gates.Count(member => member.Where(gate => gate.Name != FamilyRule.Market).Take(at + 1).All(gate => gate.Passed)))
            .ToArray();

        var steps = names
            .Select((name, at) => at == 0
                ? FormattableString.Invariant($"{reached[at]} passed the {name} gate")
                : FormattableString.Invariant($"{reached[at]} of those the {name} gate"))
            .ToArray();

        return FormattableString.Invariant($"of {results.Count} members, ")
            + (steps.Length > 1 ? string.Join(", ", steps[..^1]) + " and " + steps[^1] : string.Concat(steps));
    }
}
