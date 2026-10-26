using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Insiders;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Report;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 22.4: the brief read atop every chart page, worked by hand over constructed figures: each line's
// template over the figure it reads, the rule's plan where a rule lists the stock and the levels plan where none does,
// each chip, the line of what to watch for on both sides of each condition and nowhere else, and each figure the store
// does not hold said so in its line.
// see: The chart page opens on a brief read written by code from stored figures, and no rule reads it
public partial class ReadSurface
{
    // The row 22.4 adds that this check reaches: section 15.22's brief read.
    internal static string[] BriefRows => [CheckReach.Key("15.22 Chart", "In brief")];

    static readonly DateOnly BriefNight = new(2026, 10, 23);

    // A stock in an uptrend closing at 100 between its support band of 94 to 96 and its resistance band of 106 to 108, its
    // typical day's move 2: 2 days above the support's near edge and 3 below the resistance's.
    internal static UniverseRow BriefMember(bool bands = true) =>
        new("XYZ", "Industrials", 100m, "uptrend", bands ? 96m : null, bands ? 106m : null, 2.0, SupportFloor: bands ? 94m : null, ResistanceCeiling: bands ? 108m : null);

    // Sales up 12% on a year in the quarter to 2026-06-30 after 9% in the quarter before, the operating margin 21% against
    // 18% a year earlier, and 18.4 times earnings in the middle of its own range of 15.2 to 24.8.
    static Readings BriefBusiness() => new(
        FundamentalState.Improving,
        new DateOnly(2026, 6, 30),
        null,
        new TrajectoryReading(
        [
            new TrajectoryQuarter(new DateOnly(2026, 6, 30), 0.12m, 0.08m, 0.21m, 0.18m),
            new TrajectoryQuarter(new DateOnly(2026, 3, 31), 0.09m, null, 0.20m, 0.19m),
        ], 2, null),
        new RecordReading([], 0, 0, 0, null),
        new QualityReading([], null, null, null, null, null),
        new ValuationReading([], 18.4m, 15.2m, 24.8m, QuarterReadings.Middle, null));

    internal static BriefInputs BriefOf(
        UniverseRow? member = null,
        BriefListed? listed = null,
        IReadOnlyList<PlanRow>? levels = null,
        bool ladder = true,
        AccountSettings? account = null,
        DateOnly? report = null,
        IReadOnlyList<double>? moves = null,
        decimal sold = 0m,
        IReadOnlyList<BriefStanding>? standing = null) =>
        new(
            "XYZ",
            "S&P 500",
            BriefNight,
            member,
            new Dictionary<string, double?>(StringComparer.Ordinal) { [IndicatorSeries.Rsi14] = 62.5, [IndicatorSeries.MacdHist] = 0.4 },
            BriefBusiness(),
            0.734,
            new AnalystReading(new DateOnly(2026, 10, 20), [], [new RevisionRow("0y", 1, 0, 5, 2, 1.5)], new Dictionary<string, IReadOnlyList<TrendPoint>>(), [], 2.0, 120m, 20.0),
            listed,
            levels ?? [],
            ladder,
            account,
            standing ?? [],
            report,
            moves ?? [],
            new InsiderView([], new InsiderTotals(0m, sold, 0, sold > 0 ? 1 : 0), new InsiderTotals(0m, sold, 0, sold > 0 ? 1 : 0), 0, 0, 0, 0, null),
            2.28);

    internal static BriefListed PullbackListed() =>
        new(SetupFamilies.Pullback, new DecisionCardRow("GSPC", BriefNight, SetupFamilies.Pullback, "XYZ", 1, 100m, 96m, 110m, "the live rule", "{}", "[]", null, Cap: 21));

    static IReadOnlyList<string> LinesOf(BriefView view, string panel) =>
        [.. view.Panels.Single(one => one.Key == panel).Lines.Select(line => line.Words)];

