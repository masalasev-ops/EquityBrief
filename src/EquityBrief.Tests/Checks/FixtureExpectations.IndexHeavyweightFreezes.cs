using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 15.5's second pull request: the sector heavyweights frozen on the S&P 400 or 600 in either
// design, the live rule and its variants registered at one instant or none, each variant worked by hand beside the live
// rule on both sides of the setting it moves, each rule keeping a book of its own and selling on its own exits.
// see: A rule of the S&P 400's or 600's sector heavyweights keeps a book of its own in either design, read by the index families' step
public partial class FixtureExpectations
{
    // The row the heavyweights' freezes add that this check reaches: section 18's on a rebalance waiting for what its
    // design reads.
    internal static readonly string[] IndexHeavyweightFreezeClaims =
    [
        CheckReach.Key(Scope.FailureTable, "A registered heavyweights rule's rebalance on a night missing what its design reads"),
    ];

    static IndexHeavyweightCandidate HeavyweightIndexEvaluator(string name) => (IndexHeavyweightCandidate)CandidateEvaluators.Find(name)!;

    // A design (b) rule as a variant of the design (a) live rule states it, its parameters separated by commas as one
    // variant's are: every design (a) dial nought and its own three.
    static string FollowersVariant(int window, int industries, int members) =>
        FormattableString.Invariant($"design=1,largest=0,lookBack=0,leaders=0,highBeta=0,soldOnLeading=0,soldUnderAverage=0,window={window},industries={industries},members={members}");

    [Fact]
    public async Task AnIndexHeavyweightsFreezeRegistersItsLiveRuleAndItsVariantsInEitherDesignAtOneInstantOrNone()
    {
        using var store = new TemporaryStore().Migrated();
        var heavyweights = HeavyweightIndexEvaluator("heavyweight-400");
        var live = Typed(IndexRules.Provisional(heavyweights));
        var freezeAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        // Refused, each writing nothing: a design that is neither, a look-back its sweep did not read, a design (a) rule
        // stating a design (b) dial, a floor the heavyweights' sweeps did not read, a design (a) rule selling on neither
        // exit, and a design (b) variant at a window its sweep did not read.
        foreach (var ((parameters, variants, expected), at) in new (string Parameters, string? Variants, string Said)[]
        {
            (live.Replace("design=0", "design=2", StringComparison.Ordinal), null, "'design' is 2"),
            (live.Replace("lookBack=251", "lookBack=100", StringComparison.Ordinal), null, "'lookBack' is 100"),
            (live.Replace("window=0", "window=21", StringComparison.Ordinal), null, "'window' is 21, a dial of the other design"),
            (live.Replace("floors=1", "floors=0.5", StringComparison.Ordinal), null, "'floors' is 0.5, and the heavyweights' sweeps read it at 1 and 2 alone"),
            (live.Replace("soldOnLeading=1", "soldOnLeading=0", StringComparison.Ordinal), null, "the registration states neither"),
            (live, FollowersVariant(42, 5, 1), "'window' is 42"),
        }.Select((refusal, at) => (refusal, at)))
        {
            var (code, refused) = await RegisterVerbAt(store, freezeAt.AddSeconds(-1 - at), [RegisterVerb.IndexFamily, "heavyweight", "--index", "MID", "--parameters", parameters, .. variants is null ? Array.Empty<string>() : ["--variants", variants]]);

            Assert.Equal(1, code);
            Assert.Contains(expected, refused, StringComparison.Ordinal);
        }

        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        // Registered at one instant: the live rule at the index's own book's settings, design (a) beside one leader a
        // sector, and design (b) at a month's window, the five strongest industries and one member each.
        var (written, said) = await RegisterVerbAt(store, freezeAt, RegisterVerb.IndexFamily, "heavyweight", "--index", "MID", "--parameters", live, "--variants", "leaders=1;" + FollowersVariant(21, 5, 1));

        Assert.Equal(0, written);
        Assert.Contains("registered 3 at one instant, family of 3 of 9", said, StringComparison.Ordinal);
        Assert.Equal(
            [
                $"the live S&P 400 heavyweights rule of each sector's 10 largest members leading it over 251 sessions, 2 leader(s) a sector with a beta of at least 1, sold on no longer leading, the profit gate|heavyweight-400|{heavyweights.Version}",
                $"the S&P 400 heavyweights rule of each sector's 10 largest members leading it over 251 sessions, 1 leader(s) a sector with a beta of at least 1, sold on no longer leading, the profit gate|heavyweight-400|{heavyweights.Version}",
                $"the S&P 400 heavyweights rule following the S&P 500's 5 strongest industries over 21 sessions, 1 member(s) an industry, sold where it is no longer bought, the profit gate|heavyweight-400|{heavyweights.Version}",
            ],
            TextRows(store, "SELECT candidate || '|' || evaluator || '|' || evaluator_version FROM candidate_register ORDER BY id;"));
        Assert.Equal(1, Scalar(store, "SELECT COUNT(DISTINCT registered_at) FROM candidate_register;"));
        Assert.Equal("heavyweight on the S&P 400", CandidateFamily.SetupFamilyOf("heavyweight-400"));
        Assert.Equal(IndexRules.HeavyweightRuleWords, TextRows(store, "SELECT DISTINCT rule FROM candidate_register;").Single());

        // The provisional rule is the index's own book's setting, on design (a)'s grid.
        Assert.Equal(IndexHeavyweights.Provisional, IndexRules.HeavyweightSettingOf(IndexRules.Provisional(heavyweights)));
        Assert.Null(IndexRules.Refusal(heavyweights, IndexRules.Provisional(heavyweights)));
    }

