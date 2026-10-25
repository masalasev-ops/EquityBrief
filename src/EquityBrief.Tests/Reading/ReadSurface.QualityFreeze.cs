using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Quality;

namespace EquityBrief.Tests.Reading;

// read-surface, 22.11: Quality at a fair price frozen on the S&P 400 of the universes' store, read back off the rendered
// pages: its card on Tonight drawing its live rule's holdings, the day it went live and the line its recorded answer gives,
// in the reader's words; and paper trading keeping an account for the live rule and one for each version, the family's
// tile and its versions' and its rule scorecard's pickers listing it.
// see: Each index's quality rule is frozen at its starting setting on the operator's word, with its one-step neighbours as versions
// see: A quality rule is registered as the family on its index and kept by the family's own step alone
public partial class ReadSurface
{
    // The S&P 400's history test as its run recorded it the hour before the freeze: the starting setting's 412 holdings at
    // 1.2 points a holding behind the members after costs, behind in 6 of the 7 years it held stocks in and 2023 holding
    // none, and none of its 72 settings meeting the floors where luck alone passes 3.6.
    static readonly AnswerFigures MidQuality = new(
        QualityRule.Starting.Key,
        412,
        -0.012,
        YearsOf(-0.02, 0.01, -0.03, -0.01, null, -0.02, -0.04, -0.01),
        -0.02,
        72,
        0,
        3.6,
        null,
        null,
        null);

    // The line those figures give, worked by hand: fixed on the day of the freeze after a test from 2019, 412 holdings,
    // 0.012 of the buy as 1.2 points behind, behind in all but one of the 7 years it held stocks in, and none of the
    // settings beating luck.
    const string MidQualityFixed =
        "<p class=\"family-frozen quality-fixed\" data-fixed=\"2026-09-30\" data-holdings=\"412\" data-edge=\"-0.012\" data-years=\"7\" data-ahead=\"1\" data-beat-luck=\"false\" data-tester-passed=\"none\">" +
        "Fixed 30 Sep after its history test from 2019: 412 holdings, 1.2 points a holding behind an equal-weight hold of the index's stocks after costs, behind in 6 of the 7 years it had holdings. " +
        "No setting beat luck, so treat it as a starting point rather than a proven edge.</p>";

    static string MidLive => Core.Candidates.FamilyRecords.LivePrefix + "S&P 400 quality rule at " + QualityRules.Short(QualityRule.Starting);

    static string MidVersion(QualitySettings setting) => "the S&P 400 quality rule at " + QualityRules.Short(setting);

    // The universes' store with the S&P 400's answer recorded, its quality family frozen at the starting setting on the
    // day before its reshuffle, and its two nights kept under the live rule and under the version holding 20.
    static async Task<TemporaryStore> FrozenQualityStore()
    {
        var store = UniversesStore();

        RecordFigured(store, "20260930T110000Z", QualityRule.Name, null, "none passed", "2026-09-30T11:00:00Z", MidQuality);

        var (code, said) = await FixtureExpectations.RegisterVerbAt(store, new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), RegisterVerb.IndexFamily, QualityRule.Name, "--index", "MID", "--parameters", "floor=7,valuation=1,trend=0,book=10,sale=0");

        Assert.True(code == 0, said);

        QualityNightsOn(store, rule: MidLive);
        QualityNightsOn(store, rule: MidVersion(QualityRule.Starting with { Holdings = 20 }), setting: QualityRule.Starting with { Holdings = 20 });

        return store;
    }

    [Fact]
    public async Task AFrozenQualityCardDrawsItsLiveRulesHoldingsTheDayItWentLiveAndItsRecordedAnswerInTheReadersWords()
    {
        using var store = await FrozenQualityStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var section = Assert.Single(Blocks(page, "<section class=\"card setup-card\" id=\"setup-quality\".*?</section></div></section>"));

        // Live since the freeze, never not yet tested, and its holdings the live rule's.
        Assert.Contains("<span class=\"status-pill live\" data-live-since=\"2026-09-30\">Live since 30 Sep 2026</span>", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Not yet tested", section, StringComparison.Ordinal);
        Assert.Equal(["M1", "M2"], Regex.Matches(section, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));

        // The line its recorded answer gives, word for word, and its rule's fold saying its settings were fixed that day.
        Assert.Contains(MidQualityFixed, section, StringComparison.Ordinal);
        Assert.Contains("Its settings were fixed on 30 Sep 2026 after its test over the past, and its record counts from then.", section, StringComparison.Ordinal);

        // In the reader's words: no term of section 15.23's list reaches the card.
        Assert.Empty(PlainWords.FaultsOn(PlainWords.TonightPage, page, PlainWords.Terms()));
    }

    [Fact]
    public async Task PaperTradingKeepsAnAccountForTheFrozenQualityRuleAndEachVersionAndItsPickersListTheFamily()
    {
        using var store = await FrozenQualityStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?universe=400&variants=quality&rule=quality"));

        // The family's tile its live rule's, after the setups'.
        Assert.Equal(
            ("quality", "live"),
            Regex.Matches(page, "<div class=\"paper-tile\" data-family=\"([^\"]+)\" data-standing=\"([^\"]+)\"").Select(match => (match.Groups[1].Value, match.Groups[2].Value)).Last());

        // Both pickers list it last, and the link choosing it in each chooses it.
        Assert.Equal("quality", Regex.Matches(Regex.Match(page, "<select data-merge-key=\"rule\".*?</select>", RegexOptions.Singleline).Value, "<option value=\"([^\"]*)\"").Select(match => match.Groups[1].Value).Last());
        Assert.Contains("<option value=\"quality\" selected>", Regex.Match(page, "<select data-merge-key=\"rule\".*?</select>", RegexOptions.Singleline).Value, StringComparison.Ordinal);
        Assert.Contains("<option value=\"quality\" selected>", Regex.Match(page, "<select data-merge-key=\"variants\".*?</select>", RegexOptions.Singleline).Value, StringComparison.Ordinal);

        // An account for the live rule and one for each of its seven versions, the one holding 20 carrying its two trades.
        var live = AccountRowOf(page, ReadApi.PageList + "-" + QualityRule.Name);
        var twenty = AccountRowOf(page, RunScreen.Slug(MidVersion(QualityRule.Starting with { Holdings = 20 })));

        Assert.Contains("data-standing=\"live\"", live, StringComparison.Ordinal);
        Assert.Contains("data-standing=\"variant\"", twenty, StringComparison.Ordinal);
        Assert.Equal(7, Regex.Matches(page, "<tr data-account=\"[^\"]*s-p-400-quality-rule-at[^\"]*\"[^>]*data-standing=\"variant\"").Count);
    }
}
