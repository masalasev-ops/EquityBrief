using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Improving;

namespace EquityBrief.Tests.Reading;

// read-surface, 22.15: the Fundamentals family frozen on the S&P 400 of the universes' store at the setting the operator
// answered, read back off the rendered pages: its card on Tonight drawn by its live rule live since its freeze, the line
// every frozen card draws from its own recorded answer counted in trades with the words the operator's answer adds, its
// rule's sentences stating the dials its setting asks and its fold saying its settings were fixed that day, in the
// reader's words; and paper trading keeping an account for the live rule and one for each version, the family's tile last
// and both pickers listing it.
// see: Each index's Fundamentals rule is frozen on the operator's answers of 2026-10-28 with its one-step neighbours as versions
// see: A Fundamentals rule is registered as the family on its index and kept by the family's own step alone
public partial class ReadSurface
{
    static readonly ImprovingSweepSetting MidImprovingFrozen = new(7, true, true, false, 7, 63);

    // The S&P 400's history test as its run recorded it before the freeze: the proposal's 517 trades at 1.18 points a trade
    // ahead of the members after costs, ahead in 5 of the 8 years it held trades in, 20 of its 144 settings meeting the
    // floors where luck alone passes 20.8, and the tester not passed at an adjusted p of 0.69.
    static readonly AnswerFigures MidImproving = new(
        MidImprovingFrozen.Key,
        517,
        0.0118,
        YearsOf(0.021, -0.009, 0.030, 0.025, 0.021, -0.005, -0.011, 0.011),
        0.0035,
        144,
        20,
        20.8125,
        "20260930T100000Z",
        0.69,
        false);

    // The line those figures give, worked by hand: its settings fixed on the day of the freeze after a test from 2019, 517
    // trades, 0.0118 of the buy as 1.2 points ahead, ahead in 5 of the 8 years it had trades, 20 settings meeting the floors
    // under the 20.8 luck alone passes, and the words the operator's answer adds beneath every frozen card of the family.
    static readonly string MidImprovingFixed =
        "<p class=\"family-frozen improving-fixed\" data-fixed=\"2026-09-30\" data-trades=\"517\" data-edge=\"0.0118\" data-years=\"8\" data-ahead=\"5\" data-beat-luck=\"false\" data-tester-passed=\"false\">" +
        "Its settings were fixed on 30 Sep after its history test from 2019: 517 trades, 1.2 points a trade ahead of an equal-weight hold of the index's stocks after costs, ahead in 5 of the 8 years it had trades. " +
        "No setting beat luck, so treat it as a starting point that has not yet proved itself. " + ImprovingRule.ReadingsCaveat + "</p>";

    static string MidImprovingLive => Core.Candidates.FamilyRecords.LivePrefix + "S&P 400 Fundamentals rule at " + ImprovingRules.Short(MidImprovingFrozen);

    static string MidImprovingVersion(ImprovingSweepSetting setting) => "the S&P 400 Fundamentals rule at " + ImprovingRules.Short(setting);

    // The universes' store with the S&P 400's answer recorded, its Fundamentals family frozen at the answered setting on the
    // day before the night, and its two nights kept under the live rule and under the version holding at 6.
    static async Task<TemporaryStore> FrozenImprovingStore()
    {
        var store = UniversesStore();

        RecordFigured(store, "20260930T100000Z", ImprovingRule.Name, null, "proposed " + MidImprovingFrozen.Key, "2026-09-30T11:00:00Z", MidImproving);

        var (code, said) = await FixtureExpectations.RegisterVerbAt(store, new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), RegisterVerb.IndexFamily, ImprovingRule.Name, "--index", "MID", "--parameters", "buy=7,flag=1,rise=1,faster=0,hold=7,limit=63");

        Assert.True(code == 0, said);

        ImprovingNightOn(store, rule: MidImprovingLive, setting: MidImprovingFrozen);
        ImprovingNightOn(store, rule: MidImprovingVersion(MidImprovingFrozen with { HoldInNine = 6 }), setting: MidImprovingFrozen with { HoldInNine = 6 }, scores: false);