    // One sector of eleven members on a rebalance as the sweep's own reading takes it, each by value from A, the largest,
    // to Z, the smallest, every one closing above its 50-day average and that above its 200-day: A returns 5, 20 and 60 per
    // cent over 63, 126 and 251 sessions with a beta of 1.5; B 40, 30 and 50 with 1.2; G 6, 25 and 70 with 0.7; C 2, 5 and
    // 10 with 1.1; E and four more each -10, -10 and -20 with 1; D, sixth by value, 10, 35 and 90 with 1.3; and Z, eleventh,
    // 10, 20 and 120 with 1.4.
    static (HeavyweightSession Session, IReadOnlyDictionary<string, int> Names) HeavyweightSector()
    {
        var members = new (string Ticker, decimal Value, double[] Returns, double Beta)[]
        {
            ("A", 1200m, [0.05, 0.20, 0.60], 1.5),
            ("B", 1100m, [0.40, 0.30, 0.50], 1.2),
            ("G", 1000m, [0.06, 0.25, 0.70], 0.7),
            ("C", 900m, [0.02, 0.05, 0.10], 1.1),
            ("E", 800m, [-0.10, -0.10, -0.20], 1.0),
            ("D", 700m, [0.10, 0.35, 0.90], 1.3),
            ("F1", 600m, [-0.10, -0.10, -0.20], 1.0),
            ("F2", 500m, [-0.10, -0.10, -0.20], 1.0),
            ("F3", 400m, [-0.10, -0.10, -0.20], 1.0),
            ("F4", 300m, [-0.10, -0.10, -0.20], 1.0),
            ("Z", 100m, [0.10, 0.20, 1.20], 1.4),
        };

        Assert.Equal([63, 126, 251], HeavyweightSweep.LookBacks);

        return (
            new HeavyweightSession(
                0,
                [
                    .. members.Select((member, at) => new HeavyweightCandidate(
                        at,
                        new HeavyweightMember(member.Ticker, member.Ticker, "Industrials", member.Value, 1_000_000m, null, 110, 105, 100, member.Beta),
                        [.. member.Returns.Select(one => (double?)one)])),
                ],
                []),
            members.Select((member, at) => (member.Ticker, at)).ToDictionary(pair => pair.Ticker, pair => pair.at, StringComparer.Ordinal));
    }

