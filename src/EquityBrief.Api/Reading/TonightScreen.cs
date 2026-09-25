using System.Globalization;
using EquityBrief.Core.Research;
using EquityBrief.Core.Spending;
using System.Text.Json;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Shortlist;
using EquityBrief.Data;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from stored listing rows to what tonight's list draws.
//
// It sits beside `NameScreen` and `UniverseScreen` and in the same seam. Nothing
// here decides whether a reason fired: the builder decided that and wrote it,
// and this reads it back. What it does is order, count and cut, which is what
// section 15.7 states the page does.
// see: A screen reads and renders, and computes nothing
// see: The page shows twenty and states the true count
public static class TonightScreen
{
    // The three orders tonight's list is measured in. The first is the one the list is drawn in;
    // the second is the order it replaced, which the other two are measured against; the third
    // is the plan's reward to risk with the fired count left out.
    // see: The order tonight's list is drawn in is compared against the order it replaces, declared before any record is read
    public enum Order
    {
        FiredThenRewardToRisk,
        FiredThenBandStrength,
        RewardToRiskAlone,
    }

    // Each order, as the run page names it.
    public static string Named(Order order) => order switch
    {
        Order.FiredThenRewardToRisk => "how many reasons fired, then the plan's reward to risk",
        Order.FiredThenBandStrength => "how many reasons fired, then band strength",
        _ => "the plan's reward to risk alone",
    };

    // Rows in one of the three orders. A row with no reward to risk sorts after every row with
    // one where the ratio decides, and the ticker in code-point order settles what is left, so
    // the order is total and two reads of one night cannot disagree.
    // see: Tonight's list breaks a tie in fired count by the plan's reward to risk, and a row with none is drawn after every row with one and says why
    public static IReadOnlyList<ListingCell> Ordered(IEnumerable<ListingCell> rows, Order order) => order switch
    {
        Order.FiredThenRewardToRisk => DrawnOrder.Ordered(rows, row => row.FiredCount, row => row.RewardToRisk, row => row.Ticker),
        Order.FiredThenBandStrength =>
        [
            .. rows
                .OrderByDescending(row => row.FiredCount)
                .ThenByDescending(row => row.Strength)
                .ThenBy(row => row.Ticker, StringComparer.Ordinal),
        ],
        _ =>
        [
            .. rows
                .OrderBy(row => row.RewardToRisk is null)
                .ThenByDescending(row => row.RewardToRisk)
                .ThenBy(row => row.Ticker, StringComparer.Ordinal),
        ],
    };

    // The reward to risk the night's plan computes from its first tranche, read off the plan the
    // listing kept that night, or the plan's own words for why it computes none.
    //
    // The listing keeps the first tranche and the first traded exit, which are all the ratio from
    // the first tranche reads, so it is worked by the ladder's own arithmetic rather than by a
    // second statement of it. Rounded as the ladder stores it, so the figure the list draws is the
    // figure the night's plan holds. Read off the listing rather than the ladder because the
    // listing is kept and the ladder is dropped a year back, and an order that could not be read
    // for an old night is one the comparison could not measure.
    // see: Tonight's list breaks a tie in fired count by the plan's reward to risk, and a row with none is drawn after every row with one and says why
    // see: Code owns every number
    public static (decimal? RewardToRisk, string? Why) FirstTranche(string planAtListing)
    {
        var (ratio, absent) = DrawnOrder.FirstTranche(planAtListing);

        return ratio is { } found ? (found, null) : (null, absent ?? MarkRenderer.NoRewardToRiskStated);
    }

    // A listing row as the three orders read it: its fired count, the band strength it recorded
    // and its plan's reward to risk, and nothing a screen draws beside them.
    public static ListingCell Ranked(ListingRow listing)
    {
        var (rewardToRisk, why) = FirstTranche(listing.PlanAtListing);

        return new ListingCell(
            listing.Ticker,
            listing.SessionDate,
            listing.FiredCount,
            listing.BandStrength ?? 0,
            null,
            [],
            RewardToRisk: rewardToRisk,
            NoRewardToRisk: why);
    }

