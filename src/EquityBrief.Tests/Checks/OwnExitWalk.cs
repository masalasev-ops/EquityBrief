using EquityBrief.Core.Books;
using EquityBrief.Core.Families;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Checks;

// On the operator's ruling of 2026-11-16 each book's own exit is walked beside its live book as a record: sold only as the
// book sold before the rules restart, never at a stop or a take-profit, from the answers its step stored. Each walk is
// worked by hand over constructed nights; the fold says it draws no pick and cannot go live as it stands.
// see: Each book's own exit is walked beside its live book as a record on its card, and registers no version and draws no pick
public sealed class OwnExitWalk
{
    static readonly DateOnly Since = new(2026, 12, 1);
    static readonly DateOnly Second = new(2026, 12, 2);
    static readonly DateOnly Third = new(2026, 12, 3);
    static readonly DateOnly Fourth = new(2026, 12, 4);

    static OwnExitAnswer Quality(bool passed, int? place, double carried) => new(passed, place, 7, 9, carried);

    static OwnExitAnswer Scored(int passed, double carried) => new(false, null, passed, 9, carried);

    [Fact]
    public void QualityKeepsAHoldingToTheReshuffleItNoLongerQualifiesAtAndFillsTheEmptyPlaceFromTheStoredOrder()
    {
        // Two places. A, which the live book stopped out on the first night, is kept; B no longer qualifies at the reshuffle of
        // the second and is sold there, -0.10 = 1.8 / 2.0 - 1 - 0; the one place it leaves is filled by C, first in the order,
        // not D; A leaves the index on the third night and is sold at the second's close, 0.19 = 1.2 / 1.0 - 1 - 0.01; C is
        // open at 0.08 = 5.5 / 5.0 - 1 - 0.02.
        OwnExitNight[] nights =
        [
            new(Since, false, new Dictionary<string, OwnExitAnswer> { ["A"] = Quality(false, null, 1.10), ["B"] = Quality(true, 2, 2.2) }, []),
            new(Second, true, new Dictionary<string, OwnExitAnswer> { ["A"] = Quality(true, 3, 1.2), ["B"] = Quality(false, null, 1.8), ["C"] = Quality(true, 1, 5.0), ["D"] = Quality(true, 2, 3.0) }, []),
            new(Third, false, new Dictionary<string, OwnExitAnswer> { ["C"] = Quality(true, 1, 5.5) }, []),
        ];

        var record = OwnExit.Quality(
            Since,
            2,
            [new OwnExitStart("A", new DateOnly(2026, 11, 2), 1.0, 0.01), new OwnExitStart("B", new DateOnly(2026, 11, 2), 2.0, 0.0)],
            nights,
            (ticker, night) => !(ticker == "A" && night >= Third),
            (ticker, night) => ticker == "C" && night == Second ? 0.02 : null);

        Assert.Equal(["C"], record.Held.Select(position => position.Ticker));
        Assert.Equal(Second, Assert.Single(record.Held).ChosenOn);
        Assert.Equal(0.08, record.Held[0].Change!.Value, 9);

        var b = Assert.Single(record.Sold, position => position.Ticker == "B");
        Assert.Equal((Second, OwnExit.NoLongerQualifies), (b.EndedOn!.Value, b.EndReason));
        Assert.Equal(-0.10, b.Change!.Value, 9);

        var a = Assert.Single(record.Sold, position => position.Ticker == "A");
        Assert.Equal((Second, OwnExit.LeftTheIndex), (a.EndedOn!.Value, a.EndReason));
        Assert.Equal(0.19, a.Change!.Value, 9);

        Assert.DoesNotContain(record.Positions, position => position.Ticker == "D");
        Assert.Equal((0.08 - 0.10 + 0.19) / 3, record.MeanChange!.Value, 9);
    }