        return store;
    }

    [Fact]
    public async Task AFrozenFundamentalsCardDrawsItsLiveRulesRowsTheDayItWentLiveAndItsRecordedAnswerWithTheReadingsWords()
    {
        using var store = await FrozenImprovingStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var section = Assert.Single(Blocks(page, "<section class=\"card setup-card\" id=\"setup-improving\".*?</section></div></section>"));

        // Live since the freeze, never not yet tested, and its rows the live rule's.
        Assert.Contains("<span class=\"status-pill live\" data-live-since=\"2026-09-30\">Live since 30 Sep 2026</span>", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Not yet tested", section, StringComparison.Ordinal);
        Assert.Contains("data-family=\"improving\" data-index=\"MID\" data-bought=\"1\"", section, StringComparison.Ordinal);

        // The line its recorded answer gives, word for word with the operator's words beneath it; its sentences stating the
        // dials its setting asks, a flag first and the growth not; and its fold saying its settings were fixed that day.
        Assert.Contains(MidImprovingFixed, section, StringComparison.Ordinal);
        Assert.Contains("lifts its score to at least 7 of the 9 checks that apply and above the score of the filing before it. A flag must have stood the night before.", section, StringComparison.Ordinal);
        Assert.Contains("Kept while its score stays at 7 of 9 or better. Sold at the close of the night a new filing drops it below 7, at the close of its 63th trading day", section, StringComparison.Ordinal);
        Assert.Contains("Its settings were fixed on 30 Sep 2026 after its test over the past, and its record counts from then. " + ImprovingRule.ReadingsCaveat, section, StringComparison.Ordinal);
        Assert.DoesNotContain("with sales growing faster than the quarter before", section, StringComparison.Ordinal);

        // In the reader's words: no term of section 15.23's list reaches the card.
        Assert.Empty(PlainWords.FaultsOn(PlainWords.TonightPage, page, PlainWords.Terms()));
    }

    [Fact]
    public async Task PaperTradingKeepsAnAccountForTheFrozenFundamentalsRuleAndEachVersionAndItsPickersListTheFamily()
    {
        using var store = await FrozenImprovingStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?universe=400&variants=improving&rule=improving"));

        // The family's tile its live rule's, after Quality at a fair price's.
        Assert.Equal(
            ("improving", "live"),
            Regex.Matches(page, "<div class=\"paper-tile\" data-family=\"([^\"]+)\" data-standing=\"([^\"]+)\"").Select(match => (match.Groups[1].Value, match.Groups[2].Value)).Last());

        // Both pickers list it last, and the link choosing it in each chooses it.
        Assert.Equal("improving", Regex.Matches(Regex.Match(page, "<select data-merge-key=\"rule\".*?</select>", RegexOptions.Singleline).Value, "<option value=\"([^\"]*)\"").Select(match => match.Groups[1].Value).Last());
        Assert.Contains("<option value=\"improving\" selected>", Regex.Match(page, "<select data-merge-key=\"rule\".*?</select>", RegexOptions.Singleline).Value, StringComparison.Ordinal);
        Assert.Contains("<option value=\"improving\" selected>", Regex.Match(page, "<select data-merge-key=\"variants\".*?</select>", RegexOptions.Singleline).Value, StringComparison.Ordinal);

        // An account for the live rule and one for each of its eight versions, every other level of each dial, the one
        // holding at 6 carrying its two trades.
        var live = AccountRowOf(page, ReadApi.PageList + "-" + ImprovingRule.Name);
        var six = AccountRowOf(page, RunScreen.Slug(MidImprovingVersion(MidImprovingFrozen with { HoldInNine = 6 })));

        Assert.Contains("data-standing=\"live\"", live, StringComparison.Ordinal);
        Assert.Contains("data-standing=\"variant\"", six, StringComparison.Ordinal);
        Assert.Equal(8, Regex.Matches(page, "<tr data-account=\"[^\"]*s-p-400-fundamentals-rule-at[^\"]*\"[^>]*data-standing=\"variant\"").Count);

        // The family is named by its label on its tile and nowhere by the word the store keeps it under.
        Assert.Contains("<span class=\"fam-swatch\" data-family=\"improving\"></span>Fundamentals <span class=\"status-pill live\">", page, StringComparison.Ordinal);
    }
}