    [Fact]
    public void EveryLineOfTheBriefIsWrittenFromItsFigureAsWorkedByHand()
    {
        // Listed under the pullback with its card's plan, an account of 100,000 risking 1% a trade capped at half: 1,000 at
        // risk over the stop's 4 buys 250 shares, under the cap's 500, a position of 25,000 or a quarter of the account.
        var view = BriefScreen.Of(BriefOf(
            BriefMember(),
            PullbackListed(),
            account: new AccountSettings(100_000m, 1m, 0.5m),
            report: new DateOnly(2026, 11, 5),
            moves: [-6.0, 8.0, 5.0],
            sold: 250_000m,
            standing:
            [
                new BriefStanding(SetupFamilies.Pullback, BriefStates.Listed),
                new BriefStanding(BreakoutRule.Name, BriefStates.Short, BreakoutRule.Volume, 1),
                new BriefStanding(DriftRule.Name, BriefStates.NoReport, Missed: 4),
                new BriefStanding(FundamentalsRule.Name, BriefStates.NotClose, Missed: 2),
                new BriefStanding(DipRule.Name, BriefStates.NotRead),
                new BriefStanding(SetupFamilies.SectorHeavyweights.Name, BriefStates.NotHeld),
            ]) with { Score = ScoreRowOf(EightOfNine()) });
        var pullback = SetupFamilies.Named(SetupFamilies.Pullback)!.Label;

        Assert.Equal($"XYZ is in an uptrend, 2.0 typical days' moves above its nearest support and 3.0 typical days' moves below its nearest resistance. Its business reads improving, from sales and operating margin. Tonight {pullback} picked it.", view.Summary);
        Assert.Equal(
            [
                "Trend: an uptrend.",
                "Support at 96.00, 2.0 typical days' moves below; resistance at 106.00, 3.0 typical days' moves above.",
                "Momentum rising: relative strength 62.5 above its middle of 50, the trend momentum line above zero.",
                "Six months: its return ranks at the 73rd percentile of the S&P 500's stocks.",
            ],
            LinesOf(view, "chart"));
        // From 22.8 the business panel opens on the fundamentals card's line, eight of nine on its filings to 2026-06-30.
        Assert.Equal(
            [
                "Fundamentals: strong, 8 of 9 checks on its filings to 2026-06-30, in the card below.",
                "Sales up 12.0% on a year in the quarter to 2026-06-30, faster than the +9.0% of the quarter before.",
                "Operating margin 21.0%, wider than the 18.0% of a year earlier.",
                "It trades at 20.0 times its last four quarters' earnings, as filed with the SEC; its industry's median on the S&P 500 is 25.0.",
                "The analysts' mean target, as fetched on 2026-10-20: 120.00, 20.0% above the price; over 30 days 5 raised and 2 cut their estimate for the year.",
            ],
            LinesOf(view, "business"));

        // From the 22.8 correction of 2026-10-26 a stock the scorecard holds no multiple for has the provider's, named as its,
        // against its own quarters' range.
        var unscored = BriefScreen.Of(BriefOf(BriefMember(), PullbackListed(), account: new AccountSettings(100_000m, 1m, 0.5m), report: new DateOnly(2026, 11, 5), moves: [-6.0, 8.0, 5.0], sold: 250_000m, standing: []));

        Assert.Contains("It trades at 18.4 times its last four quarters' earnings, as the data provider reports, the middle of its own range of 15.2 to 24.8.", LinesOf(unscored, "business"));
        Assert.Equal(
            [
                $"Picked tonight by {pullback}: buy at the next open, near tonight's close of 100.00.",
                "Sell at the close of a day that closes below 96.00.",
                "Sell at the close of a day that closes at or above 110.00. Otherwise sell at the close 21 trading days after the buy.",
                "Your size: 250 shares, 25,000.00, 25.0% of the account, 1,000.00 at risk to the stop.",
            ],
            LinesOf(view, "way-in"));
        Assert.Equal(
            ["picked tonight", "one step short: volume too light", "no recent report", "not close", "not read tonight", "not in its holdings"],
            view.Chips.Select(chip => chip.Words));

        // The hold is 21 sessions to 2026-11-23, so the report of 2026-11-05 falls inside it; the moves of 6, 8 and 5 per
        // cent against a stop 4 per cent under the buy are 1.5, 2 and 1.25 of its distance, a median of 1.5.
        Assert.Equal(
            "Watch for: its report on 2026-11-05 falls inside a typical hold; its reports have moved it a median 1.5 times the first stop's distance; insiders sold $250,000 in the open market over 90 days.",
            view.WatchFor);
        Assert.Equal(new DateOnly(2026, 11, 23), CardHitsReading.HoldThrough(BriefNight, 21));
    }

