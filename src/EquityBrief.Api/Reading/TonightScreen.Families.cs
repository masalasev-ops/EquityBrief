using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from the page's stored list to the cards a night the families drew is read as. It
// evaluates no gate and draws no list: the lister drew it and stored it, and this reads it back, family by
// family in the page's order, each pick with the trade and the words its own family's stored answer holds.
// see: A screen reads and renders, and computes nothing
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
        IReadOnlyList<FamilyResultRow>? results = null)
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
                family.Rule,
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

        var closed = close is { Length: > 0 } and not "none" && high is { Length: > 0 } and not "none"
            ? FormattableString.Invariant($"Closed at {Price(close)}, above its high of {Price(high)} over the {BreakoutRule.HighSessions} sessions before")
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
    // stood at the night's end: the pullback's live candidate and the other candidates its evaluator runs.
    // A family no registration stands for runs on provisional settings and reads no day.
    // see: A family runs on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
    static (DateOnly? LiveSince, int Variants) Standing(SetupFamily family, IReadOnlyList<CandidateRow> register, DateOnly night)
    {
        if (family.Name != SetupFamilies.Pullback)
        {
            return (null, 0);
        }

        var at = new DateTimeOffset(night.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var standing = CandidateFamily.Standing(
                [
                    .. register.Select(row => new RegisterRow(
                        row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator,
                        row.Parameters, string.Empty, row.Event, row.Retires, row.RegisteredAt, row.Evidence)),
                ],
                at)
            .Where(row => row.Evaluator == SwingFilterRule.EvaluatorName)
            .ToArray();

        var live = standing.LastOrDefault(row => SwingFamily.IsLive(row.Candidate));

        return (
            live is null ? null : DateOnly.FromDateTime(live.RegisteredAt.UtcDateTime),
            standing.Count(row => !SwingFamily.IsLive(row.Candidate)));
    }

    // The notes beneath a family's picks: each stock it passed that a trade still open holds back, each one
    // the page lists under an earlier family, and how many it passed beyond the five it lists.
    // see: A stock holds one trade across every family, and one qualifying under two is listed once under the first in the page's order
    static IReadOnlyList<string> Notes(SetupFamily family, IReadOnlyList<FamilyPickRow> own, IReadOnlyDictionary<string, string> listedUnder)
    {
        var notes = new List<string>();

        foreach (var held in own.Where(pick => pick.State == FamilyList.OpenTrade).OrderBy(pick => pick.Ticker, StringComparer.Ordinal))
        {
            var from = held.HeldNight is { } listed ? listed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "an earlier night";
            var whose = held.HeldFamily is { } holder && holder != family.Name
                ? $"its {(SetupFamilies.Named(holder)?.Label ?? holder).ToLowerInvariant()} trade from {from}"
                : $"its trade from {from}";

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
    // see: The market check closes every family's list together
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
    // see: A family runs on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
    public static IReadOnlySet<string> ProvisionalSetups(IReadOnlyList<CandidateRow> register, DateOnly night) =>
        SetupFamilies.InPageOrder
            .Where(family => family.Name != SetupFamilies.Pullback && Standing(family, register, night).LiveSince is null)
            .Select(family => family.Name)
            .ToHashSet(StringComparer.Ordinal);

    // The run page's setups: each family the page draws with its rule's standing, the variants scored
    // beside it, what it lists on the night, the trades the page has listed under it and how they stand,
    // and its record, which for a family no freeze has registered is the words saying it starts at the freeze.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    public static IReadOnlyList<FamilyRunRow> FamilyRun(
        DateOnly night,
        IReadOnlyList<FamilyPickRow> picksTonight,
        IReadOnlyList<PickCell> trades,
        IReadOnlyList<CandidateRow> register)
    {
        return
        [
            .. SetupFamilies.InPageOrder.Select(family =>
            {
                var (liveSince, variants) = Standing(family, register, night);
                var own = trades.Where(trade => trade.Family == family.Name && trade.RepeatOf is null).ToArray();
                var summary = PicksScreen.Summary(own);

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
                        : FormattableString.Invariant($"{summary.Decided} decided of the {summary.MinimumDecided} its record waits for, on {summary.DecidedNights} of {summary.MinimumNights} nights"));
            }),
        ];
    }

    // What a name's page says of the page's list on its night, from the name's own rows on it: the setup that
    // lists it, its place and the labels of the other setups it qualified under, and a sentence for a setup
    // that passed it while a trade for it is still open or past that setup's five. Nothing where the
    // families drew no row for the name.
    // see: A stock holds one trade across every family, and one qualifying under two is listed once under the first in the page's order
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

    // The line the page opens its setups on: the market check's answer with its figures, the buy points the
    // cards list and how many setups list one, the stocks close to one, and the trades still open on the night.
    // see: The market check closes every family's list together
    public static MarketLineView Line(ListRuleView rule, IReadOnlyList<FamilyCardView> cards, int close, int openTrades) =>
        new(
            rule.MarketOpen,
            rule.Breadth,
            rule.Floor,
            cards.Sum(card => card.Picks.Count),
            cards.Count(card => card.Picks.Count > 0),
            cards.Count,
            close,
            openTrades);

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
