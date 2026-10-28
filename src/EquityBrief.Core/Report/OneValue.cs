namespace EquityBrief.Core.Report;

// A figure the provider states at a price of its own, read back to that price: its value, the base it divides or
// multiplies by, the price the two imply, and whether that price stands too far from the stored close to be the
// figure at it.
public sealed record ProviderFigure(decimal Value, decimal Base, decimal StruckAt, bool Stale);

// One value for each concept a page draws, worked at render time from the stored close: the company's value from the shares
// filed before the night, the forward multiple from the analysts' estimate for next year and the yield from the dividend's
// rate. A provider's own figure of the same concept is read back to the price it was struck at and is drawn beside the
// worked one as the provider's, marked stale where that price stands more than the stated share from the close.
// see: Each concept a page draws holds one value worked at the stored close beside the provider's own marked stale where struck far from it, and a check flags any concept drawn twice
public static class OneValue
{
    // How far a provider's price may stand from the stored close, as a share of the close, before its figure is stale.
    public const decimal StaleShare = 0.03m;

    public static decimal? MarketValue(decimal? shares, decimal? close) =>
        shares is { } held && close is { } price && held > 0 ? held * price : null;

    public static double? Multiple(decimal? close, decimal? earnings) =>
        close is { } price && earnings is { } perShare && perShare > 0 ? Prices.Statistic.FromRatio(price / perShare) : null;

    public static double? Yield(decimal? rate, decimal? price) =>
        rate is { } annual && price is { } paid && paid > 0 ? Prices.Statistic.FromRatio(annual / paid) : null;

    // A provider's multiple read back to its price: the multiple times the earnings it says it divided by.
    public static ProviderFigure? StruckMultiple(decimal? multiple, decimal? earnings, decimal? close) =>
        multiple is { } stated && earnings is { } perShare && stated > 0 && perShare > 0
            ? Struck(stated, perShare, stated * perShare, close)
            : null;

    // A provider's company value read back to its price: the value over the shares it was worked from.
    public static ProviderFigure? StruckValue(decimal? value, decimal? shares, decimal? close) =>
        value is { } stated && shares is { } held && stated > 0 && held > 0
            ? Struck(stated, held, stated / held, close)
            : null;

    // A provider's yield read back to its price: the rate over the yield.
    public static ProviderFigure? StruckYield(decimal? yield, decimal? rate, decimal? close) =>
        yield is { } stated && rate is { } annual && stated > 0 && annual > 0
            ? Struck(stated, annual, annual / stated, close)
            : null;

    static ProviderFigure Struck(decimal value, decimal by, decimal price, decimal? close) =>
        new(value, by, price, close is { } stored && stored > 0 && Math.Abs(price / stored - 1) > StaleShare);
}
