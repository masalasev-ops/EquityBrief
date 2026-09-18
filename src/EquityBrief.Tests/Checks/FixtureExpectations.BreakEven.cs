using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Returns;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 8.2: the bar each plan set for itself, as the filler
// writes it and as section 13 states it.
// see: A stored break-even is measured from the close the setup was entered at, as a percentage beside the figures it is compared with
public partial class FixtureExpectations
{
    [Fact]
    public async Task ASetupsBarIsWrittenOntoItsOwnRowWhenTheSetupMaturesOnALaterNight()
    {
        var constructed = Expected("forward-returns").GetProperty("constructed");
        var plan = constructed.GetProperty("plan");
        var later = constructed.GetProperty("maturedOnALaterNight");
        var reached = constructed.GetProperty("cases").EnumerateArray()
            .Single(one => one.GetProperty("case").GetString() == later.GetProperty("case").GetString());

        using var store = await WithListings();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        var session = Query(store, "SELECT DISTINCT session_date FROM listing;").Single();
        var listedOn = DateOnly.ParseExact(session, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        static string Day(DateOnly on) => on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        static string Bar(DateOnly on, string close) =>
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('ZZZA', '" +
            Day(on) + $"', '{close}', '{close}', '{close}', '{close}', 1000, 'test', '2026-09-05T21:00:00Z', '{close}');";

        // Rendered as the row query renders a stored row, so each expected row is
        // read from the expectation field by field.
        static string Rendered(JsonElement value, Func<JsonElement, string> present) =>
            value.ValueKind == JsonValueKind.Null ? "null" : present(value);

        static string Figure(JsonElement value) => value.GetDouble().ToString("0.000000", CultureInfo.InvariantCulture);

        Insert(
            store,
            "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) " +
            $"VALUES ('ZZZA', '{session}', '[]', 0, '{plan.GetRawText()}', '[]');");

        Insert(store, Bar(listedOn, reached.GetProperty("listedAt").GetString()!));

        const string Row =
            "SELECT outcome, resolved_on, " +
            "CASE WHEN return_pct IS NULL THEN NULL ELSE printf('%.6f', return_pct) END, " +
            "CASE WHEN break_even IS NULL THEN NULL ELSE printf('%.6f', break_even) END " +
            "FROM forward_return WHERE ticker = 'ZZZA' AND horizon = 'setup';";

        await new ForwardReturnFiller(clock, store.DatabaseFile).RunAsync("replay-returns-listed");

        var first = later.GetProperty("firstNight");

        Assert.Equal(
            [string.Join("|",
                Rendered(first.GetProperty("outcome"), value => value.GetString()!),
                Rendered(first.GetProperty("resolvedOn"), value => value.GetString()!),
                Rendered(first.GetProperty("returnPct"), Figure),
                Rendered(first.GetProperty("breakEven"), Figure))],
            Query(store, Row));

        // The case's closes stored on the exchange sessions after the listing, in
        // order, so its resolving close sits at the same place among them as among
        // the dates the expectation's note gives its closes.
        var closes = reached.GetProperty("closes").EnumerateArray().Select(close => close.GetString()!).ToArray();
        var sessions = new List<DateOnly>();

        for (var day = listedOn.AddDays(1); sessions.Count < closes.Length; day = day.AddDays(1))
        {
            if (ExchangeClosures.IsSession(day))
            {
                sessions.Add(day);
            }
        }

        for (var at = 0; at < closes.Length; at++)
        {
            Insert(store, Bar(sessions[at], closes[at]));
        }

        await new ForwardReturnFiller(clock, store.DatabaseFile).RunAsync("replay-returns-matured");

        var firstClose = new DateOnly(2026, 1, 2);
        var resolvedAt = DateOnly.ParseExact(reached.GetProperty("resolvedOn").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber
            - firstClose.DayNumber;

        Assert.Equal(
            [string.Join("|",
                reached.GetProperty("outcome").GetString(),
                Day(sessions[resolvedAt]),
                Figure(reached.GetProperty("returnPct")),
                Figure(reached.GetProperty("breakEven")))],
            Query(store, Row));
    }

    [Fact]
    public void SectionThirteensWorkedTrancheIsTheFirstBreakEvenCaseAtTheFiguresTheDocumentStates()
    {
        var stated = Regex.Match(
            Corpus.Read("docs/ARCHITECTURE.html"),
            @"Entry\s+near\s+(?<entry>\d+),\s+stop\s+on\s+a\s+close\s+below\s+(?<stop>\d+),\s+first\s+traded\s+target\s+at\s+(?<target>\d+)\.\s+" +
            @"Risk\s+is\s+(?<risk>\d+)\s+points,\s+reward\s+is\s+(?<reward>\d+),\s+so\s+the\s+setup\s+breaks\s+even\s+if\s+the\s+target\s+is\s+reached\s+before\s+the\s+stop\s+about\s+(?<share>\d+)%");

        Assert.True(stated.Success, "Section 13.1 no longer states its worked tranche in the form this reads.");

        decimal Stated(string name) => decimal.Parse(stated.Groups[name].Value, CultureInfo.InvariantCulture);
        decimal Worked(JsonElement one, string name) => decimal.Parse(one.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);

        var first = Expected("forward-returns").GetProperty("breakEven").GetProperty("cases")[0];

        Assert.Equal(
            (Stated("entry"), Stated("stop"), Stated("target"), Stated("risk"), Stated("reward")),
            (Worked(first, "entryClose"), Worked(first, "stop"), Worked(first, "firstTradedTarget"), Worked(first, "risk"), Worked(first, "reward")));

        var scored = ForwardReturnSeries.OverSetup(
            Closes(Stated("target")), Stated("stop"), Stated("target"), Stated("entry"), Stated("entry"));

        Assert.Equal((double)Stated("share"), Math.Round(scored.BreakEven!.Value));
    }

    [Fact]
    public void EveryOutcomeCarriesItsReturnAndItsBarTogetherOverEveryPlanShape()
    {
        const decimal Stop = 90m;
        const decimal Target = 110m;

        decimal?[] zoneTops = [null, 85m, 95m, 101m, 110m, 120m];
        decimal?[] listings = [null, 80m, 90m, 95m, 100m, 105m, 110m, 115m, 125m];
        decimal[][] series =
        [
            [],
            [105m, 112m],
            [99m, 80m],
            [85m],
            [115m],
            [100m, 95m, 108m, 111m],
            [120m, 100m, 110m],
            [.. Enumerable.Repeat(100m, ForwardReturnSeries.SetupSessionCap)],
            [.. Enumerable.Repeat(120m, ForwardReturnSeries.SetupSessionCap)],
        ];

        var carried = 0;

        foreach (var zoneTop in zoneTops)
        {
            foreach (var listed in listings)
            {
                foreach (var closes in series)
                {
                    var scored = ForwardReturnSeries.OverSetup(Closes(closes), Stop, Target, zoneTop, listed);
                    var what = string.Join(" ", zoneTop, listed, string.Join(",", closes.Take(4)));

                    Assert.Equal((what, scored.ReturnPct is null), (what, scored.BreakEven is null));

                    if (scored.BreakEven is { } bar)
                    {
                        Assert.InRange(bar, 0d, 100d);
                        carried++;
                    }
                }
            }
        }

        Assert.True(carried >= 200, $"{carried} outcomes carried a bar, expected at least 200.");
    }
}