    [Fact]
    public void TheFundamentalsWalkBuysAsTheLiveBookBoughtAndSellsOnItsScoreAndItsTimeLimitAlone()
    {
        // Hold at 7 in 9 and three sessions. X falls to 5 of 9 on the second night and is sold there, 0 = 1.0 / 1.0 - 1 - 0.
        // Y is bought on the first night as the live book bought it, not bought again on the third while held, and sold at its
        // time limit on the fourth, its third session held: 0.2 = 2.4 / 2.0 - 1 - 0.
        DateOnly[] sessions = [Since, Second, Third, Fourth];
        OwnExitNight[] nights =
        [
            new(Since, false, new Dictionary<string, OwnExitAnswer> { ["X"] = Scored(7, 1.05), ["Y"] = Scored(8, 2.0) }, ["Y"]),
            new(Second, false, new Dictionary<string, OwnExitAnswer> { ["X"] = Scored(5, 1.0), ["Y"] = Scored(8, 2.1) }, []),
            new(Third, false, new Dictionary<string, OwnExitAnswer> { ["Y"] = Scored(8, 2.2) }, ["Y"]),
            new(Fourth, false, new Dictionary<string, OwnExitAnswer> { ["Y"] = Scored(8, 2.4) }, []),
        ];

        var record = OwnExit.Improving(
            Since,
            new ImprovingSettings(8, 7, 3),
            [new OwnExitStart("X", new DateOnly(2026, 11, 20), 1.0, 0.0)],
            nights,
            (_, _) => true,
            (_, _) => 0.0,
            (_, chosen, through) => sessions.Count(session => session > chosen && session <= through));

        Assert.Empty(record.Held);

        var x = Assert.Single(record.Sold, position => position.Ticker == "X");
        Assert.Equal((Second, ImprovingRule.ScoreFell), (x.EndedOn!.Value, x.EndReason));
        Assert.Equal(0.0, x.Change!.Value, 9);

        var y = Assert.Single(record.Sold, position => position.Ticker == "Y");
        Assert.Equal((Since, Fourth, ImprovingRule.TimeLimit), (y.ChosenOn, y.EndedOn!.Value, y.EndReason));
        Assert.Equal(0.2, y.Change!.Value, 9);
    }

    [Fact]
    public void AHeavyweightIsKeptToTheRebalanceThatNoLongerHasItLeadingAndTheRebalanceBuysWhatTheLiveBookBought()
    {
        // H1, bought on 2 Nov at 100, is kept past the first night, where the live book's stop sold it, and sold at the third's
        // rebalance, whose stored leaders leave it out, at 110: 0.10. H2, which the live book bought there at 50, is held at
        // 55 on the fourth: 0.10.
        var closes = new Dictionary<(string, DateOnly), decimal>
        {
            [("H1", new DateOnly(2026, 11, 2))] = 100m, [("H1", Since)] = 90m, [("H1", Second)] = 95m, [("H1", Third)] = 110m,
            [("H2", Third)] = 50m, [("H2", Fourth)] = 55m,
        };

        var record = OwnExit.Heavyweights(
            Since,
            soldOnLeading: true,
            [new OwnExitHolding("H1", new DateOnly(2026, 11, 2))],
            [new(Since, null, null), new(Second, null, null), new(Third, ["H2"], new HashSet<string>(StringComparer.Ordinal) { "H2" }), new(Fourth, null, null)],
            (_, _) => true,
            (ticker, night) => closes.TryGetValue((ticker, night), out var close) ? close : null);

        var h1 = Assert.Single(record.Sold);
        Assert.Equal(("H1", Third, OwnExit.NoLongerTheLeader), (h1.Ticker, h1.EndedOn!.Value, h1.EndReason));
        Assert.Equal(0.10, h1.Change!.Value, 9);

        var h2 = Assert.Single(record.Held);
        Assert.Equal(("H2", Third), (h2.Ticker, h2.ChosenOn));
        Assert.Equal(0.10, h2.Change!.Value, 9);
    }

    [Fact]
    public void AHeavyweightSoldUnderItsAverageIsSoldAtThatCloseAndNotAtTheRebalance()
    {
        var closes = new Dictionary<(string, DateOnly), decimal>
        {
            [("H1", new DateOnly(2026, 11, 2))] = 100m, [("H1", Since)] = 90m, [("H1", Second)] = 80m,
        };

        var record = OwnExit.Heavyweights(
            Since,
            soldOnLeading: false,
            [new OwnExitHolding("H1", new DateOnly(2026, 11, 2))],
            [new(Since, null, null), new(Second, null, null)],
            (_, _) => true,
            (ticker, night) => closes.TryGetValue((ticker, night), out var close) ? close : null,
            (ticker, night) => night == Second);

        var h1 = Assert.Single(record.Sold);
        Assert.Equal((Second, OwnExit.UnderItsAverage), (h1.EndedOn!.Value, h1.EndReason));
        Assert.Equal(-0.20, h1.Change!.Value, 9);
    }

