using EquityBrief.Core.Families;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// Home and the combined account over the operator's answers of 2026-11-06, each part held to its new words against the page
// as it read before.
public partial class ReadSurface
{
    // On a night the monthly books reshuffled, Home's ideas were the night's new picks alone and nothing on it named what the
    // books bought; Tonight said the reshuffle added them apart.
    // see: On a night the monthly books reshuffle Home names their buys apart from the new picks and says they come from the reshuffle
    [Fact]
    public void OnAReshuffleNightHomeNamesTheBooksBuysAndSaysTheyComeFromTheReshuffle()
    {
        var marks = new MarkRenderer();
        var app = new SinglePageApp();
        (string, string)[] bought = [(HeavyweightRule.Name, "XOM"), (HeavyweightRule.Name, "CVX"), (QualityRule.Name, "AAPL")];

        var drawn = app.HomeRegion(marks, Day() with { Reshuffled = bought }, string.Empty);

        Assert.Contains(
            "<p class=\"home-more home-reshuffle\" data-reshuffled=\"3\">The monthly books reshuffled tonight and also buy 3: XOM and CVX (Leader of its sector), AAPL (Strong business at a fair price). These come from the reshuffle, not from tonight's new picks.</p>",
            drawn,
            StringComparison.Ordinal);

        // The new picks' count stands as it was, the books' buys said apart from it.
        Assert.Contains("data-ideas=\"none\">No new ideas tonight.", drawn, StringComparison.Ordinal);

        Assert.Equal(
            "The monthly books reshuffled tonight and also buy 1: AAPL (Strong business at a fair price). It comes from the reshuffle, not from tonight's new picks.",
            SinglePageApp.ReshuffleLine([(QualityRule.Name, "AAPL")]));

        // A night no book buys draws no line.
        Assert.Null(SinglePageApp.ReshuffleLine([]));
        Assert.DoesNotContain("home-reshuffle", app.HomeRegion(marks, Day(), string.Empty), StringComparison.Ordinal);
    }

    // A pick's card read its business line off the provider's quarters, which the pages no longer draw: where a filing states
    // a loss the provider's fetch filed as a profit, the card ticked the quarters' profit the page draws as a loss.
    // see: A pick's card reads its quarters as the pages draw them, from the filing and then the release and otherwise not on file
    [Fact]
    public void APicksCardReadsEachQuarterFromItsFilingThenItsReleaseAndOtherwiseNotOnFile()
    {
        static DateOnly On(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var night = new DateOnly(2026, 11, 5);
        var settings = EquityBrief.Core.Cards.CardSettings.Defaults;

        EquityBrief.Core.Readings.FiledIncome[] fetched =
        [
            new(On("2025-12-31"), On("2026-02-10"), 10m, 30m, 10m),
            new(On("2026-03-31"), On("2026-05-08"), 10m, 30m, 10m),
            new(On("2026-06-30"), On("2026-08-07"), 10m, 30m, 10m),
            new(On("2026-09-30"), On("2026-10-30"), 10m, 30m, 10m),
        ];

        EquityBrief.Core.Ledger.FiledFactRow Fact(string concept, DateOnly end, decimal value, DateOnly filed) =>
            new(concept, end.AddDays(1).AddMonths(-3), end, value, filed, "10-Q", "a-" + end.DayNumber);

        EquityBrief.Core.Ledger.FiledFactRow[] filed =
        [
            .. fetched.SelectMany(quarter => new[]
            {
                Fact("NetIncomeLoss", quarter.PeriodEnd, -10m, quarter.FilingDate),
                Fact("OperatingIncomeLoss", quarter.PeriodEnd, 30m, quarter.FilingDate),
                Fact("InterestExpense", quarter.PeriodEnd, 10m, quarter.FilingDate),
            }),
        ];

        EquityBrief.Core.Cards.CardLine Line(EquityBrief.Core.Cards.BusinessReading reading) => EquityBrief.Core.Cards.CardLines.Business(reading, "steady", settings);

        // The provider's quarters, as the card read them, and the filings, which state a loss each quarter.
        Assert.Equal(EquityBrief.Core.Cards.CardVerdict.Tick, Line(EquityBrief.Core.Cards.CardLines.Business(fetched, night, "Industrials")).Verdict);

        var read = EquityBrief.Core.Cards.CardQuarters.Of(fetched, filed);
        var card = Line(EquityBrief.Core.Cards.CardLines.Business(read, night, "Industrials"));

        Assert.All(read, quarter => Assert.Equal(QuarterSourceOf("Filing"), quarter.Source));
        Assert.Equal(EquityBrief.Core.Cards.CardVerdict.Warn, card.Verdict);
        Assert.Equal("Its four newest quarters sum their net income to nothing or less; its operating income is 3.00 times its interest; its reported quarters read steady.", card.Words);

        // The newest quarter with no filing stored and no release: not on file until its filing.
        var unfiled = EquityBrief.Core.Cards.CardQuarters.Of(fetched, [.. filed.Where(fact => fact.End != On("2026-09-30"))]);

        Assert.Equal(QuarterSourceOf("NotOnFile"), unfiled[^1].Source);
        Assert.Equal(
            "Not held: a quarter's net income is not on file until its filing; not held: its cover cannot be read, a quarter's operating income being not on file until its filing; its reported quarters read steady.",
            Line(EquityBrief.Core.Cards.CardLines.Business(unfiled, night, "Industrials")).Words);

        // The newest quarter drawn from its results release: its interest is no filing's, and the cover is not read.
        EquityBrief.Core.Cards.CardQuarter[] released = [.. read.Take(3), new(On("2026-09-30"), On("2026-10-30"), QuarterSourceOf("Release"), 12m, 33m, null)];

        Assert.Equal(
            "Its four newest quarters sum their net income to nothing or less; not held: its cover is not read, a quarter's interest being on file only once its filing is; its reported quarters read steady.",
            Line(EquityBrief.Core.Cards.CardLines.Business(released, night, "Industrials")).Words);
    }

    static EquityBrief.Core.Report.QuarterSource QuarterSourceOf(string name) => Enum.Parse<EquityBrief.Core.Report.QuarterSource>(name);

    // The card's quarters move no rule: no version, evaluator or hook pins a source they are read through.
    [Fact]
    public void NoRulePinsASourceThePicksCardReadsItsQuartersThrough()
    {
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(pinned.Count >= 30, $"Read {pinned.Count} pinned source(s), expected at least 30.");

        foreach (var source in new[] { "src/EquityBrief.Worker/Cards/DecisionCards.cs", "src/EquityBrief.Core/Cards/CardLines.cs", "src/EquityBrief.Core/Cards/CardQuarters.cs", "src/EquityBrief.Core/Report/QuarterSources.cs", "src/EquityBrief.Core/Report/ReleaseFigures.cs" })
        {
            Assert.True(File.Exists(Path.Combine(Repository.Root, source)), source);
            Assert.DoesNotContain(source, pinned);
        }
    }
}
