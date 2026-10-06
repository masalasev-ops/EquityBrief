using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;

namespace EquityBrief.Tests.Reading;

// read-surface, 15.4: a family's card says its sweep found no setting that passed the floors once the answer is recorded,
// and goes on listing its picks; the line goes with the family's next passing sweep or a freeze registered after it, and
// an answer recorded after a night, or on another index, is read on no card of it.
// see: No family on any index is set aside or hidden by a test result without the operator's word
public partial class ReadSurface
{
    const string SweepLineDrawn = "<p class=\"family-sweep\" data-sweep=\"none passed\">Its sweep found no setting that passed the floors</p>";

    static void RecordAnswer(TemporaryStore store, string run, string index, string family, string? design, string answer, string recordedAt) =>
        store.Execute(
            "INSERT INTO sweep_answer (run, index_code, family, design, answer, recorded_at) VALUES " +
            $"('{run}', '{index}', '{family}', {(design is null ? "NULL" : $"'{design}'")}, '{answer}', '{recordedAt}');");

    [Fact]
    public async Task AnSAndP400CardSaysItsSweepFoundNoneOnceTheAnswerIsRecordedUntilANewerSweepOfItPasses()
    {
        using var store = UniversesStore();

        // On the S&P 400 before the night of 2026-10-02 was past: the breakout's sweep found none, the pullback's passed,
        // the heavyweights' design (a) found none and design (b) passed. The S&P 400 drift's none came after the night was
        // past, and the S&P 600 drift's none before it is another index's.
        RecordAnswer(store, "20261002T120000Z", "MID", "breakout", null, "none passed", "2026-10-02T12:00:00Z");
        RecordAnswer(store, "20261002T120100Z", "MID", "pullback", null, "passed", "2026-10-02T12:01:00Z");
        RecordAnswer(store, "20261002T120200Z", "MID", "heavyweight", "a", "none passed", "2026-10-02T12:02:00Z");
        RecordAnswer(store, "20261002T120300Z", "MID", "heavyweight", "b", "passed", "2026-10-02T12:03:00Z");
        RecordAnswer(store, "20261003T010000Z", "MID", "drift", null, "none passed", "2026-10-03T01:00:00Z");
        RecordAnswer(store, "20261002T120400Z", "SML", "drift", null, "none passed", "2026-10-02T12:04:00Z");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> Page() => WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

        var page = await Page();
        var breakout = FamilyCardOf(page, BreakoutRule.Name);

        // The breakout's card alone draws the line, beneath its standing, and still lists M1.
        Assert.Contains("<b class=\"provisional\">Provisional: not yet frozen</b> · 1 pick tonight · 0 variants scoring in the background</p>" + SweepLineDrawn, breakout, StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"M1\" data-family=\"breakout\"", breakout, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(page, Regex.Escape(SweepLineDrawn)));
        Assert.DoesNotContain(SweepLineDrawn, FamilyCardOf(page, SetupFamilies.Pullback), StringComparison.Ordinal);
        Assert.DoesNotContain(SweepLineDrawn, FamilyCardOf(page, DriftRule.Name), StringComparison.Ordinal);
        Assert.DoesNotContain(SweepLineDrawn, HeavyweightCardOf(page), StringComparison.Ordinal);

        // Design (b) found none as well: every design's newest answer says so, and the heavyweights' card draws it.
        RecordAnswer(store, "20261002T140000Z", "MID", "heavyweight", "b", "none passed", "2026-10-02T14:00:00Z");

        Assert.Contains(SweepLineDrawn, HeavyweightCardOf(await Page()), StringComparison.Ordinal);

        // A newer sweep of the breakout passed: its line goes.
        RecordAnswer(store, "20261002T130000Z", "MID", "breakout", null, "passed", "2026-10-02T13:00:00Z");

        var after = await Page();

        Assert.DoesNotContain(SweepLineDrawn, FamilyCardOf(after, BreakoutRule.Name), StringComparison.Ordinal);
        Assert.Single(Regex.Matches(after, Regex.Escape(SweepLineDrawn)));
    }

    [Fact]
    public async Task AnSAndP500CardsLineGoesWithAFreezeRegisteredAfterItsAnswerAndIsDrawnForAnAnswerRecordedAfterTheFreeze()
    {
        using var store = await FamilyNightStore();

        // The pullback's live rule registered at 2026-09-25T10:39:49Z; its sweep's none recorded the day before.
        RecordAnswer(store, "20260924T120000Z", "GSPC", "pullback", null, "none passed", "2026-09-24T12:00:00Z");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> Card() => FamilyCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")), SetupFamilies.Pullback);

        Assert.DoesNotContain(SweepLineDrawn, await Card(), StringComparison.Ordinal);

        // A sweep after the freeze found none: the card draws the line and keeps its five picks.
        RecordAnswer(store, "20260926T120000Z", "GSPC", "pullback", null, "none passed", "2026-09-26T12:00:00Z");

        var card = await Card();

        Assert.Contains("<p class=\"family-state\">Live rule since <b>2026-09-25</b> · 5 picks tonight · 2 variants scoring in the background</p>" + SweepLineDrawn, card, StringComparison.Ordinal);
        Assert.Contains("data-picks=\"5\"", card, StringComparison.Ordinal);
    }
}
