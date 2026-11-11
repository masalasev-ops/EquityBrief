using EquityBrief.Core.Cards;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Prices;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The one place a page's count of trades comes from: over every family's live or provisional account on an index, or one
// family's, the system's trades, this version's trades, the same trades before this version and the earlier versions', and
// what waits to be bought. A trade is open only once bought, so a listing waiting on the next open is in no group and counts
// as to buy.
// see: Every count of trades names its group and one function works the four groups
public static class TradeGroups
{
    public static TradeGroupsView Of(
        IReadOnlyList<PaperAccountSource> sources,
        DateOnly night,
        Func<string, DateOnly, decimal?> close,
        IReadOnlyList<CandidateRow> register,
        string indexCode,
        string? family = null,
        IReadOnlyList<DateOnly>? sessions = null)
    {
        var heads = sources.Where(account => account.Standing is PaperAccountView.Live or PaperAccountView.Provisional && (family is null || account.Family == family)).ToArray();

        // A night's page counts what stood on that night: a trade bought at a later session's open was still to buy.
        (PaperTrade Trade, double? Result)[] version = [.. heads.SelectMany(head => head.Trades).Where(one => one.Trade.Bought <= night)];
        (PaperTrade Trade, double? Result)[] earlier = [.. heads.SelectMany(head => (head.Earlier ?? []).Select(one => (one.Trade, one.Result))).Where(one => one.Trade.Bought <= night)];
        (PaperTrade Trade, double? Result)[] same = [.. heads.SelectMany(head => head.Same).Where(one => one.Trade.Bought <= night)];

        int ToBuy(IEnumerable<PaperAccountSource> accounts) =>
            accounts.Sum(head => (head.Unfilled ?? []).Count(one => one.Why == PaperAccount.Waiting)
                + head.Trades.Count(one => one.Trade.Bought > night)
                + (head.Earlier ?? []).Count(one => one.Trade.Bought > night));

        var since = heads.Select(head => head.Since).OfType<DateOnly>().ToArray();

        return new TradeGroupsView(
            FirstLive(register, indexCode, family),
            since.Length == 0 ? null : since.Max(),
            Count([.. version, .. earlier, .. same], night, close, sessions),
            Count(version, night, close, sessions),
            Count(earlier, night, close, sessions),
            ToBuy(heads),
            ToBuy(heads.Where(head => head.Book && head.Family != Core.Families.ImprovingRule.Name)))
        {
            SameTrades = Count(same, night, close, sessions),
        };
    }

    // The day each family first went live on an index, as a setup's tile draws it.
    public static IReadOnlyDictionary<string, DateOnly?> FirstLiveByFamily(IReadOnlyList<CandidateRow> register, string indexCode) =>
        new[] { Core.Families.SetupFamilies.Pullback, Core.Families.BreakoutRule.Name, Core.Families.DriftRule.Name, Core.Families.ImprovingRule.Name, Core.Families.DipRule.Name, Core.Families.HeavyweightRule.Name, Core.Families.QualityRule.Name }
            .ToDictionary(family => family, family => FirstLive(register, indexCode, family), StringComparer.Ordinal);

    // The trades an earlier version of a family's live rule still holds of one stock, which its chart and name pages name.
    public static IReadOnlyList<EarlierHoldingView> EarlierHoldings(IReadOnlyList<PaperAccountSource> sources, string ticker, DateOnly night) =>
    [
        .. sources
            .Where(account => account.Standing is PaperAccountView.Live or PaperAccountView.Provisional)
            .SelectMany(account => (account.Earlier ?? [])
                .Where(one => one.Trade.Ticker == ticker && (one.Trade.Ended is null || one.Trade.Ended > night))
                .Select(one => new EarlierHoldingView(account.Family, one.Version, one.Trade.Bought, one.Version == PaperScreen.BeforeRegistration))),
    ];

    public static TradeCountView Count(IReadOnlyList<(PaperTrade Trade, double? Result)> trades, DateOnly night, Func<string, DateOnly, decimal?> close, IReadOnlyList<DateOnly>? sessions = null)
    {
        var closed = trades.Where(one => one.Trade.Ended is { } end && end <= night).ToArray();
        var endings = closed.Select(one => PaperAccount.Ended(one.Trade, one.Result, sessions, close).Ending).ToArray();
        var returns = trades.Select(one => ReturnOf(one.Trade, night, close)).OfType<double>().ToArray();

        // A trade's ending is the words its row on Paper trading draws, read off the exit that sold it: the open after a close
        // at its target can sell it under the target, and a trailing stop has risen past the stop it was bought with.
        return new TradeCountView(
            trades.Count,
            trades.Count - closed.Length,
            closed.Length,
            closed.Count(one => one.Result > 0 || (one.Result is null && one.Trade.Sale > one.Trade.Buy)),
            endings.Count(ending => ending is PaperAccount.ReachedTarget or PaperAccount.OpenedOverTarget),
            endings.Count(ending => ending is PaperAccount.UnderStop or PaperAccount.UnderTrail or PaperAccount.OpenedUnderStop),
            returns.Length == 0 ? null : returns.Average(),
            trades.Count(one => PaperAccount.Selling(one.Trade, night, close) is not null));
    }

    // A trade's return on its buy: its sale once it ended, and tonight's close while it is open.
    static double? ReturnOf(PaperTrade trade, DateOnly night, Func<string, DateOnly, decimal?> close) =>
        trade.Ended is { } end && end <= night
            ? trade.Sale is { } sale && trade.Buy > 0m ? Statistic.FromRatio((sale / trade.Buy) - 1m) : null
            : close(trade.Ticker, night) is { } now && trade.Buy > 0m ? Statistic.FromRatio((now / trade.Buy) - 1m) : null;

    // The day a family's live rule first went live on an index, or the earliest of its families where none is named: the
    // first registration of a rule holding the live name, under any version. The S&P 500's pullback is the swing filter's.
    public static DateOnly? FirstLive(IReadOnlyList<CandidateRow> register, string indexCode, string? family = null) =>
        register
            .Where(row => row.Event == CandidateFamily.Registered && FamilyRecords.IsLive(row.Candidate))
            .Where(row => Belongs(row.Evaluator, indexCode, family))
            .Select(row => (DateOnly?)DateOnly.FromDateTime(row.RegisteredAt.UtcDateTime))
            .Min();

    static bool Belongs(string evaluator, string indexCode, string? family) =>
        CandidateEvaluators.Find(evaluator) switch
        {
            IndexRuleCandidate rule => rule.Index == indexCode && (family is null || rule.SetupFamily == family),
            OwnStepCandidate step => step.Index == indexCode && (family is null || step.SetupFamily == family),
            null => false,
            _ => indexCode == "GSPC" && (family is null || CandidateFamily.SetupFamilyOf(evaluator) == family),
        };
}
