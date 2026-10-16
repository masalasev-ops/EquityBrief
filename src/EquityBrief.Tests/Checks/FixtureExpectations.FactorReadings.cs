using System.Text.Json;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.12: the published price and volume factors worked by hand over constructed bars, each at its
// window's edge with none one bar short of it; Kakushadze's four formulas that rank across the index worked by hand over
// constructed members, a member whose places hold one value reading none; three of the factors, Alpha#6 among them,
// worked by hand from a fixture name's bars on the fixture's night and read off the member readings the night stored;
// and every engine reading the whole catalogue, the factors among it.
// see: The published price and volume factors are ledger readings for the engines to search, and no factor chooses a stock
public partial class FixtureExpectations
{
    // Thirty constructed bars a day apart: these closes, each open half a dollar over the close before and the first's
    // half a dollar under its own close, each high a dollar over the larger of its open and close and each low a dollar
    // under the smaller, and the volume a million and ten thousand times seven times the bar's place, modulo eleven.
    static readonly decimal[] FactorCloses = [100, 102, 101, 103, 104, 102, 105, 107, 106, 108, 110, 109, 111, 113, 112, 114, 116, 115, 117, 119, 118, 120, 122, 121, 123, 125, 124, 126, 128, 127];

    static SweepBar[] FactorBars(decimal[] closes) =>
    [
        .. closes.Select((close, at) =>
        {
            var open = at == 0 ? close - 0.5m : closes[at - 1] + 0.5m;

            return new SweepBar(new DateOnly(2026, 1, 1).AddDays(at), Math.Max(open, close) + 1, Math.Min(open, close) - 1, close, 1_000_000 + (10_000 * ((at * 7) % 11)), open, close);
        }),
    ];

    static double Price(decimal value) => (double)value;

