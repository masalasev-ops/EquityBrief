using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Moves;
using EquityBrief.Core.News;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Moves;

namespace EquityBrief.Tests.Reading;

// read-surface, 18.3: the four marks over a full input and over the input each degrades on; the reactions' line in each
// of its states; the long view's levels on the line's own scale and drawn only where the plan was read on its night; the
// ladder figure at the quote's price in the session and the close outside it, reached by the shell's script by the
// figure's own edges; the plan's shares with each reward to risk worked by hand and a name holding no plan saying why;
// each move's cause naming its source through the page's own functions; and the fixture's pages drawing what the
// replay's reader stored.
// see: A stock's typical earnings move is the median size of its reaction over every print the store holds, read through the history pulled before its year
// see: The long view draws the month-end closes over the pulled history at the bar store's scale with the plan's levels on the same prices
// see: Each tranche buys an equal share of a full position as the exits divide it, and its reward to risk is read to every traded exit
// see: A move's cause is research's where written, then the labelled news inside it and then the filings dated inside it
public partial class ReadSurface
{
    // The rows 18.3 adds that this check reaches: the long view and the ladder figure, the four marks, the parts the level
    // chart, the chart, the plan, the reactions and the moves gain, and the two failures a page states.
    internal static string[] HistoryPageRows =>
    [
        CheckReach.Key("15.9 Name", "The long view"),
        CheckReach.Key("15.9 Name", "The ladder figure"),
        CheckReach.Key("15.5 The mark vocabulary", "Reaction range"),
        CheckReach.Key("15.5 The mark vocabulary", "Long view"),
        CheckReach.Key("15.5 The mark vocabulary", "Ladder figure"),
        CheckReach.Key("15.5 The mark vocabulary", "Reward to risk bars"),
        CheckReach.Key("15.5 The mark vocabulary", "Level chart, a lettered tag beneath each report's session and each ex-dividend date"),
        CheckReach.Key("15.5 The mark vocabulary", "Level chart, the year's biggest moves numbered on their last sessions"),
        CheckReach.Key("15.9 Name", "The chart, a lettered tag beneath each report's session and each ex-dividend date saying what it marks beneath the pointer"),
        CheckReach.Key("15.9 Name", "The chart, the year's biggest moves numbered on their last sessions as the table of moves numbers them"),
        CheckReach.Key("15.9 Name", "The plan, each tranche as an equal share of a full position with the price it is carried at and its stop"),
        CheckReach.Key("15.9 Name", "The plan, its risk and its reward to risk to each traded exit with the top one named where the trail begins"),
        CheckReach.Key("15.9 Name", "The plan, a tranche with no band beneath it stating no risk"),
        CheckReach.Key("15.9 Name", "The plan, a bar of each reward to risk"),
        CheckReach.Key("15.9 Name", "The plan, the trail in the words of its rule"),
        CheckReach.Key("15.9 Name", "Earnings reactions, after the valuation and before the chart"),
        CheckReach.Key("15.9 Name", "Earnings reactions, the stock's typical earnings move with the prints it was read over and the oldest of them and how many were left out or that fewer than four were read"),
        CheckReach.Key("15.9 Name", "Earnings reactions, each print of the year as a bar against that move either way"),
        CheckReach.Key("15.9 Name", "Earnings reactions, on a page for an earlier night a line saying the typical move is the newest night's alone"),
        CheckReach.Key("15.9 Name", "How it got here, each move numbered as the chart numbers it"),
        CheckReach.Key("15.9 Name", "How it got here, otherwise each move's cause taken from the labelled stories published inside it with the results and filings inside it beneath them"),
        CheckReach.Key("15.9 Name", "How it got here, or from those results and filings alone"),
        CheckReach.Key("15.9 Name", "How it got here, or said to be found in neither"),
        CheckReach.Key("15.9 Name", "How it got here, the source of each cause named beside it"),
        CheckReach.Key("15.9 Name", "How it got here, a line stating when the first and the newest labelled story were published"),
        CheckReach.Key(Scope.FailureTable, "A name holding no plan, an S&P 400 or 600 member among them"),
        CheckReach.Key(Scope.FailureTable, "The plan and the long view read on different nights"),
    ];

