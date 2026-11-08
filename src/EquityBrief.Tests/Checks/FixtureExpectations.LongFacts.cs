using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.History;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Providers;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.5: a filer's facts kept as the archive sent them and asked nothing again, the long table keeping only
// the periods before the standing table's first as first filed, a predecessor's figures joined on both sides of five figures
// in common and of the widest gap, no standing fact moving; the report days read off 8-Ks carrying item 2.02 alone; each
// first test year worked by hand on both sides of its share, the estimate on both sides of a year apart, the drift's fallback
// with its label, the long test's refusal of a family whose year does not stand, and a since-gone code by why it left.
// see: The long history's facts are the SEC's figures before the standing table's first, kept as first filed apart from it
// see: Each filing-based family's first test year is three learning years after the first year 90 in every 100 members read its score
public partial class FixtureExpectations
{
    internal static string[] LongFactsClaims =>
    [
        CheckReach.Key(Scope.LimitsTable, "The long history's first test years"),
    ];

    internal static string[] LongFactsRows => [.. ComponentAccess.LongFactsRows, .. SchemaColumns.LongFactStoreRows, .. LongFactsClaims];

    // A feed answering each filer with the facts given, counting what it was asked.
    sealed class KeptFactsFeed(IReadOnlyDictionary<string, string> answers) : IFilerFactsFeed
    {
        public int Requests { get; private set; }

        public Task<string> FactsAsync(string cik, CancellationToken cancellation = default)
        {
            Requests++;

            return answers.TryGetValue(cik, out var answer) ? Task.FromResult(answer) : throw new HttpRequestException("404 (Not Found)");
        }
    }

    // A facts answer stating net income for each period given, as the archive files it.
    static string FactsAnswer(params (string Start, string End, decimal Value, string Filed, string Accession)[] figures)
    {
        var rows = string.Join(",", figures.Select(figure => string.Create(CultureInfo.InvariantCulture,
            $"{{\"start\":\"{figure.Start}\",\"end\":\"{figure.End}\",\"val\":{figure.Value},\"accn\":\"{figure.Accession}\",\"form\":\"10-Q\",\"filed\":\"{figure.Filed}\"}}")));

        return "{\"cik\":1,\"entityName\":\"Constructed\",\"facts\":{\"us-gaap\":{\"NetIncomeLoss\":{\"units\":{\"USD\":[" + rows + "]}}}}}";
    }

    [Fact]
    public async Task ALongFilersFactsAreKeptAsSentAskedNothingAgainAndOnlyThePeriodsBeforeTheStandingTablesFirstAreStored()
    {
        using var store = new TemporaryStore();
        var folder = Path.Combine(store.Root, "long-facts");
        var answer = FactsAnswer(
            ("2014-07-01", "2014-09-30", 10m, "2014-11-01", "A-2"),
            ("2014-07-01", "2014-09-30", 11m, "2015-11-01", "A-9"),
            ("2015-01-01", "2015-03-31", 12m, "2015-05-01", "A-3"));
        var feed = new KeptFactsFeed(new Dictionary<string, string> { ["0000000101"] = answer });
        var said = new List<string>();

        Assert.Equal(2, await LongFactPull.CaptureAsync(folder, ["0000000101", "0000000102"], feed, said.Add));
        Assert.Equal(LongFactPull.Stated(2, 0), said[0]);
        Assert.Equal(answer, LongFactPull.Read(LongFactPull.FileOf(folder, "101")));
        Assert.False(File.Exists(LongFactPull.FileOf(folder, "102")));
        Assert.Contains("0000000102", File.ReadAllText(Path.Combine(folder, LongFactPull.RefusedFile)), StringComparison.Ordinal);

        await LongFactPull.CaptureAsync(folder, ["0000000101"], feed, said.Add);

        Assert.Equal(2, feed.Requests);
        Assert.Equal(LongFactPull.Stated(0, 1), said[^1]);

        var rows = LongFacts.Stored(ConceptAnswers.FromFacts(answer, "0000000101", FiledFacts.Concepts));

        Assert.Equal([("NetIncomeLoss", new DateOnly(2014, 9, 30), 10m, "A-2")], rows.Select(row => (row.Concept, row.End, row.Value, row.Accession)));
    }

