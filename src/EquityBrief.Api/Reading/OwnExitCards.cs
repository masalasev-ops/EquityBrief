using EquityBrief.Core.Books;
using EquityBrief.Core.Families;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// Each book's own exit as its card draws it, on the operator's ruling of 2026-11-16: the walk from the first night after
// its live rule's registration at the rules restart, over what the book's step stored, beside the live book's positions on
// the same nights. A book with no live rule, or read on its registration's own night, draws none.
// see: Each book's own exit is walked beside its live book as a record on its card, and registers no version and draws no pick
public static class OwnExitCards
{
    public const string QualityTurn = "at a reshuffle where a holding no longer qualifies, or when it leaves the index";
    public const string ImprovingTurn = "where its score falls under the hold floor, at its time limit, or when it leaves the index";
    public const string LeadingTurn = "at a rebalance where it no longer leads its sector, or when it leaves the index";
    public const string AverageTurn = "at a close under its 200-day average, or when it leaves the index";

    public static async Task<OwnExitView?> QualityAsync(ReadApi read, string index, string rule, DateOnly? liveSince, DateOnly night, string? settings)
    {
        if (liveSince is not { } registered || registered.AddDays(1) is var since && since > night)
        {
            return null;
        }

        var (holdings, trend) = QualitySettings(settings);
        var live = await read.OwnExitLiveAsync(ReadApi.OwnExitBook.Quality, index, rule, since, night);
        var nights = await read.QualityOwnNightsAsync(index, rule, since, night);
        var carried = await read.OwnExitCarriedAsync(ReadApi.OwnExitBook.Quality, index, rule, live.Select(position => position.ChosenOn));
        var prices = await read.OwnExitPricesAsync(
            live.Select(position => position.Ticker).Concat(nights.Where(one => one.Reshuffle).SelectMany(one => one.Answers.Where(answer => answer.Value.Passed).Select(answer => answer.Key))),
            live.Count > 0 ? live.Min(position => position.ChosenOn) : since,
            since,
            night);

        var own = OwnExit.Quality(
            since,
            holdings,
            Starts(live, since, carried),
            nights,
            await read.OwnExitMembersAsync(index),
            (ticker, on) => live.FirstOrDefault(position => position.Ticker == ticker && position.ChosenOn == on)?.Gap ?? prices.GapOf(ticker, on),
            trend ? prices.UnderAverage : null);

        return TonightScreen.OwnExitOf(own, live, position => OpenChange(position, carried, nights), QualityTurn, night);
    }

    public static async Task<OwnExitView?> ImprovingAsync(ReadApi read, string index, string rule, DateOnly? liveSince, DateOnly night, string? settings)
    {
        if (liveSince is not { } registered || registered.AddDays(1) is var since && since > night)
        {
            return null;
        }

        var setting = TonightScreen.SettingOf(settings);
        var live = await read.OwnExitLiveAsync(ReadApi.OwnExitBook.Improving, index, rule, since, night);
        var nights = await read.ImprovingOwnNightsAsync(index, rule, since, night);
        var carried = await read.OwnExitCarriedAsync(ReadApi.OwnExitBook.Improving, index, rule, live.Select(position => position.ChosenOn));
        var prices = await read.OwnExitPricesAsync(
            live.Select(position => position.Ticker).Concat(nights.SelectMany(one => one.Bought)),
            live.Count > 0 ? live.Min(position => position.ChosenOn) : since,
            since,
            night);

        var own = OwnExit.Improving(
            since,
            new ImprovingSettings(setting.BuyInNine, setting.HoldInNine, setting.Sessions),
            Starts(live, since, carried),
            nights,
            await read.OwnExitMembersAsync(index),
            (ticker, on) => live.FirstOrDefault(position => position.Ticker == ticker && position.ChosenOn == on)?.Gap ?? prices.GapOf(ticker, on),
            prices.SessionsHeld,
            setting.SoldUnderAverage ? prices.UnderAverage : null);

        return TonightScreen.OwnExitOf(own, live, position => OpenChange(position, carried, nights), ImprovingTurn, night);
    }

    // The S&P 500's page book, or an S&P 400 or 600 rule's own book: every session the store holds a close of a stock it can
    // hold from the first night on, its rebalances' buys, and the S&P 500's leaders where it sells on the lead.
    public static async Task<OwnExitView?> HeavyweightsAsync(ReadApi read, string index, string? rule, DateOnly? liveSince, DateOnly night, bool soldOnLeading, bool soldUnderAverage)
    {
        if (liveSince is not { } registered || registered.AddDays(1) is var since && since > night)
        {
            return null;
        }

        var book = rule is null ? ReadApi.OwnExitBook.Heavyweights : ReadApi.OwnExitBook.IndexHeavyweights;

        // An S&P 400 or 600 rule selling on the lead stores no leaders of its own a walk could read, so its own exit is not drawn.
        if (book == ReadApi.OwnExitBook.IndexHeavyweights && soldOnLeading)
        {
            return null;
        }

        var live = await read.OwnExitLiveAsync(book, index, rule ?? string.Empty, since, night);
        var rebalances = await read.OwnExitRebalancesAsync(book, index, rule ?? string.Empty, since, night, live);
        var prices = await read.OwnExitPricesAsync(
            live.Select(position => position.Ticker).Concat(rebalances.SelectMany(one => one.Bought ?? [])),
            live.Count > 0 ? live.Min(position => position.ChosenOn) : since,
            since,
            night);
        var byNight = rebalances.ToDictionary(one => one.Night);
        DateOnly[] sessions = [.. live.Select(position => position.Ticker)
            .SelectMany(ticker => Enumerable.Range(0, night.DayNumber - since.DayNumber + 1).Select(offset => since.AddDays(offset)).Where(day => prices.CloseOn(ticker, day) is not null))
            .Concat(byNight.Keys)
            .Distinct()
            .Order()];

        var own = OwnExit.Heavyweights(
            since,
            soldOnLeading,
            [.. live.Where(position => position.ChosenOn < since).Select(position => new OwnExitHolding(position.Ticker, position.ChosenOn))],
            [.. sessions.Select(day => byNight.TryGetValue(day, out var rebalance) ? rebalance : new OwnExitRebalance(day, null, null))],
            await read.OwnExitMembersAsync(index),
            prices.CloseOn,
            soldUnderAverage ? prices.UnderAverage : null);

        return TonightScreen.OwnExitOf(
            own,
            live,
            position => prices.CloseOn(position.Ticker, position.ChosenOn) is { } bought && bought > 0m && LastClose(prices, position.Ticker, since, night) is { } now
                ? EquityBrief.Core.Prices.Statistic.FromRatio(now / bought) - 1.0
                : null,
            soldUnderAverage && !soldOnLeading ? AverageTurn : LeadingTurn,
            night);
    }

