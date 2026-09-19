namespace EquityBrief.Core.Providers;

// The three figures the numbers section states per quarter. Money, so decimal
// here and TEXT in storage, and nullable because the provider files a quarter
// with some of them missing rather than filing zero.
public sealed record QuarterFigures(decimal? Revenue, decimal? GrossProfit, decimal? NetIncome);

// The balance sheet as of a filing. Five figures rather than the statement's
// thirty, because these are the ones section 4's numbers row names and a column
// nothing reads is a column that can be wrong without anyone noticing.
public sealed record BalanceSheet(
    decimal? TotalAssets,
    decimal? TotalLiabilities,
    decimal? Equity,
    decimal? Cash,
    decimal? NetDebt);

// What the provider filed about a print that has happened: the date it landed,
// when in the session, what was earned and what had been expected.
//
// `EpsActual` present is what makes a quarter reported. The payload carries a
// row for the next print too, with an estimate, a null actual and a difference
// of zero, so a reader keying on the difference would report that a print which
// has not happened came in exactly as expected.
public sealed record ReportedEarnings(
    DateOnly? ReportDate,
    EventTiming Timing,
    decimal? EpsActual,
    decimal? EpsEstimate);

// One filing, which is this table's grain.
//
// The period end and the filing date are two different dates and the gap between
// them is weeks: AAPL's quarter ending 2026-06-30 was filed on 2026-07-31. That
// is the same trap the earnings calendar's payload carries, and it is the reason
// the store is keyed on the filing date while the figures are labelled by the
// period they cover.
public sealed record FiledQuarter(
    DateOnly PeriodEnd,
    DateOnly FilingDate,
    QuarterFigures Figures,
    BalanceSheet Sheet,
    ReportedEarnings? Earnings);

// The next print, as the provider has it: a period end, a date it is expected on
// and the consensus estimate for it.
//
// Not the guided quarter, and named so it cannot be mistaken for one. Section 4
// places the guided quarter at the earnings release exhibit, which is management
// stating what it expects; this is what analysts expect, which the provider
// files under an estimate. Putting a consensus under the word guide would put
// one party's number beside another party's name, and the report states what was
// expected against what arrived (see: Code owns every number).
public sealed record EstimatedQuarter(
    DateOnly PeriodEnd,
    DateOnly? ReportDate,
    EventTiming Timing,
    decimal? EpsEstimate);

// The earnings bases the valuation is stated on, per share and therefore money.
//
// Three rather than one because section 4's numbers row asks for the valuation on
// each earnings basis, and the bases are what differ: the trailing twelve months
// that have been reported, and the two forward years analysts have estimated.
// The ratio itself is not here, because a ratio needs a price and a price is in
// the bar store: computing it here would take a figure from a payload that has
// no close in it (see: Code owns every number).
public sealed record EpsBases(decimal? Trailing, decimal? CurrentYear, decimal? NextYear);

// The valuation on each earnings basis, as the provider files it.
//
// Copied rather than computed, which is the second of the two things a figure in a
// report may be: this payload carries no close, so computing a ratio here would
// need a price the endpoint does not send (see: Code owns every number). Decimal
// rather than double, because both figures are a money value over a money value
// and holding them as decimal means nothing crosses between the two worlds.
//
// As of the fetch rather than as of a filing. A ratio has a price in it and a
// price moves every session, which is why these sit on the newest filing's row
// alone and why the row carries the instant it was fetched at.
public sealed record ValuationRatios(decimal? TrailingPe, decimal? ForwardPe);

// What the market says the whole company is worth, as the provider files it.
//
// Money, so decimal, and as of the fetch rather than as of a filing: it is a price
// times a share count and the price moves every session. Section 15.9's fact strip
// states it beside the close, which is why it is read at all: the multiples and
// this figure are the two parts of that strip no computed table can supply.
public sealed record MarketValue(decimal? Capitalisation);

// What the analysts following the company say of it, as the provider files it: the mean of
// their ratings on a scale of one to five, their mean target price, and how many rate it at
// each of five grades from a strong buy to a strong sell.
//
// As of the fetch rather than as of a filing, for the reason the ratios are: a rating moves
// with every note an analyst writes.
// see: The fundamentals row carries the analysts' ratings the provider files, on the newest filing alone
public sealed record AnalystRatings(decimal? Rating, decimal? TargetPrice, int? StrongBuy, int? Buy, int? Hold, int? Sell, int? StrongSell);

// One name's fundamentals as one provider files them.
//
// `PartsNotCarried` is the part of this record that took a probe to write. The
// numbers section needs five things and this endpoint supplies three: the payload
// holds no segment table and no management guidance under any key at all, so
// those two are a structural absence rather than a value that happened to be
// missing for this name. They arrive from the filings archive, and the section
// marks them absent rather than drawing a blank, because a blank cell reads as a
// zero (see: Fundamentals are stored with the filing date they came from).
//
// `QuartersWithNoFilingDate` is counted rather than dropped in silence. One of
// the four captured names files a quarter with no filing date at all, and this
// table's grain is one row per filing date, so such a quarter cannot be stored
// and the count is what says so on the run log.
//
// `Cik` is the company's identifier at the filings archive, which this payload
// carries for every captured name and which the archive is addressed by and
// nothing else. It is read here rather than resolved at the archive because the
// caller already holds it by the time it needs one, and the alternative was a
// second whole-index file or a per-name lookup on every read.
public sealed record CompanyFundamentals(
    string Ticker,
    string Currency,
    string Cik,
    IReadOnlyList<FiledQuarter> Filed,
    EstimatedQuarter? Estimated,
    EpsBases Bases,
    ValuationRatios Valuation,
    MarketValue Market,
    AnalystRatings Ratings,
    IReadOnlyList<string> PartsNotCarried,
    int QuartersWithNoFilingDate);

// One name's fundamentals, in one request, on demand.
//
// Per name and off the nightly path, which is what keeps the night's per-name
// request count at zero: the fetch happens when a name is opened and its stored
// copy predates a filing, and never in the night's own arithmetic.
// see: The nightly run is arithmetic only
// see: Everything expensive happens when a name is opened
public interface IFundamentalsFeed
{
    Task<CompanyFundamentals> FundamentalsAsync(string ticker, CancellationToken cancellation = default);

    int Requests { get; }
}