    [Fact]
    public void TheFoldSaysItIsARecordThatDrawsNoPickAndNamesWhatItHoldsAndSold()
    {
        var html = MarkRenderer.OwnExitFold(new OwnExitView(Since, "at a reshuffle where a holding no longer qualifies, or when it leaves the index", ["C"], [new OwnExitSoldCell("B", Second, OwnExit.NoLongerQualifies)], 0.0567, 3, -0.012, 4));

        Assert.Contains(MarkRenderer.OwnExitHeading, html, StringComparison.Ordinal);
        Assert.Contains("draws no pick and cannot go live as it stands", html, StringComparison.Ordinal);
        Assert.Contains("brought to you", html, StringComparison.Ordinal);
        Assert.Contains("with no stop and no take-profit", html, StringComparison.Ordinal);
        Assert.Contains("It holds 1 tonight: C.", html, StringComparison.Ordinal);
        Assert.Contains("It sold B at the close of 2 Dec 2026: no longer qualifies.", html, StringComparison.Ordinal);
        Assert.Contains("+5.7%", html, StringComparison.Ordinal);
        Assert.Contains("-1.2%", html, StringComparison.Ordinal);
        Assert.Contains("over the live rule's 4", html, StringComparison.Ordinal);
        Assert.Equal(string.Empty, MarkRenderer.OwnExitFold(null));
    }

    // On the operator's answers of 2026-11-16, item 7, the line above the fold in their words, each mean change in percentage
    // points over the trading months since the walk began, worked by hand: from 1 Dec 2026 to 30 Dec 2026 the exchange trades
    // 21 sessions (25 Dec closed), one month, 5.67 and -1.20; to 15 Dec it trades 11, under a month, so the change so far reads
    // as the month's, the same; from 1 Dec 2026 to 29 Jan 2027 it trades 41 (1 and 18 Jan closed), 41 / 21 months,
    // 0.0567 * 100 * 21 / 41 = 2.90 and -0.012 * 100 * 21 / 41 = -0.61.
    [Fact]
    public void TheLineAboveTheFoldSaysInTheOperatorsWordsWhatTheOldWayAndTheLiveRuleMadeAMonthSinceTheWalkBegan()
    {
        var month = new OwnExitView(Since, "at a reshuffle", ["C"], [], 0.0567, 3, -0.012, 4) { Through = new DateOnly(2026, 12, 30) };
        var early = month with { Through = new DateOnly(2026, 12, 15) };
        var later = month with { Through = new DateOnly(2027, 1, 29) };

        Assert.Equal("Sold the old way, with no stop and no take-profit, this book would have made 5.67 points a month against -1.20 as it trades now, since 1 Dec 2026.", MarkRenderer.OwnExitLine(month));
        Assert.Equal(MarkRenderer.OwnExitLine(month), MarkRenderer.OwnExitLine(early));
        Assert.Equal("Sold the old way, with no stop and no take-profit, this book would have made 2.90 points a month against -0.61 as it trades now, since 1 Dec 2026.", MarkRenderer.OwnExitLine(later));
        Assert.Equal("Sold the old way, with no stop and no take-profit, this book's points a month against how it trades now are not yet measured, since 1 Dec 2026.", MarkRenderer.OwnExitLine(early with { OwnMean = null }));

        // The line stands above the fold, read without opening it.
        var html = MarkRenderer.OwnExitFold(later);

        Assert.True(html.IndexOf("own-exit-line", StringComparison.Ordinal) < html.IndexOf("<details", StringComparison.Ordinal));
    }

    [Fact]
    public void NoSourceTheOwnExitRunsThroughWritesTheStoreOrNamesARegistrationOrAPick()
    {
        string[] files =
        [
            Path.Combine(Repository.Root, "src", "EquityBrief.Core", "Books", "OwnExit.cs"),
            Path.Combine(Repository.Root, "src", "EquityBrief.Api", "Reading", "ReadApi.OwnExit.cs"),
            Path.Combine(Repository.Root, "src", "EquityBrief.Api", "Reading", "OwnExitCards.cs"),
            Path.Combine(Repository.Root, "src", "EquityBrief.Web", "Marks", "MarkRenderer.OwnExit.cs"),
        ];

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);

            Assert.DoesNotMatch(@"\b(INSERT|UPDATE|DELETE|REPLACE)\b", text);
            Assert.DoesNotContain("candidate_register", text, StringComparison.Ordinal);
            Assert.DoesNotContain("decision_card", text, StringComparison.Ordinal);
            Assert.DoesNotContain("listing", text, StringComparison.Ordinal);
        }
    }
}
