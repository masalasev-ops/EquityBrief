using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Quarters;

// One quarter as one fetch reported it, with the figures worked out at that fetch from the whole
// answer.
//
// A row belongs to its fetch and never to the quarter alone. The provider restates every per-share
// figure after a split as of the day it is asked, and its closes are adjusted as of that day too, so
// a quarter's earnings a share and the close after its report are on one basis only beside the other
// quarters the same fetch returned. A reading reads one fetch's rows and never mixes two.
// see: Reported quarters are stored per fetch, so every quarter a reading reads shares one fetch's per-share basis
//
// The growths, the margins, the trailing earnings and the close after the report are worked out at
// the fetch because they read quarters and closes older than the twelve a fetch keeps: the trailing
// earnings of the twelfth quarter need three quarters before it, and a close three years back is held
// by no bar the store keeps.
public sealed record ReportedQuarter(
    DateOnly PeriodEnd,
    DateOnly? FilingDate,
    DateOnly? ReportDate,
    decimal? Revenue,
    decimal? OperatingIncome,
    decimal? NetIncome,
    decimal? OperatingCashFlow,
    decimal? EpsActual,
    decimal? EpsEstimate,
    decimal? EpsTrailing,
    decimal? SalesGrowth,
    decimal? SalesGrowthBefore,
    decimal? OperatingMargin,
    decimal? MarginYearEarlier,
    decimal? CloseAfter,
    DateOnly? CloseAfterSession,
    decimal? Shares = null);

// One fetch's quarters and the close its per-share basis is read against: the newest session of the
// closes it fetched and that session's close, which tonight's close is brought to the fetch's basis by.
public sealed record QuarterFetch(IReadOnlyList<ReportedQuarter> Quarters, DateOnly? BasisSession, decimal? BasisClose)
{
    // How many quarters one fetch keeps, the newest by period end: twelve, which the reading that
    // reads the most, the valuation's range, is stated over.
    public const int Kept = 12;

    // How many years of closes a fetch asks for beside the quarters, so each of the twelve quarters
    // has the close after its report on the fetch's own basis.
    public const int PriceYears = 3;

    // How far one quarter's end may lie from a year, or three months, before another's and still be
    // that quarter. A fiscal quarter ending on a weekday ends within a week of its month's end, and
    // quarters are three months apart, so a week never reaches the next one.
    public const int NearDays = 7;

    // How far after a report the first close may fall and still be the close after it. A report
    // older than the closes a fetch holds has no close after it rather than a close weeks later.
    public const int CloseAfterDays = 7;

    // The quarters and the basis one fetch gives, from the provider's answer and the closes fetched
    // with it. The answer's quarters are read whole, so a figure a year or three quarters back is found
    // even where it falls outside the twelve kept.
    public static QuarterFetch From(CompanyFundamentals fetched, IReadOnlyList<ProviderBar> closes)
    {
        var filed = fetched.Filed.OrderByDescending(quarter => quarter.PeriodEnd).ToArray();
        var sessions = closes.OrderBy(bar => bar.SessionDate).ToArray();

        FiledQuarter? Near(DateOnly end) => filed
            .Where(one => Math.Abs(one.PeriodEnd.DayNumber - end.DayNumber) <= NearDays)
            .OrderBy(one => Math.Abs(one.PeriodEnd.DayNumber - end.DayNumber))
            .FirstOrDefault();

        decimal? Growth(FiledQuarter quarter) =>
            Near(quarter.PeriodEnd.AddYears(-1)) is { } earlier ? Grown(quarter.Figures.Revenue, earlier.Figures.Revenue) : null;

        decimal? Margin(FiledQuarter quarter) => Margined(quarter.Figures.OperatingIncome, quarter.Figures.Revenue);

        decimal? Trailing(FiledQuarter quarter)
        {
            var four = new[] { quarter, Near(quarter.PeriodEnd.AddMonths(-3)), Near(quarter.PeriodEnd.AddMonths(-6)), Near(quarter.PeriodEnd.AddMonths(-9)) };

            return four.All(one => one?.Earnings?.EpsActual is not null) ? four.Sum(one => one!.Earnings!.EpsActual!.Value) : null;
        }

        ProviderBar? After(DateOnly? reported) =>
            reported is { } on
                ? sessions.FirstOrDefault(bar => bar.SessionDate > on && bar.SessionDate.DayNumber - on.DayNumber <= CloseAfterDays)
                : null;

        var kept = filed
            .Take(Kept)
            .Select(quarter =>
            {
                var earlier = Near(quarter.PeriodEnd.AddYears(-1));
                var after = After(quarter.Earnings?.ReportDate);

                return new ReportedQuarter(
                    quarter.PeriodEnd,
                    quarter.FilingDate,
                    quarter.Earnings?.ReportDate,
                    quarter.Figures.Revenue,
                    quarter.Figures.OperatingIncome,
                    quarter.Figures.NetIncome,
                    quarter.Figures.OperatingCashFlow,
                    quarter.Earnings?.EpsActual,
                    quarter.Earnings?.EpsEstimate,
                    Trailing(quarter),
                    Growth(quarter),
                    earlier is null ? null : Growth(earlier),
                    Margin(quarter),
                    earlier is null ? null : Margin(earlier),
                    after?.Close,
                    after?.SessionDate,
                    quarter.Sheet.SharesOutstanding);
            })
            .ToArray();

        var basis = sessions.Length > 0 ? sessions[^1] : null;

        return new QuarterFetch(kept, basis?.SessionDate, basis?.Close);
    }

    // A figure's change on an earlier one as a fraction of the earlier, rounded to six places, and
    // none where either is missing or the earlier is not above nought. Money on both sides and decimal
    // out, so nothing crosses into the statistics world.
    public static decimal? Grown(decimal? now, decimal? then) =>
        now is { } current && then is { } earlier && earlier > 0m
            ? decimal.Round((current - earlier) / earlier, 6, MidpointRounding.ToEven)
            : null;

    // One money figure over another, rounded to six places, and none over a revenue of nought or none.
    public static decimal? Margined(decimal? part, decimal? whole) =>
        part is { } numerator && whole is { } denominator && denominator != 0m
            ? decimal.Round(numerator / denominator, 6, MidpointRounding.ToEven)
            : null;
}