    // A predecessor's figures are copied under its company's new filer where at least five stated by both agree within the
    // widest gap, and not where four do or where one is past it; no standing fact moves.
    [Theory]
    [InlineData(5, 0.04, true)]
    [InlineData(4, 0.04, false)]
    [InlineData(5, 0.06, false)]
    public async Task APredecessorsFactsAreJoinedOnlyWhereFiveFiguresInCommonAgreeWithinTheWidestGap(int inCommon, double gap, bool joined)
    {
        using var store = new TemporaryStore().Migrated();
        var succession = FilerSuccessions.Stated[0];
        var folder = Path.Combine(store.Root, "long-facts");
        var quarters = Enumerable.Range(0, 6).Select(at => new DateOnly(2009, 1, 1).AddMonths(3 * at)).ToArray();
        var predecessor = quarters.Select((start, at) => (Text(start), Text(start.AddMonths(3).AddDays(-1)), 100m + at, Text(start.AddMonths(4)), "P-" + at)).ToArray();
        var successor = quarters.Take(inCommon).Select((start, at) => (Text(start), Text(start.AddMonths(3).AddDays(-1)), (100m + at) * (1m + (decimal)gap), Text(start.AddYears(5)), "S-" + at)).ToArray();

        store.Execute($"INSERT INTO long_filing (ticker, cik, accession, form, items, reported, filed, run_id) VALUES ('OLD', '{succession.Predecessor.TrimStart('0')}', 'F-1', '10-K', NULL, NULL, '2010-01-01', 'r'), ('NEW', '{succession.Successor.TrimStart('0')}', 'F-2', '10-K', NULL, NULL, '2014-01-01', 'r');");
        store.Execute($"INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('{succession.Successor}', 'NetIncomeLoss', '2016-01-01', '2016-03-31', '5', '2016-05-01', '10-Q', 'S-STD', 'r');");

        await LongFactPull.CaptureAsync(folder, [succession.Predecessor, succession.Successor], new KeptFactsFeed(new Dictionary<string, string>
        {
            [succession.Predecessor] = FactsAnswer(predecessor),
            [succession.Successor] = FactsAnswer(successor),
        }), _ => { });

        var outcome = await LongFactPull.WriteAsync(store.DatabaseFile, folder, "long-facts-a", new CallClock(new DateTimeOffset(2026, 11, 8, 6, 0, 0, TimeSpan.Zero)), true, _ => { });
        var join = outcome.Joins.Single(one => one.Succession == succession);

        Assert.Equal(inCommon, join.InCommon);
        Assert.Equal(joined, join.Refused is null);
        Assert.Equal(joined ? 6 - inCommon : 0, join.Copied);
        Assert.Equal((long)(6 + inCommon + (joined ? 6 - inCommon : 0)), Scalar(store, "SELECT COUNT(*) FROM long_fact;"));
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM filed_fact;"));
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM run_log WHERE stage = 'long-facts';"));
    }

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The report days are read off the 8-Ks carrying item 2.02 alone, on the day each was filed, beside the provider's prints,
    // and written again whole.
    // see: A report day the provider's calendar does not hold is read off the filer's 8-K carrying item 2.02, for a gate reading a date alone
    [Fact]
    public async Task TheReportDaysAreReadOffTheEightKsCarryingItem202AloneAndWrittenAgainWhole()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(@"
            INSERT INTO long_filing (ticker, cik, accession, form, items, reported, filed, run_id) VALUES
                ('AAA', '1', 'a-1', '8-K', '2.02,9.01', '2009-04-20', '2009-04-21', 'r'),
                ('AAA', '1', 'a-2', '8-K/A', '2.02', '2009-04-20', '2009-05-02', 'r'),
                ('AAA', '1', 'a-3', '8-K', '5.02', '2009-06-01', '2009-06-02', 'r'),
                ('AAA', '1', 'a-4', '10-Q', NULL, '2009-03-31', '2009-05-05', 'r'),
                ('BBB', '2', 'b-1', '8-K', '1.01,2.02', '2010-01-28', '2010-01-28', 'r');
            INSERT INTO long_print (ticker, event_date, timing, eps_actual, eps_estimate, surprise_percent, read_from, run_id) VALUES
                ('AAA', '2009-04-21', 'after', '0.5', '0.4', 25.0, 'provider', 'r');");

        var clock = new CallClock(new DateTimeOffset(2026, 11, 8, 6, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, await LongPricePull.ReportDaysAsync(store.DatabaseFile, "long-report-days-a", clock));
        Assert.Equal(2, await LongPricePull.ReportDaysAsync(store.DatabaseFile, "long-report-days-b", clock));
        Assert.Equal(
            ["AAA|2009-04-21|unstated|8-K item 2.02", "AAA|2009-04-21|after|provider", "BBB|2010-01-28|unstated|8-K item 2.02"],
            Query(store, "SELECT ticker || '|' || event_date || '|' || timing || '|' || read_from FROM long_print ORDER BY ticker, event_date, read_from;"));
        Assert.False(LongPrices.AnnouncesResults("8-K", "12.02"));
    }

    // Each first year worked by hand: a family first scored in the first year 90 in every 100 members read its score, tested
    // three years after; a year one from its estimate and one two from it; the drift's first year at 80 in every 100 and its
    // fallback to 2019 with its label; and the long test refusing a family whose year does not stand.
    [Fact]
    public void EachFirstTestYearIsWorkedByHandOnBothSidesOfItsShareAndTheLongTestRefusesAFamilyWhoseYearDoesNotStand()
    {
        YearShare[] years = [new(2010, 100, 89), new(2011, 100, 90), new(2012, 100, 99)];

        var fundamentals = LongFirstYears.FamilyYear(LongFirstYears.Fundamentals, "GSPC", years);

        Assert.Equal(2011, fundamentals.FirstScored);
        Assert.Equal(2014, fundamentals.FirstTest);
        Assert.False(fundamentals.Apart);

        var quality = LongFirstYears.FamilyYear(LongFirstYears.Quality, "MID", years);

        Assert.Equal(2014, quality.FirstTest);
        Assert.Equal(2018, quality.Estimate);
        Assert.True(quality.Apart);
        Assert.True(quality.Stands);

        var never = LongFirstYears.FamilyYear(LongFirstYears.Heavyweights, "SML", [new YearShare(2010, 100, 89)]);

        Assert.Null(never.FirstTest);
        Assert.False(never.Stands);
        Assert.Null(LongFirstYears.Refusal([fundamentals, quality, never], LongFirstYears.Fundamentals, "GSPC"));
        Assert.NotNull(LongFirstYears.Refusal([fundamentals, quality, never], LongFirstYears.Heavyweights, "SML"));
        Assert.NotNull(LongFirstYears.Refusal([fundamentals], LongFirstYears.Quality, "GSPC"));

        var reached = LongFirstYears.DriftYear("MID", [new YearShare(2008, 50, 39), new YearShare(2009, 50, 40)]);

        Assert.Equal((2009, true, (string?)null), (reached.Year, reached.Reached, reached.Label));

        var survivors = LongFirstYears.DriftYear("GSPC", [new YearShare(2008, 50, 39), new YearShare(2020, 50, 30)]);

        Assert.Equal((2019, false, "mostly survivors"), (survivors.Year, survivors.Reached, survivors.Label));
    }

    // A since-gone code no surprise covers, by why it left: failed, bought out and dropped, each worked by hand, and an older
    // code read under the calendar's.
    [Fact]
    public void ASinceGoneCodeWithNoSurpriseIsSplitByWhyItLeft()
    {
        Assert.Equal("failed", LongFirstYears.WhyLeft(bankruptcy: true, endedByFiling: true, pricedAfter: true));
        Assert.Equal("bought out", LongFirstYears.WhyLeft(bankruptcy: false, endedByFiling: true, pricedAfter: false));
        Assert.Equal("dropped", LongFirstYears.WhyLeft(bankruptcy: false, endedByFiling: false, pricedAfter: true));
        Assert.Equal("not read", LongFirstYears.WhyLeft(bankruptcy: false, endedByFiling: false, pricedAfter: false));
        Assert.Equal("BBBY", LongFirstYears.CalendarCode("BBBY_old"));
        Assert.Equal("WB", LongFirstYears.CalendarCode("WB_old1"));
        Assert.Equal("AAPL", LongFirstYears.CalendarCode("AAPL"));
    }

    [Fact]
    public void NoNightReadsTheLongFactsAndNoSourceARulesVersionPinsIsOne23Point5Writes()
    {
        var readers = Directory.EnumerateFiles(Path.Combine(Repository.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}EquityBrief.Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(path), @"\blong_fact\b"))
            .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["src/EquityBrief.Data/Migrations/SchemaMigrations.cs", "src/EquityBrief.Worker/History/LongFactPull.cs", "src/EquityBrief.Worker/History/LongFirstYearReader.cs"], readers);

        string[] written =
        [
            "src/EquityBrief.Core/History/LongFacts.cs",
            "src/EquityBrief.Core/History/LongFirstYears.cs",
            "src/EquityBrief.Core/Providers/SecEdgarFilerFactsFeed.cs",
            "src/EquityBrief.Worker/History/LongFactPull.cs",
            "src/EquityBrief.Worker/History/LongFirstYearReader.cs",
        ];

        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Concat(LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/')))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(written, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(written.Intersect(pinned, StringComparer.Ordinal));
        Assert.Contains("src/EquityBrief.Core/Ledger/FiledFacts.cs", pinned);
    }
}