    static DateOnly On(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // A plan as the ladder stores one: three tranches, the third with no stop, and one traded exit, the top, at 217.78.
    static LadderRow CvxPlan(DateOnly asOf) => new(
        "CVX",
        asOf,
        "range",
        """
        {"tranches":[{"lowEdge":"200.78","highEdge":"208.98","condition":"FirstCloseBackAbove","stop":"195.8409"},
                     {"lowEdge":"195.8409","highEdge":"197.82","condition":"ReachesTheZone","stop":"184.2619"},
                     {"lowEdge":"184.2619","highEdge":"185.7375","condition":"ReachesTheZone","stop":null}],
         "exits":[{"lowEdge":"217.78","highEdge":"217.78","traded":true,"trailing":true,"fraction":"1/1","reason":null}],
         "invalidation":"184.2619","events":[]}
        """);

    [Fact]
    public void TheReactionRangeDrawsEachPrintAgainstTheTypicalMoveEitherWayAndSaysWhatItLacks()
    {
        var marks = new MarkRenderer();
        ReactionCell[] prints =
        [
            new(On("2026-01-30"), "before", On("2026-01-30"), "2.10", "2.20", 4.8, 5.0),
            new(On("2026-05-01"), "before", On("2026-05-01"), "2.40", "2.30", -4.2, -1.0),
        ];
        var typical = new EarningsMove(35, 0, On("2018-02-02"), 2.7);
        var drawn = marks.ReactionRange("CVX", prints, typical);

        // The band from the move under nought to the same over it, and each print's bar in its hue with its move whole,
        // the one past the band ringed and saying so.
        Assert.Contains("data-mark=\"reaction-range\" data-prints=\"2\" data-typical=\"2.7\"", drawn, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"rr-range\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-low=\"-2.7\" data-high=\"2.7\"><title>The typical earnings move, 2.7% either way, the median size of 35 reactions since 2018-02-02</title>", drawn, StringComparison.Ordinal);
        Assert.Contains("class=\"rr-move rr-up rr-past\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-report-date=\"2026-01-30\" data-move=\"5\" data-past=\"yes\"><title>Reported 2026-01-30: the session of 2026-01-30 moved +5.0%, past the typical move</title>", drawn, StringComparison.Ordinal);
        Assert.Contains("class=\"rr-move rr-down\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-move=\"-1\" data-past=\"no\"><title>Reported 2026-05-01: the session of 2026-05-01 moved -1.0%, inside the typical move</title>", drawn, StringComparison.Ordinal);

        // Each bar stands on the zero rule drawn across the plot at nought, the fall's bar hanging from it.
        var zero = Regex.Match(drawn, "<line class=\"m-axisline\" x1=\"[^\"]+\" x2=\"[^\"]+\" y1=\"([^\"]+)\" y2=\"([^\"]+)\"/>");

        Assert.True(zero.Success, "The reactions draw no zero rule.");
        Assert.Equal(zero.Groups[1].Value, zero.Groups[2].Value);
        Assert.Equal(zero.Groups[1].Value, Regex.Match(drawn, "<rect class=\"rr-move rr-down\" x=\"[^\"]+\" y=\"([^\"]+)\"").Groups[1].Value);

        // A move stated over too few prints draws no band and the bars alone, and no print says so.
        Assert.DoesNotContain("rr-range\" x=", marks.ReactionRange("CVX", prints, new EarningsMove(3, 0, On("2025-10-31"), 2.0)), StringComparison.Ordinal);
        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"reaction-range\" data-prints=\"0\">No print of CVX's over the calendar's year is stored with a session the bars reach, so no reaction is drawn.</p>",
            marks.ReactionRange("CVX", [], typical));
    }

    [Fact]
    public void TheReactionsLineStatesTheMoveItsPrintsAndWhatWasLeftOutOrWhyNoneIsStated()
    {
        var marks = new MarkRenderer();

        Assert.Equal(
            "<p class=\"typical-move\" data-typical=\"2.7\" data-prints=\"35\" data-unread=\"2\">The typical earnings move: 2.7% either way, the median size of 35 reports' moves since 2018-02-02, read through the history pulled before the stored year from the pull history-pull-a, 2 reports left out where the prices did not reach them or a session around them was missing.</p>",
            marks.TypicalMoveLine("CVX", new EarningsMove(35, 2, On("2018-02-02"), 2.7), "history-pull-a", null));
        Assert.Equal(
            $"<p class=\"typical-move\" data-typical=\"too-few\" data-prints=\"3\" data-unread=\"0\">No typical earnings move is stated: 3 reports were read, under the 4 it is stated over, read over the stored year alone: {ChartAverager.NoPull}.</p>",
            marks.TypicalMoveLine("VSNT", new EarningsMove(3, 0, On("2025-10-31"), 2.0), null, ChartAverager.NoPull));
        Assert.Equal(
            "<p class=\"typical-move\" data-typical=\"none\">No typical earnings move is stored for SW tonight; the chart averages step writes one each night for every name whose stored year holds no hole.</p>",
            marks.TypicalMoveLine("SW", null, null, null));
        Assert.Equal(
            "<p class=\"typical-move\" data-typical=\"newest-night-alone\">The typical earnings move is stored for the newest night alone, over every print to it, so a page for an earlier night draws none; the newest night's page draws it.</p>",
            marks.TypicalMoveLine("CVX", new EarningsMove(35, 0, On("2018-02-02"), 2.7), "history-pull-a", null, newest: false));
    }

