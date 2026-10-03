using System.Text.Json;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the operator's ruling of 2026-10-02: the night's best three as the pullback's ninth
// rule. Its record keeps the night's first three in the list's own order once its own open trades have kept a
// stock off, worked by hand over three nights and read back through the run page's own query, naming the three
// it keeps; and the family is registered again whole at one instant under the open version, the eight the
// freeze wrote retired and the nine registered, with a tenth refused.
// see: The pullback's ninth rule keeps the night's best three in the list's own order, and the family is registered again whole to add it
public partial class FixtureExpectations
{
    static readonly DateOnly FirstNight = new(2026, 10, 5);
    static readonly DateOnly SecondNight = new(2026, 10, 6);
    static readonly DateOnly ThirdNight = new(2026, 10, 7);

    // One listing of a rule's list with where it stands in its night's order.
    static (OpenTradeListing Listing, ListOrder Order) OnTheList(string ticker, DateOnly night, double rewardToRisk, double strength, int band, string? outcome = null, DateOnly? resolvedOn = null) =>
        (new OpenTradeListing(ticker, night, true, outcome, resolvedOn, ForwardReturnSeries.SetupSessionCap), new ListOrder(rewardToRisk, strength, band));

    // Three nights worked by hand. The first: A at a reward to risk of 3 with the weakest strength, C and B
    // tied at 2.5 and C the stronger, then D and E, so A, C and B are kept and D and E are past the three. A
    // stays open, C won on its own night and B lost on the second. The second: A again, held by its own open
    // trade and passed over, so D, F and E are kept, D and E free since past the three holds nothing, and G
    // is past them. The third: A held again, B free once the night after its loss came, and B, H, I and J tied
    // on reward to risk and strength, B first on its band's strength and H, I and J on the ticker, so B, H
    // and I are kept and J is past them.
    static IReadOnlyList<(OpenTradeListing Listing, ListOrder Order)> ThreeNights() =>
    [
        OnTheList("A", FirstNight, 3.0, 0.1, 1),
        OnTheList("C", FirstNight, 2.5, 0.9, 1, "win", FirstNight),
        OnTheList("B", FirstNight, 2.5, 0.8, 5, "loss", SecondNight),
        OnTheList("D", FirstNight, 2.0, 0.7, 1),
        OnTheList("E", FirstNight, 1.8, 0.95, 9),
        OnTheList("A", SecondNight, 5.0, 0.5, 1),
        OnTheList("D", SecondNight, 2.2, 0.5, 1),
        OnTheList("F", SecondNight, 2.1, 0.5, 1),
        OnTheList("E", SecondNight, 2.0, 0.5, 1),
        OnTheList("G", SecondNight, 1.9, 0.5, 1),
        OnTheList("A", ThirdNight, 4.0, 0.5, 1),
        OnTheList("B", ThirdNight, 2.0, 0.5, 4),
        OnTheList("I", ThirdNight, 2.0, 0.5, 3),
        OnTheList("H", ThirdNight, 2.0, 0.5, 3),
        OnTheList("J", ThirdNight, 2.0, 0.5, 3),
    ];

