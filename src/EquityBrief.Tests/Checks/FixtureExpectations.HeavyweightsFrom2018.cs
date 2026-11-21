using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.8: the heavyweights' test over the long history reads a fund's closes from the first year the long
// pull holds them, the standing pull's after its last, so the lay sets the fund's close on a session before the standing
// pull's first, worked over a constructed store; both long readers of the heavyweights take that lay; on a session before
// today's eleven the lay reads each member's sector as it stood off constructed headings; a book's chosen years open on the
// family's first test year on the S&P 400 and 600 and in October 2018 on the S&P 500, its judged years staying from 2023; and
// no source 24.8 changes is one a rule's version pins or one that writes the register.
// see: The monthly run's last step judges every registered rule over the long history from each family's own first test year, capped at three hours
public partial class FixtureExpectations
{
    // Every shipped source 24.8 changes.
    internal static readonly string[] HeavyweightsFrom2018Sources =
    [
        "src/EquityBrief.Worker/Loop/HeavyweightLay.cs",
        "src/EquityBrief.Worker/Loop/WalkForwardTester.cs",
        "src/EquityBrief.Worker/Loop/WalkForwardTester.Books.cs",
        "src/EquityBrief.Worker/Loop/RuleTests.cs",
        "src/EquityBrief.Worker/Loop/BookRisk.cs",
        "src/EquityBrief.Worker/History/LongHistoryReader.cs",
    ];

    // XLK in the long pull on two sessions of 2017 and one of 2018, the standing pull holding that 2018 session again and one
    // after it: the long series reads 50, 51, 60 and 61, the overlap once off the long pull, where the standing pull alone
    // starts at 2018; laid on the four sessions, the fund's closes stand on both 2017 sessions only from the long series.
    [Fact]
    public async Task TheHeavyweightsLongTestReadsAConstructedFundFromItsFirstYearHeldAndChoosesFromOctober2018()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(@"
            INSERT INTO long_market_bar (series, session_date, open, high, low, close, run_id) VALUES
                ('XLK', '2017-01-03', '50', '50', '50', '50', 'long-prices-1'),
                ('XLK', '2017-01-04', '51', '51', '51', '51', 'long-prices-1'),
                ('XLK', '2018-01-02', '60', '60', '60', '60', 'long-prices-1');
            INSERT INTO pulled_market_bar (series, session_date, open, high, low, close, pull) VALUES
                ('XLK', '2018-01-02', '999', '999', '999', '999', 'pull-1'),
                ('XLK', '2018-01-03', '61', '61', '61', '61', 'pull-1');");

        var through = new DateOnly(2018, 1, 3);
        var standing = Assert.Single(await new SweepHistory(store.DatabaseFile).SeriesOfAsync(["XLK"], through));
        var joined = Assert.Single(await WalkForwardTester.LongSeriesAsync(new LongHistoryReader(store.DatabaseFile), new SweepHistory(store.DatabaseFile), ["XLK"], through));

        Assert.Equal(new DateOnly(2018, 1, 2), standing.Closes[0].Session);
        Assert.Equal([50.0, 51.0, 60.0, 61.0], joined.Closes.Select(one => one.Close));

        DateOnly[] calendar = [new(2017, 1, 3), new(2017, 1, 4), new(2018, 1, 2), new(2018, 1, 3)];
        var inputs = new SweepHistoryInputs(through, calendar, [], 0, 0, 0, 0, string.Empty);
        var none = new HeavyweightHistory(
            new Dictionary<string, (string? Cik, string? Sector)>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<FiledCount>>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<FiledSplit>>(StringComparer.Ordinal),
            [],
            []);
        var (fromStanding, _) = HeavyweightSweep.Lay(inputs, none, standing, new HashSet<int>(), 0);
        var (fromLong, _) = HeavyweightSweep.Lay(inputs, none, joined, new HashSet<int>(), 0);

        Assert.True(double.IsNaN(fromStanding.Index[0]) && double.IsNaN(fromStanding.Index[1]));
        Assert.Equal([50.0, 51.0, 60.0, 61.0], fromLong.Index);

        // The books' long test and the monthly per-rule long test both lay the heavyweights over the long history.
        var tester = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Loop", "WalkForwardTester.cs"));
        var books = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Loop", "WalkForwardTester.Books.cs"));

        Assert.Contains("HeavyweightLay.ReadLongAsync(read, history, longHistory, through, output.WriteLine, cancellation, large ? null : await LargeReadAsync())", tester, StringComparison.Ordinal);
        Assert.Contains("HeavyweightLay.ReadLongAsync(read, history, longHistory, through, output.WriteLine, cancellation)", books, StringComparison.Ordinal);

        // A heavyweights book on the S&P 400 chooses from 2015 and on the 600 from 2016, their family's first test years, and on
        // the S&P 500, whose first test year is the judged years' own, from October 2018; each is judged from 2023, and another
        // family's years stand as they were.
        Assert.Equal(new DateOnly(2018, 10, 1), RuleTests.BookChosenFrom(HeavyweightRule.Name, "GSPC"));
        Assert.Equal("from October 2018 to 2022", RuleTests.BookChosenYears(HeavyweightRule.Name, "GSPC"));
        Assert.Equal((2015, 2023), RuleTests.BookSplit(HeavyweightRule.Name, "MID"));
        Assert.Equal(new DateOnly(2016, 1, 1), RuleTests.BookChosenFrom(HeavyweightRule.Name, "SML"));
        Assert.Equal("from 2016 to 2022", RuleTests.BookChosenYears(HeavyweightRule.Name, "SML"));
        Assert.Equal(new DateOnly(2016, 1, 1), RuleTests.BookChosenFrom(QualityRule.Name, "GSPC"));
        Assert.Equal("from 2016 to 2018", RuleTests.BookChosenYears(QualityRule.Name, "GSPC"));
    }