    // The order section 15.7 states: how many reasons fired, then the plan's reward to risk, then
    // the ticker. Every figure is read off the night's own listing rows, which carry the plan and
    // the band strength as that night had them.
    // On a night the swing filter listed, the names it passed in its own order, each with its gates and the
    // reasons beside it as context; before the switch, the names that fired in the order section 15.7 states.
    // see: Tonight's list is the swing filter's, and an evening is listed by the rule that listed it
    public static IReadOnlyList<ListingCell> Rows(
        DateOnly night,
        IReadOnlyList<ListingRow> listings,
        IReadOnlyDictionary<string, UniverseCell> cellByTicker,
        IReadOnlyList<CloseRow> closesToTheNight,
        IReadOnlyList<SuspectSeriesRow>? suspects = null,
        IReadOnlyList<ResearchedRow>? researched = null,
        IReadOnlyList<GateResultRow>? gates = null)
    {
        // The names whose stored series is suspect, which a row says beside the name.
        var suspectByTicker = (suspects ?? [])
            .ToDictionary(row => row.Ticker, StringComparer.Ordinal);

        // The day each name's newest researched section was written, for the names
        // holding one. A name absent from this holds no research, which is what the
        // row says and what decides whether it offers to ask for one.
        var researchedByTicker = (researched ?? [])
            .ToDictionary(row => row.Ticker, row => row.Written, StringComparer.Ordinal);

        // The two newest sessions each name holds at or before the night, newest
        // first, which is what both the close cell and the day change are read
        // from. One read for the pair, because the pair is the property: two
        // reads gave 5.8 a close from one session and a previous close from
        // another, and the subtraction of two sessions that are not consecutive
        // is a number rather than an absence.
        var sessions = closesToTheNight
            .GroupBy(row => row.Ticker, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CloseRow>)[.. group.OrderByDescending(row => row.SessionDate)],
                StringComparer.Ordinal);

        ListingCell Drawn(ListingRow listing) => Cell(listing, night, cellByTicker, sessions) with
        {
            Suspect = NameScreen.Suspect(suspectByTicker.GetValueOrDefault(listing.Ticker)),
            ResearchedOn = researchedByTicker.TryGetValue(listing.Ticker, out var written) ? written : null,
        };

        if (gates is not null)
        {
            var byTicker = listings.ToDictionary(listing => listing.Ticker, StringComparer.Ordinal);

            return
            [
                .. gates
                    .Where(gate => gate.Passed && byTicker.ContainsKey(gate.Ticker))
                    .OrderBy(gate => gate.Rank ?? int.MaxValue)
                    .ThenBy(gate => gate.Ticker, StringComparer.Ordinal)
                    .Select(gate =>
                    {
                        var filter = FilterRowOf(gate);

                        return Drawn(byTicker[gate.Ticker]) with
                        {
                            Filter = filter,
                            RewardToRisk = decimal.TryParse(filter.RewardToRisk, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) ? ratio : null,
                            NoRewardToRisk = null,
                        };
                    }),
            ];
        }

