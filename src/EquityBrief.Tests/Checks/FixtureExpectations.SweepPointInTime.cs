using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// The point-in-time check: over the fixture's two-night store every name-session the sweep reads is rebuilt with
// the night's own components and found the same, a constructed seed difference is caught and named, and the
// sample draws its counts and adds the live list's name-sessions.
public partial class FixtureExpectations
{
    [Fact]
    public async Task ThePointInTimeCheckFindsNoDifferenceOverTheFixtureAndCatchesASeedDifferenceConstructed()
    {
        using var store = await FixtureExpectations.WithTwoNights();

        var inputs = await new SweepHistory(store.DatabaseFile).ReadAsync(FixtureNight);

        SweepRelease(store);

        var sessionAt = inputs.Sessions.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var series = inputs.Names.Select(name => SweepColumns.Series(name, sessionAt)).ToArray();
        var scratch = Path.Combine(store.Root, "point-in-time");

        // Every name holding a bar on either fixture night: 4 names on each of 2 nights.
        var samples = new List<(int Name, int Bar)>();

        foreach (var night in new[] { EarlierNight, FixtureNight })
        {
            for (var name = 0; name < series.Length; name++)
            {
                var bar = Array.FindIndex(series[name].Bars, one => one.Session == night);

                if (bar >= 0)
                {
                    samples.Add((name, bar));
                }
            }
        }

        Assert.Equal(8, samples.Count);

        var result = await new SweepPointInTime(scratch, 2).CheckAsync(series, samples, inputs.LiveListed.Count, CancellationToken.None);

        Assert.True(result.Clean, string.Join("\n", result.Differences.Select(difference => FormattableString.Invariant($"{difference.Ticker} {difference.Session:yyyy-MM-dd} {difference.What}: sweep {difference.Sweep}, night {difference.Night}"))));
        Assert.Equal((8, 8), (result.Samples, result.Compared));

        // The scratch stores are under the machine's temporary folder and are deleted once released; a handle
        // the pool lets go of late leaves an empty file at most, and nothing of the operator's is there.
        Assert.DoesNotContain(store.DatabaseFile, Directory.Exists(scratch) ? Directory.GetFiles(scratch, "*", SearchOption.AllDirectories) : []);

        // The live list's name-sessions the history holds are read from the filter's rows: the fixture's passes.
        Assert.Equal(SweepRows(store, "SELECT COUNT(*) FROM gate_result WHERE passed = 1;").Single(), inputs.LiveListed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // A seed difference constructed: the sweep's ATR on one name's bar nudged by a hundredth reads as a
        // difference on that name and session alone, named atr14 with both figures; the bands, which read the
        // ATR, differ with it.
        var (name0, bar0) = samples[0];
        var nudged = series[name0].Atr.ToArray();

        nudged[bar0] += 0.01;

        var altered = new SweepSeries
        {
            Name = series[name0].Name,
            Bars = series[name0].Bars,
            SessionAt = series[name0].SessionAt,
            WindowStart = series[name0].WindowStart,
            Sma20 = series[name0].Sma20,
            Sma50 = series[name0].Sma50,
            Sma200 = series[name0].Sma200,
            Atr = nudged,
            Rsi = series[name0].Rsi,
            Volume50 = series[name0].Volume50,
            Swings = series[name0].Swings,
            Confirmed = series[name0].Confirmed,
            Member = series[name0].Member,
            Gap = series[name0].Gap,
            Label = series[name0].Label,
            Uptrend = series[name0].Uptrend,
            Return63 = series[name0].Return63,
            Return126 = series[name0].Return126,
            ReturnTwelveLessOne = series[name0].ReturnTwelveLessOne,
            Depth = series[name0].Depth,
            DryUp = series[name0].DryUp,
            High252 = series[name0].High252,
            Tightness = series[name0].Tightness,
            VolumeRatio = series[name0].VolumeRatio,
            RsiUp = series[name0].RsiUp,
            SinceHigh = series[name0].SinceHigh,
            GapDown = series[name0].GapDown,
            RsiLow = series[name0].RsiLow,
            SurpriseBar = series[name0].SurpriseBar,
            NewestSurprise = series[name0].NewestSurprise,
        };
        var differences = await SweepPointInTime.RebuildAsync(altered, bar0, Path.Combine(store.Root, "seed"));

        Assert.Contains(differences, difference => difference.What == "atr14" && difference.Ticker == series[name0].Name.Ticker && difference.Session == series[name0].Bars[bar0].Session);
        Assert.All(differences, difference => Assert.Equal(series[name0].Name.Ticker, difference.Ticker));

        var atr = differences.Single(difference => difference.What == "atr14");

        Assert.Equal(nudged[bar0].ToString("R", System.Globalization.CultureInfo.InvariantCulture), atr.Sweep);
        Assert.Equal(series[name0].Atr[bar0].ToString("R", System.Globalization.CultureInfo.InvariantCulture), atr.Night);

        // The stopped page lists every difference and proposes nothing.
        var page = SweepReport.StoppedAtPointInTime(new SweepRunner.State { StoppedAtPointInTime = true }, new PointInTimeResult(1, 0, 1, differences, 1.0));

        Assert.Contains("the run stopped before stage 1", page, StringComparison.Ordinal);
        Assert.Contains("atr14", page, StringComparison.Ordinal);
        Assert.DoesNotContain("register --freeze", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSampleDrawsTwentyFiveNameSessionsAYearThirteenOfThemCandidatesAndAddsTheLiveLists()
    {
        // Two hundred names of 2,000 sessions from 2018, so every scored year holds member-sessions with a year
        // of bars, and constructed candidates on a hundred sessions a year.
        var calendar = new List<DateOnly>();

        for (var day = new DateOnly(2018, 1, 2); calendar.Count < 2_200; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                calendar.Add(day);
            }
        }

        var series = new List<SweepSeries>();

        for (var name = 0; name < 200; name++)
        {
            var bars = calendar.Select((session, at) => new SweepBar(session, 101, 99, 100, 1_000, 100)).ToArray();
            var count = bars.Length;
            var windowStart = SweepWindows.WindowStarts(bars);

            series.Add(new SweepSeries
            {
                Name = new SweepName(FormattableString.Invariant($"N{name:000}"), bars, [(null, null)], []),
                Bars = bars,
                SessionAt = [.. Enumerable.Range(0, count)],
                WindowStart = windowStart,
                Sma20 = new double[count],
                Sma50 = new double[count],
                Sma200 = new double[count],
                Atr = new double[count],
                Rsi = new double[count],
                Volume50 = new double[count],
                Swings = [],
                Confirmed = new int[count],
                Member = [.. Enumerable.Repeat(true, count)],
                Gap = new bool[count],
                Label = new string[count],
                Uptrend = new byte[count],
                Return63 = new double[count],
                Return126 = new double[count],
                ReturnTwelveLessOne = new double[count],
                Depth = new double[count * 3],
                DryUp = new double[count * 3],
                High252 = new double[count],
                Tightness = new double[count],
                VolumeRatio = new double[count],
                RsiUp = new bool[count],
                SinceHigh = new int[count * 3],
                GapDown = new double[count * 3],
                RsiLow = new double[count * 3],
                SurpriseBar = [],
                NewestSurprise = new int[count],
            });
        }

        var firstScored = calendar.FindIndex(session => session >= SweepColumns.FirstScored);
        var candidates = new List<SweepCandidate>();
        var random = new Random(1);

        for (var year = 0; year < SweepFigures.Years; year++)
        {
            for (var at = 0; at < 100; at++)
            {
                var session = calendar.FindIndex(one => one.Year == SweepColumns.FirstScored.Year + year) + random.Next(200);

                candidates.Add(new SweepCandidate { Name = random.Next(200), Session = session, Year = year });
            }
        }

        var live = new[] { ("N005", calendar[^1]), ("N006", calendar[^2]), ("ZZZ", calendar[^3]) };
        var sample = SweepPointInTime.Sample(series, candidates, calendar, live, SweepSearch.Seed);
        var candidateKeys = candidates.Select(candidate => (candidate.Name, candidate.Session)).ToHashSet();

        // 25 a year over 8 years, 13 of them candidates, plus the two live listings the history holds; ZZZ, a
        // name the history does not hold, is left out. A draw landing on a name-session already taken is not
        // drawn again, so the counts are at most these and, over 200 names, all of them here.
        Assert.Equal((8 * SweepPointInTime.SamplesAYear) + 2, sample.Count);
        Assert.Equal(8 * SweepPointInTime.CandidateSamplesAYear, sample.Count(pair => candidateKeys.Contains((pair.Name, series[pair.Name].SessionAt[pair.Bar]))));
        Assert.Contains((5, calendar.Count - 1), sample);
        Assert.Contains((6, calendar.Count - 2), sample);
        Assert.All(sample.Take(8 * SweepPointInTime.SamplesAYear), pair => Assert.True(pair.Bar - series[pair.Name].WindowStart[pair.Bar] >= SweepPointInTime.YearOfBars || candidateKeys.Contains((pair.Name, pair.Bar))));

        // The same seed draws the same sample.
        Assert.Equal(sample, SweepPointInTime.Sample(series, candidates, calendar, live, SweepSearch.Seed));
        Assert.NotEqual(sample, SweepPointInTime.Sample(series, candidates, calendar, live, SweepSearch.Seed + 1));
    }
}