    [Fact]
    public void TheLongViewDrawsEachLevelAtTheHeightACloseAtItsPriceStandsAndSaysWhenItHasTooFewMonths()
    {
        var marks = new MarkRenderer();
        MonthEnd[] closes = [new(On("2018-01-31"), 86.3376m), new(On("2022-06-30"), 160m), new(On("2026-10-09"), 211.55m)];
        LongViewLevel[] levels =
        [
            new(200.78m, 208.98m, "support", "Tranche 1, 200.78 to 208.98", "Tranche 1"),
            new(217.78m, 217.78m, "resistance", "Trail begins, 217.78 to 217.78", "Trail begins"),
            new(160m, 160m, "rule", "The year's low, 160.00 on 2026-04-14", "Year's low"),
        ];
        var drawn = marks.LongView("CVX", closes, levels);

        Assert.Contains("data-mark=\"long-view\" data-months=\"3\"", drawn, StringComparison.Ordinal);

        // A level at 160 stands at the height of the close at 160: the two are on one axis.
        var close = Regex.Match(drawn, "<circle class=\"lv-dot\" cx=\"[^\"]+\" cy=\"([^\"]+)\" r=\"1.6\" data-session=\"2022-06-30\" data-close=\"160\">").Groups[1].Value;
        var rule = Regex.Match(drawn, "<rect class=\"lv-rule\" x=\"[^\"]+\" y=\"([^\"]+)\" width=\"[^\"]+\" height=\"[^\"]+\" data-low-edge=\"160\" data-high-edge=\"160\" data-role=\"rule\"").Groups[1].Value;

        Assert.NotEqual(string.Empty, close);
        Assert.Equal(close, rule);

        // The closes as one line through every month, at the heights their own points stand at.
        var line = Regex.Match(drawn, "<polyline class=\"lv-close\" fill=\"none\" points=\"([^\"]+)\"/>").Groups[1].Value.Split(' ');

        Assert.Equal(3, line.Length);
        Assert.Equal(close, line[1].Split(',')[1]);

        // Each level in its role's hue with its words beneath the pointer and its short name at the right, and each close
        // whole with a title.
        Assert.Contains("<rect class=\"lv-sup\"", drawn, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"lv-res\"", drawn, StringComparison.Ordinal);
        Assert.Contains("<title>Trail begins, 217.78 to 217.78</title></rect>", drawn, StringComparison.Ordinal);
        Assert.Contains("<title>2026-10-09: 211.55</title>", drawn, StringComparison.Ordinal);
        Assert.Contains(">Trail begins</text>", drawn, StringComparison.Ordinal);
        Assert.Contains(">Year's low</text>", drawn, StringComparison.Ordinal);

        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"long-view\" data-months=\"1\">1 month-end close is stored for VYLR, and the line needs two.</p>",
            marks.LongView("VYLR", [closes[^1]], []));
    }

    [Fact]
    public void TheLadderFigurePlacesEachBandByThePriceItDrawsWithWhatThePlanDoesThereAndSaysWhenItHoldsNone()
    {
        var marks = new MarkRenderer();
        LadderBand[] bands =
        [
            new(217.78m, 217.78m, "resistance", 4.5, 3, On("2026-09-15"), "the trail begins here"),
            new(225m, 226m, "resistance", 9.1, 0, null, null),
            new(210m, 213m, "support", 0, 2, On("2026-10-01"), null),
            new(200.78m, 208.98m, "support", 1.2, 5, On("2026-08-20"), "tranche 1 buys here"),
        ];

        // At a quote of 211.55, inside the third band: the two resistance bands above under the sell heading, furthest
        // first, the band holding the price beside it on its role's side and the support below under the buy heading.
        var drawn = marks.LadderFigure("CVX", bands, 211.55m, live: true);

        Assert.Contains("data-mark=\"ladder-figure\" data-bands=\"4\" data-above=\"2\" data-below=\"2\" data-price=\"211.55\" data-at=\"quote\"", drawn, StringComparison.Ordinal);
        Assert.Equal(
            ["225", "217.78", "210", "200.78"],
            Regex.Matches(drawn, "<g class=\"lf-band [^\"]+\" data-low-edge=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.True(drawn.IndexOf(">Sell into these<", StringComparison.Ordinal) < drawn.IndexOf("data-low-edge=\"225\"", StringComparison.Ordinal));
        Assert.True(drawn.IndexOf("price now 211.55", StringComparison.Ordinal) < drawn.IndexOf(">Buy into these<", StringComparison.Ordinal));
        Assert.Contains("data-side=\"above\" data-touches=\"3\" data-last-touch=\"2026-09-15\" data-use=\"the trail begins here\">", drawn, StringComparison.Ordinal);
        Assert.Contains(">4.5 typical days above</text>", drawn, StringComparison.Ordinal);
        Assert.Contains(">the price is inside it</text>", drawn, StringComparison.Ordinal);
        Assert.Contains(">1.2 typical days below</text>", drawn, StringComparison.Ordinal);
        Assert.Contains("<text class=\"m-row lf-use\" x=\"420\" y=\"", drawn, StringComparison.Ordinal);
        Assert.Contains(">tranche 1 buys here</text>", drawn, StringComparison.Ordinal);
        Assert.Contains(">turned the price 5 times, last 2026-08-20</text>", drawn, StringComparison.Ordinal);
        Assert.Contains(">never turned the price</text>", drawn, StringComparison.Ordinal);

        // Each row's role a pill in its own hue, beside its edges, a band of one price named at its price.
        Assert.Equal(
            ["lf-res", "lf-res", "lf-sup", "lf-sup"],
            Regex.Matches(drawn, "<rect class=\"lf-pill (lf-[a-z]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains(">resistance</text>", drawn, StringComparison.Ordinal);
        Assert.Contains(">support</text>", drawn, StringComparison.Ordinal);
        Assert.Contains(">225.00 to 226.00</text>", drawn, StringComparison.Ordinal);
        Assert.Contains(">217.78</text>", drawn, StringComparison.Ordinal);

        // The rows spaced evenly in price order and not on a price scale: the two above sit 7.22 apart in price and the
        // two below 4.02 apart by their high edges, and each pair stands one row apart.
        var rows = Regex.Matches(drawn, "<rect class=\"lf-pill lf-[a-z]+\" x=\"8\" y=\"([^\"]+)\"")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(rows[1] - rows[0], rows[3] - rows[2], 6);

        // Outside the session the price row names the last close.
        Assert.Contains("data-at=\"close\"", marks.LadderFigure("CVX", bands, 211.55m, live: false), StringComparison.Ordinal);
        Assert.Contains(">last close 211.55</text>", marks.LadderFigure("CVX", bands, 211.55m, live: false), StringComparison.Ordinal);

        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"ladder-figure\" data-bands=\"0\">CVX has no band stored to stack about the price.</p>",
            marks.LadderFigure("CVX", [], 211.55m, live: false));
    }

    [Fact]
    public void CvxsPlanDrawsAThirdATrancheWithEachRewardToRiskTheThirdStatingNoneAndTheTrailInWords()
    {
        var marks = new MarkRenderer();
        var view = NameScreen.History(
            new HistoryInputs(null, null, [], [], [], TimeZoneInfo.Utc, Newest: true),
            [],
            [],
            null,
            CvxPlan(On("2026-10-09")),
            [],
            [],
            null,
            null);
        var table = marks.SharesTable("CVX", view.Shares, view.NoPlan);

        // Each a third at its zone's midpoint with its stop and its risk, the third stating neither, and the three
        // making the whole position.
        Assert.Contains("<tr data-tranche=\"1\" data-share=\"1/3\" data-buy=\"204.88\" data-stop=\"195.8409\" data-risk=\"9.0391\">", table, StringComparison.Ordinal);
        Assert.Contains("<tr data-tranche=\"2\" data-share=\"1/3\" data-buy=\"196.83045\" data-stop=\"184.2619\" data-risk=\"12.56855\">", table, StringComparison.Ordinal);
        Assert.Contains("<tr data-tranche=\"3\" data-share=\"1/3\" data-buy=\"184.9997\" data-stop=\"\" data-risk=\"\">", table, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\">none: no band beneath it</td><td class=\"num\">none stated</td>", table, StringComparison.Ordinal);
        Assert.Contains("<p class=\"shares-sum\" data-held=\"3\" data-whole=\"3\">3 equal shares of 1/3 make the whole position.</p>", table, StringComparison.Ordinal);

        // The reward to risk to 217.78, the trail's start: 12.90 over 9.0391 and 20.94955 over 12.56855, 1.4271 and
        // 1.6668 to four places, the third none.
        Assert.Contains("<th class=\"num\">Reward to risk to 217.78, where the trail begins</th>", table, StringComparison.Ordinal);

        var ratios = Regex.Matches(table, "data-target=\"217.78\" data-reward-to-risk=\"([^\"]*)\"").Select(match => match.Groups[1].Value).ToArray();

        Assert.Equal(3, ratios.Length);
        Assert.Equal(1.4271m, Math.Round(decimal.Parse(ratios[0], CultureInfo.InvariantCulture), 4));
        Assert.Equal(1.6668m, Math.Round(decimal.Parse(ratios[1], CultureInfo.InvariantCulture), 4));
        Assert.Equal(string.Empty, ratios[2]);
        Assert.Contains(">1.43 on 12.90</td>", table, StringComparison.Ordinal);
        Assert.Contains(">none, no risk stated</td>", table, StringComparison.Ordinal);

        // A bar each for the two tranches stating a risk, marked where the trail begins.
        var bars = marks.RewardToRiskBars("CVX", view.Shares);

        Assert.Contains("data-mark=\"reward-to-risk\" data-bars=\"2\"", bars, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(bars, "data-trail=\"yes\"").Count);
        Assert.Contains(">Tranche 1 to 217.78 (trail)</text>", bars, StringComparison.Ordinal);
        Assert.Contains(">1.67</text>", bars, StringComparison.Ordinal);

        // Each bar from nought on one axis, with the rule at one standing where a reward equal to its risk would end:
        // the 1.6668 bar's length over 1.6668 from the bars' start, within the half pixel the figure is drawn to.
        var drawnBars = Regex.Matches(bars, "<rect class=\"rk-bar[^\"]*\" x=\"([^\"]+)\" y=\"[^\"]+\" width=\"([^\"]+)\"")
            .Select(match => (From: double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), Long: double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToArray();
        var one = double.Parse(Regex.Match(bars, "<line class=\"rk-one\" x1=\"([^\"]+)\"").Groups[1].Value, CultureInfo.InvariantCulture);

        Assert.Single(drawnBars.Select(bar => bar.From).Distinct());
        Assert.InRange(one - drawnBars[1].From, (drawnBars[1].Long / 1.6668) - 0.5, (drawnBars[1].Long / 1.6668) + 0.5);
        Assert.Contains(">reward equals risk</text>", bars, StringComparison.Ordinal);

        // A bar past the axis's end, a reward eight times its risk where the axis ends at six, held at the end and saying
        // so, where a bar inside it says nothing of the end.
        var past = marks.RewardToRiskBars("XYZ", [new TrancheShare(10m, 11m, 10.5m, 1, 9.5m, 1m, [new TargetReward(18.5m, 8m, 8m, false)])]);

        Assert.Contains("data-mark=\"reward-to-risk\" data-bars=\"1\" data-axis=\"6\"", past, StringComparison.Ordinal);
        Assert.Contains(">8.00, past the axis's end</text>", past, StringComparison.Ordinal);
        Assert.DoesNotContain("past the axis's end", bars, StringComparison.Ordinal);
        Assert.Equal(

            "<p class=\"mark-short\" data-mark=\"reward-to-risk\" data-tranches=\"1\">CVX's plan holds no tranche stating a risk with a traded exit to read a reward to risk against.</p>",
            marks.RewardToRiskBars("CVX", [view.Shares[2]]));

        // The trail in its rule's words, the whole position held on it from the top exit.
        Assert.Equal(
            "In an uptrend each tranche's stop is raised to the higher of the band beneath it and the last swing low beneath it, and a stop that would sit inside a support band moves to that band's low edge; from 217.78, the top exit, the whole position is held on a stop trailed under the price rather than sold at a price.",
            view.Trail);
    }

    [Fact]
    public void ANameHoldingNoPlanAndALongViewOfAnotherNightDrawNoPlanLevelAndSayWhy()
    {
        var year = new YearExtremes("CVX", 217.78m, On("2026-09-15"), 146.0126m, On("2026-01-05"), 252);
        MonthEnd[] closes = [new(On("2026-08-31"), 205m), new(On("2026-09-30"), 211m)];

        HistoryView Drawn(LadderRow? ladder, DateOnly viewed) => NameScreen.History(
            new HistoryInputs(null, new LongViewRow(viewed, closes, null, null, ChartAverager.NoPull), [], [], [], TimeZoneInfo.Utc, Newest: true),
            [],
            [],
            null,
            ladder,
            [],
            [],
            year,
            null);

        // The plan and the long view read on one night: the three tranches, the trail's start, the invalidation and the
        // year's high and low.
        var same = Drawn(CvxPlan(On("2026-10-09")), On("2026-10-09"));

        Assert.Null(same.PlanLevelsAbsent);
        Assert.Equal(
            ["Tranche 1, 200.78 to 208.98", "Tranche 2, 195.84 to 197.82", "Tranche 3, 184.26 to 185.74", "Trail begins, 217.78 to 217.78", "The invalidation, 184.26", "The year's high, 217.78 on 2026-09-15", "The year's low, 146.01 on 2026-01-05"],
            same.Levels.Select(level => level.Says));
        Assert.Equal(
            ["support", "support", "support", "resistance", "rule", "rule", "rule"],
            same.Levels.Select(level => level.Role));

        // On another night, the year's high and low alone and the two nights named.
        var apart = Drawn(CvxPlan(On("2026-10-08")), On("2026-10-09"));

        Assert.Equal("the plan was read on 2026-10-08 and the long view on 2026-10-09, so the plan's levels are drawn once both are read on one night", apart.PlanLevelsAbsent);
        Assert.Equal(2, apart.Levels.Count);

        // A name holding no plan, an S&P 400 or 600 member's: no share, no plan level and why.
        var none = Drawn(null, On("2026-10-09"));

        Assert.Equal("no plan is stored for the night, the plan being read for the S&P 500's members alone", none.PlanLevelsAbsent);
        Assert.Equal(none.PlanLevelsAbsent, none.NoPlan);
        Assert.Empty(none.Shares);
        Assert.Null(none.Trail);
        Assert.Equal(2, none.Levels.Count);
        Assert.Equal(
            "<p class=\"degraded\" data-tranche-shares=\"no-plan\">No share is drawn: no plan is stored for the night, the plan being read for the S&amp;P 500's members alone.</p>",
            new MarkRenderer().SharesTable("MIDC", none.Shares, none.NoPlan));
    }

    [Fact]
    public void EachMovesCauseNamesItsSourceInItsStatedOrderAndTheLineSaysWhenTheLabelsRan()
    {
        // Five sessions and four moves over them, each its own session: research names the first, a
        // labelled story was published inside the second, the third holds a result and a filing, the fourth nothing.
        BarRow Bar(string day, decimal close) => new("CVX", On(day), close, close, close, close, 1000);

        BarRow[] bars = [Bar("2026-01-12", 100m), Bar("2026-01-13", 104m), Bar("2026-01-14", 99m), Bar("2026-01-15", 106m), Bar("2026-01-16", 100m)];
        MoveCell[] cells =
        [
            new(On("2026-01-13"), 1, 4.0, 1, Cause: "Shares rose on the guidance."),
            new(On("2026-01-14"), 1, -4.8, 2),
            new(On("2026-01-15"), 1, 7.1, 3),
            new(On("2026-01-16"), 1, -5.7, 4),
        ];
        NewsArticleRow Story(string published, string title, string kind) =>
            new("a-" + title, title, "wire.example", published, "https://wire.example/" + title, "admitted", NewsLabelling.Labelled, null, kind, "negative", "why", "deepseek", "deepseek");

        var inputs = new HistoryInputs(
            null,
            null,
            [new FiledRow("10-Q", On("2026-01-15"), "0000093410-26-000003", "0000093410")],
            [],
            [
                Story("2026-01-13T23:00:00Z", "Output cut", "company news"),
                Story("2026-01-14T15:00:00Z", "Why I would sell", NewsInstruction.Opinion),
            ],
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York"),
            Newest: true);
        ReactionRow[] reactions = [new("CVX", On("2026-01-14"), "after", On("2026-01-15"), null, null, null, 7.1)];

        var taken = NameScreen.TakenCauses(cells, bars, reactions, inputs);
        var cover = MoveCauses.LabelsCover([new(DateTimeOffset.Parse("2026-01-13T23:00:00Z", CultureInfo.InvariantCulture), "Output cut", "wire.example", "https://wire.example/Output cut", "negative", "company news")]);
        var table = WebUtility.HtmlDecode(new MarkRenderer().MovesTable("CVX", taken, new CauseSource(On("2026-01-20"), "deepseek-flash"), cover));

        Assert.Contains("data-cause-column=\"taken\"", table, StringComparison.Ordinal);
        Assert.Equal(
            [MoveCauses.Research, MoveCauses.News, MoveCauses.Filed, MoveCauses.Neither],
            Regex.Matches(table, "<tr id=\"move-CVX-\\d\" [^>]* data-cause=\"([^\"]+)\">").Select(match => match.Groups[1].Value));

        // Research's sentence with its day and model; the story with its source, the opinion piece left out; the result
        // and the 10-Q filed on the third move's session; and none found for the fourth.
        Assert.Contains("<td class=\"cause\">Shares rose on the guidance.</td><td class=\"cause-source\">research written 2026-01-20 by deepseek-flash</td>", table, StringComparison.Ordinal);
        Assert.Contains(">Output cut</a> (wire.example, 2026-01-13, negative)</td><td class=\"cause-source\">the news labeller's labels</td>", table, StringComparison.Ordinal);
        Assert.DoesNotContain("Why I would sell", table, StringComparison.Ordinal);
        Assert.Contains("the results reported 2026-01-14, after the close, moving the session of 2026-01-15; ", table, StringComparison.Ordinal);
        Assert.Contains(">the 10-Q filed 2026-01-15</a></td><td class=\"cause-source\">the earnings calendar and the SEC's archive</td>", table, StringComparison.Ordinal);
        Assert.Contains("<td class=\"cause\">no cause found in the stored news or filings</td><td class=\"cause-source\">neither the stored news nor the filings</td>", table, StringComparison.Ordinal);

        // Beneath the table, when the first and the newest labelled story were published; a name with none says so.
        Assert.Contains("<p class=\"labels-cover\" data-first=\"2026-01-13\" data-last=\"2026-01-13\">", table, StringComparison.Ordinal);
        Assert.Contains(
            "none of CVX's stories is labelled, so each move takes its cause from research, its results or its filings alone.",
            WebUtility.HtmlDecode(new MarkRenderer().MovesTable("CVX", taken, null, null)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLadderFiguresDistancesAreTheQuotesInTheSessionAndTheClosesOutsideItReachedByTheScriptByTheFiguresEdges()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var name = FiredNamesOn(store, night)[0];
        var band = Rows(store, $"SELECT low_edge, high_edge FROM level WHERE ticker = '{name}' AND as_of = (SELECT MAX(as_of) FROM level WHERE ticker = '{name}') ORDER BY low_edge LIMIT 1;").Single();

        StoreQuote(store, name, "2026-10-07T14:55:00Z", night, $"[{{\"low\":\"{band[0]}\",\"high\":\"{band[1]}\",\"role\":\"support\",\"days\":7.7}}]");

        // The quote stands at 231.40, and the band is placed and worded by its side of that price.
        var side = decimal.Parse(band[0], CultureInfo.InvariantCulture) > 231.40m ? "above" : "below";

        // The figure's row for the band carries the edges the level summary's row carries, which the shell's script finds
        // both by, and in the session its distance is the quote's.
        var rung = $"<g class=\"lf-band [^\"]+\" data-low-edge=\"{Regex.Escape(band[0])}\" data-high-edge=\"{Regex.Escape(band[1])}\"[^>]*>.*?<text class=\"m-row lf-away\" x=\"250\" y=\"[^\"]+\" data-away=\"([^\"]+)\">([^<]+)</text>";

        using (var host = new PassHost(store.Root) { Clock = FixedClock.At(InTheSession, SessionZones.UnitedStates) })
        {
            using var client = host.CreateClient();
            var page = await client.GetStringAsync($"/screens/name/{name}");
            var figure = Regex.Match(page, "<svg class=\"ladder-figure\"[^>]*>.*?</svg>", RegexOptions.Singleline).Value;

            Assert.Contains("data-at=\"quote\"", figure, StringComparison.Ordinal);
            Assert.Contains($"<tr class=\"band", page, StringComparison.Ordinal);
            Assert.Matches($"<tr class=\"band[^\"]*\" data-low-edge=\"{Regex.Escape(band[0])}\" data-high-edge=\"{Regex.Escape(band[1])}\"", page);

            var distance = Regex.Match(figure, rung, RegexOptions.Singleline);

            Assert.True(distance.Success, "the figure draws no row for the band");
            Assert.Equal(("7.7", "7.7 typical days " + side), (distance.Groups[1].Value, WebUtility.HtmlDecode(distance.Groups[2].Value)));
            Assert.Contains($"data-side=\"{side}\"", Regex.Match(figure, $"<g class=\"lf-band [^\"]+\" data-low-edge=\"{Regex.Escape(band[0])}\"[^>]*>").Value, StringComparison.Ordinal);

            // The quote's reply restates the band at the price by the edges the figure carries, in the figure's words.
            using var reply = JsonDocument.Parse(await client.GetStringAsync($"{SinglePageApp.QuoteRoute}{name}"));
            var restated = reply.RootElement.GetProperty("distances").EnumerateArray().Single();

            Assert.Equal((band[0], band[1], "7.7 typical days " + side), (restated.GetProperty("low").GetString(), restated.GetProperty("high").GetString(), restated.GetProperty("ladder").GetString()));
            Assert.Equal("231.40", reply.RootElement.GetProperty("priceText").GetString());
        }

        // The shell's script reaches each row's distance and the price row by the selector the figure answers.
        using (var host = new PassHost(store.Root))
        {
            using var client = host.CreateClient();
            var shell = await client.GetStringAsync("/");

            Assert.Contains("screen.querySelector('.ladder-figure g.lf-band[data-low-edge=\"' + CSS.escape(band.low) + '\"][data-high-edge=\"' + CSS.escape(band.high) + '\"] text.lf-away')", shell, StringComparison.Ordinal);
            Assert.Contains("rung.textContent = band.ladder;", shell, StringComparison.Ordinal);
            Assert.Contains("ladder.querySelector('text.lf-price')", shell, StringComparison.Ordinal);
            Assert.Contains("tag.textContent = 'price now ' + reply.priceText;", shell, StringComparison.Ordinal);
        }

        // After the close the figure draws the close and the distance from it, not the quote's.
        using (var host = new PassHost(store.Root) { Clock = FixedClock.At(AfterTheClose, SessionZones.UnitedStates) })
        {
            using var client = host.CreateClient();
            var page = await client.GetStringAsync($"/screens/name/{name}");
            var figure = Regex.Match(page, "<svg class=\"ladder-figure\"[^>]*>.*?</svg>", RegexOptions.Singleline).Value;

            Assert.Contains("data-at=\"close\"", figure, StringComparison.Ordinal);
            Assert.Contains(">last close ", figure, StringComparison.Ordinal);
            Assert.NotEqual("7.7", Regex.Match(figure, rung, RegexOptions.Singleline).Groups[1].Value);
        }
    }

    [Fact]
    public async Task EachFixtureNamesPageDrawsTheTypicalMoveAndTheLongViewTheReplaysReaderStoredAndAnEarlierNightsPageDrawsNeither()
    {
        using var store = await FixtureReplay.ReplayedAsync();
        using var host = new PassHost(store.Root);
        using var client = host.CreateClient();

        foreach (var ticker in new[] { "AAPL", "KEYS", "MSFT", "NFLX" })
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}"));
            var stored = Rows(store, $"SELECT prints, unread, printf('%.6f', median_move), IFNULL(reason, '') FROM earnings_move WHERE ticker = '{ticker}';").Single();
            var line = Regex.Match(page, "<p class=\"typical-move\" data-typical=\"([^\"]+)\" data-prints=\"(\\d+)\" data-unread=\"(\\d+)\">([^<]+)</p>");

            // The line states the move the replay's reader stored, read over the stored year alone since the fixture
            // holds no pull, and says why.
            Assert.True(line.Success, $"{ticker}'s page draws no typical move");
            Assert.Equal((stored[0], stored[1]), (line.Groups[2].Value, line.Groups[3].Value));
            Assert.Equal(double.Parse(stored[2], CultureInfo.InvariantCulture), double.Parse(line.Groups[1].Value, CultureInfo.InvariantCulture), 6);
            Assert.Contains("read over the stored year alone: " + ChartAverager.NoPull, line.Groups[4].Value, StringComparison.Ordinal);

            // The reactions come after the valuation and before the chart.
            var cards = Regex.Matches(page, "<section class=\"card\"[^>]* data-card=\"([^\"]+)\">").Select(card => card.Groups[1].Value).ToList();

            Assert.Equal("chart", cards[cards.IndexOf("reactions") + 1]);
            Assert.Equal("valuation", cards[cards.IndexOf("reactions") - 1]);

            // The long view draws the months the reader stored and why no pull was read.
            var months = Rows(store, $"SELECT json_array_length(closes) FROM long_view WHERE ticker = '{ticker}';").Single()[0];

            Assert.Contains($"data-mark=\"long-view\" data-months=\"{months}\"", page, StringComparison.Ordinal);
            Assert.Contains($"<p class=\"long-view-basis\" data-reason=\"{ChartAverager.NoPull}\">The line starts with the stored year: {ChartAverager.NoPull}.</p>", page, StringComparison.Ordinal);

            // A page for an earlier night says each is the newest night's alone.
            var night = Rows(store, $"SELECT MAX(session_date) FROM bar WHERE ticker = '{ticker}';").Single()[0];
            var earlier = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}/{night}"));

            Assert.Contains("data-typical=\"newest-night-alone\"", earlier, StringComparison.Ordinal);
            Assert.Contains("data-long-view=\"newest-night-alone\"", earlier, StringComparison.Ordinal);
            Assert.DoesNotContain("data-mark=\"long-view\" data-months", earlier, StringComparison.Ordinal);
        }
    }
}