    [Fact]
    public void EachDesignAVariantReadsItsLeadersAtItsOwnSettingOnBothSidesOfTheDialItMoves()
    {
        var (session, names) = HeavyweightSector();
        var live = IndexRules.Provisional(HeavyweightIndexEvaluator("heavyweight-400"));
        var tickers = names.ToDictionary(pair => pair.Value, pair => pair.Key);

        string Buys(params (string Name, double Value)[] moved)
        {
            var p = new Dictionary<string, double>(live, StringComparer.Ordinal);

            foreach (var (name, value) in moved)
            {
                p[name] = value;
            }

            Assert.Null(IndexRules.Refusal(HeavyweightIndexEvaluator("heavyweight-400"), p));

            return string.Join(",", HeavyweightSweep.Read(session, IndexRules.HeavyweightSettingOf(p), names).Buys.Select(name => tickers[name]).Order(StringComparer.Ordinal));
        }

        // The live rule, the ten largest over 251 sessions with two leaders and a beta of at least 1: the sector's mean is
        // 3.0 / 11, so D leads by 0.627, A by 0.327 and B by 0.227, G's 0.427 failing its beta and Z eleventh by value.
        Assert.Equal("A,D", Buys());

        // One leader: D alone.
        Assert.Equal("D", Buys((IndexHeavyweightCandidate.LeadersParameter, 1)));

        // Every company: Z, leading by 0.927, joins the cut and is bought with D.
        Assert.Equal("D,Z", Buys((IndexHeavyweightCandidate.LargestParameter, 0)));

        // The five largest: A, B, G, C and E, of which A and B lead with a beta of 1 or more.
        Assert.Equal("A,B", Buys((IndexHeavyweightCandidate.LargestParameter, 5)));

        // No beta floor: G's lead of 0.427 is second.
        Assert.Equal("D,G", Buys((IndexHeavyweightCandidate.BetaParameter, 0)));

        // 63 sessions: the mean is 0.23 / 11, B leading by 0.379 and D by 0.079 over A's 0.029.
        Assert.Equal("B,D", Buys((IndexHeavyweightCandidate.LookBackParameter, 63)));

        // 126 sessions: the mean is 0.85 / 11, D leading by 0.273 and B by 0.223 over A's 0.123.
        Assert.Equal("B,D", Buys((IndexHeavyweightCandidate.LookBackParameter, 126)));

        // And each exit's pair is the setting's exit.
        Assert.Equal(
            [HeavyweightExit.Drop, HeavyweightExit.Break, HeavyweightExit.Both],
            new[] { (1.0, 0.0), (0.0, 1.0), (1.0, 1.0) }.Select(pair => IndexRules.HeavyweightSettingOf(new Dictionary<string, double>(live, StringComparer.Ordinal) { [IndexHeavyweightCandidate.LeadingExitParameter] = pair.Item1, [IndexHeavyweightCandidate.AverageExitParameter] = pair.Item2 }).Exit));
    }

    // The index heavyweights' store widened for the rule books: L2 trading a hundred thousand shares a day, about $16
    // million of dollar volume, clearing the S&P 400's floor and not twice it; and each member's readings on the books'
    // first night under its industry, L1 and L2 in Machinery and L3 in Airlines, L2's coverage failing, with four S&P 500 industries'
    // returns on rows of their own: every industry's over a month and a quarter, Software 0.20 and 0.30, Semiconductors
    // 0.18 and 0.28, Banks 0.16 and 0.26, Insurance 0.14 and 0.24, Machinery 0.12 and 0.01, Airlines 0.10 and 0.22, and
    // Media -0.05 over both.
    static (TemporaryStore Store, Dictionary<string, decimal[]> Closes) HeavyweightRuleStore()
    {
        var (store, closes) = HeavyweightIndexStore();

        store.Execute("UPDATE bar SET volume = 100000 WHERE ticker = 'L2';");
        IndustryReadings(store, "2026-10-01", ("Machinery", 0.12, 0.01), ("Airlines", 0.10, 0.22));

        return (store, closes);
    }

    // Each member's readings on a night under its industry, L1's and L3's coverage passing and L2's failing, and the four
    // S&P 500 industries' and Media's returns beside them, Machinery's and Airlines' as given.
    static void IndustryReadings(TemporaryStore store, string night, (string Industry, double Month, double Quarter) machinery, (string Industry, double Month, double Quarter) airlines)
    {
        foreach (var (index, ticker, coverage, (industry, month, quarter)) in new[]
        {
            ("MID", "L1", 1, machinery),
            ("MID", "L2", 0, machinery),
            ("MID", "L3", 1, airlines),
            ("GSPC", "S1", 1, ("Software", 0.20, 0.30)),
            ("GSPC", "S2", 1, ("Semiconductors", 0.18, 0.28)),
            ("GSPC", "S3", 1, ("Banks", 0.16, 0.26)),
            ("GSPC", "S4", 1, ("Insurance", 0.14, 0.24)),
            ("GSPC", "S5", 1, ("Media", -0.05, -0.05)),
        })
        {
            store.Execute(FormattableString.Invariant(
                $"INSERT INTO member_reading (index_code, session_date, ticker, profit, coverage, industry, industry_month, industry_quarter) VALUES ('{index}', '{night}', '{ticker}', 1, {coverage}, '{industry}', {month}, {quarter});"));
        }
    }

