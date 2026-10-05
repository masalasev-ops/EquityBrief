using EquityBrief.Core.Providers;

namespace EquityBrief.Web.App;

// One index a page reads: the word its link keeps, its code in the store and its name.
public sealed record UniverseChoice(string Word, string Code, string Name)
{
    public string Possessive => Name + "'s";
}

// The three indices a page reads one at a time, in the order the Universe selector offers them, the S&P 500 first and
// chosen where the link names none.
// see: Every page reads one index at a time chosen under Universe, and every figure names its index
public static class Universes
{
    // The query the choice is kept under in a page's link.
    public const string Query = "universe";

    public static UniverseChoice Large { get; } = new("500", "GSPC", "S&P 500");

    public static UniverseChoice Mid { get; } = new("400", FundHoldings.MidCapIndex, "S&P 400");

    public static UniverseChoice Small { get; } = new("600", FundHoldings.SmallCapIndex, "S&P 600");

    public static IReadOnlyList<UniverseChoice> Offered { get; } = [Large, Mid, Small];

    // The index a link names, and the S&P 500 where it names none or one the selector does not offer.
    public static UniverseChoice Of(string? word) =>
        Offered.FirstOrDefault(choice => string.Equals(choice.Word, word, StringComparison.Ordinal)) ?? Large;

    // The index stored under a code, and none for a code the selector does not offer.
    public static UniverseChoice? ByCode(string? code) =>
        Offered.FirstOrDefault(choice => string.Equals(choice.Code, code, StringComparison.Ordinal));
}
