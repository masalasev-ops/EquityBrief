namespace EquityBrief.Core.History;

// One quarter end of one fund against the stop: its holdings and their value, and those matched and priced.
public sealed record LongCoverage(string Index, DateOnly Period, int Holdings, int Matched, decimal Value, decimal MatchedValue)
{
    public double HoldingsShare => Holdings == 0 ? 0 : 1.0 * Matched / Holdings;

    public decimal ValueShare => Value == 0m ? 0m : MatchedValue / Value;

    public bool Stands => HoldingsShare >= LongFloors.HoldingsStop && ValueShare >= LongFloors.ValueStop;

    // Whether the quarter end's figures carry the lower coverage label.
    public bool Lower => LongFloors.IsLower(Index, Period);
}

// A holding matched to none whose value a share fell by half or more over its last two quarter ends: a company that may have
// failed, listed by name and checked by hand against the SEC's filings.
public sealed record LongFall(string Index, string Name, DateOnly Before, decimal BeforePrice, DateOnly Last, decimal LastPrice);

// A company the long history leaves out on the operator's word, how every figure leaving it out names it, and what was found.
public sealed record LongLeftOut(string Name, string NamedAs, string Found);

// What the long history's membership is held to before anything is written, and the stops: each quarter end stands at the
// coverage it reached and stops under 90 in every 100 of its holdings or of their value; the S&P 400's and 600's figures from
// 2004 to 2008 carry the lower coverage label; a fall by half priced under no code stops the run but where the operator left
// the company out by name.
// see: The funds' older schedules are read from the trust's N-Q and N-CSR filings at each quarter end from 2004-12-31 to 2018-09-30
// see: Countrywide is left out of the long history on the operator's word and named in every figure that leaves it out
// see: Each quarter end of the long history stands at the coverage it reached and stops under 90 in every 100 of its holdings or their value
// see: The S&P 400's and 600's figures for 2004 to 2008 carry the lower coverage label
// see: The failed companies the long history cannot price are left out by name and the run goes on
public static class LongFloors
{
    public const double HoldingsStop = 0.90;

    public const decimal ValueStop = 0.90m;

    // The label every figure of the S&P 400 and 600 from 2004 to 2008 carries.
    public const string LowerLabel = "lower coverage: about 90% of holdings matched";

    public const int LowerFrom = 2004;

    public const int LowerThrough = 2008;

    public static bool IsLower(string index, DateOnly period) => index is "MID" or "SML" && period.Year is >= LowerFrom and <= LowerThrough;

    // A value a share at or under this share of the one a quarter end before is a fall by half.
    public const decimal FellBy = 0.5m;

    public const string BoughtOut = "bought out";

    public const string Failed = "failed";

    public const string Unpriced = "unpriced";

    // The companies left out by name on the operator's word, each named in every figure that leaves it out.
    public static IReadOnlyList<LongLeftOut> LeftOut { get; } =
    [
        new("Countrywide Financial Corp.", BoughtOut, "bought by Bank of America with its own stock in July 2008, its 8-K of 2008-07-08 carrying items 2.01 and 5.01, accession 0000950144-08-005376 of filer 25191; the provider holds no prices for it under any code tried"),
        new("Ambac Financial Group Inc.", Failed, "its 8-K of 2010-11-10 carries item 1.03, accession 0001193125-10-256201 of filer 874501; the provider's ABK closes in its day files are another listing's, 0.33 at 2004-12-31, and ABK's older forms answer nothing"),
        new("Building Materials Holding Corp.", Failed, "filer 1046356's last annual report was filed 2009-04-15, an 8-K of 2009-09-02 carries item 3.01, a delisting notice, and its registration ended by Form 15 on 2010-01-06; the provider answers nothing for BMHC or its older forms"),
        new("Anchor BanCorp Wisconsin Inc.", Failed, "its 8-K of 2013-08-13 carries item 1.03, accession 0001341004-13-000851 of filer 885322; the provider's ABCW holds prices only from 2014-12, its stock issued after, and ABCW's older forms answer nothing"),
        new("Sterling Financial Corp. (Washington)", Failed, "filer 891106's 8-Ks of 2010-04-15 and 2010-12-10 carry item 3.03, a material change to its holders' rights, and its 8-K of 2014-04-21 item 5.01; no filing carries item 1.03"),
        new("Adeptus Health Inc. Class A", Failed, "its 8-K of 2017-04-20 carries item 1.03, accession 0001104659-17-024683 of filer 1602367; the provider's ADPT is Adaptive Biotechnologies, and ADPT's older forms answer nothing"),
        new("Belo Corp. Class A", Unpriced, "survived and merged by its 8-K of 2013-12-24 carrying items 2.01 and 5.01, accession 0001193125-13-483827 of filer 356080; the provider's BLC closes in its day files are another listing's, unchanged for quarters at a time, and BLC's older form answers nothing"),
    ];