    [Fact]
    public void TheLineOfWhatToWatchForStandsExactlyWhereOneOfItsConditionsHolds()
    {
        string? Watch(DateOnly? report = null, IReadOnlyList<double>? moves = null, decimal sold = 0m) =>
            BriefScreen.Of(BriefOf(BriefMember(), PullbackListed(), report: report, moves: moves, sold: sold)).WatchFor;

        // The hold's last session inside it and the session after it outside; a report on the night itself is not ahead.
        Assert.Equal("Watch for: its report on 2026-11-23 falls inside a typical hold.", Watch(report: new DateOnly(2026, 11, 23)));
        Assert.Null(Watch(report: new DateOnly(2026, 11, 24)));
        Assert.Null(Watch(report: BriefNight));

        // A median move of exactly the stop's distance is no warning, a tenth past it is.
        Assert.Null(Watch(moves: [4.0]));
        Assert.Equal("Watch for: its reports have moved it a median 1.1 times the first stop's distance.", Watch(moves: [4.4]));

        // No open-market sale and nothing else: no line.
        Assert.Null(Watch(sold: 0m));
        Assert.Equal("Watch for: insiders sold $1.5M in the open market over 90 days.", Watch(sold: 1_500_000m));
    }

    [Fact]
    public void AFigureTheStoreDoesNotHoldIsSaidSoInItsLine()
    {
        // No membership row, no reading, no answer: every line says what it could not read rather than leaving the line.
        var bare = BriefScreen.Of(BriefOf() with { Latest = new Dictionary<string, double?>(), Business = null, HalfYear = null, Analysts = null });

        Assert.Equal("XYZ has no trend read for the night, with no close and typical day's move to read its bands against. No reading of its reported quarters is stored for the night. No setup's answer for it is stored tonight.", bare.Summary);
        Assert.Equal(
            ["Trend: not read for the night.", "Its bands: no close is stored to read them against.", "Momentum: not read for the night.", "Six months: its return against the index's stocks is not read."],
            LinesOf(bare, "chart"));
        Assert.Equal(
            ["Fundamentals: not scored, as no scorecard is stored for it on the night.", "Sales: too few quarters are stored to read them against a year earlier.", "Operating margin: too few quarters are stored to read it against a year earlier.", "Earnings multiple: not read for the night.", "The analysts' mean target: not stored."],
            LinesOf(bare, "business"));
        Assert.Equal(
            ["Not among tonight's picks, and no levels plan is stored for the night.", "No stop is stored.", "No first sale is stored.", "No plan is stored to size."],
            LinesOf(bare, "way-in"));

        // A stock with no bands says so in its summary and its line.
        var unbanded = BriefScreen.Of(BriefOf(BriefMember(bands: false)));

        Assert.StartsWith("XYZ is in an uptrend, with no support band below it and with no resistance band above it.", unbanded.Summary, StringComparison.Ordinal);
        Assert.Equal("No support band below; no resistance band above.", LinesOf(unbanded, "chart")[1]);

        // The account not set: the size line links the settings.
        var unset = BriefScreen.Of(BriefOf(BriefMember(), PullbackListed())).Panels.Single(one => one.Key == "way-in").Lines[^1];

        Assert.Equal(new BriefLine("size", "Your account is not set:", Href: MarkRenderer.AccountRoute, LinkWords: "set your account's size and risk", After: " to see a size."), unset);
    }

    [Fact]
    public void AStockOnNoListReadsTheLevelsPlanAndAnIndexWithNoLadderSaysSo()
    {
        // On no list, the night's levels plan: its first zone of 95 to 97 with its stop under 92, and its first sale at 108
        // read by the trade step at 2.28 times the risk; sized off the zone's high edge over the stop, 1,000 at risk over 5
        // buying 200 shares of 97.
        IReadOnlyList<PlanRow> levels =
        [
            new PlanRow(95m, 97m, PlanKind.Tranche, "buy on a touch, stop on a daily close below 92.00", true, "a touch", 92m),
            new PlanRow(92m, 92m, PlanKind.Stop, "stop for the 95.00 zone", true),
            new PlanRow(108m, 108m, PlanKind.Exit, "sell a third", true),
        ];

        Assert.Equal(
            [
                "Not among tonight's picks. The night's levels plan buys in its first zone at 95.00 to 97.00.",
                "Its stop: a daily close below 92.00.",
                "Its first sale at 108.00: risk $1 to make $2.28.",
                "Your size: 200 shares, 19,400.00, 19.4% of the account, 1,000.00 at risk to the stop.",
            ],
            LinesOf(BriefScreen.Of(BriefOf(BriefMember(), levels: levels, account: new AccountSettings(100_000m, 1m, 0.5m))), "way-in"));

        // On the S&P 400 and 600, whose plans are not worked as a ladder, no plan stored and why, in the chart page's own
        // words and none naming a row or a card the chart page does not draw.
        Assert.Equal(
            ["Not among tonight's picks, and no plan is stored for the night, the plan being read for the S&P 500's members alone.", "No stop is stored.", "Its bands stand on its full page.", "No plan is stored to size."],
            LinesOf(BriefScreen.Of(BriefOf(BriefMember(), ladder: false)), "way-in"));
    }

