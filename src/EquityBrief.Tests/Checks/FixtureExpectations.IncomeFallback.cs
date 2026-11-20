using System.IO.Compression;
using System.Text;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using BusinessReading = EquityBrief.Core.Cards.BusinessReading;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Report;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.12: the diagnosis's causes and each fallback line worked by hand over a constructed filer, the
// same line read for every quarter compared; the card's business line and the report's operating income naming the line;
// a filer covered by neither saying so; the two lines kept apart from the filed facts; the fill reading the kept answers
// and asking for a filer that filed since; and no source a rule's version pins among those the pages' fallback writes.
// see: Where operating income is not filed a reading takes income before taxes plus interest expense, else revenue less costs and expenses
public partial class FixtureExpectations
{
    // The row of section 18 24.12 adds that this check reaches.
    internal static readonly string[] IncomeFallbackClaims =
    [
        CheckReach.Key(Scope.FailureTable, "A filer that files no operating income for a member's four newest quarters"),
    ];

    internal static string[] IncomeFallbackRows => [.. ComponentAccess.FallbackRows, .. SchemaColumns.FallbackStoreRows, .. IncomeFallbackClaims];

    static readonly DateOnly[] FallbackEnds = [new(2025, 3, 31), new(2025, 6, 30), new(2025, 9, 30), new(2025, 12, 31)];

    static readonly DateOnly FallbackSession = new(2026, 3, 1);