    static double SampleDeviation(IReadOnlyList<double> values)
    {
        var mean = values.Sum() / values.Count;

        return Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / (values.Count - 1));
    }

    static double Pearson(IReadOnlyList<double> one, IReadOnlyList<double> other)
    {
        var (a, b) = (one.Sum() / one.Count, other.Sum() / other.Count);

        return one.Zip(other).Sum(pair => (pair.First - a) * (pair.Second - b))
            / Math.Sqrt(one.Sum(value => Math.Pow(value - a, 2)) * other.Sum(value => Math.Pow(value - b, 2)));
    }

    [Fact]
    public void EachPublishedFactorIsWorkedByHandOverConstructedBarsAndNoneOneBarShortOfItsWindow()
    {
        var bars = FactorBars(FactorCloses);
        double Close(int at) => Price(FactorCloses[at]);
        double Volume(int at) => bars[at].Volume;
        double Daily(int at) => (Close(at) / Close(at - 1)) - 1;

        // The returns over 5 and 21 sessions: 127 over 123 and over 106, less one; the first bar to reach back five is the
        // sixth, 102 over 100, and one short reads none.
        Assert.Equal((127.0 / 123) - 1, FactorReadings.ReturnOver(bars, 29, 5)!.Value, 12);
        Assert.Equal(0.02, FactorReadings.ReturnOver(bars, 5, 5)!.Value, 12);
        Assert.Null(FactorReadings.ReturnOver(bars, 4, 5));
        Assert.Equal((127.0 / 106) - 1, FactorReadings.ReturnOver(bars, 29, 21)!.Value, 12);
        Assert.Null(FactorReadings.ReturnOver(bars, 20, 21));

        // The largest daily return of the 21 to the thirtieth bar is the tenth's, 108 over 106, two over 106.
        Assert.Equal(2.0 / 106, FactorReadings.LargestReturn(bars, 29, 21)!.Value, 12);
        Assert.Null(FactorReadings.LargestReturn(bars, 20, 21));

        // The deviation of those 21 returns, over twenty, and their skewness, the mean cubed deviation over the population
        // deviation cubed; 63 returns reach past the first bar and read none.
        double[] month = [.. Enumerable.Range(9, 21).Select(Daily)];
        var mean = month.Average();
        var population = Math.Sqrt(month.Average(value => Math.Pow(value - mean, 2)));

        Assert.Equal(SampleDeviation(month), FactorReadings.DeviationOver(bars, 29, 21)!.Value, 12);
        Assert.Null(FactorReadings.DeviationOver(bars, 20, 21));
        Assert.Equal(month.Average(value => Math.Pow(value - mean, 3)) / Math.Pow(population, 3), FactorReadings.Skewness(bars, 29, 21)!.Value, 9);
        Assert.Null(FactorReadings.Skewness(bars, 29, 63));

        // The close's place in each day's range over the ten bars to the thirtieth: a rising bar's close stands a dollar
        // under its high over a range two dollars wider than its move, and a falling bar's a dollar over its low.
        double Place(int at) => (Close(at) - Price(bars[at].Low)) / (Price(bars[at].High) - Price(bars[at].Low));

        Assert.Equal(Enumerable.Range(20, 10).Average(Place), FactorReadings.ClosePlace(bars, 29, 10)!.Value, 12);
        Assert.Null(FactorReadings.ClosePlace(bars, 8, 10));

        // The share of the twenty bars' volume traded on rising bars, the correlation of each bar's change in close with its
        // volume over them, and Amihud's illiquidity over 21 as the base-10 logarithm of the mean absolute return over the
        // dollars traded.
        int[] twenty = [.. Enumerable.Range(10, 20)];

        Assert.Equal(twenty.Where(at => Close(at) > Close(at - 1)).Sum(Volume) / twenty.Sum(Volume), FactorReadings.UpVolumeShare(bars, 29, 20)!.Value, 12);
        Assert.Null(FactorReadings.UpVolumeShare(bars, 19, 20));
        Assert.Equal(Pearson([.. twenty.Select(at => Close(at) - Close(at - 1))], [.. twenty.Select(Volume)]), FactorReadings.PriceVolumeCorrelation(bars, 29, 20)!.Value, 12);
        Assert.Null(FactorReadings.PriceVolumeCorrelation(bars, 19, 20));
        Assert.Equal(Math.Log10(Enumerable.Range(9, 21).Average(at => Math.Abs(Daily(at)) / (Close(at) * Volume(at)))), FactorReadings.Illiquidity(bars, 29, 21)!.Value, 12);
        Assert.Null(FactorReadings.Illiquidity(bars, 20, 21));

        // Each open half a dollar over the close before: the overnight return half a dollar over that close and the
        // intraday the close over the open, less one, each summed over the 21 bars; a bar holding no open reads none.
        var overnight = Enumerable.Range(9, 21).Sum(at => 0.5 / Close(at - 1));
        var intraday = Enumerable.Range(9, 21).Sum(at => (Close(at) / (Close(at - 1) + 0.5)) - 1);
        var noOpen = bars.ToArray();

        noOpen[25] = noOpen[25] with { Open = 0 };

        Assert.Equal(overnight - intraday, FactorReadings.OvernightLessIntraday(bars, 29, 21)!.Value, 12);
        Assert.Null(FactorReadings.OvernightLessIntraday(bars, 20, 21));
        Assert.Null(FactorReadings.OvernightLessIntraday(noOpen, 29, 21));

        // The lowest low of the ten bars to the thirtieth is the twenty-first's, a dollar under its close of 118.
        Assert.Equal(127.0 / 117, FactorReadings.LowRatio(bars, 29, 10)!.Value, 12);
        Assert.Null(FactorReadings.LowRatio(bars, 8, 10));
        Assert.Null(FactorReadings.LowRatio(bars, 29, 252));

        // The thirtieth bar fell from 128 after a rise, one down; the twenty-ninth rose twice after a fall; the second rose
        // and runs back to the first bar held, which closes on no close before it; a level close is nought.
        var level = FactorCloses.ToArray();

        level[29] = 128;

        Assert.Equal(-1.0, FactorReadings.Streak(bars, 29));
        Assert.Equal(2.0, FactorReadings.Streak(bars, 28));
        Assert.Null(FactorReadings.Streak(bars, 1));
        Assert.Equal(0.0, FactorReadings.Streak(FactorBars(level), 29));

        // On-balance volume over the twenty bars, each volume added on a rise and taken off on a fall: the least-squares
        // slope of its running sum on the bars' order, over the mean volume.
        var balance = new List<double>();
        var running = 0.0;

        foreach (var at in twenty)
        {
            running += Math.Sign(Close(at) - Close(at - 1)) * Volume(at);
            balance.Add(running);
        }

        double[] order = [.. Enumerable.Range(0, 20).Select(at => (double)at)];
        var slope = order.Zip(balance).Sum(pair => (pair.First - order.Average()) * (pair.Second - balance.Average())) / order.Sum(at => Math.Pow(at - order.Average(), 2));

        Assert.Equal(slope / twenty.Average(Volume), FactorReadings.VolumeSlope(bars, 29, 20)!.Value, 12);
        Assert.Null(FactorReadings.VolumeSlope(bars, 19, 20));

        // Alpha#1's Ts_ArgMax over the five bars to the thirtieth, 125 up, 124 down, 126 up, 128 up and 127 down: the
        // square of the close on a rise and of the deviation on a fall, the largest the fourth's 128; the twenty-fourth bar
        // is the first whose five reach twenty returns back.
        Assert.Equal(4.0, FactorReadings.ArgMaxValue(bars, 29));
        Assert.NotNull(FactorReadings.ArgMaxValue(bars, 24));
        Assert.Null(FactorReadings.ArgMaxValue(bars, 23));

        // Alpha#2's parts: the volume's logarithm 2 bars apart, 1,050,000 over 1,020,000 or 35 over 34, and the close less
        // the open over the open, 127 less 128.5 over 128.5.
        Assert.Equal(Math.Log(35.0 / 34), FactorReadings.VolumeChange(bars, 29)!.Value, 12);
        Assert.Null(FactorReadings.VolumeChange(bars, 1));
        Assert.Equal(-1.5 / 128.5, FactorReadings.IntradayReturn(bars, 29)!.Value, 12);

        // A place among the members, the share under and half the share level, its own among them; Ts_Rank the newest's
        // place among a series, 1 for the lowest and a tie the mean of its places.
        Assert.Equal(0.5, FactorReadings.Place(0.5, [1.0 / 6, 0.5, 0.5, 5.0 / 6]), 12);
        Assert.Equal(2.5, FactorReadings.TimeRank([0.5, 1.0 / 6, 0.5]), 12);
        Assert.Equal(3.0, FactorReadings.TimeRank([1, 2, 3]), 12);

        // Alpha#6, minus the correlation of the ten opens with the ten volumes; nine bars read none.
        double[] ten = [.. Enumerable.Range(20, 10)];

        Assert.Equal(-Pearson([.. ten.Select(at => Price(bars[(int)at].Open))], [.. ten.Select(at => Volume((int)at))]), FactorReadings.Alpha6(bars, 29)!.Value, 12);
        Assert.Null(FactorReadings.Alpha6(bars, 8));
    }

    [Fact]
    public void ABetaAResidualAndASectorsReturnAreWorkedByHandOverConstructedSeries()
    {
        // The index moves a per cent down, nothing and a per cent up in turn; the member moves twice as far, so the slope
        // of its returns on the index's is two and the residuals of that line are nothing; 21 returns need 22 closes.
        var index = new List<(DateOnly Session, double Close)>();
        var member = new List<SweepBar>();
        var (level, close) = (1000m, 100m);

        for (var at = 0; at < 30; at++)
        {
            var move = at == 0 ? 0m : 0.01m * ((at % 3) - 1);

            level *= 1 + move;
            close *= 1 + (2 * move);
            index.Add((new DateOnly(2026, 1, 1).AddDays(at), Price(level)));
            member.Add(new SweepBar(new DateOnly(2026, 1, 1).AddDays(at), close + 1, close - 1, close, 1_000_000, close, close));
        }

        Assert.Equal(2.0, FactorReadings.Beta(member, 29, index, 21)!.Value, 9);
        Assert.Null(FactorReadings.Beta(member, 20, index, 21));
        Assert.True(FactorReadings.ResidualDeviation(member, 29, index, 21)!.Value < 1e-12);
        Assert.Null(FactorReadings.Beta(member, 29, [.. index.Skip(10)], 21));

        // A fund's closes 50 and one more a day: the member's 21-session return less the fund's between the same two days,
        // 79 over 58 less one; a fund starting after the first of them reads none.
        var bars = FactorBars(FactorCloses);
        IReadOnlyList<(DateOnly Session, double Close)> fund = [.. Enumerable.Range(0, 30).Select(at => (new DateOnly(2026, 1, 1).AddDays(at), 50.0 + at))];

        Assert.Equal(((127.0 / 106) - 1) - ((79.0 / 58) - 1), FactorReadings.RelativeReturn(bars, 29, fund, 21)!.Value, 12);
        Assert.Null(FactorReadings.RelativeReturn(bars, 29, [.. fund.Skip(10)], 21));
    }

    // Three constructed members over 28 sessions, each pattern four sessions long: the first opens at 300 and closes a
    // dollar over it on the third and fourth sessions of each four, trading four million, and opens at 100 and closes a
    // dollar under on the first two, trading a million; the third the other way round; the second opens and closes at 200
    // and trades two million every session.
    static (SweepSeries[] Series, SweepBenchmark.Members Members) RankedMembers()
    {
        var calendar = Enumerable.Range(0, 28).Select(at => new DateOnly(2026, 1, 1).AddDays(at)).ToArray();
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);

        SweepBar Bar(int at, decimal open, decimal close, long volume) =>
            new(calendar[at], Math.Max(open, close) + 1, Math.Min(open, close) - 1, close, volume, open, close);

        SweepBar[] Pattern(bool high) =>
        [
            .. Enumerable.Range(0, 28).Select(at => (at % 4 >= 2) == high ? Bar(at, 300, 301, 4_000_000) : Bar(at, 100, 99, 1_000_000)),
        ];

        SweepName Named(string ticker, SweepBar[] bars) => new(ticker, bars, [(null, null)], []);

        var series = new[]
        {
            SweepColumns.Series(Named("AAA", Pattern(true)), sessionAt),
            SweepColumns.Series(Named("BBB", [.. Enumerable.Range(0, 28).Select(at => Bar(at, 200, 200, 2_000_000))]), sessionAt),
            SweepColumns.Series(Named("CCC", Pattern(false)), sessionAt),
        };

        return (series, SweepBenchmark.On(series, calendar.Length));
    }

    [Fact]
    public void KakushadzesRankedFormulasPlaceEachMemberAmongTheMembersOnEachSessionWorkedByHand()
    {
        var (series, members) = RankedMembers();
        var ranks = new FactorRanks(series, members);

        // Alpha#3 and Alpha#2: on the third and fourth sessions of each four the first member's open, volume, change in the
        // logarithm of volume and intraday return are all the highest of the three, a place of five sixths, and on the
        // first two the lowest, a sixth, so each pair of places moves together and minus their correlation is minus one.
        Assert.Equal(-1.0, ranks.Alpha3(0, 27)!.Value, 12);
        Assert.Equal(-1.0, ranks.Alpha2(0, 27)!.Value, 12);

        // The second member's places are a half on every session, one value throughout, so its correlations read none.
        Assert.Null(ranks.Alpha3(1, 27));
        Assert.Null(ranks.Alpha2(1, 27));

        // Alpha#4: the first member's low is the highest on the third and fourth sessions of each four and the lowest on the
        // first two; of its nine places to the twenty-eighth, the fourth of a four, five are five sixths and four a sixth,
        // so the newest's time-series rank is four under and five level, 4 + 3 = 7, and the formula minus seven.
        Assert.Equal(-7.0, ranks.Alpha4(0, 27)!.Value, 12);

        // Alpha#1: of the first member's five bars to the twenty-eighth, closing 301, 99, 99, 301 and 301, the square of the
        // close is largest on the first and the fourth and fifth, the older taken, place 1; the second's level closes take
        // place 1; the third's, 99, 301, 301, 99 and 99, place 2. Among 1, 1 and 2 the first and second stand a third and
        // the third five sixths, less a half: minus a sixth, minus a sixth and a third.
        Assert.Equal(-1.0 / 6, ranks.Alpha1(0, 27)!.Value, 12);
        Assert.Equal(-1.0 / 6, ranks.Alpha1(1, 27)!.Value, 12);
        Assert.Equal(1.0 / 3, ranks.Alpha1(2, 27)!.Value, 12);

        // A bar whose five do not reach twenty returns back holds no place, and nine bars read no time-series rank.
        Assert.Null(ranks.Alpha1(0, 23));
        Assert.Null(ranks.Alpha4(0, 7));
    }

    [Fact]
    public async Task ThreeFactorsAreWorkedByHandFromAFixtureNamesBarsOnItsNightAndReadOffTheReadingsItStored()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        var night = Query(store, "SELECT MAX(session_date) FROM ledger_reading;").Single();
        var stored = JsonSerializer.Deserialize<double?[]>(Query(store, $"SELECT readings FROM ledger_reading WHERE index_code = 'GSPC' AND session_date = '{night}' AND ticker = 'MSFT';").Single())!;
        var rows = Query(store, $"SELECT open || '|' || close || '|' || volume FROM bar WHERE ticker = 'MSFT' AND session_date <= '{night}' ORDER BY session_date;")
            .Select(row => row.Split('|'))
            .Select(parts => (Open: double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), Close: double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), Volume: double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();
        double Read(string column) => stored[LedgerReadings.IndexOf(column)]!.Value;

        // The return over 21 sessions, the newest close over the close 21 bars before, less one; the sample deviation of the
        // 21 daily returns to the night; and Alpha#6, minus the correlation of the ten opens with the ten volumes.
        var last = rows.Length - 1;
        double Daily(int at) => (rows[at].Close / rows[at - 1].Close) - 1;

        Assert.Equal((rows[last].Close / rows[last - 21].Close) - 1, Read("return_month"), 9);
        Assert.Equal(SampleDeviation([.. Enumerable.Range(last - 20, 21).Select(Daily)]), Read("volatility_month"), 9);
        Assert.Equal(-Pearson([.. rows[^10..].Select(row => row.Open)], [.. rows[^10..].Select(row => row.Volume)]), Read("alpha_six"), 9);
    }

    [Fact]
    public void EveryEngineReadsTheWholeCatalogueTheNewFactorsAmongIt()
    {
        // Winners against losers: forty listings over ten nights, every reading held level but the last factor, which is
        // one on the winners and nought on the losers; the search's strongest condition reads it, and its spreads read one
        // reading of the catalogue each.
        var alpha = LedgerReadings.IndexOf("alpha_six");
        ConditionUnit[] units =
        [
            .. Enumerable.Range(0, 40).Select(at =>
            {
                var readings = new double?[LedgerReadings.Count];

                Array.Fill(readings, 1);
                readings[alpha] = at % 2;

                return new ConditionUnit(at / 4, at % 2 == 1 ? 1.0 : -1.0, readings);
            }),
        ];

        Assert.Equal("alpha_six", ConditionSearch.Search(units, 2)[0].Conditions[0].Column);
        Assert.Equal(Enumerable.Range(0, LedgerReadings.Count), ConditionSearch.Spreads(units).Select(spread => spread.Reading));

        // The learned score and its challenger weigh every reading but the family's own and the one read ahead, the factors
        // among them, and a score over six hundred and eighty setups whose edges move with the last factor alone weighs it.
        Assert.Equal(
            Enumerable.Range(0, LedgerReadings.Count).Where(at => !RidgeScore.FamilyOwn.Contains(LedgerReadings.All[at].Column) && !RidgeScore.LookAhead.Contains(LedgerReadings.All[at].Column)),
            RidgeScore.Weighable);
        Assert.Contains(alpha, RidgeScore.Weighable);

        var rows = new ScoreRows();

        for (var at = 0; at < 680; at++)
        {
            rows.Add("GSPC", ScoreSession(at), ScoreSession(at + 10), Weighed(1, ("alpha_six", at % 4)), 2.0 * (at % 4), 1);
        }

        Assert.Equal(["alpha_six"], Fitted(rows).Readings.Select(reading => LedgerReadings.All[reading].Column));

        // No engine's source names a reading list of its own: the catalogue's columns stand as words in the engines' files
        // only in the learned score's three named lists, the family's own readings, the one read ahead and the business
        // readings, fourteen in all.
        var columns = LedgerReadings.All.Select(reading => reading.Column).ToHashSet(StringComparer.Ordinal);
        var named = Repository.SourceFiles()
            .Where(file => file.Contains(Path.Combine("EquityBrief.Core", "Loop"), StringComparison.Ordinal) || file.Contains(Path.Combine("EquityBrief.Worker", "Loop"), StringComparison.Ordinal))
            .SelectMany(file => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "\"([a-z_]+)\"").Where(match => columns.Contains(match.Groups[1].Value)).Select(match => (File: Path.GetFileName(file), Column: match.Groups[1].Value)))
            .ToArray();

        Assert.Equal(14, named.Length);
        Assert.All(named, one => Assert.Equal("RidgeScore.cs", one.File));
        Assert.Equal(
            [.. RidgeScore.FamilyOwn, .. RidgeScore.LookAhead, .. RidgeScore.Business],
            named.Select(one => one.Column));
    }
}