    // A zone or a band whose two edges are one price is drawn as that price, and one wider as its two edges.
    [Fact]
    public void AZoneOrBandOfOnePriceIsDrawnAsThatPrice()
    {
        IReadOnlyList<PlanRow> levels =
        [
            new PlanRow(97m, 97m, PlanKind.Tranche, "buy on a touch, stop on a daily close below 92.00", true, "a touch", 92m),
            new PlanRow(108m, 108m, PlanKind.Exit, "sell a third", true),
        ];

        Assert.Equal("Not among tonight's picks. The night's levels plan buys in its first zone at 97.00.", LinesOf(BriefScreen.Of(BriefOf(BriefMember(), levels: levels)), "way-in")[0]);

        // Closing at 96 on a support band of 96 to 96, its resistance 106 five typical days' moves above; and closing at 106 on a
        // resistance band of 106 to 106, its support 96 five below.
        var onSupport = BriefMember() with { Close = 96m, SupportFloor = 96m };
        var onResistance = BriefMember() with { Close = 106m, ResistanceCeiling = 106m };

        Assert.Equal("Inside its support band at 96.00; resistance at 106.00, 5.0 typical days' moves above.", LinesOf(BriefScreen.Of(BriefOf(onSupport)), "chart")[1]);
        Assert.Equal("Support at 96.00, 5.0 typical days' moves below; inside its resistance band at 106.00.", LinesOf(BriefScreen.Of(BriefOf(onResistance)), "chart")[1]);

        // A band of two prices keeps both.
        Assert.Equal("Inside its support band at 94.00 to 96.00; resistance at 106.00, 5.5 typical days' moves above.", LinesOf(BriefScreen.Of(BriefOf(BriefMember() with { Close = 95m })), "chart")[1]);
    }