    // An S&P 400 or 600 heavyweights rule's newest registration: its day and whether it sells on the lead and under its
    // 200-day average, read off the parameters it was registered with; none where it was never registered.
    public static (DateOnly Since, bool SoldOnLeading, bool SoldUnderAverage)? IndexHeavyweightLive(IReadOnlyList<CandidateRow> register, string rule)
    {
        if (register.Where(row => row.Candidate == rule && row.Event == EquityBrief.Core.Candidates.CandidateFamily.Registered).OrderBy(row => row.RegisteredAt).LastOrDefault() is not { } registered)
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(registered.Parameters);
            var root = document.RootElement;

            static bool On(System.Text.Json.JsonElement root, string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.Number && value.GetInt32() == 1;

            return (DateOnly.FromDateTime(registered.RegisteredAt.UtcDateTime), On(root, "soldOnLeading"), On(root, "soldUnderAverage"));
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    static decimal? LastClose(OwnExitPrices prices, string ticker, DateOnly since, DateOnly night)
    {
        for (var day = night; day >= since.AddDays(-10); day = day.AddDays(-1))
        {
            if (prices.CloseOn(ticker, day) is { } close)
            {
                return close;
            }
        }

        return null;
    }

    // The live positions open on the evening before the walk's first night, each with the growth its stored answer carried on
    // the night it was chosen; one whose answer the store does not hold is left out of the walk.
    static OwnExitStart[] Starts(IReadOnlyList<OwnExitLive> live, DateOnly since, IReadOnlyDictionary<(string Ticker, DateOnly Night), double> carried) =>
        [.. live
            .Where(position => position.ChosenOn < since && carried.ContainsKey((position.Ticker, position.ChosenOn)))
            .Select(position => new OwnExitStart(position.Ticker, position.ChosenOn, carried[(position.Ticker, position.ChosenOn)], position.Gap))];

    // An open live position's change at the newest night that answered it, in the book's own measure.
    static double? OpenChange(OwnExitLive position, IReadOnlyDictionary<(string Ticker, DateOnly Night), double> carried, IReadOnlyList<OwnExitNight> nights)
    {
        if (position.Gap is not { } gap || !carried.TryGetValue((position.Ticker, position.ChosenOn), out var start) || !(start > 0))
        {
            return null;
        }

        foreach (var night in nights.OrderByDescending(one => one.Night))
        {
            if (night.Answers.TryGetValue(position.Ticker, out var answer))
            {
                return (answer.Carried / start) - 1.0 - gap;
            }
        }

        return null;
    }

    static (int Holdings, bool Trend) QualitySettings(string? settings)
    {
        if (settings is null)
        {
            return (QualityRule.Starting.Holdings, false);
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(settings);
            var root = document.RootElement;

            return (
                root.TryGetProperty("holdings", out var holdings) ? holdings.GetInt32() : QualityRule.Starting.Holdings,
                root.TryGetProperty("trend", out var trend) && trend.ValueKind == System.Text.Json.JsonValueKind.True);
        }
        catch (System.Text.Json.JsonException)
        {
            return (QualityRule.Starting.Holdings, false);
        }
    }
}

public static partial class TonightScreen
{
    // The view a book's card draws of its own exit: the walk's record, and the live book's positions over the same nights,
    // each ended one at its stored result and each open one at the change given for it.
    // see: Each book's own exit is walked beside its live book as a record on its card, and registers no version and draws no pick
    public static OwnExitView OwnExitOf(OwnExitRecord own, IReadOnlyList<OwnExitLive> live, Func<OwnExitLive, double?> openChange, string turn, DateOnly? night = null)
    {
        double[] liveChanges = [.. live
            .Select(position => position.EndedOn is not null ? position.Result : openChange(position))
            .Where(change => change is not null)
            .Select(change => change!.Value)];

        return new OwnExitView(
            own.Since,
            turn,
            [.. own.Held.Select(position => position.Ticker).Order(StringComparer.Ordinal)],
            [.. own.Sold.OrderBy(position => position.EndedOn).ThenBy(position => position.Ticker, StringComparer.Ordinal).Select(position => new OwnExitSoldCell(position.Ticker, position.EndedOn!.Value, position.EndReason!))],
            own.MeanChange,
            own.Positions.Count(position => position.Change is not null),
            liveChanges.Length > 0 ? liveChanges.Average() : null,
            liveChanges.Length)
        {
            Through = night,
        };
    }
}