    // One quarter's figure as its filing stated it, filed a month after the quarter ended.
    static FiledFactRow Quarter(string concept, DateOnly end, decimal value) =>
        new(concept, end.AddDays(-90), end, value, end.AddDays(30), "10-Q", "acc-" + end.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));

    static IEnumerable<FiledFactRow> Each(string concept, params decimal[] values) => FallbackEnds.Select((end, at) => Quarter(concept, end, values[at]));

    // Net income of 10 a quarter; operating income where given; income before taxes of 20 a quarter and interest of 3 on two;
    // revenue of 100 and costs and expenses of 70 a quarter.
    static List<FiledFactRow> Filer(bool operating = false, bool someOperating = false, bool pretax = false, bool costs = false)
    {
        var facts = Each("NetIncomeLoss", 10, 10, 10, 10).ToList();

        if (operating)
        {
            facts.AddRange(Each("OperatingIncomeLoss", 15, 15, 15, 15));
        }

        if (someOperating)
        {
            facts.AddRange(Each("OperatingIncomeLoss", 15, 15, 15, 15).Take(2));
        }

        if (pretax)
        {
            facts.AddRange(Each(IncomeDiagnosis.PretaxIncome[0], 20, 20, 20, 20));
            facts.AddRange(Each("InterestExpense", 3, 3, 3, 3).Take(2));
        }

        if (costs)
        {
            facts.AddRange(Each(FiledFacts.Revenue[0], 100, 100, 100, 100));
            facts.AddRange(Each(IncomeDiagnosis.CostsAndExpenses[0], 70, 70, 70, 70));
        }

        return facts;
    }

    static decimal[] OperatingRead(IReadOnlyList<FiledFactRow> facts) =>
    [
        .. FirstFiledRevenue.Quarters(facts.Select(fact => new FiledRevenue(fact.Concept, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession)), FiledFacts.OperatingIncome)
            .OrderBy(quarter => quarter.End)
            .Select(quarter => quarter.Value),
    ];

    [Fact]
    public void EachCauseAndEachFallbackLineIsWorkedByHandTheSameLineReadForEveryQuarter()
    {
        Assert.Equal(IncomeCause.Read, IncomeDiagnosis.CauseOf(Filer(operating: true), FallbackSession));
        Assert.Equal(IncomeCause.NoFiler, IncomeDiagnosis.CauseOf(null, FallbackSession));
        Assert.Equal(IncomeCause.FewerQuarters, IncomeDiagnosis.CauseOf([.. Filer(operating: true).Where(fact => fact.End != FallbackEnds[0])], FallbackSession));
        Assert.Equal(IncomeCause.NoOperatingLine, IncomeDiagnosis.CauseOf(Filer(), FallbackSession));
        Assert.Equal(IncomeCause.SomeQuarters, IncomeDiagnosis.CauseOf(Filer(someOperating: true), FallbackSession));

        // As filed where it covers the four; the facts handed back unchanged.
        var filed = Filer(operating: true, pretax: true, costs: true);
        Assert.Equal((IncomeLine.Operating, (IReadOnlyList<FiledFactRow>)filed), IncomeDiagnosis.WithLine(filed, FallbackSession));

        // Income before taxes of 20 plus the interest of 3 where filed and nought where not: 23, 23, 20, 20.
        var (pretaxLine, pretaxFacts) = IncomeDiagnosis.WithLine(Filer(pretax: true, costs: true), FallbackSession);
        Assert.Equal(IncomeLine.PretaxPlusInterest, pretaxLine);
        Assert.Equal([23m, 23m, 20m, 20m], OperatingRead(pretaxFacts));

        // With two quarters' operating income filed, all four read on the fallback alike, none on the 15 filed.
        Assert.Equal([23m, 23m, 20m, 20m], OperatingRead(IncomeDiagnosis.WithLine(Filer(someOperating: true, pretax: true), FallbackSession).Facts));

        // Revenue of 100 less costs and expenses of 70: 30 a quarter.
        var (costsLine, costsFacts) = IncomeDiagnosis.WithLine(Filer(costs: true), FallbackSession);
        Assert.Equal(IncomeLine.RevenueLessCosts, costsLine);
        Assert.Equal([30m, 30m, 30m, 30m], OperatingRead(costsFacts));

        // Neither line: none, the facts as they were.
        Assert.Equal(IncomeLine.None, IncomeDiagnosis.WithLine(Filer(), FallbackSession).Line);

        // The cover over the pretax line: 86 over 6 of interest clears the floor of 2; none where no line covers four.
        Assert.True(IncomeDiagnosis.Covers(pretaxFacts, FallbackSession));
        Assert.Null(IncomeDiagnosis.Covers(Filer(), FallbackSession));
    }

    [Fact]
    public void TheCardAndTheReportNameTheLineTheirOperatingIncomeWasReadOnAndAFilerNeitherCoversSaysSo()
    {
        var settings = CardSettings.Defaults;

        Assert.Contains(
            "its income before taxes plus interest expense, its filings stating no operating income, is 5.00 times its interest",
            CardLines.Business(new BusinessReading(40m, 5m, false, false, 4) { OperatingLine = IncomeLine.PretaxPlusInterest }, "improving", settings).Words,
            StringComparison.Ordinal);
        Assert.Contains(
            "its cover cannot be read: its filer files no operating income for its four quarters and neither income before taxes plus interest expense nor revenue less costs and expenses covers them",
            CardLines.Business(new BusinessReading(40m, null, false, false, 4) { OperatingLine = IncomeLine.None }, "improving", settings).Words,
            StringComparison.Ordinal);
        Assert.Contains("its operating income is 5.00 times its interest", CardLines.Business(new BusinessReading(40m, 5m, false, false, 4), "improving", settings).Words, StringComparison.Ordinal);

        // The report's quarters: the newest filed quarter's operating income read as 20 on the pretax line, and said so.
        QuarterRow[] labels = [.. FallbackEnds.Select(end => new QuarterRow(end, null, null, null, null, null, null, null, null, null, null, null, null, false))];
        var sourced = QuarterSources.Of(labels, Filer(pretax: true));

        Assert.Equal(IncomeLine.PretaxPlusInterest, sourced.OperatingLine);
        Assert.Equal(20m, sourced.Quarters.Single(quarter => quarter.PeriodEnd == FallbackEnds[^1]).OperatingIncome);
        Assert.Equal(
            "read as income before taxes plus interest expense, the filing stating no operating income",
            QuarterLines.Latest(sourced.Quarters, sourced: sourced)!.Lines.Single(line => line.Line == QuarterLines.OperatingIncome).From);
        Assert.Equal(IncomeLine.Operating, QuarterSources.Of(labels, Filer(operating: true, pretax: true)).OperatingLine);
    }

    [Fact]
    public void TheTwoLinesAreKeptApartFromTheFiledFactsAndAFallbackAloneNeverStandsForAReportsFigures()
    {
        IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>> answer = new Dictionary<string, IReadOnlyList<ConceptFact>>
        {
            [IncomeDiagnosis.PretaxIncome[0]] = [new(new(2025, 1, 1), new(2025, 3, 31), 20m, new(2025, 4, 30), "10-Q", "acc-1"), new(new(2025, 1, 1), new(2025, 3, 31), 21m, new(2025, 7, 30), "10-Q", "acc-2"), new(new(2014, 1, 1), new(2014, 3, 31), 9m, new(2014, 4, 30), "10-Q", "acc-0")],
            ["NetIncomeLoss"] = [new(new(2025, 1, 1), new(2025, 3, 31), 10m, new(2025, 4, 30), "10-Q", "acc-1")],
        };

        // One row a period as first filed, from the filed facts' first period, and none of a concept the filed facts keep.
        Assert.Equal([(IncomeDiagnosis.PretaxIncome[0], 20m)], IncomeDiagnosis.Stored(answer).Select(row => (row.Concept, row.Value)));
        Assert.DoesNotContain(FiledFacts.Stored(answer), row => IncomeDiagnosis.FallbackConcepts.Contains(row.Concept));

        var fallbackOnly = new Dictionary<string, IReadOnlyList<ConceptFact>> { [IncomeDiagnosis.PretaxIncome[0]] = answer[IncomeDiagnosis.PretaxIncome[0]] };

        Assert.False(FilingsRefresher.Carries(fallbackOnly, "acc-1"));
        Assert.True(FilingsRefresher.Carries(answer, "acc-1"));
    }

    // From the 24.12 correction income before taxes filed in parts: the domestic part alone where the filer files no foreign
    // part, the two summed where it files both for the period, a period it files the domestic part of alone read as none once
    // it files a foreign part anywhere, and a total filed for the period read before the parts.
    [Fact]
    public void IncomeBeforeTaxesFiledInPartsIsReadAsTheirSumAndAPeriodMissingAPartIsNotRead()
    {
        // Domestic 18 a quarter and interest of 3 on two: 21, 21, 18, 18.
        List<FiledFactRow> domestic = [.. Each("NetIncomeLoss", 10, 10, 10, 10), .. Each(IncomeDiagnosis.PretaxDomestic, 18, 18, 18, 18), .. Each("InterestExpense", 3, 3, 3, 3).Take(2)];
        var (line, facts) = IncomeDiagnosis.WithLine(domestic, FallbackSession);

        Assert.Equal(IncomeLine.PretaxPlusInterest, line);
        Assert.Equal([21m, 21m, 18m, 18m], OperatingRead(facts));

        // Foreign 2 filed for every quarter: 23, 23, 20, 20.
        Assert.Equal([23m, 23m, 20m, 20m], OperatingRead(IncomeDiagnosis.WithLine([.. domestic, .. Each(IncomeDiagnosis.PretaxForeign, 2, 2, 2, 2)], FallbackSession).Facts));

        // Foreign filed for the first quarter alone: the three others hold no line, so neither fallback covers the four.
        Assert.Equal(IncomeLine.None, IncomeDiagnosis.WithLine([.. domestic, .. Each(IncomeDiagnosis.PretaxForeign, 2, 2, 2, 2).Take(1)], FallbackSession).Line);

        // A total of 20 filed for every quarter is read before the parts: 23, 23, 20, 20, not 21, 21, 18, 18.
        Assert.Equal([23m, 23m, 20m, 20m], OperatingRead(IncomeDiagnosis.WithLine([.. domestic, .. Each(IncomeDiagnosis.PretaxIncome[0], 20, 20, 20, 20)], FallbackSession).Facts));
        Assert.Contains(IncomeDiagnosis.PretaxDomestic, IncomeDiagnosis.FallbackConcepts);
        Assert.Contains(IncomeDiagnosis.PretaxForeign, IncomeDiagnosis.FallbackConcepts);
    }

    // From the 24.12 correction a real estate trust, a bank or an insurer, named from its industry as filed, and the card's
    // business line naming the kind as the reason its cover is not read, a company of no such kind keeping the line as it was.
    [Fact]
    public void ACardNamesARealEstateTrustABankOrAnInsurerAsTheReasonItsCoverIsNotRead()
    {
        Assert.Equal("a real estate trust", IncomeDiagnosis.KindOf("Equity Real Estate Investment Trusts (REITs)", "Residential REITs", null));
        Assert.Equal("a real estate trust", IncomeDiagnosis.KindOf("Financial Services", "Mortgage Real Estate Investment Trusts (REITs)", "Mortgage REITs"));
        Assert.Equal("a bank", IncomeDiagnosis.KindOf("Banks", "Banks", "Regional Banks"));
        Assert.Equal("an insurer", IncomeDiagnosis.KindOf("Insurance", "Insurance", "Property & Casualty Insurance"));
        Assert.Null(IncomeDiagnosis.KindOf("Energy", "Oil, Gas & Consumable Fuels", "Integrated Oil & Gas"));
        Assert.Null(IncomeDiagnosis.KindOf("Financial Services", "Capital Markets", "Investment Banking & Brokerage"));
        Assert.Null(IncomeDiagnosis.KindOf(null, null, null));

        var settings = CardSettings.Defaults;

        Assert.Contains(
            "its cover cannot be read: a real estate trust files no operating income line, and neither income before taxes plus interest expense nor revenue less costs and expenses covers its four quarters",
            CardLines.Business(new BusinessReading(40m, null, false, false, 4) { OperatingLine = IncomeLine.None, Kind = "a real estate trust" }, "improving", settings).Words,
            StringComparison.Ordinal);
        Assert.Contains(
            "its cover cannot be read: its filer files no operating income for its four quarters",
            CardLines.Business(new BusinessReading(40m, null, false, false, 4) { OperatingLine = IncomeLine.None }, "improving", settings).Words,
            StringComparison.Ordinal);

        // A bank or an insurer, a financial company whose cover is not read, is named by its kind; one of no kind as before.
        Assert.Contains(
            "its cover is not read: a bank files no operating income line, its interest being its cost of business",
            CardLines.Business(new BusinessReading(40m, null, false, true, 4) { Kind = "a bank" }, "improving", settings).Words,
            StringComparison.Ordinal);
        Assert.Contains(
            "its cover is not read, a financial company's interest being its cost of business",
            CardLines.Business(new BusinessReading(40m, null, false, true, 4), "improving", settings).Words,
            StringComparison.Ordinal);

        // Fewer than four quarters filed says so, whatever the kind.
        Assert.DoesNotContain("a real estate trust", CardLines.Business(new BusinessReading(null, null, false, false, 2) { Kind = "a real estate trust" }, "improving", settings).Words, StringComparison.Ordinal);
    }

    // From the 24.12 correction of the eleven the last fallback: net income plus income tax expense plus interest, read only
    // where neither income before taxes nor costs and expenses covers the four, and the card saying so in the operator's words.
    [Fact]
    public void NetIncomePlusTaxPlusInterestIsReadLastAndTheCardSaysTheCompanyTagsNoPretaxLine()
    {
        // Net income of 10, tax of 4 and interest of 3 on two: 17, 17, 14, 14.
        List<FiledFactRow> taxed = [.. Filer(), .. Each(IncomeDiagnosis.IncomeTax, 4, 4, 4, 4), .. Each("InterestExpense", 3, 3, 3, 3).Take(2)];
        var (line, facts) = IncomeDiagnosis.WithLine(taxed, FallbackSession);

        Assert.Equal(IncomeLine.NetPlusTaxPlusInterest, line);
        Assert.Equal([17m, 17m, 14m, 14m], OperatingRead(facts));

        // Income before taxes or costs and expenses covering the four are read before it.
        Assert.Equal(IncomeLine.PretaxPlusInterest, IncomeDiagnosis.WithLine([.. taxed, .. Each(IncomeDiagnosis.PretaxIncome[0], 20, 20, 20, 20)], FallbackSession).Line);
        Assert.Equal(IncomeLine.RevenueLessCosts, IncomeDiagnosis.WithLine([.. Filer(costs: true), .. Each(IncomeDiagnosis.IncomeTax, 4, 4, 4, 4)], FallbackSession).Line);

        // Tax filed for three of the four: none.
        Assert.Equal(IncomeLine.None, IncomeDiagnosis.WithLine([.. Filer(), .. Each(IncomeDiagnosis.IncomeTax, 4, 4, 4, 4).Take(3)], FallbackSession).Line);
        Assert.Contains(IncomeDiagnosis.IncomeTax, IncomeDiagnosis.FallbackConcepts);

        var words = CardLines.Business(new BusinessReading(40m, 5m, false, false, 4) { OperatingLine = IncomeLine.NetPlusTaxPlusInterest }, "improving", CardSettings.Defaults).Words;

        Assert.Contains("Operating income read as net income plus tax plus interest; the company tags no pretax line that quarter", words, StringComparison.Ordinal);
        Assert.Contains("its net income plus tax plus interest is 5.00 times its interest", words, StringComparison.Ordinal);
    }

    sealed class OneAnswer(string json) : IFilerFactsFeed
    {
        public int Requests { get; private set; }

        public Task<string> FactsAsync(string cik, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult(json);
        }
    }

    // Two filers of the newest night's members: one whose kept answer is newer than its newest filing, read from it, and
    // one that filed after its answer was kept, asked again; each period stored as first filed, a second fill adding none.
    [Fact]
    public async Task TheFillReadsAKeptAnswerAndAsksAgainForAFilerThatFiledSinceItWasKept()
    {
        using var store = new TemporaryStore().Migrated();
        var kept = Path.Combine(store.Root, "kept");

        static string Answer(decimal value) =>
            "{\"cik\":1,\"facts\":{\"us-gaap\":{\"" + IncomeDiagnosis.PretaxIncome[0] + "\":{\"units\":{\"USD\":[{\"start\":\"2025-01-01\",\"end\":\"2025-03-31\",\"val\":"
            + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"filed\":\"2025-04-30\",\"form\":\"10-Q\",\"accn\":\"acc-1\",\"fy\":2025,\"fp\":\"Q1\"}]}}}}}";

        {
            store.Execute("INSERT INTO member_reading (index_code, session_date, ticker) VALUES ('GSPC', '2026-11-19', 'OLD'), ('GSPC', '2026-11-19', 'NEW');");
            store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('OLD', '2026-11-01T00:00:00Z', '0000000001'), ('NEW', '2026-11-01T00:00:00Z', '0000000002');");
            store.Execute("INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('0000000002', 'NetIncomeLoss', '2025-07-01', '2025-09-30', '1', '2026-11-15', '10-Q', 'acc-9', 'run');");

            foreach (var cik in new[] { "0000000001", "0000000002" })
            {
                var file = LongFactPull.FileOf(kept, cik);

                Directory.CreateDirectory(Path.GetDirectoryName(file)!);

                await using (var gz = new GZipStream(File.Create(file), CompressionLevel.Fastest))
                {
                    await gz.WriteAsync(Encoding.UTF8.GetBytes(Answer(20m)));
                }

                File.SetLastWriteTimeUtc(file, new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc));
            }

            var feed = new OneAnswer(Answer(25m));
            var clock = Core.Time.FixedClock.At(new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero), Core.Time.SessionZones.UnitedStates);
            var fill = await new FallbackFactFill(clock, store.DatabaseFile).FillAsync(kept, feed, "fallback-facts-test", _ => { });

            Assert.Equal((2, 1, 1, 0, 2), (fill.Filers, fill.FromKept, fill.Asked, fill.Refused, fill.Stored));
            Assert.Equal(1, feed.Requests);

            var stored = new List<string>();

            using (var connection = store.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT cik || ' ' || dollars FROM fallback_fact ORDER BY cik;";

                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    stored.Add(reader.GetString(0));
                }
            }

            Assert.Equal(["0000000001 20", "0000000002 25"], stored);
            Assert.Equal(0, (await new FallbackFactFill(clock, store.DatabaseFile).FillAsync(kept, null, "fallback-facts-again", _ => { })).Stored);
        }
    }

    // Every shipped source the pages' fallback writes or changes, none of them one a rule's version pins.
    internal static readonly string[] IncomeFallbackSources =
    [
        "src/EquityBrief.Core/Ledger/IncomeDiagnosis.cs",
        "src/EquityBrief.Core/Report/QuarterSources.cs",
        "src/EquityBrief.Core/Report/QuarterLines.cs",
        "src/EquityBrief.Core/Cards/CardQuarters.cs",
        "src/EquityBrief.Core/Cards/CardLines.cs",
        "src/EquityBrief.Worker/Cards/DecisionCards.cs",
        "src/EquityBrief.Worker/Ledger/FilingsRefresher.cs",
        "src/EquityBrief.Worker/Ledger/FallbackFactFill.cs",
        "src/EquityBrief.Worker/History/IncomeDiagnosisRun.cs",
        "src/EquityBrief.Api/Reading/ReadApi.History.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Report.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Report.cs",
    ];

    [Fact]
    public void NoSourceThePagesFallbackWritesIsOneARulesVersionPins()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Concat(LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/')))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(IncomeFallbackSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(IncomeFallbackSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.Contains("src/EquityBrief.Core/Ledger/FiledFacts.cs", pinned);
    }
}
