using EquityBrief.Api.Reading;
using EquityBrief.Core.EntryCheck;
using EquityBrief.Core.Report;
using EquityBrief.Tests.Harness;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The entry check's bar readings and its two fixes. Every expected value is worked by hand from the operator's order over the
// constructed series below, before the code ran.
// see: The entry check reads a member's bars every night into a table of its own, and no rule reads it
// see: A page with no resistance level in the last year of bars says so and draws the nearest older band, for display alone
// see: A figure fetched before the newest report says so with its fetch date, and a company's value names the day its shares were filed
public partial class ReadSurface
{
    internal static string[] EntryCheckRows =>
    [
        CheckReach.Key("15.9 Name", "Entry check"),
        CheckReach.Key("15.9 Name", "No resistance level in the year"),
        CheckReach.Key("15.9 Name", "A figure from before the newest report"),
        CheckReach.Key("15.18 The decision card", "The entry check's line"),
        CheckReach.Key("15.24 Home", "An idea's entry check"),
    ];

    static readonly DateOnly EntryNight = new(2026, 11, 4);

    // Twenty-nine sessions closing at 100, each with a high of 101 and a low of 99, and the night closing at 110 with a low of 109
    // on 2,000 shares against an average of 1,000. The twenty-session average on the night is (19 x 100 + 110) / 20 = 100.5, so the
    // close stands 9.4527% above it, which at a typical day of 2.5 is 9.5 / 2.5 = 3.8 days; of the eleven days holding the average,
    // ten stood 0% above and the night alone this far, 1 in 11, 9.09%. The close twenty sessions back is 100, a gain of 10%.
    static IReadOnlyList<EntryBar> EntryYear() =>
    [
        .. Enumerable.Range(0, 29).Select(at => new EntryBar(EntryNight.AddDays(at - 29), 101m, 99m, 100m, 1000)),
        new EntryBar(EntryNight, 111m, 109m, 110m, 2000),
    ];

    // Three hundred older sessions at 100, one of them, 2024-08-20, over 250 sessions back, turning at a high of 130 and closing at 115.
    static IReadOnlyList<EntryBar> EntryOlder()
    {
        var first = new DateOnly(2024, 8, 1);

        return
        [
            .. Enumerable.Range(0, 300).Select(at =>
            {
                var day = first.AddDays(at);

                return day == new DateOnly(2024, 8, 20) ? new EntryBar(day, 130m, 100m, 115m, 1000) : new EntryBar(day, 101m, 99m, 100m, 1000);
            }),
        ];
    }

    static EntryReading EntryWorked() => EntryReadings.Of("HAND", EntryYear(), EntryOlder(), 2.5, 70, 1000)!;

    [Fact]
    public void TheEntryCheckReadsTheBarsAsWorkedByHand()
    {
        var reading = EntryWorked();

        Assert.Equal(9.4527, reading.StretchPercent!.Value, 3);
        Assert.Equal(3.8, reading.StretchDays!.Value, 6);
        Assert.Equal(100d / 11, reading.StretchShare!.Value, 6);
        Assert.Equal(10, reading.GainTwenty!.Value, 6);
        Assert.Equal(70, reading.Rsi);
        Assert.True(reading.LargestGain);
        Assert.Equal(2, reading.VolumeRatio);
        Assert.Equal((101m, 109m), (reading.GapHigh, reading.GapLow));
        Assert.Equal(new DateOnly(2024, 8, 20), reading.HighestSince);
        Assert.Equal(new DateOnly(2024, 8, 1), reading.HistoryStart);
        Assert.Equal((128.75m, 130m, new DateOnly(2024, 8, 20)), (reading.OlderLow, reading.OlderHigh, reading.OlderOn));
        Assert.Equal(7.5, reading.OlderDays!.Value, 6);

        // No older history: the highest close since the store's own first session, no largest gain read over thirty sessions, no band.
        var alone = EntryReadings.Of("HAND", EntryYear(), [], 2.5, null, null)!;

        Assert.Null(alone.HighestSince);
        Assert.Equal(EntryNight.AddDays(-29), alone.HistoryStart);
        Assert.Null(alone.LargestGain);
        Assert.Null(alone.OlderLow);
        Assert.Null(alone.VolumeRatio);

        // A night that closes under the session before: no gap, and the newest earlier close at or above it.
        var fell = EntryReadings.Of("HAND", [.. EntryYear().Take(29), new EntryBar(EntryNight, 100m, 98m, 99m, 500)], [], 2.5, null, null)!;

        Assert.Null(fell.GapHigh);
        Assert.Equal(EntryNight.AddDays(-1), fell.HighestSince);
        Assert.Null(EntryReadings.Of("HAND", [], [], 2.5, null, null));
    }