    // A chart page's brief, read back off the rendered page: its summary, each panel's lines and each chip's state and words.
    static (string Summary, IReadOnlyDictionary<string, string> Lines, IReadOnlyList<string> Chips) BriefOfPage(string page)
    {
        var brief = Assert.Single(Blocks(page, "<div class=\"brief-read\".*?<p class=\"brief-foot\">"));

        return (
            Regex.Match(brief, "<p class=\"brief-summary\" data-summary>(.*?)</p>").Groups[1].Value,
            Regex.Matches(brief, "<li data-line=\"([^\"]+)\"[^>]*>(.*?)</li>").ToDictionary(match => match.Groups[1].Value, match => Regex.Replace(match.Groups[2].Value, "<[^>]+>", string.Empty), StringComparer.Ordinal),
            [.. Regex.Matches(brief, "data-family=\"([^\"]+)\" data-state=\"([^\"]+)\"><span[^>]*></span>([^<]*)</span>").Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}|{match.Groups[3].Value}")]);
    }

    // Over the night the family pages are read on: F1 on the pullback's list with its card's plan of 100, 96 and 110 over 21
    // sessions; N1 one step short at its trade with no levels plan stored; HELD passing the pullback and held off the list
    // by its open trade; and K3 one step short of the breakout at its volume. Each chart page opens on its brief, every
    // chip read off the night's stored answers, the drift, the fundamentals-first family and the dip answering none of them.
    [Fact]
    public async Task OnTheSAndP500TheBriefReadsTheRulesPlanWhereARuleListsTheStockAndEachChipOffTheNightsAnswers()
    {
        using var store = await FamilyPagesStore();

        store.Execute(
            "INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record, cap) VALUES " +
            $"('GSPC', '{TheSwitch}', 'pullback', 'F1', 1, '100', '96', '110', 'the live swing filter, version 1', '{{}}', '[]', NULL, 21);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<(string Summary, IReadOnlyDictionary<string, string> Lines, IReadOnlyList<string> Chips)> Brief(string ticker) =>
            BriefOfPage(System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/chart/{ticker}")));

        var pullback = SetupFamilies.Named(SetupFamilies.Pullback)!.Label;
        var breakout = SetupFamilies.Named(BreakoutRule.Name)!.Label;
        var f1 = await Brief("F1");

        Assert.EndsWith($"Tonight {pullback} picked it.", f1.Summary, StringComparison.Ordinal);
        Assert.Equal($"Picked tonight by {pullback}: buy at the next open, near tonight's close of 100.00.", f1.Lines["buy"]);
        Assert.Equal("Sell at the close of a day that closes below 96.00.", f1.Lines["stop"]);
        Assert.Equal("Sell at the close of a day that closes at or above 110.00. Otherwise sell at the close 21 trading days after the buy.", f1.Lines["exit"]);
        Assert.Equal("Your account is not set: set your account's size and risk to see a size.", f1.Lines["size"]);
        Assert.Equal($"pullback|listed|{pullback}: picked tonight", f1.Chips[0]);

        var n1 = await Brief("N1");

        Assert.EndsWith($"Tonight the closest setup is {pullback}, one step short: reward not worth the risk.", n1.Summary, StringComparison.Ordinal);
        Assert.Equal($"pullback|short|{pullback}: one step short: reward not worth the risk", n1.Chips[0]);
        Assert.Equal("Not among tonight's picks, and no levels plan is stored for the night.", n1.Lines["buy"]);

        var held = await Brief("HELD");

        Assert.EndsWith($"Tonight it qualified for {pullback} but is not among tonight's picks.", held.Summary, StringComparison.Ordinal);
        Assert.Equal($"pullback|passed|{pullback}: qualified, not among tonight's picks", held.Chips[0]);

        // K3: two of the pullback's gates missed, one step short of the breakout at its volume; the drift, from 22.13 the
        // Fundamentals family in the fundamentals-first family's place, and the dip stored no answer for it, and the
        // heavyweights' book holds nothing.
        var k3 = await Brief("K3");

        Assert.Equal(
            [
                $"pullback|not-close|{pullback}: not close",
                $"breakout|short|{breakout}: one step short: volume too light",
                $"drift|not-read|{SetupFamilies.Named(DriftRule.Name)!.Label}: not read tonight",
                $"improving|not-read|{ImprovingRule.Label}: not read tonight",
                $"dip|not-read|{SetupFamilies.Dips.Label}: not read tonight",
                $"heavyweight|not-held|{SetupFamilies.SectorHeavyweights.Label}: not in its holdings",
            ],
            k3.Chips);
        Assert.EndsWith($"Tonight the closest setup is {breakout}, one step short: volume too light.", k3.Summary, StringComparison.Ordinal);
    }

    // Over the closed S&P 400 night: IA breaks out and the market check alone keeps it off, so its chip and its summary name
    // that step; its index works no ladder, so its way in says no plan is stored and why; the book holds nothing.
    [Fact]
    public async Task OnTheSAndP400TheBriefReadsEachChipOffTheIndexsAnswersAndSaysItWorksNoLadder()
    {
        using var store = Checks.FixtureExpectations.ClosedIndexStore();
        var night = new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero);

        await new EquityBrief.Worker.Indices.IndexFamilies(EquityBrief.Core.Time.FixedClock.At(night, EquityBrief.Core.Time.SessionZones.UnitedStates), store.DatabaseFile).RunAsync("night-brief", default, [], night);

        // The S&P 500's listing of the night, which every night stores and the chart page reads its night from.
        store.Execute($"INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) VALUES ('ZZZ', '{IndexNight}', '[]', 0, '{{}}', '[]');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var ia = BriefOfPage(System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/screens/chart/IA")));
        var breakout = SetupFamilies.Named(BreakoutRule.Name)!.Label;

        Assert.Contains($"breakout|short|{breakout}: one step short: the market check closed", ia.Chips);
        Assert.Contains($"heavyweight|not-held|{SetupFamilies.SectorHeavyweights.Label}: not in its holdings", ia.Chips);
        Assert.EndsWith($"Tonight the closest setup is {breakout}, one step short: the market check closed.", ia.Summary, StringComparison.Ordinal);
        Assert.Equal("Not among tonight's picks, and no plan is stored for the night, the plan being read for the S&P 500's members alone.", ia.Lines["buy"]);
        Assert.Equal("No stop is stored.", ia.Lines["stop"]);
        Assert.DoesNotContain(ia.Lines.Values, line => line.Contains("its row", StringComparison.Ordinal) || line.Contains("its card", StringComparison.Ordinal));
    }