    public static LongLeftOut? LeftOutAs(string name) => LeftOut.FirstOrDefault(company => company.Name == name);

    // Each schedule's figures against the stop.
    public static IReadOnlyList<LongCoverage> Coverage(IReadOnlyList<LongSchedule> schedules, Func<LongSchedule, int, bool> matched) =>
    [
        .. schedules.OrderBy(schedule => schedule.Index, StringComparer.Ordinal).ThenBy(schedule => schedule.Period).Select(schedule =>
        {
            var (count, value) = (0, 0m);

            for (var place = 0; place < schedule.Holdings.Count; place++)
            {
                if (matched(schedule, place))
                {
                    count++;
                    value += schedule.Holdings[place].Value ?? 0m;
                }
            }

            return new LongCoverage(schedule.Index, schedule.Period, schedule.Holdings.Count, count, schedule.Holdings.Sum(holding => holding.Value ?? 0m), value);
        }),
    ];

    // Each name a fund held whose last two quarter ends both match it to none and whose value a share fell by half between them.
    public static IReadOnlyList<LongFall> Falls(IReadOnlyList<LongSchedule> schedules, Func<LongSchedule, int, bool> matched) =>
    [
        .. schedules
            .SelectMany(schedule => schedule.Holdings.Select((holding, place) => (Schedule: schedule, Place: place, Holding: holding)))
            .GroupBy(one => (one.Schedule.Index, one.Holding.Name))
            .Select(group => group.OrderBy(one => one.Schedule.Period).ToArray())
            .Where(held => held.Length >= 2)
            .Select(held => (Before: held[^2], Last: held[^1]))
            .Where(pair => !matched(pair.Before.Schedule, pair.Before.Place) && !matched(pair.Last.Schedule, pair.Last.Place)
                && pair.Before.Holding.ValuePerShare is { } before && pair.Last.Holding.ValuePerShare is { } last && before > 0m && last <= FellBy * before)
            .Select(pair => new LongFall(pair.Last.Schedule.Index, pair.Last.Holding.Name, pair.Before.Schedule.Period, pair.Before.Holding.ValuePerShare!.Value, pair.Last.Schedule.Period, pair.Last.Holding.ValuePerShare!.Value))
            .OrderBy(fall => fall.Index, StringComparer.Ordinal)
            .ThenBy(fall => fall.Name, StringComparer.Ordinal),
    ];

    // Why the run stops, none where nothing stops it: each quarter end under 90 in every 100 of its holdings or their value, and
    // each fall by half priced under no code that the operator has not left out by name.
    public static IReadOnlyList<string> Stops(IReadOnlyList<LongCoverage> coverage, IReadOnlyList<LongFall> falls) =>
    [
        .. coverage.Where(quarter => !quarter.Stands).Select(quarter => FormattableString.Invariant(
            $"{quarter.Index} {quarter.Period:yyyy-MM-dd}: {quarter.Matched} of {quarter.Holdings} holdings matched and priced, {quarter.HoldingsShare:P1} and {quarter.ValueShare:P1} of their value, under the stop of {HoldingsStop:P0} of either")),
        .. falls.Where(fall => LeftOutAs(fall.Name) is null).Select(fall => FormattableString.Invariant(
            $"{fall.Index} {fall.Name}: a value a share of {fall.BeforePrice:0.00} at {fall.Before:yyyy-MM-dd} and {fall.LastPrice:0.00} at {fall.Last:yyyy-MM-dd}, priced under no code and not left out by the operator")),
    ];
}
