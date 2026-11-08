namespace EquityBrief.Core.History;

// One year of an index as a first-year rule reads it: the members it counts and how many of them read what the rule asks.
public sealed record YearShare(int Year, int Members, int Read)
{
    public double Share => Members == 0 ? 0 : (double)Read / Members;
}

// A filing-based family's first test year on an index as measured: the first year enough members read its score, the test
// year three learning years after it, the plan's estimate beside it and whether the two stand more than a year apart.
public sealed record FamilyFirstYear(string Family, string Index, int? FirstScored, int? FirstTest, int Estimate)
{
    public bool Apart => FirstTest is { } measured && LongFirstYears.Apart(measured, Estimate);

    // On the operator's answer of 2026-11-07 a measured year stands as measured, both years stated, and the work goes on; a
    // year no index reached stands nowhere.
    public bool Stands => FirstTest is not null;
}

// The drift's first test year on an index: the first year surprises cover enough of its since-gone members, or the year it
// is judged from where none does, every year then labelled mostly survivors.
public sealed record DriftFirstYear(string Index, int Year, bool Reached, IReadOnlyList<YearShare> Coverage)
{
    public string? Label => Reached ? null : LongFirstYears.MostlySurvivors;
}

// The first test years the long history reads each family from.
//
// A filing-based family is first tested three learning years after the first year at least 90 in every 100 of an index's
// members on its first session read the family's score; a measured year differing from the plan's estimate by more than a
// year is used as measured with both years stated, on the operator's answer of 2026-11-07. The drift is read from the first
// year its surprises cover at least 80 in every 100 of the index's since-gone members, and where no year does, from 2019 with
// every year labelled mostly survivors. The long test refuses a family on an index whose year does not stand.
// see: Each filing-based family's first test year is three learning years after the first year 90 in every 100 members read its score
// see: A rule reading a surprise is judged from the first year surprises cover 80% of an index's since-gone members, the years before printed beside it
// see: The S&P 500 drift is judged from 2019 where no year's surprises reach the coverage its rule asks and every year is labelled mostly survivors
public static class LongFirstYears
{
    // The share of an index's members on a year's first session that must read the family's score.
    public const double ScoreShare = 0.90;

    // The years a family learns on before its first test year.
    public const int LearningYears = 3;

    // The share of an index's since-gone members a year's surprises must cover.
    public const double SurpriseShare = 0.80;

    // The year the drift is judged from where no year's surprises reach the share.
    public const int DriftFallback = 2019;

    // The widest a measured first year may stand from its estimate without both being stated as apart.
    public const int EstimateWithin = 1;

    public const string MostlySurvivors = "mostly survivors";

    public const string Fundamentals = "Fundamentals";
    public const string Heavyweights = "heavyweights";
    public const string Quality = "quality";
    public const string Drift = "drift";

    // The plan's estimates of each filing-based family's first test year, by index.
    public static IReadOnlyDictionary<(string Family, string Index), int> Estimates { get; } = new Dictionary<(string, string), int>
    {
        [(Fundamentals, "GSPC")] = 2015,
        [(Fundamentals, "MID")] = 2017,
        [(Fundamentals, "SML")] = 2017,
        [(Heavyweights, "GSPC")] = 2015,
        [(Heavyweights, "MID")] = 2017,
        [(Heavyweights, "SML")] = 2017,
        [(Quality, "GSPC")] = 2016,
        [(Quality, "MID")] = 2018,
        [(Quality, "SML")] = 2018,
    };

    public static bool Apart(int measured, int estimate) => Math.Abs(measured - estimate) > EstimateWithin;

    // The first year, in order, whose share reaches the given one; none where no year does.
    public static int? FirstReached(IEnumerable<YearShare> years, double share) =>
        years.Where(year => year.Members > 0 && year.Read >= share * year.Members).OrderBy(year => year.Year).Select(year => (int?)year.Year).FirstOrDefault();

    // A filing-based family's first test year on an index from its years' shares.
    public static FamilyFirstYear FamilyYear(string family, string index, IEnumerable<YearShare> years)
    {
        var scored = FirstReached(years, ScoreShare);

        return new FamilyFirstYear(family, index, scored, scored + LearningYears, Estimates[(family, index)]);
    }

    // The drift's first test year on an index from its years' coverage of since-gone members.
    public static DriftFirstYear DriftYear(string index, IReadOnlyList<YearShare> coverage) =>
        FirstReached(coverage, SurpriseShare) is { } year
            ? new DriftFirstYear(index, year, true, coverage)
            : new DriftFirstYear(index, DriftFallback, false, coverage);

    // Why the long test refuses a family on an index, none where its year stands.
    public static string? Refusal(IReadOnlyCollection<FamilyFirstYear> measured, string family, string index) =>
        measured.FirstOrDefault(one => one.Family == family && one.Index == index) is { Stands: true }
            ? null
            : $"the long test refuses {family} on {index}: its first test year has not been measured to stand";

    // A since-gone code with no surprise, by why it left: failed where its filer reported a bankruptcy, bought out where a
    // filing ends its history, dropped where its prices go on past the day it left the index, and otherwise not read.
    public static string WhyLeft(bool bankruptcy, bool endedByFiling, bool pricedAfter) =>
        bankruptcy ? "failed" : endedByFiling ? "bought out" : pricedAfter ? "dropped" : "not read";

    // The calendar's code for a store's older code: its suffix marking an older listing taken off.
    public static string CalendarCode(string ticker)
    {
        var at = ticker.IndexOf("_old", StringComparison.Ordinal);

        return at > 0 ? ticker[..at] : ticker;
    }
}