    // SPY's closes on a night and 21 and 63 calendar sessions before it, the store holding a bar every calendar day.
    static void SpyCloses(TemporaryStore store, DateOnly night, decimal now, decimal month, decimal quarter) =>
        store.Execute(
            "INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) VALUES " +
            string.Join(", ", new[] { (night.AddDays(-63), quarter), (night.AddDays(-21), month), (night, now) }.Select(one => FormattableString.Invariant($"('SPY', '{Day(one.Item1)}', '{one.Item2}', '{one.Item2}', '{one.Item2}', '{one.Item2}', 'test')"))) + ";");

    [Fact]
    public async Task EachFrozenHeavyweightsRuleKeepsABookOfItsOwnAndSellsOnItsOwnExits()
    {
        var (store, closes) = HeavyweightRuleStore();

        using (store)
        {
            var heavyweights = HeavyweightIndexEvaluator("heavyweight-400");
            var freezeAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

            // The live rule at the index's own book's settings and eight variants: the cover, twice the floor, one leader,
            // and sold under the average alone; and design (b) at a month's window, the five strongest industries and one
            // member each, at a quarter's window, at every leading industry, and at two members each. Registered 1 to 9.
            Assert.Equal(0, (await RegisterVerbAt(
                store,
                freezeAt,
                RegisterVerb.IndexFamily, "heavyweight", "--index", "MID", "--parameters", Typed(IndexRules.Provisional(heavyweights)),
                "--variants",
                string.Join(";", "quality=2", "floors=2", "leaders=1", "soldOnLeading=0,soldUnderAverage=1", FollowersVariant(21, 5, 1), FollowersVariant(63, 5, 1), FollowersVariant(21, 0, 1), FollowersVariant(21, 5, 2)))).Code);

            var register = await new CandidateRegistrar(FixedClock.At(freezeAt, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();

            async Task Night(DateOnly session, string run) =>
                await new IndexFamilies(FixedClock.At(new DateTimeOffset(session.ToDateTime(new TimeOnly(23, 40)), TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
                    .RunAsync(run, default, register, freezeAt.AddHours(1));

            IReadOnlyList<string> Books() => TextRows(
                store,
                "SELECT r.id || '|' || h.ticker || '|' || h.entered_on || '|' || COALESCE(h.ended_on, 'open') || '|' || COALESCE(h.reason, 'held') FROM index_heavyweight_rule_holding h " +
                "JOIN candidate_register r ON r.candidate = h.candidate ORDER BY r.id, h.ticker;");

            // Over the month before the night, L1 and L2 in Machinery each read their own return: the stronger is design
            // (b)'s one member, worked from the closes the store holds and not from the night.
            var stronger = closes["L1"][260] / closes["L1"][239] > closes["L2"][260] / closes["L2"][239] ? "L1" : "L2";
            var floor = MemberReadings.DollarVolumeFloor("MID")!.Value;
            var l2Dollars = Enumerable.Range(211, 50).Average(at => closes["L2"][at] * 100_000m);

            Assert.InRange(l2Dollars, floor, floor * 2);

            // The first night of a month and every rule's first, so each rebalances where the night holds what its design
            // reads. The live rule and the one selling under the average buy L1 and L2, the index's own book's leaders; the
            // cover leaves L2 off on its coverage, twice the floor on its dollars, and one leader keeps L1, the larger lead.
            // The store holds no close of SPY, so design (b) waits, buying nothing and writing no rebalance.
            await Night(new DateOnly(2026, 10, 1), "first");

            IReadOnlyList<string> designA =
            [
                "1|L1|2026-10-01|open|held", "1|L2|2026-10-01|open|held",
                "2|L1|2026-10-01|open|held",
                "3|L1|2026-10-01|open|held",
                "4|L1|2026-10-01|open|held",
                "5|L1|2026-10-01|open|held", "5|L2|2026-10-01|open|held",
            ];

            Assert.Equal(designA, Books());
            Assert.Equal(["1", "2", "3", "4", "5"], TextRows(store, "SELECT r.id FROM index_heavyweight_rule_night n JOIN candidate_register r ON r.candidate = n.candidate ORDER BY r.id;"));

            // The live rule reads at the index's own book's settings, so each of its holdings is bought with its lead, its
            // return over the 251 sessions less the mean of the three members' returns from the close 251 sessions before.
            var returns = new[] { "L1", "L2", "L3" }.ToDictionary(ticker => ticker, ticker => Statistic.FromRatio(closes[ticker][260] / closes[ticker][9]) - 1.0, StringComparer.Ordinal);

            foreach (var ticker in new[] { "L1", "L2" })
            {
                Assert.Equal(
                    returns[ticker] - returns.Values.Average(),
                    double.Parse(TextRows(store, $"SELECT printf('%.12f', h.lead) FROM index_heavyweight_rule_holding h JOIN candidate_register r ON r.candidate = h.candidate WHERE r.id = 1 AND h.ticker = '{ticker}';").Single(), CultureInfo.InvariantCulture),
                    9);
            }

            // With SPY's closes of 100, 101 and 102 stored 63 and 21 sessions before the night and on it, the night run again
            // writes what it wrote for design (a) and design (b) rebalances: at a month's window Machinery leads SPY's 0.0099
            // fifth, so it buys Machinery's stronger member, or both at two a industry, and Airlines sixth joins it at
            // every leading industry; at a quarter's window Machinery trails SPY's 0.02 and Airlines is fifth, so it buys L3.
            SpyCloses(store, new DateOnly(2026, 10, 1), 102m, 101m, 100m);
            await Night(new DateOnly(2026, 10, 1), "first-again");

            IReadOnlyList<string> first =
            [
                "1|L1|2026-10-01|open|held", "1|L2|2026-10-01|open|held",
                "2|L1|2026-10-01|open|held",
                "3|L1|2026-10-01|open|held",
                "4|L1|2026-10-01|open|held",
                "5|L1|2026-10-01|open|held", "5|L2|2026-10-01|open|held",
                $"6|{stronger}|2026-10-01|open|held",
                "7|L3|2026-10-01|open|held",
                .. new[] { $"8|{stronger}|2026-10-01|open|held", "8|L3|2026-10-01|open|held" }.Order(StringComparer.Ordinal),
                "9|L1|2026-10-01|open|held", "9|L2|2026-10-01|open|held",
            ];

            Assert.Equal(first, Books());
            Assert.Equal(9, Scalar(store, "SELECT COUNT(*) FROM index_heavyweight_rule_night WHERE session_date = '2026-10-01';"));

            // Every design (a) holding stores a lead and no design (b) holding does, that design reading none over a sector.
            Assert.Equal(
                ["1|2", "2|1", "3|1", "4|1", "5|2", "6|0", "7|0", "8|0", "9|0"],
                TextRows(store, "SELECT r.id || '|' || COUNT(h.lead) FROM index_heavyweight_rule_holding h JOIN candidate_register r ON r.candidate = h.candidate GROUP BY r.id ORDER BY r.id;"));
            Assert.Equal(["L1", "L2"], TextRows(store, "SELECT ticker FROM index_heavyweight_rule_holding h JOIN candidate_register r ON r.candidate = h.candidate WHERE r.id = 1 ORDER BY ticker;"));

            // The next night, the same month's, with no rebalance: L1 closes at half the night before's, under its 200-day
            // average. The rule selling under the average sells it there; every other rule holds it, design (b) selling
            // only at a rebalance.
            var crash = Math.Round(closes["L1"][260] / 2, 4);

            StoreYear(store, "L1", [new FamilyBar(new DateOnly(2026, 10, 2), crash, crash, crash, 1_000_000)]);
            StoreYear(store, "L2", [new FamilyBar(new DateOnly(2026, 10, 2), closes["L2"][261], closes["L2"][261], closes["L2"][261], 100_000)]);
            StoreYear(store, "L3", [new FamilyBar(new DateOnly(2026, 10, 2), closes["L3"][261], closes["L3"][261], closes["L3"][261], 1_000_000)]);

            await Night(new DateOnly(2026, 10, 2), "second");

            Assert.Equal([.. first.Select(row => row == "5|L1|2026-10-01|open|held" ? $"5|L1|2026-10-01|2026-10-02|{IndexHeavyweights.UnderTheAverage}" : row)], Books());

            // Its result is half less one, its size cut's the mean of L1's half, L2's and L3's closes over the night
            // before's, less one, and its round trip half the published spread at each end of a company read in the $1 to 2
            // billion band, both closes $40 and over: 0.129 per cent of each over the buy.
            var entry = closes["L1"][260];
            var ended = TextRows(store, "SELECT printf('%.9f', h.result) || '|' || printf('%.9f', h.cut_return) || '|' || printf('%.9f', h.cost) FROM index_heavyweight_rule_holding h JOIN candidate_register r ON r.candidate = h.candidate WHERE r.id = 5 AND h.ticker = 'L1';").Single().Split('|');
            var cut = new[] { Statistic.FromRatio(crash / entry), Statistic.FromRatio(closes["L2"][261] / closes["L2"][260]), Statistic.FromRatio(closes["L3"][261] / closes["L3"][260]) }.Average() - 1.0;

            Assert.Equal((Statistic.FromRatio(crash / entry) - 1.0).ToString("F9", CultureInfo.InvariantCulture), ended[0]);
            Assert.Equal(cut.ToString("F9", CultureInfo.InvariantCulture), ended[1]);
            Assert.Equal(Statistic.FromRatio((0.129m / 200m * entry + 0.129m / 200m * crash) / entry).ToString("F9", CultureInfo.InvariantCulture), ended[2]);

            // The next month's first night, the members flat since and SPY at 100, 102 and 102 63 and 21 sessions before and
            // on it, Machinery's return over the month -0.10 and Airlines' 0.12 and the quarter's as before. The
            // fund's closes stored to the day before alone: every design (a) rule here reads a beta, so each waits, holding
            // what it held, while design (b) rebalances. At a month's window Airlines is fifth and Machinery no longer
            // leads, so the first sells its Machinery member and buys L3, every leading industry keeps L3 and sells its
            // Machinery member, and two a industry sells L1 and L2 and buys L3; at a quarter's window L3 is held.
            var calendar = Enumerable.Range(1, 30).Select(day => new DateOnly(2026, 10, 2).AddDays(day)).ToArray();

            foreach (var (ticker, close, volume) in new[] { ("L1", crash, 1_000_000L), ("L2", closes["L2"][261], 100_000L), ("L3", closes["L3"][261], 1_000_000L) })
            {
                StoreYear(store, ticker, [.. calendar.Select(day => new FamilyBar(day, close, close, close, volume))]);
            }

            var fund = TextRows(store, "SELECT close FROM market_bar WHERE series = 'IJH' AND session_date = '2026-10-02';").Single();

            store.Execute(
                "INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) VALUES " +
                string.Join(", ", calendar[..^1].Select(day => $"('IJH', '{Day(day)}', '{fund}', '{fund}', '{fund}', '{fund}', 'test')")) + ";");
            SpyCloses(store, new DateOnly(2026, 11, 1), 102m, 102m, 100m);
            IndustryReadings(store, "2026-11-01", ("Machinery", -0.10, 0.01), ("Airlines", 0.12, 0.22));

            await Night(new DateOnly(2026, 11, 1), "third");

            IReadOnlyList<string> designB =
            [
                .. new[] { $"6|{stronger}|2026-10-01|2026-11-01|{IndexHeavyweights.NoLongerBought}", "6|L3|2026-11-01|open|held" }.Order(StringComparer.Ordinal),
                "7|L3|2026-10-01|open|held",
                .. new[] { $"8|{stronger}|2026-10-01|2026-11-01|{IndexHeavyweights.NoLongerBought}", "8|L3|2026-10-01|open|held" }.Order(StringComparer.Ordinal),
                $"9|L1|2026-10-01|2026-11-01|{IndexHeavyweights.NoLongerBought}", $"9|L2|2026-10-01|2026-11-01|{IndexHeavyweights.NoLongerBought}", "9|L3|2026-11-01|open|held",
            ];

            Assert.Equal([.. first.Where(row => row[0] <= '5').Select(row => row == "5|L1|2026-10-01|open|held" ? $"5|L1|2026-10-01|2026-10-02|{IndexHeavyweights.UnderTheAverage}" : row), .. designB], Books());
            Assert.Equal(["6|1|1", "7|0|0", "8|0|1", "9|1|2"], TextRows(store, "SELECT r.id || '|' || n.bought || '|' || n.sold FROM index_heavyweight_rule_night n JOIN candidate_register r ON r.candidate = n.candidate WHERE n.session_date = '2026-11-01' ORDER BY r.id;"));

            // The fund's close on the night stored, the night run again rebalances design (a) too. L1, its year's return
            // fallen under the sector's mean, no longer leads, and L2 alone does: the live rule sells L1 and keeps L2; the
            // cover and twice the floor sell L1 and buy nothing, L2's coverage failing and its dollars short of twice the
            // floor; one leader sells L1 and buys L2; and the rule selling under the average sells nothing for not leading
            // and holds L2.
            store.Execute(FormattableString.Invariant($"INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) VALUES ('IJH', '2026-11-01', '{fund}', '{fund}', '{fund}', '{fund}', 'test');"));

            await Night(new DateOnly(2026, 11, 1), "third-again");

            Assert.Equal(
                [
                    $"1|L1|2026-10-01|2026-11-01|{IndexHeavyweights.NoLongerTheLeader}", "1|L2|2026-10-01|open|held",
                    $"2|L1|2026-10-01|2026-11-01|{IndexHeavyweights.NoLongerTheLeader}",
                    $"3|L1|2026-10-01|2026-11-01|{IndexHeavyweights.NoLongerTheLeader}",
                    $"4|L1|2026-10-01|2026-11-01|{IndexHeavyweights.NoLongerTheLeader}", "4|L2|2026-11-01|open|held",
                    $"5|L1|2026-10-01|2026-10-02|{IndexHeavyweights.UnderTheAverage}", "5|L2|2026-10-01|open|held",
                    .. designB,
                ],
                Books());
            Assert.Equal(
                ["1|0|1", "2|0|1", "3|0|1", "4|1|1", "5|0|0", "6|1|1", "7|0|0", "8|0|1", "9|1|2"],
                TextRows(store, "SELECT r.id || '|' || n.bought || '|' || n.sold FROM index_heavyweight_rule_night n JOIN candidate_register r ON r.candidate = n.candidate WHERE n.session_date = '2026-11-01' ORDER BY r.id;"));

            // And the index's own book kept beside them as before, its rows untouched by any rule's.
            Assert.Equal(["L1|2026-10-01", "L2|2026-10-01"], TextRows(store, "SELECT ticker || '|' || entered_on FROM index_heavyweight_holding WHERE index_code = 'MID' ORDER BY ticker;"));

            // L2 leaves the index on the session after, holding no bar since: each rule holding it is sold at its last close
            // as a member, the close of the session it was last carried to, and not at its buy, the live rule's and the
            // one selling under the average bought a month before at a close of their own.
            store.Execute("UPDATE membership SET \"left\" = '2026-11-02' WHERE ticker = 'L2';");
            StoreYear(store, "L1", [new FamilyBar(new DateOnly(2026, 11, 2), crash, crash, crash, 1_000_000)]);
            StoreYear(store, "L3", [new FamilyBar(new DateOnly(2026, 11, 2), closes["L3"][261], closes["L3"][261], closes["L3"][261], 1_000_000)]);

            await Night(new DateOnly(2026, 11, 2), "fourth");

            Assert.Equal(
                [
                    FormattableString.Invariant($"1|2026-10-01|2026-11-01|{closes["L2"][261]}|{IndexHeavyweights.LeftTheIndex}"),
                    FormattableString.Invariant($"4|2026-11-01|2026-11-01|{closes["L2"][261]}|{IndexHeavyweights.LeftTheIndex}"),
                    FormattableString.Invariant($"5|2026-10-01|2026-11-01|{closes["L2"][261]}|{IndexHeavyweights.LeftTheIndex}"),
                ],
                TextRows(store, "SELECT r.id || '|' || h.entered_on || '|' || h.ended_on || '|' || h.exit_close || '|' || h.reason FROM index_heavyweight_rule_holding h JOIN candidate_register r ON r.candidate = h.candidate WHERE h.ticker = 'L2' AND h.ended_on = '2026-11-01' AND h.reason = 'left the index' ORDER BY r.id;"));
            Assert.NotEqual(closes["L2"][260], closes["L2"][261]);
        }
    }
}