    // The sources 22.4 writes.
    static readonly string[] BriefSources =
    [
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Api/Reading/BriefScreen.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Brief.cs",
        "src/EquityBrief.Web/App/SinglePageApp.Chart.cs",
        "src/EquityBrief.Web/App/Stylesheet.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Brief.cs",
    ];

    // No source a rule's version pins is one the brief read writes, so no rule's version moves, the reader shown to find a
    // source a rule pins.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneTheBriefReadWrites()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(BriefSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(BriefSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.NotEmpty(new[] { "src/EquityBrief.Worker/Indices/IndexFamilies.cs" }.Intersect(pinned, StringComparer.Ordinal));
    }

    // The three panels stand side by side and one beneath another at a phone's width, each line wrapping inside its panel,
    // and no rule of the brief gives it a width of its own.
    [Fact]
    public async Task TheBriefsPanelsStackAtAPhonesWidthAndNothingInItSetsAWidth()
    {
        using var store = await FamilyPagesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var shell = await client.GetStringAsync("/");
        var rules = Regex.Matches(shell, @"\.brief-[a-z-]+(?: [a-z0-9]+)?\{[^}]*\}").Select(match => match.Value).ToArray();

        Assert.Contains(".brief-panels{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:14px}", shell, StringComparison.Ordinal);
        Assert.Contains("@media (max-width:760px){.brief-panels{grid-template-columns:1fr}}", shell, StringComparison.Ordinal);
        Assert.Contains(".brief-panel li{margin:0 0 4px;overflow-wrap:anywhere}", shell, StringComparison.Ordinal);
        Assert.NotEmpty(rules);
        Assert.All(rules, rule => Assert.DoesNotMatch(@"(?<![a-z-])(width|min-width):", rule));
        Assert.DoesNotContain("style=", Assert.Single(Blocks(System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/screens/chart/F1")), "<div class=\"brief-read\".*?</div></div>")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheBriefIsDrawnWithItsPanelsChipsWatchLineAndFooter()
    {
        var view = BriefScreen.Of(BriefOf(
            BriefMember(),
            PullbackListed(),
            report: new DateOnly(2026, 11, 5),
            standing: [new BriefStanding(SetupFamilies.Pullback, BriefStates.Listed), new BriefStanding(DipRule.Name, BriefStates.Short, DipRule.TrendGate, 1)]));
        var drawn = new MarkRenderer().BriefRead("XYZ", view);

        Assert.StartsWith("<div class=\"brief-read\" data-ticker=\"XYZ\"><p class=\"brief-summary\" data-summary>", drawn, StringComparison.Ordinal);
        Assert.Equal(["chart", "business", "way-in"], Regex.Matches(drawn, "<section class=\"brief-panel\" data-panel=\"([^\"]+)\">").Select(match => match.Groups[1].Value));
        Assert.Equal(13, Regex.Matches(drawn, "<li data-line=").Count);
        Assert.Contains($"<li data-line=\"size\">Your account is not set: <a href=\"{MarkRenderer.AccountRoute}\">set your account's size and risk</a> to see a size.</li>", drawn, StringComparison.Ordinal);
        Assert.Contains("data-family=\"dip\" data-state=\"short\">", drawn, StringComparison.Ordinal);
        Assert.Contains("<p class=\"brief-watch\">Watch for: its report on 2026-11-05 falls inside a typical hold.</p>", drawn, StringComparison.Ordinal);
        Assert.EndsWith($"<p class=\"brief-foot\"><span>{MarkRenderer.BriefFooter}</span> <a href=\"#/name/XYZ\">The stock's full page</a></p></div>", drawn, StringComparison.Ordinal);
        Assert.Equal("Written by code from tonight's stored figures. No model judged it, and nothing here picks it.", MarkRenderer.BriefFooter);

        // No condition holding: no line of what to watch for at all.
        Assert.DoesNotContain("brief-watch", new MarkRenderer().BriefRead("XYZ", view with { WatchFor = null }), StringComparison.Ordinal);
    }
}