    [Fact]
    public void EachEntryReadingReadsAsOnePlainSentence()
    {
        var reading = EntryWorked();

        Assert.Equal("Price is 9.5% above its 20-session average, which is 3.8 typical days. 9% of this stock's days have been this far above it or more.", EntryWords.Stretch(reading));
        Assert.Equal("The price moved +10.0% over 20 sessions; its relative strength reads 70.0 of 100; tonight was the largest one-day gain in 250 sessions; volume was 2.0 times its 50-session mean.", EntryWords.HotRun(reading));
        Assert.Equal("Tonight's close was last matched or passed on 2024-08-20.", EntryWords.Highest(reading));
        Assert.Equal("The nearest band above tonight's close from the pulled history is $128.75 to $130.00, from 2024, 7.5 typical days above.", EntryWords.Older(reading));

        // The stop at the gap's top, inside it, and under it: only the first two say a fall back would reach it.
        const string Gapped = "Tonight's low of $109.00 is above the prior session's high of $101.00, a gap of $8.00.";

        Assert.Equal(Gapped, EntryWords.Gap(reading, null, null, null));
        Assert.Equal(Gapped + " The earnings drift rule's stop is $109.00, 0.4 typical days under the buy. A fall back into the gap would reach it.", EntryWords.Gap(reading, 110m, 109m, "earnings drift"));
        Assert.Equal(Gapped + " The earnings drift rule's stop is $105.00, 2.0 typical days under the buy. A fall back into the gap would reach it.", EntryWords.Gap(reading, 110m, 105m, "earnings drift"));
        Assert.Equal(Gapped + " The earnings drift rule's stop is $100.00, 4.0 typical days under the buy.", EntryWords.Gap(reading, 110m, 100m, "earnings drift"));
        Assert.Null(EntryWords.Gap(reading with { GapHigh = null, GapLow = null }, 110m, 100m, "earnings drift"));

        var alone = EntryReadings.Of("HAND", EntryYear(), [], 2.5, null, null)!;

        Assert.Equal("This is the highest close since its history starts on 2026-10-06.", EntryWords.Highest(alone));
        Assert.Equal("Older resistance: none above in the history held.", EntryWords.Older(alone));

        // Below its average the sentence says below.
        Assert.StartsWith("Price is 1.0% below its 20-session average", EntryWords.Stretch(reading with { StretchPercent = -1 })!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCardsLineNamesTheFlagsThatApplyOrThatNoneDoes()
    {
        var reading = EntryWorked();

        Assert.Equal(
            "Stretched: 3.8 typical days above its 20-session average · Largest one-day gain in 250 sessions · Gap below the buy · Highest close since 2024-08-20",
            EntryWords.CardLine(reading));

        var quiet = reading with { StretchShare = 50, LargestGain = false, GapHigh = null, GapLow = null, HighestSince = EntryNight.AddDays(-30) };

        Assert.Equal("Entry check: nothing flagged", EntryWords.CardLine(quiet));

        // A tenth of its days is the last share the card names, a year the last age it does not.
        Assert.False(EntryReadings.Stretched(quiet with { StretchShare = 10.01 }));
        Assert.True(EntryReadings.Stretched(quiet with { StretchShare = 10 }));
        Assert.False(EntryReadings.Stretched(quiet with { StretchShare = 5, StretchDays = -1 }));
        Assert.False(EntryReadings.HighestLongSince(quiet with { HighestSince = EntryNight.AddDays(-365) }));
        Assert.True(EntryReadings.HighestLongSince(quiet with { HighestSince = EntryNight.AddDays(-366) }));
        Assert.True(EntryReadings.HighestLongSince(quiet with { HighestSince = null }));

        var line = MarkRenderer.EntryCheckLine(quiet, "BF.B");

        Assert.Equal("<p class=\"entry-card-line\" data-entry-flags=\"0\">Entry check: nothing flagged <a href=\"#/name/BF.B\">See the entry check</a></p>", line);
        Assert.Equal(string.Empty, MarkRenderer.EntryCheckLine(null, "BF.B"));
    }

    [Fact]
    public void TheRegionDrawsEveryReadingAndTheCopyLeavesOutTheOutline()
    {
        var region = new MarkRenderer().EntryCheck(EntryWorked(), 110m, 109m, "drift");
        var decoded = System.Net.WebUtility.HtmlDecode(region);

        Assert.StartsWith("<section class=\"entry-check\" id=\"entry-check\" data-ticker=\"HAND\"><h3>Entry check</h3>", region, StringComparison.Ordinal);

        foreach (var key in new[] { "stretch", "hot-run", "gap", "highest", "older" })
        {
            Assert.Contains($"data-entry=\"{key}\"", region, StringComparison.Ordinal);
        }

        Assert.Contains("The earnings drift rule's stop is $109.00", decoded, StringComparison.Ordinal);
        Assert.Contains("<p class=\"entry-state\">not set yet: thresholds come from the test over stored history</p>", region, StringComparison.Ordinal);

        var copy = new MarkRenderer { Copying = true }.EntryCheck(EntryWorked(), 110m, 109m, "drift");

        Assert.Contains("data-entry=\"stretch\"", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("thresholds", copy, StringComparison.Ordinal);
        Assert.Equal(string.Empty, new MarkRenderer().EntryCheck(null, 110m, 109m, "drift"));
    }

    [Fact]
    public void TheOverheadLineNamesTheNearestOlderBandItsYearAndThatThePlanDoesNotUseIt()
    {
        var drawn = System.Net.WebUtility.HtmlDecode(MarkRenderer.OlderBandsTable(
            [new OlderBand("resistance", 129m, 130m, new DateOnly(2023, 6, 1)), new OlderBand("resistance", 150m, 151m, new DateOnly(2021, 6, 1)), new OlderBand("support", 90m, 91m, new DateOnly(2022, 6, 1))],
            110m));

        Assert.Contains("The nearest older band above is 129.00 to 130.00, from 2023. Prices above this traded on 2023-06-01. The plan's levels read one year, so they do not use these.", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("older-overhead", MarkRenderer.OlderBandsTable([new OlderBand("support", 90m, 91m, new DateOnly(2022, 6, 1))], 110m), StringComparison.Ordinal);
    }

    // A card and an idea carry the line under the buy; on the copy too; and the read hands each card its stock's reading of its night.
    [Fact]
    public async Task ACardAndAnIdeaCarryTheLineAndTheReadHandsItTheStocksReadingOfItsNight()
    {
        var row = new DecisionCardRow("GSPC", EntryNight, EquityBrief.Core.Families.DriftRule.Name, "HAND", 1, 110m, 109m, 130m, "the drift rule", "{}", "[]", null, Cap: 60, EntryCheck: EntryWorked());
        var view = CardScreen.View(row, CardContext.None);
        const string Line = "Stretched: 3.8 typical days above its 20-session average";

        Assert.Same(row.EntryCheck, view.EntryCheck);
        Assert.Contains(Line, System.Net.WebUtility.HtmlDecode(new MarkRenderer().DecisionCard(view)), StringComparison.Ordinal);
        Assert.Contains(Line, System.Net.WebUtility.HtmlDecode(new MarkRenderer { Copying = true }.DecisionCard(view)), StringComparison.Ordinal);
        Assert.DoesNotContain("entry-card-line", new MarkRenderer().DecisionCard(CardScreen.View(row with { EntryCheck = null }, CardContext.None)), StringComparison.Ordinal);

        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record) VALUES " +
            "('GSPC', '2026-11-04', 'drift', 'HAND', 1, '110', '109', '130', 'the rule', '{}', '[]', NULL), ('GSPC', '2026-11-04', 'drift', 'NONE', 2, '110', '109', '130', 'the rule', '{}', '[]', NULL);");
        store.Execute(
            "INSERT INTO entry_reading (ticker, session_date, close, stretch_percent, gap_high, gap_low, highest_since, run_id) VALUES " +
            "('HAND', '2026-11-04', '110', 9.5, '101', '109', '2024-08-20', 'r'), ('HAND', '2026-11-03', '100', 0, NULL, NULL, NULL, 'r');");

        var cards = await Api(store).DecisionCardsAsync("GSPC", EntryNight);

        Assert.Equal((9.5, 101m, new DateOnly(2024, 8, 20)), (cards.Single(card => card.Ticker == "HAND").EntryCheck!.StretchPercent, cards.Single(card => card.Ticker == "HAND").EntryCheck!.GapHigh, cards.Single(card => card.Ticker == "HAND").EntryCheck!.HighestSince));
        Assert.Null(cards.Single(card => card.Ticker == "NONE").EntryCheck);
        Assert.Equal(9.5, (await Api(store).EntryReadingOfAsync("HAND", EntryNight))!.StretchPercent);
        Assert.Null(await Api(store).EntryReadingOfAsync("NONE", EntryNight));

        var region = new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [new HomeIdea(EquityBrief.Core.Families.SetupFamilies.Pullback, "HAND", null, "A dip.", 100m, 90m, 125m, false, null, EntryCheck: EntryWorked())], IdeasCount = 1 }, string.Empty);

        Assert.Contains(Line, System.Net.WebUtility.HtmlDecode(region), StringComparison.Ordinal);
    }

    [Fact]
    public void AFigureFromBeforeTheNewestReportSaysSo()
    {
        var report = new DateOnly(2026, 10, 30);

        Assert.Equal("Set before the 2026-10-30 report", MarkRenderer.SetBeforeWords(report));
        Assert.Contains(">Set before the 2026-10-30 report</p>", MarkRenderer.SetBefore(new DateOnly(2026, 10, 29), report), StringComparison.Ordinal);
        Assert.Equal(string.Empty, MarkRenderer.SetBefore(report, report));
        Assert.Equal(string.Empty, MarkRenderer.SetBefore(new DateOnly(2026, 10, 29), null));

        Assert.Contains(">The provider's figures here were fetched on 2026-10-29, before the 2026-10-30 report.</p>", System.Net.WebUtility.HtmlDecode(MarkRenderer.FetchedBefore(new DateOnly(2026, 10, 29), report)), StringComparison.Ordinal);
        Assert.Equal(string.Empty, MarkRenderer.FetchedBefore(report, report));
        Assert.Equal(string.Empty, MarkRenderer.FetchedBefore(null, report));
    }
}
