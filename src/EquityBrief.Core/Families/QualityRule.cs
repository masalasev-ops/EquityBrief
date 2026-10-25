using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Families;

// How the quality family reads a stock's price over its earnings against its own past: not at all, at or below the
// median of its own last twelve quarters, or in the cheapest third of them.
public enum QualityValuation
{
    Off,
    AtOrBelowMedian,
    CheapestThird,
}

// What the quality family's rule reads: the score's floor stated in nine and read as a share of the checks that applied,
// how it reads the price against its own past, whether it reads the close above its 200-day average, the holdings it
// keeps, and whether a holding is also sold at the close of the night that first reads a filing dropping its score
// under the floor.
public sealed record QualitySettings(int ScoreFloorInNine, QualityValuation Valuation, bool Trend, int Holdings, bool SellOnFiling)
{
    public string Words => string.Create(
        CultureInfo.InvariantCulture,
        $"a score of at least {ScoreFloorInNine} in 9 of the checks that apply, {ValuationWords}{(Trend ? ", the close above its 200-day average" : string.Empty)}, the top {Holdings} kept, each sold at {(SellOnFiling ? "a reshuffle or on a filing dropping its score" : "a reshuffle")} where it no longer qualifies");

    string ValuationWords => Valuation switch
    {
        QualityValuation.Off => "its price over earnings not read",
        QualityValuation.AtOrBelowMedian => "its price over earnings at or below its own median of 12 quarters",
        _ => "its price over earnings in the cheapest third of its own 12 quarters",
    };

    // The setting as the history test's grid names it: the floor, the valuation, the trend, the book and the sale.
    public string Key => string.Join(
        "|",
        "floor=" + ScoreFloorInNine.ToString(CultureInfo.InvariantCulture),
        "valuation=" + Valuation switch { QualityValuation.Off => "off", QualityValuation.AtOrBelowMedian => "median", _ => "third" },
        "trend=" + (Trend ? "on" : "off"),
        "book=" + Holdings.ToString(CultureInfo.InvariantCulture),
        "sale=" + (SellOnFiling ? "filing" : "reshuffle"));

    // How many of the five dials stand apart from another setting's.
    public int Changes(QualitySettings other) =>
        (ScoreFloorInNine != other.ScoreFloorInNine ? 1 : 0) + (Valuation != other.Valuation ? 1 : 0) + (Trend != other.Trend ? 1 : 0)
        + (Holdings != other.Holdings ? 1 : 0) + (SellOnFiling != other.SellOnFiling ? 1 : 0);
}

// Whether a member qualifies, and why not where it does not.
public sealed record QualityAnswer(bool Passed, string? Reason);

// Quality at a fair price: the companies the fundamentals scorecard rates strongest that are not dear against their own
// history, kept as a set of holdings reshuffled on the first night of each month, after Piotroski (2000) for the score
// and its floor and Novy-Marx (2013) for profitable companies bought cheap.
// see: Quality at a fair price holds the companies the scorecard rates strongest that are not dear against their own history, reshuffled on each month's first session
public static class QualityRule
{
    public const string Name = "quality";

    public const string Label = "Quality at a fair price";

    public const string Description = "Holds the companies with the strongest fundamentals that are not expensive against their own history, reshuffled monthly.";

    // The valuation's place, the share of its own twelve quarters' earnings yields the newest stands above, a tie
    // counting half: at or above a half its price over earnings stands at or below its own median, and at or above two
    // thirds in the cheapest third of its own range.
    public const double MedianPlace = 0.5;

    public const double CheapestThirdPlace = 2.0 / 3;

    public static QualitySettings Starting { get; } = new(7, QualityValuation.AtOrBelowMedian, Trend: false, Holdings: 10, SellOnFiling: false);

    // The history test's dials, each level a setting may stand at.
    public static IReadOnlyList<int> Floors { get; } = [6, 7, 8];

    public static IReadOnlyList<QualityValuation> Valuations { get; } = [QualityValuation.Off, QualityValuation.AtOrBelowMedian, QualityValuation.CheapestThird];

    public static IReadOnlyList<bool> Trends { get; } = [false, true];

    public static IReadOnlyList<int> Books { get; } = [10, 20];

    public static IReadOnlyList<bool> FilingSales { get; } = [false, true];

    // The history test's grid, every crossing of the five dials: 72 settings, the starting one among them.
    public static IReadOnlyList<QualitySettings> Grid { get; } =
    [
        .. from floor in Floors
           from valuation in Valuations
           from trend in Trends
           from book in Books
           from sale in FilingSales
           select new QualitySettings(floor, valuation, trend, book, sale),
    ];

    public const string NoWord = "too few checks read for a score";
    public const string UnderTheFloor = "a score under the floor";
    public const string NotValued = "no price over earnings read";
    public const string TooDear = "dearer than its own median";
    public const string NotCheapest = "not in the cheapest third of its own range";
    public const string UnderTheAverage = "a close under its 200-day average";

    public static QualityAnswer Answer(int passed, int applied, double? place, bool? aboveAverage, QualitySettings settings) =>
        applied < FundamentalScorecard.FewestRead ? new(false, NoWord)
        : !FundamentalScorecard.Clears(passed, applied, settings.ScoreFloorInNine) ? new(false, UnderTheFloor)
        : settings.Valuation != QualityValuation.Off && place is null ? new(false, NotValued)
        : settings.Valuation == QualityValuation.AtOrBelowMedian && place < MedianPlace ? new(false, TooDear)
        : settings.Valuation == QualityValuation.CheapestThird && place < CheapestThirdPlace ? new(false, NotCheapest)
        : settings.Trend && aboveAverage != true ? new(false, UnderTheAverage)
        : new(true, null);

    // The order the qualifying members are taken in: the score as a share of the checks that applied, the higher first,
    // then the valuation's place, the cheaper against its own past first, then the ticker.
    public static IReadOnlyList<T> Ranked<T>(IEnumerable<T> qualifying, Func<T, (int Passed, int Applied, double? Place, string Ticker)> of) =>
        [.. qualifying
            .OrderByDescending(one => Share(of(one).Passed, of(one).Applied))
            .ThenByDescending(one => of(one).Place ?? double.MinValue)
            .ThenBy(one => of(one).Ticker, StringComparer.Ordinal)];

    public static double Share(int passed, int applied) => applied > 0 ? 1.0 * passed / applied : 0;

    // Whether a night reshuffles: the first the family reads, or the first it reads in a calendar month that read none.
    public static bool Reshuffles(DateOnly night, DateOnly? lastReshuffle) =>
        lastReshuffle is not { } last || last.Year != night.Year || last.Month != night.Month;

    // The session the holdings are next reshuffled on after a night: the next session on the exchange's calendar in a
    // month no reshuffle was read in; none past the end of the calendar's table.
    public static DateOnly? NextReshuffle(DateOnly night, DateOnly? lastReshuffle)
    {
        for (var day = night.AddDays(1); day <= ExchangeClosures.CoveredThrough; day = day.AddDays(1))
        {
            if (ExchangeClosures.IsSession(day) && Reshuffles(day, lastReshuffle))
            {
                return day;
            }
        }

        return null;
    }
}