    [Fact]
    public void TheBestThreeKeepTheNightsFirstThreeInTheListsOwnOrderOnceTheirOwnOpenTradesHaveKeptAStockOff()
    {
        var walked = OpenTrades.WalkTheFirst(ThreeNights(), TheSwingFamily.VariantBestOf);

        IReadOnlyList<(string, DateOnly)> kept = [.. walked.Where(verdict => verdict.Value is null).Select(verdict => verdict.Key).OrderBy(key => key.Night).ThenBy(key => key.Ticker, StringComparer.Ordinal)];

        Assert.Equal(
            [("A", FirstNight), ("B", FirstNight), ("C", FirstNight), ("D", SecondNight), ("E", SecondNight), ("F", SecondNight), ("B", ThirdNight), ("H", ThirdNight), ("I", ThirdNight)],
            kept);

        // A listing past the three answers its own night, a repeat the night of the trade it repeats.
        Assert.Equal(FirstNight, walked[("D", FirstNight)]);
        Assert.Equal(FirstNight, walked[("E", FirstNight)]);
        Assert.Equal(FirstNight, walked[("A", SecondNight)]);
        Assert.Equal(SecondNight, walked[("G", SecondNight)]);
        Assert.Equal(FirstNight, walked[("A", ThirdNight)]);
        Assert.Equal(ThirdNight, walked[("J", ThirdNight)]);
        Assert.Equal(15, walked.Count);

        // The run page's reading: the rule whose registration states three counts the nine kept, and the live
        // rule beside it every listing its own open trades do not repeat, five on the first night among them.
        static CandidateSetupRow Row(string candidate, DateOnly night) => new(candidate, night, null, null, null, null, null, null, false);

        var read = ThreeNights()
            .SelectMany(one => new[] { (Row("best", one.Listing.Night), one.Listing, one.Order), (Row("live", one.Listing.Night), one.Listing, one.Order) })
            .ToArray();
        var setups = ReadApi.CandidateSetupsKept(read, new Dictionary<string, int>(StringComparer.Ordinal) { ["best"] = TheSwingFamily.VariantBestOf });

        Assert.Equal(9, setups.Count(row => row.Candidate == "best"));
        Assert.Equal(5, setups.Count(row => row.Candidate == "live" && row.SessionDate == FirstNight));

        // The count is read off the registration each candidate stands by, and a registration stating none, or
        // nought, keeps every member it fires on.
        static CandidateRow Registered(long id, string candidate, double bestOf, int hour) =>
            new(id, candidate, SwingFilterRule.EvaluatorName, CandidateFamily.Registered, null, new DateTimeOffset(2026, 10, 2, hour, 0, 0, TimeSpan.Zero),
                CandidateEvaluator.Write(new Dictionary<string, double>(StringComparer.Ordinal) { [SwingFilterRule.BestOfParameter] = bestOf }));

        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal) { ["best"] = 3 },
            ReadApi.KeptANight([Registered(1, "best", 2, 10), Registered(2, "live", 0, 10), Registered(3, "best", 3, 11), new(4, "older", "momentum-index-reading", CandidateFamily.Registered, null, new DateTimeOffset(2026, 9, 7, 21, 0, 0, TimeSpan.Zero), "{}")]));
    }

    [Fact]
    public async Task TheBestThreeAreReadBackThroughTheRunPagesOwnQueryOverAConstructedStore()
    {
        using var store = await FamilyStore(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal(0, (await RegisterVerbAt(store, new DateTimeOffset(2026, 9, 7, 22, 0, 0, TimeSpan.Zero), RegisterVerb.TheFamily)).Code);

        var live = SwingFamily.LiveCandidate("1");
        var best = TheSwingFamily.Variant(TheSwingFamily.BestThreeName, "1");

        // One night on which four members pass and both rules fire, each verdict naming section 10's plan, whose
        // reward to risk the row stores: S at 4, R at 3, and Q and P at 2 with Q the stronger and P on the stronger
        // band. The best three keep S, R and Q in the list's own order, and the live rule all four. Read with
        // strength first they would be Q, P and S, with band strength first P, Q and R, by ticker P, Q and R, and
        // on the swing plan's reward to risk, 9 for all four, Q, P and R. The rows the query hands back carry no
        // ticker, so each setup's stored return names it: P 0.1, Q 0.2, R 0.3 and S 0.4.
        foreach (var (ticker, rewardToRisk, strength, band, returned) in new[] { ("P", 2.0, 0.6, 9, 0.1), ("Q", 2.0, 0.7, 3, 0.2), ("R", 3.0, 0.5, 2, 0.3), ("S", 4.0, 0.5, 1, 0.4) })
        {
            var shadow = JsonSerializer.Serialize(new
            {
                candidates = new[] { live, best }.Select(candidate => new
                {
                    candidate,
                    fired = true,
                    values = new Dictionary<string, string> { [SwingFilterRule.PlanValue] = FilterSettings.ClearWord },
                }),
                skipped = Array.Empty<object>(),
            });

            store.Execute(
                "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, trigger_pass, trade, swing_stop, swing_target, exclusions, passed, gates, shadow, clear_reward_to_risk, strength, band_strength, swing_reward_to_risk) " +
                $"VALUES ('{ticker}', '2026-09-14', '1', 'code', 1, 1, 1, 1, 1, '95', '110', '[]', 1, '{{\"gates\":[],\"notes\":[]}}', '{shadow}', {rewardToRisk.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {strength.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {band}, 9);");
            store.Execute(
                "INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even) " +
                $"VALUES ('{ticker}', '2026-09-14', '{ForwardReturnSeries.Clear}', NULL, NULL, {returned.ToString(System.Globalization.CultureInfo.InvariantCulture)}, NULL, NULL);");
        }

        var setups = await new ReadApi(store.DatabaseFile, FixedClock.At(new DateTimeOffset(2026, 9, 15, 2, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates)).CandidateSetupsAsync();

        Assert.Equal([0.1, 0.2, 0.3, 0.4], setups.Where(row => row.Candidate == live).Select(row => row.ReturnPct!.Value).Order());
        Assert.Equal([0.2, 0.3, 0.4], setups.Where(row => row.Candidate == best).Select(row => row.ReturnPct!.Value).Order());
    }

    [Fact]
    public async Task TheFamilyRegisteredAgainRetiresEveryStandingRuleAndRegistersTheNineAtOneInstantUnderTheOpenVersion()
    {
        var threeAt = new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);
        var freezeAt = new DateTimeOffset(2026, 10, 2, 11, 35, 0, TimeSpan.Zero);
        var againAt = new DateTimeOffset(2026, 10, 2, 19, 0, 0, TimeSpan.Zero);
        const string Evidence = "the operator's ruling of 2026-10-02 registering the night's best three";

        // With no swing family rule standing there is no family to register again, and nothing is written.
        using (var none = await FamilyStore(threeAt))
        {
            var (code, said) = await RegisterVerbAt(none, againAt, RegisterVerb.TheFamilyAgain, "--evidence", Evidence);

            Assert.Equal(1, code);
            Assert.Contains("no swing family candidate stands registered", said, StringComparison.Ordinal);
            Assert.Equal(3, Scalar(none, "SELECT COUNT(*) FROM candidate_register;"));
        }

        // With no version open the live filter's candidate has no settings to state.
        using (var closed = await FamilyStore(threeAt))
        {
            Assert.Equal(0, (await RegisterVerbAt(closed, freezeAt, RegisterVerb.TheFamily)).Code);
            closed.Execute("UPDATE filter_version SET closed_at = '2026-10-02T12:00:00Z';");

            var (code, said) = await RegisterVerbAt(closed, againAt, RegisterVerb.TheFamilyAgain, "--evidence", Evidence);

            Assert.Equal(1, code);
            Assert.Contains("no filter version is open", said, StringComparison.Ordinal);
            Assert.Equal(15, Scalar(closed, "SELECT COUNT(*) FROM candidate_register;"));
        }

        // The operator's store as the freeze left it: the three retired and the eight the freeze wrote standing.
        using var store = await FamilyStore(threeAt);

        foreach (var (retired, minute) in TheThreeCandidates.All.Select((one, index) => (one, index)))
        {
            Assert.Equal(0, (await RegisterVerbAt(store, threeAt.AddHours(1).AddMinutes(minute), "--retire", retired.Candidate, "--evidence", "retired before the family registered")).Code);
        }

        var eight = await new CandidateRegistrar(FixedClock.At(freezeAt, SessionZones.UnitedStates), store.DatabaseFile)
            .RegisterTogetherAsync([.. TheSwingFamily.For("1", Ruled).Take(8)], "register-the-eight");

        Assert.Equal(CandidateRegistrar.Registered, eight.Outcome);
        Assert.Contains("family of 8 of 9", eight.Detail, StringComparison.Ordinal);

        var before = Scalar(store, "SELECT COUNT(*) FROM candidate_register;");
        var (written, told) = await RegisterVerbAt(store, againAt, RegisterVerb.TheFamilyAgain, "--evidence", Evidence);

        Assert.Equal(0, written);
        Assert.Contains("retired 8 and registered 9 at one instant, family of 9 of 9", told, StringComparison.Ordinal);

        // Seventeen rows at one instant, no version opened, and the best three stating three.
        Assert.Equal(before + 17, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
        Assert.Equal(17, Scalar(store, "SELECT COUNT(*) FROM candidate_register WHERE registered_at = '2026-10-02T19:00:00Z';"));
        Assert.Equal(1, Scalar(store, "SELECT COUNT(*) FROM filter_version;"));

        var best = TheSwingFamily.Variant(TheSwingFamily.BestThreeName, "1");
        var parameters = CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{best.Replace("'", "''", StringComparison.Ordinal)}' AND event = 'registered';"));

        Assert.Equal(3, parameters[SwingFilterRule.BestOfParameter]);

        // A tenth rule of the pullback's family is refused.
        var tenth = await new CandidateRegistrar(FixedClock.At(againAt.AddHours(1), SessionZones.UnitedStates), store.DatabaseFile).RegisterAsync(
            "the swing filter keeping the night's best four",
            TheSwingFamily.Rule,
            TheSwingFamily.Test,
            SwingFilterRule.EvaluatorName,
            SwingFilterRule.ParametersOf(Ruled, bestOf: 4),
            "register-tenth");

        Assert.Equal(CandidateRegistrar.Refused, tenth.Outcome);
        Assert.Contains("9 candidates of the pullback family already stand registered, which is the maximum family of 9", tenth.Detail, StringComparison.Ordinal);
    }
}