    // Four codes' headings on a constructed store, each as the funds' schedules filed them: a real estate trust read under
    // Financials before 2016-09-19 and under Real Estate after; a media company under Consumer Discretionary and an internet
    // one under Information Technology before 2018-09-24; a heading the map names nothing for reading none. Laid on a session
    // before today's eleven a member takes its sector as it stood, and on one after keeps the sector it was laid with.
    [Fact]
    public async Task TheHeavyweightsLongLayReadsEachSectorAsItStoodBeforeTodaysElevenFromConstructedHeadings()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(@"
            INSERT INTO long_holding (index_code, period, holding, name, value_usd, ticker, matched_by, run_id, industry) VALUES
                ('MID', '2015-12-31', 0, 'Trust Co.', '10', 'TRST', 'name', 'long-members-1', 'Real Estate Investment Trusts'),
                ('MID', '2017-03-31', 0, 'Trust Co.', '10', 'TRST', 'name', 'long-members-1', 'REITs'),
                ('MID', '2015-12-31', 1, 'Media Co.', '10', 'MDIA', 'name', 'long-members-1', 'Media'),
                ('MID', '2015-12-31', 2, 'Web Co.', '10', 'WEBB', 'name', 'long-members-1', 'Internet Software & Services'),
                ('MID', '2015-12-31', 3, 'Odd Co.', '10', 'ODDD', 'name', 'long-members-1', 'Miscellaneous'),
                ('SML', '2015-12-31', 0, 'Elsewhere Co.', '10', 'ELSE', 'name', 'long-members-1', 'Banks');");

        var headings = await new LongHistoryReader(store.DatabaseFile).HeadingsAsync("MID");

        Assert.Equal(["MDIA", "ODDD", "TRST", "WEBB"], headings.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(GicsSectors.Financials, HeavyweightLay.SectorAsItStood(headings, "TRST", new DateOnly(2016, 9, 16)));
        Assert.Equal(GicsSectors.Financials, HeavyweightLay.SectorAsItStood(headings, "TRST", new DateOnly(2015, 6, 1)));
        Assert.Equal(GicsSectors.RealEstate, HeavyweightLay.SectorAsItStood(headings, "TRST", new DateOnly(2016, 9, 19)));
        Assert.Equal(GicsSectors.ConsumerDiscretionary, HeavyweightLay.SectorAsItStood(headings, "MDIA", new DateOnly(2018, 9, 21)));
        Assert.Equal(GicsSectors.InformationTechnology, HeavyweightLay.SectorAsItStood(headings, "WEBB", new DateOnly(2018, 9, 21)));
        Assert.Null(HeavyweightLay.SectorAsItStood(headings, "ODDD", new DateOnly(2016, 1, 4)));
        Assert.Null(HeavyweightLay.SectorAsItStood(headings, "ELSE", new DateOnly(2016, 1, 4)));

        DateOnly[] calendar = [new(2018, 9, 21), new(2018, 9, 24)];
        HeavyweightCandidate Laid(string ticker, string? sector) => new(0, new HeavyweightMember(ticker, ticker, sector, null, 0m, null, 1, null, null), []);
        var laid = new Dictionary<int, HeavyweightSession>
        {
            [0] = new(0, [Laid("MDIA", null), Laid("TRST", null)], []),
            [1] = new(1, [Laid("MDIA", GicsSectors.CommunicationServices)], []),
        };

        var read = HeavyweightLay.AsItStood(laid, calendar, (ticker, day) => HeavyweightLay.SectorAsItStood(headings, ticker, day));

        Assert.Equal([GicsSectors.ConsumerDiscretionary, GicsSectors.RealEstate], read[0].Members.Select(one => one.Member.Sector));
        Assert.Same(laid[1], read[1]);

        // Both long readers of the heavyweights read the headings of the index they lay.
        var lay = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Loop", "HeavyweightLay.cs"));

        Assert.Contains("await longHistory.HeadingsAsync(read.Index, cancellation)", lay, StringComparison.Ordinal);
    }

    // The pin check: every source a registered rule's code version, the ladder rules' windows, the swing filter's versions, the
    // plan walk, the fills or the ledger pins.
    [Fact]
    public void NoSource24Point8ChangesIsOneARulesVersionPinsOrOneThatWritesTheRegister()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Concat(Core.Loop.RuleHooks.PlanWalkSources)
            .Concat(Worker.Rules.RuleVersionScorer.CodeVersionSources)
            .Concat(Worker.Filter.SwingFilter.CodeVersionSources)
            .Concat(LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/')))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 100, $"Read {pinned.Length} pinned source(s), expected at least 100.");
        Assert.Contains("src/EquityBrief.Worker/Sweep/HeavyweightSweep.cs", pinned);
        Assert.Contains("src/EquityBrief.Core/Families/GicsSectors.cs", pinned);
        Assert.All(HeavyweightsFrom2018Sources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(HeavyweightsFrom2018Sources.Intersect(pinned, StringComparer.Ordinal));
        Assert.All(HeavyweightsFrom2018Sources, path => Assert.DoesNotContain("INSERT INTO candidate_register", File.ReadAllText(Path.Combine(Repository.Root, path)), StringComparison.Ordinal));
    }
}
