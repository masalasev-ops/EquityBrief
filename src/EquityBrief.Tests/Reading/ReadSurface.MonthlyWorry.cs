using System.Globalization;
using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Reading;

// read-surface, 17.10: the Run page's checklist reads the newest monthly run off its steps' rows. Held where every step
// it ran held, failed naming its month, the step it stopped on and why where one did not hold, and not read where no
// monthly run has started; a history the run kept is named with the counts that kept it.
// see: The monthly run writes the history again only where no family on any index loses more than half a per cent of its benchmarks or ten setups
public partial class ReadSurface
{
    // The checklist's item on the newest monthly run, which the 17.10 correction adds.
    internal static readonly string[] MonthlyWorryRows =
    [
        CheckReach.Key("15.10 Run", "Anything to worry about, the newest monthly run having held every step it ran, and where it stopped its month and step named with why"),
    ];

    const string MonthKept = "the standing history is kept and nothing is written, since the S&P 600's breakout 5100 against the standing 6018, 918 fewer where 31 are allowed";

    static RunStageRow MonthStep(string runId, string step, string outcome, string detail) =>
        new(runId, RunScreen.MonthlyPrefix + step, DateTimeOffset.Parse("2026-11-07T12:00:00Z", CultureInfo.InvariantCulture), DateTimeOffset.Parse("2026-11-07T12:30:00Z", CultureInfo.InvariantCulture), "ok" == outcome ? RunScreen.Ok : outcome, 0, 0, 0, "0", detail);

    [Fact]
    public async Task TheRunPagesChecklistSaysWhereTheNewestMonthlyRunStoppedAndWhy()
    {
        Assert.Equal(MonthlyRun.StagePrefix, RunScreen.MonthlyPrefix);

        const string run = "loop-month-2026-11-20261107T120000Z";
        var none = RunScreen.MonthItem([]);
        var held = RunScreen.MonthItem([MonthStep(run, "facts", "ok", "2104 filer(s) asked"), MonthStep(run, "history", "ok", "every index's history written again")]);
        var stopped = RunScreen.MonthItem([MonthStep(run, "facts", "ok", "2104 filer(s) asked"), MonthStep(run, "history", "failed", MonthKept)]);

        Assert.Equal((RunScreen.MonthWorry, WorryItem.NotRead, "no monthly run has started"), (none.Item, none.State, none.Why));
        Assert.Equal((WorryItem.Held, (string?)null), (held.State, held.Why));
        Assert.Equal((WorryItem.Failed, "the run for 2026-11 stopped at its history step: " + MonthKept), (stopped.State, stopped.Why));

        // On the page: an earlier try that held its facts, and the newest, which held them and kept the history; the
        // facts' own refresh row under the newest try is none of its steps.
        using var store = await FixtureExpectations.WithListings();
        var night = NightIn(store);

        foreach (var (id, stage, outcome, detail) in new[]
        {
            ("loop-month-2026-11-20261107T100000Z", "loop-month-facts", "ok", "2104 filer(s) asked"),
            (run, "loop-month-facts", "ok", "2104 filer(s) asked"),
            (run, "filings-whole", "ok", "2104 of 2104 filer(s) asked"),
            (run, "loop-month-history", "failed", MonthKept),
        })
        {
            store.Execute($"INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, detail) VALUES ('{id}', '{stage}', '2026-11-07T12:00:00Z', '2026-11-07T12:30:00Z', '{outcome}', '{detail.Replace("'", "''", StringComparison.Ordinal)}');");
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{night}"));

        Assert.Contains($"<li data-item=\"{RunScreen.MonthWorry}\" data-state=\"{WorryItem.Failed}\">", page, StringComparison.Ordinal);
        Assert.Contains($"<b class=\"worry-why\">the run for 2026-11 stopped at its history step: {MonthKept}</b>", page, StringComparison.Ordinal);
    }
}