        return Ordered(listings.Where(listing => listing.FiredCount > 0).Select(Drawn), Order.FiredThenRewardToRisk);
    }

    // A gate row as the list draws it: its rank, the setup's family, the session its trigger arrived on,
    // the plan its trade gate read with the reward to risk and the stop's distance, and each gate with why.
    public static FilterRow FilterRowOf(GateResultRow gate)
    {
        using var document = JsonDocument.Parse(gate.Gates);

        var gates = document.RootElement.GetProperty("gates").EnumerateArray()
            .Select(one => (
                Name: one.GetProperty("gate").GetString()!,
                Passed: one.GetProperty("passed").GetBoolean(),
                Reason: one.GetProperty("reason").GetString()!,
                Values: one.GetProperty("values").EnumerateObject().ToDictionary(value => value.Name, value => value.Value.GetString() ?? string.Empty, StringComparer.Ordinal)))
            .ToArray();

        string Value(string name, string key) =>
            gates.FirstOrDefault(one => one.Name == name).Values is { } values && values.TryGetValue(key, out var value) ? value : "none";

        return new FilterRow(
            gate.Rank ?? 0,
            gate.Family,
            Value(EquityBrief.Core.Filter.SwingGates.Trigger, EquityBrief.Core.Filter.SwingGates.ArrivedValue),
            Value(EquityBrief.Core.Filter.SwingGates.Trade, "input"),
            Value(EquityBrief.Core.Filter.SwingGates.Trade, "reward to risk"),
            Value(EquityBrief.Core.Filter.SwingGates.Trade, "stop in typical moves"),
            [.. gates.Select(one => new FilterGate(one.Name, one.Passed, one.Reason))]);
    }

    // The rule the night's list was drawn by, and on a night the swing filter drew it, whether the market
    // gate opened, the breadth and the floor its rows read, and how many members reached each gate after it.
    public static ListRuleView RuleView(string rule, IReadOnlyList<GateResultRow>? gates, MarketReadingRow? market)
    {
        if (rule != ListRules.Filter || gates is null)
        {
            return new ListRuleView(ListRules.Reasons, true, null, null, []);
        }

        double? floor = null;

        if (gates.Count > 0)
        {
            using var document = JsonDocument.Parse(gates[0].Gates);

            var marketGate = document.RootElement.GetProperty("gates").EnumerateArray().FirstOrDefault(one => one.GetProperty("gate").GetString() == EquityBrief.Core.Filter.SwingGates.Market);

            if (marketGate.ValueKind == JsonValueKind.Object
                && marketGate.GetProperty("values").TryGetProperty("floor", out var held)
                && double.TryParse(held.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                floor = parsed;
            }
        }

        int Through(int last) => gates.Count(gate => gate.Market && gate.Trend && (last < 1 || gate.Setup) && (last < 2 || gate.Trigger) && (last < 3 || gate.Trade));

        return new ListRuleView(ListRules.Filter, gates.Any(gate => gate.Market), market?.Breadth, floor, [Through(0), Through(1), Through(2), Through(3)]);
    }

    // How many names the night listed, by the rule that listed it.
    public static int Listed(IReadOnlyList<ListingRow> listings) => listings.Count(listing => listing.IsListed);

    // The day's change, as a signed percentage of the session before.
    //
    // Derived, and derived here for the reason `UniverseScreen` states about the
    // distance: it is not a stored column, the read surface computes nothing and
    // the page computes nothing, so the projection is the seam it belongs in.
    //
    // It takes the name's own sessions rather than two closes, because the two
    // closes are what went wrong: a change is only a day's change where the two
    // sessions behind it are the name's two newest at the night and the newer of
    // them is the night. A name with no bar on the night has no day change at
    // all, and drawing one from its last two stored sessions would be a figure
    // about a day the page is not showing. It read the newest bar against the
    // newest bar before the night until the sixth phase 5 sign-off review, which
    // is a wrong number on any past night and exactly 0.00 for a stale name.
    //
    // A previous close of zero or less gives no change rather than an infinite
    // one. That is a name the store holds a close of nothing for, and dividing
    // by it would report the price itself as a day's move, which is the shape
    // the earnings rule's own earlier-session guard was written against.
    // see: Code owns every number
    public static double? DayChange(DateOnly night, IReadOnlyList<CloseRow>? sessions)
    {
        if (sessions is not { Count: >= 2 } held || held[0].SessionDate != night || held[1].Close <= 0m)
        {
            return null;
        }

        return Statistic.FromPrice((held[0].Close - held[1].Close) / held[1].Close) * 100;
    }

    // The close the row shows: the name's close on the night, and nothing where
    // the name has no bar on it.
    //
    // Read from the same pair the change is, rather than from the universe row,
    // whose close column is the name's newest bar whatever night is asked for.
    // That column is what made a past night's row carry today's close beside a
    // change computed from that night's, and the two disagreeing on one row is
    // what the sixth review demonstrated. The universe screen still reads it, and
    // a past night's bands and plan are the same shape and are carried at 6.0.
    // owes: The phase 5 sign-off's remaining store-shape findings ruled or fixed
    public static decimal? CloseOn(DateOnly night, IReadOnlyList<CloseRow>? sessions) =>
        sessions is { Count: > 0 } held && held[0].SessionDate == night ? held[0].Close : null;

    // Which row the selected-name region is drawn for: the one the reader asked
    // for, and the first when they have asked for none or for a name that is not
    // on tonight's list.
    //
    // Section 15.7 says the region is for whichever row is selected. A
    // composition that always took the first would satisfy every count on this
    // page and answer a question the reader did not ask, which is what it did
    // until 5.8. The fallback is the first row rather than nothing, because the
    // page opens with no name in the hash and a region that were absent then
    // would make the common case the empty one.
    public static ListingCell? Selected(IReadOnlyList<ListingCell> rows, string? asked) =>
        rows.FirstOrDefault(row => string.Equals(row.Ticker, asked, StringComparison.Ordinal))
            ?? rows.FirstOrDefault();

    static ListingCell Cell(
        ListingRow listing,
        DateOnly night,
        IReadOnlyDictionary<string, UniverseCell> cellByTicker,
        IReadOnlyDictionary<string, IReadOnlyList<CloseRow>> sessionsByTicker)
    {
        using var document = JsonDocument.Parse(listing.Reasons);

        // The values that made each reason true, carried beside the names since
        // 5.6, because 15.7's reasons-per-row half shows them on hover and a row
        // holding only the names could not.
        var fired = document.RootElement.EnumerateArray()
            .Where(reason => reason.GetProperty("fired").GetBoolean())
            .Select(reason => new FiredReason(
                reason.GetProperty("name").GetString()!,
                reason.GetProperty("values").EnumerateObject()
                    .ToDictionary(value => value.Name, value => value.Value.GetString()!, StringComparer.Ordinal)))
            .ToArray();

        var cell = cellByTicker.GetValueOrDefault(listing.Ticker);
        var sessions = sessionsByTicker.GetValueOrDefault(listing.Ticker);
        var (rewardToRisk, why) = FirstTranche(listing.PlanAtListing);

        return new ListingCell(
            listing.Ticker,
            listing.SessionDate,
            listing.FiredCount,
            listing.BandStrength ?? 0,
            CloseOn(night, sessions),
            [.. fired.Select(reason => reason.Name)],
            fired,
            // The three the row states beside the name, the close and the
            // reasons. Each is absent rather than zero for a name the night
            // computed nothing for, which is the rule every other column on
            // every other screen already follows.
            DayChange(night, sessions),
            cell?.TrendState,
            cell,
            RewardToRisk: rewardToRisk,
            NoRewardToRisk: why);
    }

    // The true fired count over the whole index, which is the headline. It is
    // the market's mood and it is the one number the twenty drawn rows cannot
    // tell you.
    public static int Fired(IReadOnlyList<ListingRow> listings) =>
        listings.Count(listing => listing.FiredCount > 0);

    // Which of the six fired, and how often, across tonight's list. Whether the
    // evening is one thing happening to many names or many things happening to a
    // few.
    public static IReadOnlyList<ReasonTotal> Totals(IReadOnlyList<ListingRow> listings)
    {
        var counted = ShortlistSeries.Reasons.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);

        foreach (var listing in listings)
        {
            using var document = JsonDocument.Parse(listing.Reasons);

            foreach (var reason in document.RootElement.EnumerateArray())
            {
                if (reason.GetProperty("fired").GetBoolean())
                {
                    counted[reason.GetProperty("name").GetString()!]++;
                }
            }
        }

        // Every reason is a row whether or not it fired, in section 11's own
        // order, so a reason that fires on no night is still visible as one that
        // fires on no night.
        return [.. ShortlistSeries.Reasons.Select(name => new ReasonTotal(name, counted[name]))];
    }

    // The sessions among these rows that were written before the 5.4 correction,
    // oldest first, read off whether earnings soon's values carry the event date
    // the corrected rule writes. The rows are kept as written, and every route
    // that draws them says so beside them rather than presenting what the defect
    // wrote as that night's reading.
    // see: Sessions to a dated event are counted on the exchange calendar and never on stored bars
    public static IReadOnlyList<DateOnly> WrittenBeforeTheCorrection(IEnumerable<ListingRow> listings) =>
    [
        .. listings
            .Where(listing =>
            {
                using var document = JsonDocument.Parse(listing.Reasons);

                return document.RootElement.EnumerateArray()
                    .Where(reason => reason.GetProperty("name").GetString() == ShortlistSeries.EarningsSoon)
                    .Any(reason => ShortlistSeries.WrittenBeforeTheCorrection(
                        ShortlistSeries.EarningsSoon,
                        value => reason.GetProperty("values").TryGetProperty(value, out _)));
            })
            .Select(listing => listing.SessionDate)
            .Distinct()
            .Order(),
    ];

    // The evenings a name was on the list over a window, which is what the
    // listing strip draws and what the universe screen's two right-hand columns
    // count. They say nothing about index membership, which every name in that
    // table has by definition.
    public static IReadOnlyList<bool> Strip(IReadOnlyList<ListingRow> history) =>
        [.. history.OrderBy(listing => listing.SessionDate).Select(listing => listing.IsListed)];

    // What research spent on a night: its UTC day, and its month up to the end of that
    // day, from the rows the month holds, beside the caps. Summed from the rows as the
    // spend cap sums them, so the header and the cap read one ledger.
    public static NightSpend Spend(DateOnly night, IReadOnlyList<SpentRow> rows, SpendCaps caps)
    {
        var ledger = new SpendLedger(rows);
        var endOfDay = new DateTimeOffset(night.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        return new NightSpend(
            ledger.SpentOn(night),
            rows.Where(row => row.StartedAt < endOfDay && row.StartedAt >= SpendLedger.MonthStart(endOfDay.AddTicks(-1))).Sum(row => row.Spend),
            caps.Day,
            caps.Month);
    }

    // Reports carrying fresh prose against reused, on a night: of the names with a
    // researched section as of the night, those with one written on the night, and those
    // whose every researched section was written before it. The rows are bounded here as
    // well as by the read, so a row after the night counts for neither, and a name is
    // counted once however many sections it carries.
    //
    // The key under each figure is not one of them. It is written for every name each
    // night whatever was researched, so counting it made this figure a count of the index
    // rather than of the reports: on the operator's store it stood at 502 where one name
    // held research.
    // see: A researched name is one holding an accepted section besides the key under each figure
    public static NightProse Prose(DateOnly night, IReadOnlyList<WrittenOnRow> rows)
    {
        var byName = rows
            .Where(row => row.AsOf <= night && ClaimRules.IsResearched(row.Section))
            .GroupBy(row => row.Ticker, StringComparer.Ordinal)
            .ToArray();

        var fresh = byName.Count(name => name.Any(row => row.AsOf == night));

        return new NightProse(fresh, byName.Length - fresh, byName.Length);
    }

    // The window a night's spend is read over: the first instant of its UTC month to
    // the end of its UTC day.
    public static (DateTimeOffset From, DateTimeOffset To) SpendWindow(DateOnly night)
    {
        var to = new DateTimeOffset(night.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        return (SpendLedger.MonthStart(to.AddTicks(-1)), to);
    }
}
