using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data.Migrations;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker;
using EquityBrief.Worker.Calendar;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Fundamentals;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Moves;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Levels;
using EquityBrief.Worker.Membership;
using EquityBrief.Worker.Nights;
using EquityBrief.Worker.Rules;
using EquityBrief.Worker.Shortlist;
using EquityBrief.Worker.Swings;
using EquityBrief.Worker.Volume;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// The read surface, the mark renderer and the page, asserted together because
// the claim the checkpoint owes runs through all three: the page draws the
// fixture's sessions, the drawn candle count matches the stored row count, and
// the API computes nothing.
public partial class ReadSurface
{
    // What this reaches, declared in the check itself. The three screens claims
    // are claims about a surface, so they are reached by a check that renders
    // the surface and reads it back, and never by a declaration.
    internal static CheckReach Reach => new(
        "read-surface",
        ["fixtures/membership-2026-09-05"],
        [
            // 6.11, the exported report, read off the file the export route offers.
            CheckReach.Key("15.4 The two surfaces", "The exported report"),

            // 6.9, the page of a name whose theme could not be refreshed, which is the half
            // of section 18's row a reader sees.
            CheckReach.Key(Scope.FailureTable, "A theme refresh fails while a name's pass depends on it"),

            // 6.10, the run page's overnight queue region, and the night the machine slept,
            // which is a claim about what that region states.
            CheckReach.Key("15.10 Run", "Overnight queue"),

            // 9.1, what a row on tonight's list says about the name's research, which is
            // read back off the row against the store rather than off the page.
            CheckReach.Key("15.7 Tonight", "Research, per row"),

            // 8.4, the shadow candidates region: how many stand registered, the
            // divisor that number sets, the line saying each record is withheld
            // until promotion, and the half asserted by its absence, which is
            // that no evaluation of a name reaches any route.
            // 8.5, the reason verdicts: nine claims and the significance row
            // that arrives with them.
            CheckReach.Key("15.10 Run", "Reason records, the share that reached target before stop"),
            CheckReach.Key("15.10 Run", "Reason records, the break-even those setups demanded"),
            CheckReach.Key("15.11 How a reason's record is displayed", "Below the minimum"),
            CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum, the share that reached the target before the stop"),
            CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum, the number resolved"),
            CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum, the break-even those setups demanded"),
            CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum, always the three together"),
            CheckReach.Key("15.11 How a reason's record is displayed", "Unresolved setups"),
            CheckReach.Key("15.11 How a reason's record is displayed", "Never shown"),
            CheckReach.Key(Scope.FailureTable, "A condition has fired but nothing has resolved yet"),
            CheckReach.Key(Scope.LimitsTable, "Reason record display"),
            CheckReach.Key(Scope.LimitsTable, "Minimum resolved setups"),
            CheckReach.Key(Scope.LimitsTable, "Significance threshold"),
            CheckReach.Key(Scope.LimitsTable, "Frozen measurement windows, threshold"),

            CheckReach.Key("15.10 Run", "Shadow candidates, how many candidate conditions are registered"),
            CheckReach.Key("15.10 Run", "Shadow candidates, the correction divisor that number sets"),
            CheckReach.Key("15.10 Run", "Shadow candidates, one line saying each candidate's record is withheld until it is promoted"),

            // 10.2, the candidates' record region, read off the run page: every part its row
            // enumerates, and the power figure section 17 states beside them.
            CheckReach.Key("15.10 Run", "Candidates' records, the verdict field stating what the last look read and what triggers the next"),
            CheckReach.Key("15.10 Run", "Candidates' records, the nightly figure beside it labelled as monitoring"),
            CheckReach.Key("15.10 Run", "Candidates' records, the Poisson-binomial tail labelled and deciding nothing"),
            CheckReach.Key("15.10 Run", "Candidates' records, each look's setups and the share they won against the bar the calibration set"),
            CheckReach.Key("15.10 Run", "Candidates' records, the level that look spent and whether it crossed its boundary"),
            CheckReach.Key("15.10 Run", "Candidates' records, the smallest excess that look could have detected"),
            CheckReach.Key("15.10 Run", "Candidates' records, the design effect and what a loss cost in multiples of the planned risk"),
            CheckReach.Key("15.10 Run", "Candidates' records, the setups entered and stopped on one session and those stopped out on a session the name reported on"),
            CheckReach.Key("15.10 Run", "Candidates' records, the step the graph stands at with its level and the count of candidates ever registered"),
            CheckReach.Key("15.10 Run", "Candidates' records, no name anywhere in it"),
            CheckReach.Key(Scope.LimitsTable, "Power stated at a look"),
            CheckReach.Key(Scope.FailureTable, "The machine slept and the overnight queue did not run"),

            // 5.6, the run page. Every one of these is a claim about a surface,
            // which is why they are reached by a check that draws the surface
            // and reads it back rather than by a declaration.
            CheckReach.Key(Scope.LimitsTable, "Base rate"),
            // Every drawn part of the section 15 rows the fifth phase 5 sign-off
            // review decomposed. Each is a claim about a surface, so each is
            // reached by the check that draws the surface and reads it back.
            CheckReach.Key("15.4 The two surfaces", "The app, the single page"),
            CheckReach.Key("15.4 The two surfaces", "The app, routing"),
            CheckReach.Key("15.4 The two surfaces", "The app, filters"),
            // The nine parts 5.8 draws, and the tenth claim the selection makes
            // on the app's own row. Each is asserted off the markup rather than
            // off the model behind it, which is what the checkpoint owed.
            // 6.1, the two claims the numbers section carries: section 18's row
            // for a filing not parsed, which is the absence marked rather than
            // drawn blank, and figure 12.1's first box, which is the computed
            // sections rendering whatever the fundamentals say.
            CheckReach.Key(Scope.FailureTable, "Filing not yet parsed for a name"),
            CheckReach.Key("Figure 12.1", "Computed sections appear"),

            // 6.1, section 15.9's fact strip, decomposed into the seven parts
            // its row enumerates. Two of the seven are fundamentals, which is
            // why the whole strip is owed here and not at a phase 3 checkpoint.
            CheckReach.Key("15.9 Name", "Fact strip, close"),
            CheckReach.Key("15.9 Name", "Fact strip, market capitalisation"),
            CheckReach.Key("15.9 Name", "Fact strip, the high and low of the move"),
            CheckReach.Key("15.9 Name", "Fact strip, next earnings date"),
            CheckReach.Key("15.9 Name", "Fact strip, the multiples"),
            CheckReach.Key("15.9 Name", "Fact strip, the averages"),
            CheckReach.Key("15.9 Name", "Fact strip, momentum and the typical daily move"),
            CheckReach.Key("15.4 The two surfaces", "The app, selection"),
            CheckReach.Key("15.7 Tonight", "Night header, the harness verdict"),
            CheckReach.Key("15.7 Tonight", "The list, day change"),
            CheckReach.Key("15.7 Tonight", "The list, trend state in a word"),
            CheckReach.Key("15.7 Tonight", "The list, the distance row mark"),
            CheckReach.Key("15.7 Tonight", "Selected name, the level summary"),
            CheckReach.Key("15.7 Tonight", "Selected name, whichever row is selected"),
            CheckReach.Key("15.8 Universe", "The table, paged"),
            CheckReach.Key("15.8 Universe", "The table, sessions until earnings"),
            CheckReach.Key("15.8 Universe", "Researched"),
            CheckReach.Key("15.7 Tonight", "Ask for a report"),
            CheckReach.Key("15.15 Queue", "Outstanding"),
            CheckReach.Key("15.15 Queue", "Being written"),
            CheckReach.Key("15.15 Queue", "Settled"),
            CheckReach.Key("15.15 Queue", "Take it out"),
            CheckReach.Key("15.15 Queue", "Which lane would write one"),
            CheckReach.Key("15.9 Name", "A pass as it runs"),
            CheckReach.Key("15.9 Name", "Listing history"),
            CheckReach.Key("15.9 Name", "How it got here, the twelve-month picture"),
            CheckReach.Key("15.5 The mark vocabulary", "Plan column, Everything above the marker is a sale"),
            CheckReach.Key("15.5 The mark vocabulary", "Plan column, everything below is a purchase"),
            CheckReach.Key("15.5 The mark vocabulary", "Plan column, stops are horizontal rules"),
            CheckReach.Key("15.5 The mark vocabulary", "Plan column, the invalidation is the lowest one"),
            CheckReach.Key("15.5 The mark vocabulary", "Distance row, A name's close between its nearest support and its nearest resistance"),
            CheckReach.Key("15.5 The mark vocabulary", "Distance row, sized for a table cell"),
            CheckReach.Key("15.5 The mark vocabulary", "Distance row, the distances in typical days"),
            CheckReach.Key("15.5 The mark vocabulary", "Reason track, setups resolved as a win"),
            CheckReach.Key("15.5 The mark vocabulary", "Reason track, resolved as a loss"),
            CheckReach.Key("15.5 The mark vocabulary", "Reason track, unresolved"),
            CheckReach.Key("15.7 Tonight", "Night header, names in the index"),
            CheckReach.Key("15.7 Tonight", "Night header, names that fired"),
            CheckReach.Key("15.7 Tonight", "Night header, run duration"),
            CheckReach.Key("15.7 Tonight", "The list, one row per name that fired"),
            CheckReach.Key("15.7 Tonight", "The list, ordered by how many fired then by the plan's reward to risk"),
            CheckReach.Key("15.7 Tonight", "The list, the reward to risk or the plan's reason for none"),
            CheckReach.Key(Scope.LimitsTable, "Blocks a record is judged over"),
            CheckReach.Key("15.10 Run", "Tonight's order, the three orders of tonight's list over the twenty each would draw"),
            CheckReach.Key("15.10 Run", "Tonight's order, the old order named as the benchmark"),
            CheckReach.Key("15.10 Run", "Tonight's order, the setups each order drew"),
            CheckReach.Key("15.10 Run", "Tonight's order, the setups whose whole window has closed"),
            CheckReach.Key("15.10 Run", "Tonight's order, the blocks holding one against the floor"),
            CheckReach.Key("15.10 Run", "Tonight's order, no comparison drawn before every order reaches it"),
            CheckReach.Key("15.7 Tonight", "The list, at most twenty drawn"),
            CheckReach.Key("15.7 Tonight", "The list, name"),
            CheckReach.Key("15.7 Tonight", "The list, close"),
            CheckReach.Key("15.7 Tonight", "The list, the reasons"),
            CheckReach.Key("15.7 Tonight", "The list, beside the name a line saying so where its prices may not reflect a dividend or split"),
            CheckReach.Key("15.9 Name", "Prices may be out of date, one line saying its prices may not reflect a recent dividend or split"),
            CheckReach.Key(Scope.FailureTable, "Listings written before the 5.4 correction"),
            CheckReach.Key("15.9 Name", "Prices may be out of date, when the refetch was last tried and why it failed"),
            CheckReach.Key("15.9 Name", "Prices may be out of date, above everything the page draws from those prices"),
            CheckReach.Key("15.7 Tonight", "Selected name, the plan column"),
            CheckReach.Key("15.8 Universe", "Sector strip, one line per sector"),
            CheckReach.Key("15.8 Universe", "Sector strip, how many are on tonight's list"),
            CheckReach.Key("15.8 Universe", "Sector strip, names"),
            CheckReach.Key("15.8 Universe", "Sector strip, how many are in an uptrend"),
            CheckReach.Key("15.8 Universe", "The table, every name in the index"),
            CheckReach.Key("15.8 Universe", "The table, the listing strip over sixty sessions"),
            CheckReach.Key("15.8 Universe", "The table, sorted by distance to the nearest level ascending"),
            CheckReach.Key("15.8 Universe", "The table, name"),
            CheckReach.Key("15.8 Universe", "The table, sector"),
            CheckReach.Key("15.8 Universe", "The table, close"),
            CheckReach.Key("15.8 Universe", "The table, trend state"),
            CheckReach.Key("15.8 Universe", "The table, the distance row mark"),
            CheckReach.Key("15.8 Universe", "The table, the evening last on the list"),
            CheckReach.Key("15.9 Name", "The chart, the level chart"),
            CheckReach.Key("15.9 Name", "The chart, the volume profile beside it on the same price axis"),
            CheckReach.Key("15.9 Name", "The chart, the momentum panel beneath"),
            CheckReach.Key("15.9 Name", "The chart, the level summary table with each band's members and dates"),
            CheckReach.Key("15.9 Name", "The plan, the plan column mark"),
            CheckReach.Key("15.9 Name", "The plan, the tranche table with conditions and stops"),
            CheckReach.Key("15.9 Name", "The plan, the exit table with actions"),
            CheckReach.Key("15.9 Name", "The plan, the earnings setups"),
            CheckReach.Key("15.9 Name", "The plan, the sizing arithmetic"),
            CheckReach.Key("15.9 Name", "How it got here, the table of the biggest moves"),
            CheckReach.Key("15.10 Run", "Operational header, what ran"),
            CheckReach.Key("15.10 Run", "Operational header, the instant each stage started and how long it took"),
            CheckReach.Key("15.10 Run", "Operational header, model calls"),
            CheckReach.Key("15.10 Run", "Operational header, network requests"),
            CheckReach.Key("15.10 Run", "Operational header, spend"),
            CheckReach.Key("15.10 Run", "Operational header, what each stage said about itself"),
            CheckReach.Key("15.10 Run", "Reason records, the resolved count"),
            CheckReach.Key("15.10 Run", "Reason records, the never-entered count"),
            CheckReach.Key("15.10 Run", "Reason records, one row per reason with the reason track mark"),
            CheckReach.Key("15.10 Run", "Harness, passed"),
            CheckReach.Key("15.10 Run", "Harness, failed"),
            CheckReach.Key("15.10 Run", "Harness, unexamined"),
            CheckReach.Key("15.7 Tonight", "Reasons, per row"),
            CheckReach.Key("15.7 Tonight", "Reason totals"),
            CheckReach.Key("15.10 Run", "Stale and failed, names carrying yesterday's bars"),

            // 6.3, the third part of that region. The refusals are drawn from the
            // store's own rows, with the category beside each, which is the only
            // surface a refusal is visible on: nothing else records that a
            // document was fetched and not stored.
            CheckReach.Key("15.10 Run", "Stale and failed, documents refused by admissibility"),

            // 6.4, the claim checker's two surfaces: the sections that fell back on
            // the run page, and the line a name's page draws for a section left
            // out, which is what section 18's two rows about the checker say a
            // reader sees.
            CheckReach.Key("15.10 Run", "Stale and failed, sections that fell back"),

            // 6.5, the two research-state lines section 15.9 names, each the part
            // of its row the staleness verdict draws, and the computed sections the
            // missing line is drawn beside.
            CheckReach.Key("15.9 Name", "Research not yet written, the researched sections absent with one line saying they have not been written"),
            CheckReach.Key("15.9 Name", "Research not yet written, beside the computed sections rendered whole"),
            CheckReach.Key("15.9 Name", "Research stale, one line naming which of the four triggers fired"),
            CheckReach.Key(Scope.FailureTable, "Claim checker rejects twice"),
            CheckReach.Key(Scope.FailureTable, "A pass finds no admissible source for a section"),

            // 6.8, the name page once a pass has written what a reader is shown: the
            // written sections, the dates and sources, what each research state offers
            // and the cost beside it, the page's half of section 18's cloud model row
            // and of its two local lane rows, and tonight's fresh prose against reused.
            CheckReach.Key("15.7 Tonight", "Night header, reports carrying fresh prose against reused"),
            CheckReach.Key("15.9 Name", "The short version"),
            CheckReach.Key("15.9 Name", "What it sells, the numbers, the cycle, the two cases, the risks"),
            CheckReach.Key("15.9 Name", "Dates and sources"),
            CheckReach.Key("15.9 Name", "Research not yet written, a control that writes them with its cost stated before it is pressed"),
            CheckReach.Key("15.9 Name", "Research stale, the stored sections rendered with their own dates"),
            CheckReach.Key("15.9 Name", "Research stale, the option to have them rewritten"),
            CheckReach.Key("15.9 Name", "Research paused, with the stored sections still rendered under their own dates"),
            CheckReach.Key(Scope.FailureTable, "Cloud model unavailable"),
            CheckReach.Key(Scope.FailureTable, "A section is assigned to the local lane that the machine cannot hold, the option to have it written"),
            CheckReach.Key(Scope.FailureTable, "The local model is unavailable, the option to have the paid model write them"),

            // 6.7, what research spent in tonight's header, and research paused on the
            // name page, which is the page's half of section 18's spend cap row.
            CheckReach.Key("15.7 Tonight", "Night header, spend"),
            CheckReach.Key("15.9 Name", "Research paused, one line saying that research is paused and when it resumes"),
            CheckReach.Key(Scope.FailureTable, "Spend cap reached, the name says research is paused and when it resumes"),

            // 6.6, the two regions of the name page a written section first reaches,
            // and the page's half of section 18's two local lane rows.
            CheckReach.Key("15.9 Name", "How it got here, the cause of each where research has been written"),
            CheckReach.Key("15.9 Name", "Contents"),
            CheckReach.Key("15.9 Name", "How far each band is"),
            CheckReach.Key("15.9 Name", "The case for and the case against"),
            CheckReach.Key("15.9 Name", "The risks as parts"),
            CheckReach.Key("15.9 Name", "Sections left out"),
            CheckReach.Key(Scope.FailureTable, "A section is assigned to the local lane that the machine cannot hold, the section is absent as usual"),
            CheckReach.Key(Scope.FailureTable, "The local model is unavailable, the local-lane sections absent with their reason"),

            // The run page's own expectation file, which 5.6 added and section
            // 19.1 did not name until the phase 5 sign-off. It is reached here
            // rather than by `fixture-expectations` because what it holds is the
            // counts a record is drawn from, computed across every night the
            // store holds, and the check that reads it is the one that draws the
            // page.
            CheckReach.Key(Scope.FixtureTable, "run page"),

            // 5.4, tonight's list.
            CheckReach.Key(Scope.LimitsTable, "List display"),
            CheckReach.Key("15.5 The mark vocabulary", "Listing strip"),
            CheckReach.Key("15.7 Tonight", "Watch list"),
            CheckReach.Key("15.9 Name", "Why it is here"),
            CheckReach.Key("15.9 Name", "Walk"),
            CheckReach.Key(Scope.FailureTable, "Bulk price feed unavailable, banner"),

            // 5.2, the move annotator.

            // The universe screen, 5.1.
            CheckReach.Key("15.8 Universe", "Filters"),
            CheckReach.Key(Scope.FailureTable, "A name leaves the index"),

            CheckReach.Key("15.5 The mark vocabulary", "Level chart, candles"),
            CheckReach.Key("15.5 The mark vocabulary", "Level chart, a volume pane"),
            CheckReach.Key("15.5 The mark vocabulary", "Level chart, the moving averages"),
            CheckReach.Key("15.5 The mark vocabulary", "Volume profile"),
            CheckReach.Key("15.5 The mark vocabulary", "Level chart, the level bands"),
            CheckReach.Key("15.5 The mark vocabulary", "Momentum panel"),
            CheckReach.Key(Scope.FailureTable, "Fewer than 200 bars for a new index member, nn bars"),
        ]);

    const string Fixture = "membership-2026-09-05";
    const string Index = "GSPC";
    const string Name = "AAPL";

    static readonly DateTimeOffset Instant = new(2026, 9, 5, 21, 10, 0, TimeSpan.Zero);

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    // The fixture's year in a temporary store. Nothing here reaches data/.
    static async Task<TemporaryStore> Populated()
    {
        var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        await new MembershipLoader(
            RecordedIndexMembershipFeed.FromFile(Path.Combine(FixtureFolder(), "index-constituents.json")),
            clock,
            store.DatabaseFile).LoadAsync(Index, "run-0");

        await new Backfill(
            RecordedHistoricalBarFeed.FromFolder(FixtureFolder()),
            clock,
            store.DatabaseFile).RunAsync(Index, "run-1");

        return store;
    }

    static ReadApi Api(TemporaryStore store) =>
        new(store.DatabaseFile, FixedClock.At(Instant, SessionZones.UnitedStates));

    // The stored rows for one name, read by a path that is not the API, so the
    // comparison below is against the store and not against itself.
    static IReadOnlyList<string> StoredRows(TemporaryStore store, string ticker)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT session_date, open, high, low, close, volume FROM bar " +
            "WHERE ticker = $ticker ORDER BY session_date;";
        command.Parameters.AddWithValue("$ticker", ticker);

        var rows = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(string.Join(
                "|",
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5)));
        }

        return rows;
    }

    [Fact]
    public async Task TheApiHandsBackEveryStoredValueUnchanged()
    {
        // The done condition's third clause, carried by behaviour rather than
        // by a source scan. If the API derived any value, adjusted any price or
        // filled any gap, one of these strings would differ. A scan for
        // arithmetic would report a pattern; this reports the values.
        using var store = await Populated();

        var stored = StoredRows(store, Name);
        var served = await Api(store).BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);

        Assert.True(stored.Count >= 250, $"The fixture holds {stored.Count} sessions for {Name}, expected at least 250.");
        Assert.Equal(stored.Count, served.Count);

        // Rendered back into the storage form, so the comparison is against
        // what the store holds rather than against a parse of it.
        var round = served.Select(bar => string.Join(
            "|",
            bar.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EquityBrief.Data.Money.ToStorage(bar.Open),
            EquityBrief.Data.Money.ToStorage(bar.High),
            EquityBrief.Data.Money.ToStorage(bar.Low),
            EquityBrief.Data.Money.ToStorage(bar.Close),
            bar.Volume));

        Assert.Equal(stored, round);
    }

    [Fact]
    public async Task TheApiServesOnlyTheNameAndRangeItIsAsked()
    {
        using var store = await Populated();
        var api = Api(store);

        var all = await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);
        var first = all[0].SessionDate;
        var window = await api.BarsAsync(Name, first, first.AddDays(30));

        Assert.NotEmpty(window);
        Assert.True(window.Count < all.Count, "A thirty-day window returned the whole year.");
        Assert.All(window, bar => Assert.Equal(Name, bar.Ticker));
        Assert.All(window, bar => Assert.True(bar.SessionDate <= first.AddDays(30)));

        // A name with no stored series is an empty answer and never an
        // invention, which is the failure a read surface that computes would
        // produce here.
        Assert.Empty(await api.BarsAsync("XRAY", DateOnly.MinValue, DateOnly.MaxValue));
    }

    [Fact]
    public async Task TheApiAppendsOneRunLogRowWhenTheSurfaceStarts()
    {
        // The W in its matrix row, exercised rather than only declared. The
        // grain is one row per run per stage, so the same run id twice is a
        // primary key violation and not a second row.
        using var store = await Populated();

        await Api(store).RecordStartAsync("read-api-1", "the read surface started");

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM run_log WHERE stage = $stage;";
        command.Parameters.AddWithValue("$stage", ReadApi.Stage);

        Assert.Equal(1L, (long)command.ExecuteScalar()!);

        await Assert.ThrowsAsync<SqliteException>(
            () => Api(store).RecordStartAsync("read-api-1", "the same run again"));
    }

    [Fact]
    public async Task TheDrawnCandleCountIsTheStoredRowCount()
    {
        // The done condition's second clause. Counted off the rendered markup
        // rather than off the list handed to the renderer, because the claim is
        // about what the page draws and a renderer that dropped every third bar
        // would pass a count of its input.
        using var store = await Populated();

        var served = await Api(store).BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);
        var svg = new MarkRenderer().LevelChart(Name, Bars(served));

        var candles = Regex.Matches(svg, "class=\"candle\"").Count;
        var volumes = Regex.Matches(svg, "class=\"volume\"").Count;

        Assert.Equal(StoredRows(store, Name).Count, candles);
        Assert.Equal(candles, volumes);
        Assert.True(candles >= 250, $"Drew {candles} candles, expected at least 250.");
    }

    [Fact]
    public async Task EverySessionIsDrawnAndNoneIsInvented()
    {
        // Stronger than the count, and it is the assertion that survives a
        // renderer drawing the right number of the wrong days.
        using var store = await Populated();

        var served = await Api(store).BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);
        var svg = new MarkRenderer().LevelChart(Name, Bars(served));

        var drawn = Regex.Matches(svg, "<g class=\"candle\" data-session=\"([0-9-]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(served.Select(bar => bar.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), drawn);
    }

    // ---- the moving averages, the third of the level chart's four elements ----

    static async Task<TemporaryStore> WithIndicators()
    {
        var store = await Populated();

        await new IndicatorEngine(
            FixedClock.At(Instant, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("run-2");

        return store;
    }

    // The averages as the endpoint assembles them: aligned to the drawn bars by
    // session, so a line is one value per candle and a session the store has no
    // indicator for is a break rather than a shift.
    static ChartAverage[] Averages(IReadOnlyList<IndicatorRow> rows, ChartBar[] bars, params string[] names)
    {
        var bySession = rows
            .GroupBy(row => row.Name)
            .ToDictionary(group => group.Key, group => group.ToDictionary(row => row.SessionDate, row => row.Value));

        return [.. names
            .Where(bySession.ContainsKey)
            .Select(name => new ChartAverage(
                name,
                bars.Select(bar => bySession[name].GetValueOrDefault(bar.SessionDate)).ToArray()))];
    }

    [Fact]
    public async Task TheChartDrawsTheAveragesOnTheCandlesOwnScale()
    {
        // 3.1's done condition. Counted off the rendered markup rather than off
        // the values handed to the renderer, for the reason the candle count is:
        // a renderer that took the averages and drew nothing would pass a count
        // of its input.
        using var store = await WithIndicators();

        var api = Api(store);
        var served = await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);
        var bars = Bars(served);
        var rows = await api.IndicatorsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);

        var averages = Averages(rows, bars, "sma20", "sma50", "sma200");

        Assert.Equal(3, averages.Length);

        var svg = new MarkRenderer().LevelChart(Name, bars, averages);

        var groups = Regex.Matches(svg, "<g class=\"moving-average\" data-average=\"([a-z0-9_]+)\" data-values=\"([0-9]+)\"")
            .Select(match => (Name: match.Groups[1].Value, Values: int.Parse(match.Groups[2].Value)))
            .ToArray();

        Assert.Equal(["sma20", "sma50", "sma200"], groups.Select(group => group.Name));

        // Each line draws as many points as the store has values for it, and
        // the counts are the store's rather than a figure written here.
        foreach (var group in groups)
        {
            var stored = rows.Count(row => row.Name == group.Name && row.Value is not null);

            Assert.Equal(stored, group.Values);
            Assert.True(stored > 0, $"{group.Name} has no stored value, so the line would be vacuously right.");
        }

        // The long average is the one the done condition is about: it has fewer
        // drawn points than the short one, because it has no value until two
        // hundred bars sit behind the session.
        Assert.True(
            groups.Single(group => group.Name == "sma200").Values
                < groups.Single(group => group.Name == "sma20").Values,
            "The 200-day average drew as many points as the 20-day one, so its warm-up was not honoured.");

        // Drawn on the candles' own scale. Every path coordinate sits inside the
        // price pane, which is what a second scale would break.
        var points = Regex.Matches(svg, "<path d=\"([^\"]+)\"")
            .SelectMany(match => Regex.Matches(match.Groups[1].Value, @"[ML]([0-9.]+) ([0-9.]+)"))
            .Select(match => double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.NotEmpty(points);
        Assert.All(points, y => Assert.InRange(y, 0, 340));
    }

    [Fact]
    public async Task AnAverageIsBrokenWhereItHasNoValueRatherThanJoinedAcrossIt()
    {
        // A 200-day average has no value for the first 199 sessions of a stored
        // year. A single path from the first value it does have would claim the
        // average was flat over sessions it did not exist for, which is a
        // drawing of something that never happened.
        using var store = await WithIndicators();

        var api = Api(store);
        var bars = Bars(await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue));
        var rows = await api.IndicatorsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);

        var svg = new MarkRenderer().LevelChart(Name, bars, Averages(rows, bars, "sma200"));

        var group = Regex.Match(svg, "<g class=\"moving-average\"[^>]*>(.*?)</g>", RegexOptions.Singleline).Groups[1].Value;
        var paths = Regex.Matches(group, "<path ").Count;

        // One run of values, so one path, and the run starts where the warm-up
        // ends rather than at the first session.
        Assert.Equal(1, paths);

        var first = Regex.Match(group, @"d=""M([0-9.]+) ").Groups[1].Value;

        Assert.True(
            double.Parse(first, CultureInfo.InvariantCulture) > 700,
            $"The 200-day average starts at x={first}, which is near the left edge, so it was drawn from a session it has no value for.");
    }

    [Fact]
    public void AnAverageOfTheWrongLengthIsRefusedRatherThanDrawnAgainstTheWrongDates()
    {
        // The failure that would report green. A line one session short draws
        // every point one slot to the left and looks entirely plausible, so it
        // is refused at the door rather than checked by eye.
        var bars = new ChartBar[]
        {
            new(new DateOnly(2026, 9, 1), 10m, 12m, 9m, 11m, 100),
            new(new DateOnly(2026, 9, 2), 11m, 12m, 8m, 9m, 120),
        };

        var refusal = Assert.Throws<ArgumentException>(() =>
            new MarkRenderer().LevelChart("TEST", bars, [new ChartAverage("sma20", [10.5])]));

        Assert.Contains("1 values against 2 sessions", refusal.Message, StringComparison.Ordinal);

        // And the control: the right length draws.
        var svg = new MarkRenderer().LevelChart("TEST", bars, [new ChartAverage("sma20", [10.5, 10.0])]);

        Assert.Contains("class=\"moving-average\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ACandleIsHollowWhenItRoseAndFilledWhenItFell()
    {
        // Neutral ink either way. Section 15.6 forbids the two hues here by
        // name, so the assertion is that neither appears rather than only that
        // the fill differs.
        var svg = new MarkRenderer().LevelChart("TEST",
        [
            new ChartBar(new DateOnly(2026, 9, 1), 10m, 12m, 9m, 11m, 100),
            new ChartBar(new DateOnly(2026, 9, 2), 11m, 12m, 8m, 9m, 120),
        ]);

        var bodies = Regex.Matches(svg, "<rect x=\"[^\"]+\" y=\"[^\"]+\" width=\"[^\"]+\" height=\"[^\"]+\" fill=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(2, bodies.Length);
        Assert.Equal("none", bodies[0]);
        Assert.Equal("var(--ink, #1c1c1c)", bodies[1]);

        Assert.DoesNotContain("green", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orange", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AChartOverTooFewBarsStatesItsBarCountRatherThanDrawing()
    {
        // Section 15.5's degradation rule. A sparse series must never look like
        // a quiet one, so the mark says what it has.
        var renderer = new MarkRenderer();

        var none = renderer.LevelChart("TEST", []);
        var one = renderer.LevelChart("TEST", [new ChartBar(new DateOnly(2026, 9, 1), 10m, 12m, 9m, 11m, 100)]);

        Assert.DoesNotContain("<svg", none, StringComparison.Ordinal);
        Assert.Contains("0 stored sessions", none, StringComparison.Ordinal);
        Assert.Contains("1 stored session,", one, StringComparison.Ordinal);
        Assert.Contains($"at least {MarkRenderer.FewestBars}", one, StringComparison.Ordinal);
    }

    [Fact]
    public void AFlatSeriesDrawsRatherThanDividingByZero()
    {
        // A name that traded at one price for every session in the window is a
        // real series and the obvious scaling divides by its range.
        var svg = new MarkRenderer().LevelChart("TEST",
        [
            new ChartBar(new DateOnly(2026, 9, 1), 10m, 10m, 10m, 10m, 0),
            new ChartBar(new DateOnly(2026, 9, 2), 10m, 10m, 10m, 10m, 0),
        ]);

        Assert.Equal(2, Regex.Matches(svg, "class=\"candle\"").Count);
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShellRoutesOnTheHashAndDrawsNothingItself()
    {
        // Section 15.4 puts the shell and the marks on the server. What the
        // page may do is ask for a mark; what it may not do is build one, so
        // the assertion is that no drawing element appears in the shell.
        var shell = new SinglePageApp().Shell("EquityBrief");

        Assert.Contains(SinglePageApp.NameRoute, shell, StringComparison.Ordinal);

        // The region rather than one mark. 4.1 composes the name screen's chart
        // region on the server, because the profile is drawn against the chart's
        // own price axis and a second request would be a second axis.
        Assert.Contains("/screens/name/", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("<rect", shell, StringComparison.Ordinal);
    }

    // ---- 3.3, the volume profile mark ----

    // Bands well inside the chart's own range, so the two scales are
    // distinguishable. A profile whose bands spanned the chart's whole range
    // would sit correctly under either rule and prove nothing.
    static ProfileBand[] Bands() =>
    [
        new(20m, 22m, 300, 0.3),
        new(22m, 24m, 500, 0.5),
        new(24m, 26m, 200, 0.2),
    ];

    static ChartBar[] Wide() =>
    [
        new(new DateOnly(2026, 9, 1), 12m, 40m, 10m, 30m, 100),
        new(new DateOnly(2026, 9, 2), 30m, 38m, 11m, 20m, 120),
    ];

    [Fact]
    public void TheVolumeProfileIsDrawnAgainstTheChartsPriceAxisAndNotItsOwn()
    {
        // Section 15.5 says the profile is drawn against the same price axis as
        // the chart beside it. That is a claim about two pictures agreeing, and
        // it is asserted three ways: the two marks declare the same axis, the
        // topmost band is not flush to the top of the pane where its own scale
        // would put it, and handing the mark a different axis moves it.
        var renderer = new MarkRenderer();
        var bars = Wide();
        var axis = renderer.AxisFor(bars);

        var chart = renderer.LevelChart("TEST", bars);
        var profile = renderer.VolumeProfile("TEST", Bands(), axis);

        var chartAxis = Regex.Match(chart, @"data-axis-low=""([^""]+)"" data-axis-high=""([^""]+)""");
        var profileAxis = Regex.Match(profile, @"data-axis-low=""([^""]+)"" data-axis-high=""([^""]+)""");

        Assert.True(chartAxis.Success && profileAxis.Success);
        Assert.Equal(chartAxis.Groups[1].Value, profileAxis.Groups[1].Value);
        Assert.Equal(chartAxis.Groups[2].Value, profileAxis.Groups[2].Value);

        // The chart's range is 10 to 40 and the bands run 20 to 26, so the top
        // band sits around the middle of the pane. Its own scale would put it at
        // the top margin, which is the picture this is written to refuse.
        var tops = Regex.Matches(profile, @"class=""band""[^>]*? y=""([0-9.]+)""")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(3, tops.Length);
        Assert.True(tops.Min() > 100, $"The topmost band sits at y={tops.Min()}, which is where its own axis would put it.");

        // And the same bands against a different axis are drawn somewhere else,
        // so the axis is used rather than carried.
        var elsewhere = renderer.VolumeProfile("TEST", Bands(), new PriceAxis(19, 27));
        var moved = Regex.Matches(elsewhere, @"class=""band""[^>]*? y=""([0-9.]+)""")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.NotEqual(tops, moved);
    }

    [Fact]
    public void EveryBandIsDrawnWithItsSharesAndItsShareOfThePeriod()
    {
        // The mark carries both numbers, because the count alone says nothing
        // without the period it is a share of, and the share is what section
        // 17's shelf threshold is read against.
        var renderer = new MarkRenderer();
        var profile = renderer.VolumeProfile("TEST", Bands(), renderer.AxisFor(Wide()));

        Assert.Equal(3, Regex.Matches(profile, "class=\"band\"").Count);
        Assert.Contains("data-shares=\"500\"", profile, StringComparison.Ordinal);
        Assert.Contains("data-share-of-period=\"0.5\"", profile, StringComparison.Ordinal);
        Assert.Contains("data-band-low=\"22\" data-band-high=\"24\"", profile, StringComparison.Ordinal);

        // Widths are relative to the busiest band rather than to the period, so
        // a name whose volume is evenly spread draws twenty full rows rather
        // than twenty stubs. The busiest is the full width less the margins.
        var widths = Regex.Matches(profile, @"class=""band""[^>]*? width=""([0-9.]+)""")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(widths.Max(), widths[1]);
        Assert.True(widths[0] / widths[1] is > 0.59 and < 0.61, $"The 300 share band is {widths[0] / widths[1]:0.###} of the 500 share band.");

        // Neutral ink. The two hues belong to support and resistance, and a
        // volume band is a magnitude.
        // see: Support and resistance own two hues and nothing else uses them
        Assert.DoesNotContain("green", profile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orange", profile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AProfileWithNoBandsSaysSoAndABandWithNoHeightIsRefused()
    {
        var renderer = new MarkRenderer();

        // The degradation, which is what a name with fewer than sixty sessions
        // gets. It states the case rather than drawing an empty box.
        var none = renderer.VolumeProfile("TEST", [], new PriceAxis(10, 40));

        Assert.DoesNotContain("<svg", none, StringComparison.Ordinal);
        Assert.Contains("no volume profile", none, StringComparison.Ordinal);

        // And a band that is not a band. A row of no height draws nothing and
        // takes its share of the period with it, which is a band silently
        // missing from a picture whose whole point is where the volume is.
        var refusal = Assert.Throws<ArgumentException>(() =>
            renderer.VolumeProfile("TEST", [new ProfileBand(22m, 22m, 100, 0.1)], new PriceAxis(10, 40)));

        Assert.Contains("which is not a band", refusal.Message, StringComparison.Ordinal);
    }

    // ---- 3.4, the level bands on the chart ----

    static ChartBand[] Shading() =>
    [
        new(20m, 22m, "support", false, 3),
        new(26m, 26m, "support", true, 9),
        new(34m, 36m, "resistance", true, 5),
    ];

    [Fact]
    public void TheBandsAreDrawnBehindTheCandlesAndNotOverThem()
    {
        // Section 15.5's fourth level chart element, and the reason the bands
        // are the point of the mark: a table of levels is a list of numbers, and
        // the same levels drawn behind the price show which ones the price has
        // respected.
        //
        // Behind is asserted by position in the markup, because SVG paints in
        // document order and there is nowhere else the answer lives. A band
        // drawn last covers the candles it is a statement about.
        var svg = new MarkRenderer().LevelChart("TEST", Wide(), null, Shading());

        var bands = svg.IndexOf("class=\"level-bands\"", StringComparison.Ordinal);
        var candles = svg.IndexOf("class=\"candle\"", StringComparison.Ordinal);
        var volume = svg.IndexOf("class=\"volume-pane\"", StringComparison.Ordinal);

        Assert.True(bands > 0 && candles > 0);
        Assert.True(bands < candles, "The level bands are drawn after the candles, so they cover the price.");
        Assert.True(bands < volume);

        Assert.Equal(3, Regex.Matches(svg, "class=\"level-band\"").Count);

        // Each band carries its role in words as well as in hue, so a reader who
        // cannot separate the two colours still reads it, and the drawn count is
        // stated in the description a reader who cannot see the picture gets.
        Assert.Contains("data-role=\"support\"", svg, StringComparison.Ordinal);
        Assert.Contains("data-role=\"resistance\"", svg, StringComparison.Ordinal);
        Assert.Contains("3 level band(s) shaded behind them", svg, StringComparison.Ordinal);
        Assert.Contains("data-strength=\"9\"", svg, StringComparison.Ordinal);

        // A band of one price is a real band and becomes a rule rather than a
        // rectangle of no height, which draws nothing.
        var heights = Regex.Matches(svg, "class=\"level-band\"[^/]*height=\"([0-9.]+)\"")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(3, heights.Length);
        Assert.All(heights, height => Assert.True(height >= 1, $"A band was drawn {height} high, which is nothing."));
    }

    [Fact]
    public void SupportAndResistanceAreTheOnlyTwoHuesAndTheImmediateBandIsStronger()
    {
        // The one place in the system those hues appear.
        // see: Support and resistance own two hues and nothing else uses them
        var svg = new MarkRenderer().LevelChart("TEST", Wide(), null, Shading());

        var fills = Regex.Matches(svg, "class=\"level-band\" data-role=\"([a-z]+)\"[^/]*fill=\"([^\"]+)\" fill-opacity=\"([0-9.]+)\"")
            .Select(match => (
                Role: match.Groups[1].Value,
                Hue: match.Groups[2].Value,
                Opacity: double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)))
            .ToArray();

        Assert.Equal(3, fills.Length);
        Assert.Equal(2, fills.Select(fill => fill.Hue).Distinct(StringComparer.Ordinal).Count());

        // owes: The band hue mapping asserted, and not only the two hues
        //
        // Which band got which hue, and not only that both hues appear. The test
        // asserted that three fills carry exactly two distinct hues and that one
        // contains the support token and one the resistance token, and never
        // that a support band got the support hue: swapping the two constants
        // drew every support band in the resistance hue and left the suite
        // green. Green is a level below the price and orange is one above it, so
        // the mapping is the claim rather than the palette.
        // see: Support and resistance own two hues and nothing else uses them
        Assert.All(fills, fill => Assert.Contains(
            "--" + fill.Role,
            fill.Hue,
            StringComparison.Ordinal));

        // Both roles are present, so the assertion above is over a population
        // that can fail rather than over three bands of one kind.
        Assert.Contains(fills, fill => fill.Role == "support");
        Assert.Contains(fills, fill => fill.Role == "resistance");

        // The immediate band on each side reads stronger, and it is a second
        // channel rather than a second hue.
        Assert.True(fills[1].Opacity > fills[0].Opacity, "The immediate band is not drawn stronger than the one beyond it.");

        // And a band given inverted edges is refused rather than drawn as a
        // rectangle of negative height, which renders as nothing at all.
        var refusal = Assert.Throws<ArgumentException>(() =>
            new MarkRenderer().LevelChart("TEST", Wide(), null, [new ChartBand(30m, 20m, "support", false, 1)]));

        Assert.Contains("which is inverted", refusal.Message, StringComparison.Ordinal);
    }

    // ---- 3.5, the momentum panel and the level summary table ----

    static async Task<TemporaryStore> WithLadders()
    {
        var store = await WithBands();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        // The calendar first, because the second book is keyed to a dated event
        // and a ladder built before it would carry none. The night runs them in
        // that order for the same reason.
        await new CalendarFetcher(
            RecordedEarningsCalendarFeed.FromFolder(FixtureFolder()),
            clock,
            store.DatabaseFile).RunAsync(Index, new DateOnly(2026, 9, 8), "run-calendar");

        await new LadderBuilder(clock, store.DatabaseFile).RunAsync(Index, "run-ladders");
        await new MoveAnnotator(clock, store.DatabaseFile).RunAsync("run-moves");

        return store;
    }

    static async Task<TemporaryStore> WithBands()
    {
        var store = await WithIndicators();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        await new SwingFinder(clock, store.DatabaseFile).RunAsync("run-swings");
        await new VolumeProfileBuilder(clock, store.DatabaseFile).RunAsync("run-profile");
        await new LevelBuilder(clock, store.DatabaseFile).RunAsync("run-levels");

        return store;
    }

    [Fact]
    public async Task TheNameRegionDrawsEveryMarkSectionFifteenPutsInItAndTheTrendState()
    {
        // 4.1's visible output, and the repair for what 4.0 found: every mark
        // this region names has existed since 3.5 and the app served none of
        // them. The region is composed by the shipped composer rather than by
        // this test, because a test that assembled it would prove only that it
        // agrees with itself.
        using var store = await WithLadders();

        var api = Api(store);

        var region = NameScreen.Region(
            new SinglePageApp(),
            new MarkRenderer(),
            Name,
            await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue),
            await api.IndicatorsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue),
            await api.LevelsAsync(Name),
            await api.ProfileAsync(Name),
            await api.LadderAsync(Name),
            await api.NextEventAsync(Name, DateOnly.MinValue),
            await api.MovesAsync(Name));

        // The four marks section 15.9 lists for this region, each named and each
        // asserted, rather than a count of svg elements which two of one kind
        // would satisfy.
        Assert.Contains("class=\"level-chart\"", region, StringComparison.Ordinal);
        Assert.Contains("class=\"volume-profile\"", region, StringComparison.Ordinal);
        Assert.Contains("class=\"momentum-panel\"", region, StringComparison.Ordinal);
        Assert.Contains("class=\"level-summary\"", region, StringComparison.Ordinal);

        // The bands are drawn, which is what the chart route did not do. Counted
        // against the store rather than asserted to be present, because one band
        // drawn out of five is a chart that looks right.
        var levels = await api.LevelsAsync(Name);

        Assert.Equal(
            levels.Count,
            Regex.Matches(region, "class=\"level-band\"").Count);

        // And the profile is drawn against the chart's own price axis rather
        // than one of its own, which is what composing the region in one place
        // buys. The two marks carry the axis they used, so the assertion is
        // against the values rather than against the arrangement.
        var chartAxis = Regex.Match(region, "class=\"level-chart\"[^>]*data-axis-low=\"([^\"]+)\" data-axis-high=\"([^\"]+)\"");
        var profileAxis = Regex.Match(region, "class=\"volume-profile\"[^>]*data-axis-low=\"([^\"]+)\" data-axis-high=\"([^\"]+)\"");

        Assert.True(chartAxis.Success, "the level chart does not carry the axis it drew against.");
        Assert.True(profileAxis.Success, "the volume profile does not carry the axis it drew against.");
        Assert.Equal(chartAxis.Groups[1].Value, profileAxis.Groups[1].Value);
        Assert.Equal(chartAxis.Groups[2].Value, profileAxis.Groups[2].Value);

        // The trend state, in a word, read off the ladder row the night wrote
        // and matched against the store rather than against a literal.
        var ladder = await api.LadderAsync(Name);

        Assert.NotNull(ladder);
        Assert.Contains($"data-trend-state=\"{ladder!.TrendState}\"", region, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"data-as-of=\"{ladder.AsOf:yyyy-MM-dd}\""), region, StringComparison.Ordinal);

        // A name with no ladder row says so rather than drawing nothing, which
        // is the same rule the absent average follows.
        var missing = NameScreen.Region(
            new SinglePageApp(),
            new MarkRenderer(),
            "NOSUCH",
            await api.BarsAsync("NOSUCH", DateOnly.MinValue, DateOnly.MaxValue),
            await api.IndicatorsAsync("NOSUCH", DateOnly.MinValue, DateOnly.MaxValue),
            await api.LevelsAsync("NOSUCH"),
            await api.ProfileAsync("NOSUCH"),
            await api.LadderAsync("NOSUCH"),
            await api.NextEventAsync("NOSUCH", DateOnly.MinValue),
            await api.MovesAsync("NOSUCH"));

        Assert.Contains("data-trend-state=\"none\"", missing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePlanColumnDrawsNoValueTheLadderDoesNotCarry()
    {
        // The containment property applied to pictures, which is 4.6's done
        // condition. Read off the mark's own data attributes and matched against
        // the stored plan rather than by eye, so a figure the drawing invented
        // fails rather than looking plausible.
        using var store = await WithLadders();

        var api = Api(store);
        var ladder = await api.LadderAsync(Name);

        Assert.NotNull(ladder);

        var rows = NameScreen.PlanRows(ladder);
        var svg = new MarkRenderer().PlanColumn(Name, 319.97m, rows);

        // Every price the figure draws, taken off the markup.
        var drawn = Regex.Matches(svg, "data-low-edge=\"([^\"]+)\" data-high-edge=\"([^\"]+)\"")
            .SelectMany(match => new[] { match.Groups[1].Value, match.Groups[2].Value })
            .Distinct()
            .OrderBy(price => price, StringComparer.Ordinal)
            .ToArray();

        // Every price the stored plan carries.
        using var plan = JsonDocument.Parse(ladder!.Plan);
        var stored = new List<string>();

        foreach (var tranche in plan.RootElement.GetProperty("tranches").EnumerateArray())
        {
            stored.Add(tranche.GetProperty("lowEdge").GetString()!);
            stored.Add(tranche.GetProperty("highEdge").GetString()!);

            if (tranche.GetProperty("stop").GetString() is { } stop)
            {
                stored.Add(stop);
            }
        }

        foreach (var exit in plan.RootElement.GetProperty("exits").EnumerateArray())
        {
            stored.Add(exit.GetProperty("lowEdge").GetString()!);
            stored.Add(exit.GetProperty("highEdge").GetString()!);
        }

        if (plan.RootElement.GetProperty("invalidation").GetString() is { } invalidation)
        {
            stored.Add(invalidation);
        }

        // Both directions. Nothing drawn that is not stored, which is the
        // containment property, and nothing stored that is not drawn, which is
        // the half a figure that quietly omitted a stop would pass.
        Assert.Equal(stored.Distinct().OrderBy(price => price, StringComparer.Ordinal), drawn);

        // The population carrying the property, stated: a plan with no rows
        // would satisfy the comparison above.
        Assert.True(rows.Count >= 5, $"the plan draws {rows.Count} rows, expected at least 5.");

        // The invalidation is the lowest rule of all, which is what the mark
        // exists to make legible.
        var lowest = rows.Min(row => row.LowEdge);

        Assert.Equal(lowest, Assert.Single(rows, row => row.Kind == PlanKind.Invalidation).LowEdge);

        // And it is one rule rather than two at one price. The invalidation is
        // the lowest stop, so drawing both would put two horizontal rules on the
        // same line and say two things where the figure says one.
        Assert.DoesNotContain(rows, row => row.Kind == PlanKind.Stop && row.LowEdge == lowest);

        // Everything above the marker is a sale and everything below is a
        // purchase, asserted against the close rather than against the drawing
        // order.
        Assert.All(
            rows.Where(row => row.Kind == PlanKind.Tranche),
            row => Assert.True(row.LowEdge < 319.97m, $"a tranche at {row.LowEdge} sits above the price."));

        Assert.All(
            rows.Where(row => row.Kind == PlanKind.Exit),
            row => Assert.True(row.LowEdge > 319.97m, $"an exit at {row.LowEdge} sits below the price."));

        // And a name with no plan says so rather than drawing an empty column.
        Assert.Contains(
            "has no plan to draw",
            new MarkRenderer().PlanColumn("NOSUCH", 10m, []),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePlanTablesCarryEveryTrancheAndEveryExitWithItsAction()
    {
        // The two tables the figure is read beside. A skipped exit is a row with
        // its reason rather than an absence, which is the rule the ladder
        // already applies and the table has to carry through.
        using var store = await WithLadders();

        var api = Api(store);

        foreach (var name in new[] { "AAPL", "MSFT", "NFLX" })
        {
            var ladder = await api.LadderAsync(name);
            var rows = NameScreen.PlanRows(ladder);
            var tables = new MarkRenderer().PlanTables(name, rows);

            using var plan = JsonDocument.Parse(ladder!.Plan);

            var tranches = plan.RootElement.GetProperty("tranches").GetArrayLength();
            var exits = plan.RootElement.GetProperty("exits").GetArrayLength();

            Assert.Contains($"class=\"tranche-table\" data-ticker=\"{name}\" data-rows=\"{tranches}\"", tables, StringComparison.Ordinal);
            Assert.Contains($"class=\"exit-table\" data-ticker=\"{name}\" data-rows=\"{exits}\"", tables, StringComparison.Ordinal);

            // Every tranche row names its condition in words and its stop, and
            // every exit row says what to do there.
            Assert.Equal(tranches, Regex.Matches(tables, "<tr data-low-edge=\"[^\"]+\" data-high-edge=\"[^\"]+\"><td class=\"num\">").Count);
            Assert.Equal(exits, Regex.Matches(tables, "data-traded=\"(true|false)\"").Count);
        }

        // MSFT's only exit is the skipped one, so its exit table is a row saying
        // why rather than an empty table.
        var msft = NameScreen.PlanRows(await api.LadderAsync("MSFT"));
        var skipped = new MarkRenderer().PlanTables("MSFT", msft);

        Assert.Contains("data-traded=\"false\"", skipped, StringComparison.Ordinal);
        Assert.Contains("typical days", skipped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheInvalidationRelabelsTheStopSittingAtItAndAddsARowWhereNoneDoes()
    {
        // The other half of the invariant 5.0 wrote down. The stops a plan
        // carries are strictly decreasing, which `fixture-expectations` asserts
        // over the arithmetic, so at most one stop row can sit at the
        // invalidation price. This is what that buys the projection: the lowest
        // stop is relabelled in place rather than a second rule being drawn on
        // top of it, which is what section 15.5 says the figure shows.
        //
        // Both branches are asserted, because the second is the one the
        // committed fixture cannot reach: every tranche in it has a stop.
        using var store = await WithLadders();

        var api = Api(store);

        foreach (var name in new[] { "AAPL", "MSFT", "NFLX", "KEYS" })
        {
            var ladder = await api.LadderAsync(name);
            var rows = NameScreen.PlanRows(ladder);

            using var plan = JsonDocument.Parse(ladder!.Plan);

            if (plan.RootElement.GetProperty("invalidation").GetString() is not { } price)
            {
                continue;
            }

            var at = decimal.Parse(price, CultureInfo.InvariantCulture);

            // Exactly one row carries the invalidation, and it is the stop that
            // was already there rather than a row beside it.
            var marked = Assert.Single(rows, row => row.Kind == PlanKind.Invalidation);

            Assert.Equal(at, marked.LowEdge);
            Assert.Contains("stop for the", marked.Detail, StringComparison.Ordinal);
            Assert.Contains("the whole position is wrong below this", marked.Detail, StringComparison.Ordinal);

            // And no stop row is left at that price, so the relabel moved the
            // one that was there rather than adding a second.
            Assert.DoesNotContain(rows, row => row.Kind == PlanKind.Stop && row.LowEdge == at);
        }

        // The branch the fixture holds no case for: a lowest tranche with no
        // band beneath it invalidates at its own low edge, where no stop row
        // sits, so the invalidation is a row of its own.
        var alone = new LadderRow(
            "ZZZZ",
            new DateOnly(2026, 9, 8),
            "range",
            """
            {"tranches":[{"lowEdge":"90.0000","highEdge":"92.0000","condition":"AvailableNow","stop":null}],
             "exits":[],"invalidation":"90.0000","events":[]}
            """);

        var drawn = NameScreen.PlanRows(alone);

        Assert.DoesNotContain(drawn, row => row.Kind == PlanKind.Stop);

        var added = Assert.Single(drawn, row => row.Kind == PlanKind.Invalidation);

        Assert.Equal(90m, added.LowEdge);
        Assert.Equal("the whole position is wrong below this", added.Detail);
        Assert.Contains("no stop beneath", Assert.Single(drawn, row => row.Kind == PlanKind.Tranche).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEventBookSaysItsFiguresAreProposals()
    {
        // A figure on a screen gets acted on, and one that looks measured and is
        // not is the failure this states its way out of. The page says so once
        // for the book and once on every row, so a reader who reads one setup
        // still reads it.
        // see: The event setups' triggers are proposals until resolved setups can score them
        using var store = await WithLadders();

        var api = Api(store);
        var written = NameScreen.EventBook(await api.LadderAsync(Name));

        Assert.Contains("data-proposal=\"true\"", written, StringComparison.Ordinal);
        Assert.Contains("is a proposal and none has been tested", written, StringComparison.Ordinal);
        Assert.Contains("run page", written, StringComparison.Ordinal);

        // One row per setup, each naming its trigger, entry, stop and target.
        var ladder = await api.LadderAsync(Name);
        using var plan = JsonDocument.Parse(ladder!.Plan);
        var setups = plan.RootElement.GetProperty("events").GetArrayLength();

        Assert.Equal(setups, Regex.Matches(written, "data-setup=\"[^\"]+\"").Count);
        Assert.Contains($"data-setups=\"{setups}\"", written, StringComparison.Ordinal);

        // And a name with no dated event says so rather than showing an empty
        // table, which is the same rule the plan column follows.
        var absent = NameScreen.EventBook(await api.LadderAsync("KEYS"));

        Assert.Contains("no dated event is on file", absent, StringComparison.Ordinal);
        Assert.DoesNotContain("data-setup=", absent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePlanSectionStatesItsArithmeticAndTheEarningsRule()
    {
        // The figures a reader acts on, drawn from the ladder row rather than
        // computed on the page: two implementations of one arithmetic disagree
        // eventually, and the report and the score would be the two.
        using var store = await WithLadders();

        var api = Api(store);
        var written = NameScreen.Arithmetic(await api.LadderAsync(Name));

        using var plan = JsonDocument.Parse((await api.LadderAsync(Name))!.Plan);
        var figures = plan.RootElement.GetProperty("arithmetic");

        // Every figure on the page is the figure the row carries, matched rather
        // than merely present.
        foreach (var key in new[] { "firstEntry", "firstRisk", "firstReward", "firstRewardToRisk", "breakEven" })
        {
            Assert.Contains(figures.GetProperty(key).GetString()!, written, StringComparison.Ordinal);
        }

        Assert.Contains("data-from=\"first\"", written, StringComparison.Ordinal);
        Assert.Contains("data-from=\"blended\"", written, StringComparison.Ordinal);

        // The break-even is stated as what this plan demands of itself rather
        // than as a benchmark, and the sizing paragraph says the sizing is the
        // reader's.
        Assert.Contains("worth taking if its first tranche", written, StringComparison.Ordinal);
        Assert.Contains("never sizes it", written, StringComparison.Ordinal);

        // The earnings rule, one row per print, with the session the timing
        // decided and the move against the stop.
        var prints = plan.RootElement.GetProperty("earningsRule").GetArrayLength();

        Assert.Equal(prints, Regex.Matches(written, "data-print=\"[^\"]+\"").Count);

        // A name whose plan has no reward to measure says so rather than showing
        // an empty table, and still shows its prints.
        var msft = NameScreen.Arithmetic(await api.LadderAsync("MSFT"));

        Assert.Contains("data-absent=\"true\"", msft, StringComparison.Ordinal);
        Assert.Contains("no exit is traded", msft, StringComparison.Ordinal);
        Assert.Contains("data-print=", msft, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNightWritesTheTablesTheNameRegionDrawsFrom()
    {
        // The other half of 4.1, and the half 4.0 found missing. The three
        // components were called only from the suite, so every phase 3
        // assertion held over stores the tests built and a store the shipped
        // night wrote held no swing, no profile and no band.
        //
        // This runs the night's own entry point over the fixture and reads the
        // tables back, so what is asserted is what an evening produces rather
        // than what a test can assemble.
        using var store = new TemporaryStore().Migrated();

        var output = new StringWriter();
        var error = new StringWriter();

        var code = await Nightly.RunAsync(
            new StoreLocation(Path.GetDirectoryName(store.DatabaseFile)!),
            FixtureFolder(),
            Index,

            // The session the captured bulk file carries, not the fixture's
            // as-of date. A night told a different session refuses the payload,
            // which is 2.3's wrong-session row doing its job.
            FixedClock.At(new DateTimeOffset(2026, 9, 8, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            output,
            error,
            "run-name-region");

        Assert.True(code == 0, $"the night exited {code}. {error} {output}");

        foreach (var table in new[] { "swing", "volume_profile", "level", "ladder" })
        {
            Assert.True(
                Count(store, table) > 0,
                $"a night wrote no rows to {table}, so the page draws it from nothing.");
        }

        // The ladder row count is the index size, which is this fixture's
        // current members, and it is read from the expectation rather than
        // written here.
        Assert.Equal(FixtureExpectation.CurrentMembers.Length, Count(store, "ladder"));
    }

    [Fact]
    public async Task TheHowItGotHereTableDrawsTheMovesAndStatesThatTheCauseIsAbsent()
    {
        // Section 15.9's second region, read off the markup rather than by eye.
        // Its cause column arrives at 6.5 with the pass that writes a cause, and
        // until then it is absent and said so once rather than drawn as an empty
        // cell in every row: an absence stated and an absence drawn as emptiness
        // are different things.
        using var store = await WithLadders();

        var api = Api(store);
        var moves = await api.MovesAsync(Name);
        var marks = new MarkRenderer();
        var table = marks.MovesTable(
            Name,
            [.. moves.Select(move => new MoveCell(move.SessionDate, move.Sessions, move.ChangePct, move.Rank))],
            NameScreen.TwelveMonths(await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue)));

        Assert.NotEmpty(moves);
        Assert.Contains($"data-moves=\"{moves.Count}\"", table, StringComparison.Ordinal);
        Assert.Contains("data-cause-column=\"absent\"", table, StringComparison.Ordinal);
        Assert.Contains("data-cause=\"absent\"", table, StringComparison.Ordinal);

        // No cell holds a cause, and no row carries an empty one. The second is
        // the half a blank column would satisfy.
        Assert.DoesNotContain("data-cause=\"\"", table, StringComparison.Ordinal);
        Assert.Equal(moves.Count, Regex.Matches(table, "<tr data-session-date=\"[^\"]+\"").Count);

        // Every value drawn is one the store carries, which is the containment
        // property applied to a table.
        foreach (var move in moves)
        {
            Assert.Contains(FormattableString.Invariant($"data-session-date=\"{move.SessionDate:yyyy-MM-dd}\""), table, StringComparison.Ordinal);
            Assert.Contains($"data-rank=\"{move.Rank}\"", table, StringComparison.Ordinal);
        }

        // A five-day run reads as one rather than as a day that moved that far,
        // which is what the span column is for.
        Assert.Contains("sessions</td>", table, StringComparison.Ordinal);

        // And a name with no stored moves says so rather than drawing an empty
        // table, which a reader would read as a name that never moved.
        Assert.Contains("no moves are stored", marks.MovesTable("NOSUCH", [], []), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhyItIsHereGivesEachReasonASentenceAndIsAbsentForANameNotOnTheList()
    {
        // Section 15.9's region, present only when the name is on tonight's
        // list. Each reason in a full sentence rather than a label, because a
        // label is what the list row shows and this is the page a reader acts
        // from.
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync(night);
        var listed = listings.First(listing => listing.FiredCount > 0);

        var marks = new MarkRenderer();

        var region = NameScreen.Region(
            new SinglePageApp(),
            marks,
            listed.Ticker,
            await api.BarsAsync(listed.Ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.IndicatorsAsync(listed.Ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.LevelsAsync(listed.Ticker),
            await api.ProfileAsync(listed.Ticker),
            await api.LadderAsync(listed.Ticker),
            await api.NextEventAsync(listed.Ticker, DateOnly.MinValue),
            await api.MovesAsync(listed.Ticker),
            await api.FundamentalsAsync(listed.Ticker),
            await api.MoveExtremesAsync(listed.Ticker),
            listed,
            "PREV",
            "NEXT");

        Assert.Contains($"data-reasons=\"{listed.FiredCount}\"", region, StringComparison.Ordinal);

        // Every reason that fired is a sentence rather than its stored label,
        // and the values that made it true are beside it.
        using var reasons = JsonDocument.Parse(listed.Reasons);

        foreach (var reason in reasons.RootElement.EnumerateArray().Where(reason => reason.GetProperty("fired").GetBoolean()))
        {
            var name = reason.GetProperty("name").GetString()!;

            Assert.Contains($"data-reason=\"{name}\"", region, StringComparison.Ordinal);
            Assert.Contains("data-values=\"", region, StringComparison.Ordinal);
        }

        // A sentence rather than a label: the region carries prose the stored
        // name does not contain.
        Assert.Contains("so the plan's first step is available", region, StringComparison.Ordinal);

        // A reason the mapping has no sentence for fails rather than rendering a
        // default, which is the same rule the plan column's mapping now follows.
        Assert.Throws<InvalidOperationException>(
            () => marks.WhyItIsHere("ZZZZ", [new FiredReason("a seventh reason", new Dictionary<string, string>())]));

        // And a name that fired nothing says it is not on tonight's list rather
        // than drawing an empty region.
        Assert.Contains("not on tonight's list", marks.WhyItIsHere("ZZZZ", []), StringComparison.Ordinal);
    }

    [Fact]
    public void TheNamePagesBreakoutSentenceStatesSectionElevensCondition()
    {
        // The sentence the name page acts from, held to the row it paraphrases rather than to
        // a copy of its words written here.
        var row = ArchitectureTables.In(Checks.Corpus.Read("docs/ARCHITECTURE.html"))
            .Single(table => table.Heading == "11. The shortlist and its six reasons")
            .Body.Single(cells => cells.Count > 1 && cells[0].Equals(ShortlistSeries.BreakoutOnVolume, StringComparison.OrdinalIgnoreCase));

        // The condition as the cell states it, less the citation that follows it.
        var condition = row[1].Split(" (", 2)[0];
        var drawn = System.Net.WebUtility.HtmlDecode(new MarkRenderer().WhyItIsHere(
            "ZZZZ",
            [new FiredReason(ShortlistSeries.BreakoutOnVolume, new Dictionary<string, string>())]));

        Assert.Contains("last night's close", condition, StringComparison.Ordinal);
        Assert.Contains(condition, drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("resistance", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWalkLinksBothNeighboursAndSaysSoAtEitherEnd()
    {
        // Section 15.9's last region, so an evening's reading is one pass
        // through with no return to the list.
        var marks = new MarkRenderer();
        var middle = marks.Walk("BBBB", "AAAA", "CCCC");

        Assert.Contains("data-previous=\"AAAA\"", middle, StringComparison.Ordinal);
        Assert.Contains("data-next=\"CCCC\"", middle, StringComparison.Ordinal);
        Assert.Contains("href=\"#/name/AAAA\"", middle, StringComparison.Ordinal);
        Assert.Contains("href=\"#/name/CCCC\"", middle, StringComparison.Ordinal);

        // Either end says so rather than linking to the other end of a list, and
        // both ends are asserted rather than one: a walk that wrapped would
        // satisfy an assertion over the first alone.
        var first = marks.Walk("AAAA", null, "BBBB");
        var last = marks.Walk("CCCC", "BBBB", null);

        Assert.Contains("no previous name", first, StringComparison.Ordinal);
        Assert.DoesNotContain("no next name", first, StringComparison.Ordinal);
        Assert.Contains("no next name", last, StringComparison.Ordinal);
        Assert.DoesNotContain("no previous name", last, StringComparison.Ordinal);

        // A name that is not on the list has neither neighbour.
        var alone = marks.Walk("ZZZZ", null, null);

        Assert.Contains("data-previous=\"none\"", alone, StringComparison.Ordinal);
        Assert.Contains("data-next=\"none\"", alone, StringComparison.Ordinal);
    }

    [Fact]
    public void ANightWithNoListShowsTheDataDateRatherThanAListBuiltFromOlderBars()
    {
        // Section 18's banner half. The bulk price feed not answering keeps last
        // night's bars, and what a reader must not be shown is tonight's list
        // built from them.
        var page = new SinglePageApp();
        var banner = page.StaleBanner(new DateOnly(2026, 9, 9), new DateOnly(2026, 9, 8));

        Assert.Contains("data-list=\"absent\"", banner, StringComparison.Ordinal);
        Assert.Contains("data-data-date=\"2026-09-08\"", banner, StringComparison.Ordinal);
        Assert.Contains("2026-09-08", banner, StringComparison.Ordinal);
        Assert.Contains("absent rather than wrong", banner, StringComparison.Ordinal);

        // No list is drawn at all, which is the half a banner above a stale list
        // would not satisfy.
        Assert.DoesNotContain("list-table", banner, StringComparison.Ordinal);
        Assert.DoesNotContain("data-fired=", banner, StringComparison.Ordinal);

        // A store with no night at all says that rather than naming a date it
        // does not have.
        var empty = page.StaleBanner(new DateOnly(2026, 9, 9), null);

        Assert.Contains("data-data-date=\"none\"", empty, StringComparison.Ordinal);
        Assert.Contains("no night at all", empty, StringComparison.Ordinal);
    }

    // ---- 5.4, tonight's list ----

    [Fact]
    public async Task EveryPlanSentenceIsAssertedAndAnUnknownConditionFailsRatherThanRendering()
    {
        // The eleventh obligation the phase 4 sign-off created. `NameScreen`'s
        // condition-to-words mapping ended in a catch-all arm, so a sixth
        // condition, a typo or an unset value rendered as the sentence for
        // reaching the zone with nothing failing, and no test in the suite
        // asserted any plan sentence at all.
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);

        // Every condition the enum carries has its own sentence, asserted one at
        // a time rather than as a set: a mapping with two arms swapped produces
        // the same set of sentences over the same plans.
        foreach (var (condition, words) in new[]
        {
            (TrancheCondition.AvailableNow, "this price now"),
            (TrancheCondition.FailedBreakdown, "a failed breakdown back into the zone"),
            (TrancheCondition.FirstCloseBackAbove, "the first close back above the zone after a dip"),
            (TrancheCondition.SecondDayAfterAShock, "the second day after a shock, once the first day's low has held"),
            (TrancheCondition.ReachesTheZone, "the price reaching the zone"),
        })
        {
            var row = NameScreen.PlanRows(Ladder(Plan(condition))).First(row => row.Kind == PlanKind.Tranche);

            Assert.Contains($"buy on {words}", row.Detail, StringComparison.Ordinal);
        }

        // And a value the mapping has no words for fails rather than rendering a
        // default. A sentence a reader acts on that was produced by a value
        // nobody wrote is what a catch-all makes invisible.
        var unknown = Assert.Throws<InvalidOperationException>(
            () => NameScreen.PlanRows(Ladder(Plan("ASixthCondition"))));

        Assert.Contains("has no words for", unknown.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => NameScreen.PlanRows(Ladder(Plan(string.Empty))));

        // The sentences reach the surface a person reads them on, which is what
        // makes this a claim about a page rather than about a function.
        var drawn = new MarkRenderer().PlanTables(Name, NameScreen.PlanRows(await api.LadderAsync(Name)));

        Assert.Contains("buy on ", drawn, StringComparison.Ordinal);
    }

    static LadderRow Ladder(string plan) => new("ZZZZ", new DateOnly(2026, 9, 8), "range", plan);

    static string Plan(TrancheCondition condition) => Plan(condition.ToString());

    static string Plan(string condition) =>
        $$"""
        {"tranches":[{"lowEdge":"90.0000","highEdge":"92.0000","condition":"{{condition}}","stop":"88.0000"}],
         "exits":[],"invalidation":"88.0000","events":[]}
        """;

    [Fact]
    public async Task TheNightHeaderStatesTheTrueFiredCountAndTheListDrawsAtMostTwenty()
    {
        // Section 17's list display. A page that shows twenty every night cannot
        // tell you how busy the night was, so the header carries the true count
        // over the whole index and the list carries what it drew.
        // see: The page shows twenty and states the true count
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var night = await api.NewestNightAsync();

        Assert.NotNull(night);

        var listings = await api.ListingsAsync(night!.Value);
        var universe = await api.UniverseAsync("GSPC");
        var fired = TonightScreen.Fired(listings);

        // The header's count is the fired rows over the whole index, not the
        // drawn rows, and the index size is the listing count rather than the
        // names with bars.
        Assert.Equal(listings.Count, universe.Count);
        Assert.Equal(listings.Count(listing => listing.FiredCount > 0), fired);

        var marks = new MarkRenderer();
        var header = marks.NightHeader(night.Value, universe.Count, fired, "00:00:01", new HarnessCounts(211, 0, 0, 87));

        Assert.Contains($"data-fired=\"{fired}\"", header, StringComparison.Ordinal);
        Assert.Contains($"data-index=\"{universe.Count}\"", header, StringComparison.Ordinal);
        Assert.Contains("data-prose=\"absent\"", header, StringComparison.Ordinal);

        // A night with more than twenty fired names draws twenty and states the
        // true count. The committed fixture holds four names, so the night of
        // forty is constructed: this is section 17's own asserted-by cell.
        var many = Enumerable.Range(0, 40)
            .Select(at => new ListingCell($"N{at:00}", night.Value, 3 - (at % 3), 40 - at, 100m, ["at entry zone"]))
            .ToArray();

        var list = marks.TonightList(many, SinglePageApp.TonightDrawn);

        Assert.Contains("data-fired=\"40\"", list, StringComparison.Ordinal);
        Assert.Contains("data-drawn=\"20\"", list, StringComparison.Ordinal);
        Assert.Contains("data-undrawn=\"20\"", list, StringComparison.Ordinal);
        Assert.Equal(20, Regex.Matches(list, "<tr data-ticker=\"[^\"]+\"").Count);

        // And a night where nothing fired says so rather than drawing an empty
        // table, which a reader would read as a page that failed.
        Assert.Contains("no name fired a reason tonight", marks.TonightList([], SinglePageApp.TonightDrawn), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheListOrdersOnHowManyFiredThenOnThePlansRewardToRisk()
    {
        // Section 15.7's order, read back off the page the route draws against what the store
        // holds, in both directions: every fired listing is drawn with the ratio its night's own
        // ladder row stored, or with the reason that plan gives for none, and the rows stand in
        // the order the stored fired counts and those ratios give by the rule as stated, worked
        // here and not by the projection.
        // see: Tonight's list breaks a tie in fired count by the plan's reward to risk, and a row with none is drawn after every row with one and says why
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync(night);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync($"/screens/tonight/{Stamp(night)}");
        var stored = new List<(string Ticker, int Fired, decimal? Ratio)>();

        foreach (var listing in listings.Where(listing => listing.FiredCount > 0))
        {
            var arithmetic = JsonDocument.Parse((await api.LadderAsync(listing.Ticker, night))!.Plan).RootElement.GetProperty("arithmetic");
            var ratio = arithmetic.GetProperty("firstRewardToRisk") is { ValueKind: JsonValueKind.String } figure
                ? decimal.Parse(figure.GetString()!, CultureInfo.InvariantCulture)
                : (decimal?)null;
            var row = RowOf(page, listing.Ticker);
            var drawn = Regex.Match(row, "data-reward-to-risk=\"([^\"]+)\"").Groups[1].Value;

            if (ratio is { } held)
            {
                Assert.Equal(held, decimal.Parse(drawn, CultureInfo.InvariantCulture));
            }
            else
            {
                Assert.Equal("none", drawn);
                Assert.Contains(arithmetic.GetProperty("absent").GetString()!, row, StringComparison.Ordinal);
            }

            stored.Add((listing.Ticker, listing.FiredCount, ratio));
        }

        // The fixture's night holds a row with no reward to risk among rows with one, so the rule's
        // last-in-its-group half is read over the store as well as over the constructed rows below.
        Assert.Contains(stored, row => row.Ratio is null);
        Assert.Contains(stored, row => row.Ratio is not null);

        Assert.Equal(
            [.. stored
                .OrderByDescending(row => row.Fired)
                .ThenBy(row => row.Ratio is null)
                .ThenByDescending(row => row.Ratio)
                .ThenBy(row => row.Ticker, StringComparer.Ordinal)
                .Select(row => row.Ticker)
                .Take(SinglePageApp.TonightDrawn)],
            Regex.Matches(page, "<tr data-ticker=\"([^\"]+)\" data-fired-count=").Select(match => match.Groups[1].Value).ToArray());

        // The tiebreak where it decides, over constructed rows, because four names of real bars
        // are not guaranteed to tie: within a fired count the higher ratio first, a row with none
        // after every row with one however strong its bands, equal ratios by ticker, and a lower
        // fired count after all of them whatever its ratio.
        static string Plan(string target, string stop = "95") =>
            $"{{\"entryLow\":\"100\",\"entryHigh\":\"110\",\"stop\":\"{stop}\",\"firstTradedTarget\":\"{target}\"}}";

        var tied = TonightScreen.Rows(
            night,
            [
                new ListingRow("AAAA", night, Fired(2), 2, Plan("120"), BandStrength: 9),
                new ListingRow("BBBB", night, Fired(2), 2, Plan("130"), BandStrength: 1),
                new ListingRow("CCCC", night, Fired(2), 2, "{\"entryLow\":\"100\",\"entryHigh\":\"110\",\"stop\":\"95\",\"firstTradedTarget\":null}", BandStrength: 99),
                new ListingRow("DDDD", night, Fired(2), 2, Plan("130"), BandStrength: 5),
                new ListingRow("EEEE", night, Fired(1), 1, Plan("200"), BandStrength: 99),
            ],
            new Dictionary<string, UniverseCell>(StringComparer.Ordinal),
            []);

        Assert.Equal(["BBBB", "DDDD", "AAAA", "CCCC", "EEEE"], [.. tied.Select(row => row.Ticker)]);

        // Worked by hand: an entry at the zone's middle, 105, risks 10 to a stop at 95, so a target
        // at 130 rewards 25 and one at 120 rewards 15.
        Assert.Equal(2.5m, tied.Single(row => row.Ticker == "BBBB").RewardToRisk);
        Assert.Equal(1.5m, tied.Single(row => row.Ticker == "AAAA").RewardToRisk);

        // The row with none says why in the plan's own words, and is drawn saying it.
        var none = tied.Single(row => row.Ticker == "CCCC");

        Assert.Null(none.RewardToRisk);
        Assert.Equal("no exit is traded, so there is no reward to measure", none.NoRewardToRisk);
        Assert.Contains(
            "data-reward-to-risk=\"none\">no exit is traded, so there is no reward to measure</span>",
            new MarkRenderer().TonightList(tied, SinglePageApp.TonightDrawn),
            StringComparison.Ordinal);

        // The other two reasons a plan gives, each read off the arithmetic the ladder states.
        Assert.Equal("no tranche is placed, so there is nothing to size", TonightScreen.FirstTranche("{\"entryLow\":null,\"entryHigh\":null,\"stop\":null,\"firstTradedTarget\":null}").Why);
        Assert.Equal("the first tranche has no stop, so there is no risk to measure", TonightScreen.FirstTranche(Plan("130", "null").Replace("\"null\"", "null", StringComparison.Ordinal)).Why);
    }

    static string Fired(int count) =>
        "[" + string.Join(",", Enumerable.Range(0, count)
            .Select(at => $"{{\"name\":\"reason {at}\",\"fired\":true,\"values\":{{}}}}")) + "]";

    [Fact]
    public async Task TheListingStripDrawsOneCellPerEveningAndSaysNothingAboutMembership()
    {
        // Section 15.5's strip. Its note in 15.8 says the columns say nothing
        // about index membership, which every name in that table has by
        // definition, and that sentence is there because they were misread that
        // way once.
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var history = await api.ListingsAsync(Name, 60);
        var evenings = TonightScreen.Strip(history);

        Assert.NotEmpty(evenings);

        var strip = new MarkRenderer().ListingStrip(Name, evenings);

        Assert.Contains($"data-evenings=\"{evenings.Count}\"", strip, StringComparison.Ordinal);
        Assert.Contains($"data-listed=\"{evenings.Count(listed => listed)}\"", strip, StringComparison.Ordinal);
        Assert.Equal(evenings.Count, Regex.Matches(strip, "<rect ").Count);

        // A listed evening and a quiet one are drawn differently in shape as
        // well as in ink, because hue is never the only channel that carries a
        // meaning. Asserted over constructed evenings, since the fixture holds
        // one night per name and cannot show both.
        var both = new MarkRenderer().ListingStrip("ZZZZ", [true, false]);

        Assert.Contains("data-listed=\"1\"", both, StringComparison.Ordinal);
        Assert.Contains("height=\"10\"", both, StringComparison.Ordinal);
        Assert.Contains("height=\"1\"", both, StringComparison.Ordinal);
    }

    // ---- 5.1, the universe screen ----

    [Fact]
    public async Task TheUniverseReachesEveryIndexMemberAndNotOnlyTheNamesWithBars()
    {
        // The population is the index, which is what section 15.8 means by
        // answering where everything sits including the names nothing happened
        // to. A screen over the names with bars would be a screen over the wrong
        // population, and it would look complete.
        using var store = await WithLadders();

        var api = Api(store);
        var rows = await api.UniverseAsync("GSPC");

        Assert.Equal(FixtureExpectation.CurrentMembers.Length, rows.Count);

        Assert.Equal(
            [.. FixtureExpectation.CurrentMembers],
            [.. rows.Select(row => row.Ticker)]);

        // A departed name is absent from the universe and still holds its row in
        // the store, which is section 18's row read on the surface it names: it
        // disappears from the universe and its history is kept.
        foreach (var departed in FixtureExpectation.Departed)
        {
            Assert.DoesNotContain(rows, row => row.Ticker == departed);
        }

        Assert.Equal(FixtureExpectation.Constituents, (int)Count(store, "membership"));
    }

    [Fact]
    public async Task TheUniverseIsOrderedByDistanceToTheNearestLevelWithAbsentDistancesLast()
    {
        // Section 15.8 sorts ascending so the top is what nearly fired. A name
        // the night computed nothing for has no distance, and an absent distance
        // read as zero would put every such name at the top of the screen whose
        // whole point is the top.
        using var store = await WithLadders();

        var api = Api(store);
        var cells = UniverseScreen.Rows(await api.UniverseAsync("GSPC"));

        var measured = cells.Where(cell => cell.Nearest is not null).Select(cell => cell.Nearest!.Value).ToArray();

        Assert.NotEmpty(measured);
        Assert.Equal([.. measured.OrderBy(distance => distance)], measured);

        // Every name carrying a distance sorts above every name carrying none,
        // asserted as a partition rather than by reading the first row.
        var lastMeasured = cells.Select((cell, at) => (cell, at)).Last(pair => pair.cell.Nearest is not null).at;
        var firstAbsent = cells.Select((cell, at) => (cell, at)).FirstOrDefault(pair => pair.cell.Nearest is null, (null!, cells.Count));

        Assert.True(
            lastMeasured < firstAbsent.Item2,
            $"a name with no distance sorts at {firstAbsent.Item2}, above one with a distance at {lastMeasured}.");

        // The partition above passes over a fixture where every name has a
        // distance, because there is nothing on the other side of it. 5.1's own
        // mutation found that: sorting absent distances first left the suite
        // green. So the case is constructed, since the committed fixture holds
        // four names and every one of them has bands and bars.
        var constructed = UniverseScreen.Rows(
        [
            new UniverseRow("NONE", "Technology", null, null, null, null, null),
            new UniverseRow("FAR", "Technology", 100m, "range", 80m, null, 1),
            new UniverseRow("NEAR", "Technology", 100m, "range", 99m, null, 1),
        ]);

        Assert.Equal(["NEAR", "FAR", "NONE"], [.. constructed.Select(cell => cell.Ticker)]);
        Assert.Null(constructed[^1].Nearest);

        // And a name whose typical move is zero has no distance rather than an
        // infinite one, which would sort it to the bottom for the same reason a
        // zero would sort it to the top: it is a name that has not moved, and
        // neither end of the screen is a statement about it.
        var still = UniverseScreen.Rows([new UniverseRow("STILL", "Utilities", 100m, "range", 90m, 110m, 0)]);

        Assert.Null(still.Single().Nearest);
        Assert.Null(still.Single().ToSupport);

        // The distance is in typical days' moves and is derived from the close
        // and the band edge, so it is asserted against the arithmetic rather
        // than against itself.
        foreach (var cell in cells.Where(cell => cell.ToSupport is not null))
        {
            var bands = await api.LevelsAsync(cell.Ticker);
            var support = bands.Where(band => band.Role == "support" && band.Immediate).Max(band => band.HighEdge);
            var typical = (await api.IndicatorsAsync(cell.Ticker, new DateOnly(2000, 1, 1), new DateOnly(2100, 1, 1)))
                .Where(row => row.Name == IndicatorSeries.Atr14)
                .OrderBy(row => row.SessionDate)
                .Last()
                .Value;

            Assert.Equal(
                Math.Abs((double)(cell.Close!.Value - support)) / typical!.Value,
                cell.ToSupport!.Value,
                6);
        }
    }

    [Fact]
    public async Task TheDistanceRowDrawsBothEdgesAndSaysSoWhenThereIsNeither()
    {
        // 15.5's mark for a table cell. Read off the markup rather than by eye,
        // which is the containment property applied to a picture.
        using var store = await WithLadders();

        var api = Api(store);
        var cells = UniverseScreen.Rows(await api.UniverseAsync("GSPC"));
        var marks = new MarkRenderer();

        var drawn = cells.First(cell => cell.ToSupport is not null && cell.ToResistance is not null);
        var svg = marks.DistanceRow(drawn);

        Assert.Contains($"data-ticker=\"{drawn.Ticker}\"", svg, StringComparison.Ordinal);
        Assert.Contains("class=\"support-edge\"", svg, StringComparison.Ordinal);
        Assert.Contains("class=\"resistance-edge\"", svg, StringComparison.Ordinal);
        Assert.Contains("typical days to support", svg, StringComparison.Ordinal);

        // The two hues are the ones support and resistance own, and which is
        // which is asserted rather than that there are two of them: the phase 3
        // sign-off found a mark drawing every support band in the resistance hue
        // while a test asserting two distinct hues stayed green.
        var support = svg[svg.IndexOf("support-edge", StringComparison.Ordinal)..];
        var resistance = svg[svg.IndexOf("resistance-edge", StringComparison.Ordinal)..];

        Assert.Contains("--support", support[..support.IndexOf("/>", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("--resistance", resistance[..resistance.IndexOf("/>", StringComparison.Ordinal)], StringComparison.Ordinal);

        // A name with neither edge says so rather than drawing a shape at one
        // end, which a reader would read.
        var neither = new UniverseCell("ZZZZ", "not on file", null, null, null, null, null, null, null);
        var absent = marks.DistanceRow(neither);

        Assert.Contains("data-nearest=\"none\"", absent, StringComparison.Ordinal);
        Assert.Contains("no band on either side yet", absent, StringComparison.Ordinal);
        Assert.DoesNotContain("support-edge", absent, StringComparison.Ordinal);
        Assert.DoesNotContain("resistance-edge", absent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSectorStripCountsEverySectorAndKeepsNotOnFileOutOfAllOfThem()
    {
        // The strip's lines sum to the index, and a name with no sector is
        // counted in its own line rather than folded into a real one. A falsy
        // value standing in for an absent one is the class this store has been
        // bitten by twice, and a sector count is where it would bite.
        using var store = await WithLadders();

        var api = Api(store);
        var cells = UniverseScreen.Rows(await api.UniverseAsync("GSPC"));
        var lines = UniverseScreen.Sectors(cells);

        Assert.Equal(cells.Count, lines.Sum(line => line.Names));

        var expected = FixtureExpectation.Of("membership").GetProperty("sectors");

        foreach (var line in lines.Where(line => line.Sector != UniverseScreen.SectorNotOnFile))
        {
            Assert.Equal(
                expected.EnumerateObject().Count(named => named.Value.GetString() == line.Sector),
                line.Names);
        }

        // The uptrend count is read against the ladder rows rather than against
        // itself.
        foreach (var line in lines)
        {
            Assert.Equal(
                cells.Count(cell => cell.Sector == line.Sector && cell.TrendState == "uptrend"),
                line.InUptrend);
        }

        var strip = new MarkRenderer().SectorStrip(lines);

        Assert.Contains($"data-sectors=\"{lines.Count}\"", strip, StringComparison.Ordinal);

        // The count of names on the list, which arrived at 5.4 with the store
        // that feeds it and was absent and said so until then.
        Assert.All(lines, line => Assert.Contains($"data-listed=\"{line.OnTheList}\"", strip, StringComparison.Ordinal));

        // A name with no sector never answers to a real sector's bucket,
        // asserted over a constructed row because every fixture name has one.
        var mixed = UniverseScreen.Sectors(
        [
            new UniverseCell("AAAA", "Technology", null, "uptrend", null, null, null, null, null, null, [true]),
            new UniverseCell("BBBB", UniverseScreen.SectorNotOnFile, null, "range", null, null, null, null, null, null, [false]),
        ]);

        Assert.Equal(1, mixed.Single(line => line.Sector == "Technology").Names);
        Assert.Equal(1, mixed.Single(line => line.Sector == UniverseScreen.SectorNotOnFile).Names);

        // And the on-the-list count is per sector rather than over the whole
        // index, which is the half a total would also satisfy.
        Assert.Equal(1, mixed.Single(line => line.Sector == "Technology").OnTheList);
        Assert.Equal(0, mixed.Single(line => line.Sector == UniverseScreen.SectorNotOnFile).OnTheList);
        Assert.Equal(UniverseScreen.SectorNotOnFile, mixed[^1].Sector);
    }

    [Fact]
    public async Task TheUniverseScreenDrawsEveryNameAndItsFiltersAreLinks()
    {
        // The screen reaches every name rather than the first page of them,
        // which is 5.1's done condition, and the filters are hash routes so a
        // filtered view is a link.
        using var store = await WithLadders();

        var api = Api(store);
        var cells = UniverseScreen.Rows(await api.UniverseAsync("GSPC"));
        var app = new SinglePageApp();
        var region = app.UniverseRegion(new MarkRenderer(), cells, UniverseScreen.Sectors(cells));

        Assert.Contains($"data-names=\"{cells.Count}\"", region, StringComparison.Ordinal);
        Assert.Contains($"data-shown=\"{cells.Count}\"", region, StringComparison.Ordinal);
        Assert.Contains($"data-rows=\"{cells.Count}\"", region, StringComparison.Ordinal);

        // Every name in the index has a row, counted off the markup rather than
        // trusted.
        foreach (var cell in cells)
        {
            Assert.Contains($"data-ticker=\"{cell.Ticker}\"", region, StringComparison.Ordinal);
        }

        Assert.Equal(
            cells.Count,
            Regex.Matches(region, "<tr data-ticker=\"[^\"]+\"").Count);

        // The filters, one chip per trend state and one per sector, each a link
        // carrying its value in the hash.
        Assert.Contains("data-filter=\"trend\"", region, StringComparison.Ordinal);
        Assert.Contains("data-filter=\"sector\"", region, StringComparison.Ordinal);
        Assert.Contains("href=\"#/universe?sector=", region, StringComparison.Ordinal);

        // And a filter narrows the table rather than the strip, so the strip
        // keeps saying what the whole index looks like while the table shows the
        // part being read.
        var filtered = app.UniverseRegion(
            new MarkRenderer(),
            cells,
            UniverseScreen.Sectors(cells),
            trendFilter: "uptrend");

        var uptrend = cells.Count(cell => cell.TrendState == "uptrend");

        Assert.Contains($"data-names=\"{cells.Count}\"", filtered, StringComparison.Ordinal);
        Assert.Contains($"data-shown=\"{uptrend}\"", filtered, StringComparison.Ordinal);
        Assert.Equal(uptrend, Regex.Matches(filtered, "<tr data-ticker=\"[^\"]+\"").Count);
    }

    [Fact]
    public async Task EveryStageOfANightRecordsWhenItStartedAndWhenItEnded()
    {
        // The guard 5.1 carries in place of the limit it cannot measure. A
        // night's wall clock at index size is a property of the running system
        // and is read on the run page's operational header once five scheduled
        // nights over the whole index have run; what the code carries is
        // that every stage records its own instants, so a night landing inside
        // its limit by one step doing nothing is legible rather than hidden in a
        // total.
        using var store = await WithLadders();

        Assert.Equal(
            0,
            Scalar(store, "SELECT COUNT(*) FROM run_log WHERE started_at IS NULL OR ended_at IS NULL OR ended_at < started_at;"));


    }

    static long Scalar(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar());
    }

    static long Count(TemporaryStore store, string table)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";

        return Convert.ToInt64(command.ExecuteScalar());
    }

    [Fact]
    public async Task TheLevelSurfaceServesTheLatestNightAloneWhenTwoAreStored()
    {
        // owes: The level read surface asserted over two stored as-of dates
        //
        // The query binds as_of to the maximum and the comment beside it says
        // why: a page holding two nights of bands is a page holding two maps.
        // Deleting that clause left the whole suite green, because the fixture
        // holds one as-of date per name and the two queries cannot differ over
        // it. The fault first appears on the second night rather than in any
        // fixture, which is what makes a constructed second night the only way
        // to assert it.
        //
        // The two-date store is also what 4.2's retention test is asserted
        // against, so it is built once here rather than twice.
        using var store = await WithBands();

        var api = Api(store);
        var tonight = await api.LevelsAsync(Name);

        Assert.NotEmpty(tonight);

        var asOf = tonight[0].AsOf;
        var yesterday = asOf.AddDays(-1);

        // Yesterday's night, at prices nothing tonight carries, so a row from it
        // is unmistakable in the answer.
        await using (var connection = new SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText = @"
                INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members)
                VALUES ($ticker, $as_of, '1.0000', '2.0000', 'support', 0, 1, 1, '[]');
            ";

            command.Parameters.AddWithValue("$ticker", Name);
            command.Parameters.AddWithValue("$as_of", yesterday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await command.ExecuteNonQueryAsync();
        }

        // The store now holds two nights, which is the population that makes the
        // clause observable. Stated rather than assumed, because an insert that
        // silently did nothing would leave this asserting over one night again.
        var stored = new List<string>();

        await using (var connection = new SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText = "SELECT DISTINCT as_of FROM level WHERE ticker = $ticker ORDER BY as_of;";
            command.Parameters.AddWithValue("$ticker", Name);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                stored.Add(reader.GetString(0));
            }
        }

        Assert.Equal(2, stored.Count);

        var served = await api.LevelsAsync(Name);

        Assert.Equal(tonight.Count, served.Count);
        Assert.All(served, row => Assert.Equal(asOf, row.AsOf));
        Assert.DoesNotContain(served, row => row.LowEdge == 1.0000m);
    }

    static MomentumReading[] Readings(IReadOnlyList<IndicatorRow> rows, int sessions) =>
    [
        .. IndicatorSeries.Momentum.Select(name =>
        {
            var bounds = IndicatorSeries.BoundsOf(name);

            return new MomentumReading(
                name,
                [.. rows.Where(row => row.Name == name).OrderBy(row => row.SessionDate).Select(row => row.Value)],
                IndicatorSeries.NeutralOf(name),
                bounds?.Floor,
                bounds?.Ceiling);
        }),
    ];

    [Fact]
    public async Task EveryMomentumReadingDrawsItsNeutralRule()
    {
        // 3.5's second done condition. A number like 53 means nothing without
        // the band it sits in, so the rule is the mark rather than decoration,
        // and it is counted off the rendered markup rather than off the input.
        using var store = await WithIndicators();

        var api = Api(store);
        var bars = await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);
        var rows = await api.IndicatorsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);

        var svg = new MarkRenderer().MomentumPanel(Name, Readings(rows, bars.Count));

        var groups = Regex.Matches(svg, "<g class=\"reading\" data-name=\"([^\"]+)\" data-neutral=\"([^\"]+)\"")
            .Select(match => (Name: match.Groups[1].Value, Neutral: match.Groups[2].Value))
            .ToArray();

        // owes: The momentum panel's reading set asserted independently of the constant it is drawn from
        //
        // The four are named here rather than read back out of the constant the
        // panel is drawn from. Asserted against `IndicatorSeries.Momentum` alone,
        // both sides of the comparison were the same value: dropping macd_hist
        // drew three readings and left the suite green, while adding atr14
        // turned it red only because NeutralOf throws on a reading with no
        // neutral rule. The test was sensitive to the set through an exception
        // and never through membership, which is the asymmetry that identified
        // it as a tautology in the phase 3 sign-off's sweep.
        //
        // The document enumerates the four nowhere, so a literal here is the
        // independent statement rather than a second copy of one.
        string[] theFourReadings = ["rsi14", "macd", "macd_signal", "macd_hist"];

        Assert.Equal(theFourReadings, groups.Select(group => group.Name).ToArray());

        // And the constant agrees with the literal, so the two are reconciled
        // once rather than the panel being free to drift from what is drawn.
        Assert.Equal(theFourReadings, IndicatorSeries.Momentum.ToArray());
        Assert.Equal(IndicatorSeries.Momentum.Count, groups.Length);

        // One rule per reading, and its value is the one the arithmetic states
        // rather than one the mark chose.
        Assert.Equal(groups.Length, Regex.Matches(svg, "class=\"neutral-rule\"").Count);

        foreach (var group in groups)
        {
            Assert.Equal(
                IndicatorSeries.NeutralOf(group.Name).ToString("0.##", CultureInfo.InvariantCulture),
                group.Neutral);
        }

        // The RSI rule sits at 50 on an axis running 0 to 100, so it is halfway
        // down its own pane whatever the stock did. That is what a fixed range
        // buys and it is asserted rather than assumed: a reading scaled to its
        // own values would put the rule wherever the week happened to end.
        var rsi = Regex.Match(svg, "data-name=\"rsi14\".*?class=\"neutral-rule\" x1=\"[0-9]+\" y1=\"([0-9.]+)\"", RegexOptions.Singleline);

        Assert.True(rsi.Success);
        Assert.Equal(38, double.Parse(rsi.Groups[1].Value, CultureInfo.InvariantCulture), 1);
    }

    [Fact]
    public void AReadingHasNoNeutralRuleUnlessItIsAMomentumReading()
    {
        // The counter-test. An average is a price and a volume ratio is a
        // quantity, and neither is read against a rule, so asking for one is
        // refused rather than answered with a zero that would draw a line
        // through the middle of a price.
        Assert.Equal(50, IndicatorSeries.NeutralOf(IndicatorSeries.Rsi14));
        Assert.Equal(0, IndicatorSeries.NeutralOf(IndicatorSeries.MacdHist));

        Assert.Throws<ArgumentOutOfRangeException>(() => IndicatorSeries.NeutralOf(IndicatorSeries.Sma200));
        Assert.Throws<ArgumentOutOfRangeException>(() => IndicatorSeries.NeutralOf(IndicatorSeries.VolAvg20));

        Assert.Equal((0d, 100d), IndicatorSeries.BoundsOf(IndicatorSeries.Rsi14));
        Assert.Null(IndicatorSeries.BoundsOf(IndicatorSeries.Macd));

        // And a panel with nothing to draw states that rather than drawing an
        // empty box.
        var none = new MarkRenderer().MomentumPanel("TEST", []);

        Assert.DoesNotContain("<svg", none, StringComparison.Ordinal);
        Assert.Contains("no momentum readings", none, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryBandInTheTableNamesItsMembersWithTheirDates()
    {
        // 3.5's first done condition, read off the rendered table rather than
        // off the rows handed to it, because the claim is about what a person
        // sees and a table that dropped every member would pass a count of its
        // input.
        using var store = await WithBands();

        var bands = await Api(store).LevelsAsync(Name);

        Assert.NotEmpty(bands);

        var summary = new MarkRenderer().LevelSummary(Name, Summarised(bands));

        Assert.Equal(bands.Count, Regex.Matches(summary, "class=\"band\"").Count);

        var members = Regex.Matches(summary, "class=\"member\" data-kind=\"([^\"]+)\" data-date=\"([^\"]+)\"")
            .Select(match => (Kind: match.Groups[1].Value, Date: match.Groups[2].Value))
            .ToArray();

        // Every member of every band, and every one of them dated. The stored
        // count is read from the JSON the level builder wrote, so this compares
        // the surface against the store rather than against itself.
        var stored = bands.Sum(band => JsonDocument.Parse(band.Members).RootElement.GetArrayLength());

        Assert.Equal(stored, members.Length);
        Assert.True(members.Length >= 20, $"The table drew {members.Length} members, expected at least 20.");
        Assert.All(members, member => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", member.Date));
        Assert.All(members, member => Assert.NotEmpty(member.Kind));

        // The immediate band on each side says so in words, because a row a
        // reader scans should carry its own meaning.
        Assert.Contains("support, immediate", summary, StringComparison.Ordinal);
        Assert.Contains("resistance, immediate", summary, StringComparison.Ordinal);

        // A band of one price reads as one price rather than as a range from a
        // number to itself.
        var single = Summarised(bands).FirstOrDefault(band => band.LowEdge == band.HighEdge);

        if (single is not null)
        {
            Assert.DoesNotContain($"{single.LowEdge.ToString(CultureInfo.InvariantCulture)} to {single.LowEdge.ToString(CultureInfo.InvariantCulture)}", summary, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnAverageWithNoValueSaysSoWithTheBarCountThatExplainsIt()
    {
        // Section 18's row, on the surface a person reads it on. The stored half
        // passes at 3.1 by fixture-expectations; this is the other element, and
        // it is here because an average with no value cannot be a band member,
        // so the table would otherwise not mention it at all.
        var bands = new SummaryBand[]
        {
            new(100m, 102m, "support", true, 4, true,
                [new SummaryMember("swing low", 100m, new DateOnly(2026, 8, 3))]),
        };

        var summary = new MarkRenderer().LevelSummary(
            "TEST",
            bands,
            [new AbsentAverage(IndicatorSeries.Sma200, 60)]);

        Assert.Contains("not available, 60 bars", summary, StringComparison.Ordinal);
        Assert.Contains("data-name=\"sma200\"", summary, StringComparison.Ordinal);
        Assert.Contains("data-bar-count=\"60\"", summary, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(summary, "class=\"absent-average\""));

        // And with nothing absent there is no footer at all, so the row appears
        // when it is true and not as a permanent caveat.
        var complete = new MarkRenderer().LevelSummary("TEST", bands);

        Assert.DoesNotContain("not available", complete, StringComparison.Ordinal);
        Assert.DoesNotContain("<tfoot", complete, StringComparison.Ordinal);

        // A name with no bands says so rather than drawing an empty table.
        var none = new MarkRenderer().LevelSummary("TEST", []);

        Assert.DoesNotContain("<table", none, StringComparison.Ordinal);
        Assert.Contains("no level bands stored", none, StringComparison.Ordinal);
    }

    static SummaryBand[] Summarised(IReadOnlyList<LevelRow> bands) =>
    [
        .. bands.Select(band => new SummaryBand(
            band.LowEdge,
            band.HighEdge,
            band.Role,
            band.Immediate,
            band.Strength,
            band.HasNonAverageAnchor,
            [
                .. JsonDocument.Parse(band.Members).RootElement.EnumerateArray().Select(member => new SummaryMember(
                    member.GetProperty("kind").GetString()!,
                    decimal.Parse(member.GetProperty("price").GetString()!, CultureInfo.InvariantCulture),
                    DateOnly.ParseExact(member.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture))),
            ])),
    ];

    // ---- 5.6, the run page ----

    // Constructed input, written straight into a throwaway store, which is what
    // the suite is exempt from writer ownership for. Nothing here reaches the
    // configured data root.
    static void Insert(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static JsonElement Expected(string stage) =>
        JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FixtureFolder(), "expectations", stage + ".json"))).RootElement;

    // Constructed listing reasons, in section 11's own names, because a stored
    // reason the roster does not carry is refused rather than counted.
    static string FiredNamed(params string[] names) =>
        "[" + string.Join(",", names.Select(name =>
            $"{{\"name\":\"{name}\",\"fired\":true,\"values\":{{\"close\":\"10\"}}}}")) + "]";

    static string RunRow(string runId, string stage, string started, string ended, string outcome = "ok") =>
        "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, " +
        $"model_calls, network_requests, spend, detail) VALUES ('{runId}', '{stage}', '{started}', " +
        $"'{ended}', '{outcome.Replace("'", "''", StringComparison.Ordinal)}', 3, 0, 1, '0', 'constructed');";

    // A command a person runs by hand writes to the run log between nights. The page draws
    // it as its own: never a stage of the night that failed, never in the night's stage
    // time, and never the night the page opens on.
    [Fact]
    public async Task ACommandRunByHandIsDrawnAsItsOwnAndIsNeitherAFailedStageNorInTheNightsTimeNorTheNightThePageOpensOn()
    {
        using var store = await FixtureExpectations.WithListings();

        var refused = VersionVerb.RunIdAt(new DateTimeOffset(2026, 9, 10, 23, 45, 0, TimeSpan.Zero));
        var backfill = VersionVerb.RunIdAt(new DateTimeOffset(2026, 9, 10, 23, 50, 0, TimeSpan.Zero));
        var later = VersionVerb.RunIdAt(new DateTimeOffset(2026, 9, 11, 14, 0, 0, TimeSpan.Zero));

        store.Execute(RunRow("night-20260910T233000Z", "rule-versions", "2026-09-10T23:29:00Z", "2026-09-10T23:30:00Z"));
        store.Execute(RunRow("night-20260910T233000Z", "close", "2026-09-10T23:30:00Z", "2026-09-10T23:31:00Z"));
        store.Execute(RunRow(refused, "rule-versions", "2026-09-10T23:45:00Z", "2026-09-10T23:45:00Z", "refused"));
        store.Execute(RunRow(backfill, "rule-versions", "2026-09-10T23:50:00Z", "2026-09-10T23:51:35Z"));
        store.Execute(RunRow(later, "rule-versions", "2026-09-11T14:00:00Z", "2026-09-11T14:00:00Z", "refused"));

        var api = Api(store);
        var night = new DateOnly(2026, 9, 10);

        // The newest row is a command, so the page opens on the night before it.
        Assert.Equal(night, await api.RunNightAsync());

        var stages = RunScreen.Stages(await api.RunLogAsync(night));
        var header = new MarkRenderer().OperationalHeader(night, stages);

        Assert.Equal(4, stages.Count);
        Assert.Equal(2, stages.Count(stage => stage.ByHand));
        Assert.Empty(RunScreen.Failed(stages));
        Assert.Equal(2, Regex.Matches(header, "data-by-hand=\"1\"").Count);
        Assert.Contains("<td>rule-versions (run by hand)</td>", header, StringComparison.Ordinal);

        // Sixty seconds and sixty seconds of the night, and the backfill's ninety-five left out.
        Assert.Contains(
            "<p class=\"total\" data-seconds=\"120\" data-by-hand=\"2\">2 stage(s) of the night, 120 second(s) of stage time, and 2 command(s) run by hand</p>",
            header,
            StringComparison.Ordinal);

        // The run ids the page reads as by hand are the verb's, and no night's, pass's or surface's.
        Assert.Contains(VersionVerb.RunPrefix, RunScreen.RunsByHand);
        Assert.True(RunScreen.IsByHand(later));
        Assert.All(
            ["night-20260910T233000Z", "night-20260910T233000Z-for-2026-09-10", "research-20260910T010000Z-KEYS", "fundamentals-20260910T010000Z-KEYS", "read-api-20260910T010000Z", "fixture-night"],
            id => Assert.False(RunScreen.IsByHand(id), id));
    }

    // A store behind the checkout is one migration short of a column some screen reads.
    // Every screen names both schema numbers rather than failing on the first column it
    // lacks, and the run page still draws the run log, which is where a refused night is.
    [Fact]
    public async Task EveryScreenOverAStoreBehindTheCheckoutNamesTheSchemaAndTheRunPageStillDrawsItsRunLog()
    {
        using var store = new TemporaryStore();

        new MigrationRunner([.. SchemaMigrations.All.Where(migration => migration.Version <= 23)]).Apply(store.DatabaseFile);
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
            "('night-refused', 'migrate', '2026-09-10T23:30:00Z', '2026-09-10T23:30:00Z', 'refused', 0, 0, 0, '0', 'refused before the first step: a key is missing');");

        using (var host = new Host(store.Root))
        using (var client = host.CreateClient())
        {
            foreach (var route in new[] { "/screens/run", "/screens/tonight", "/screens/universe", "/screens/name/AAPL" })
            {
                var response = await client.GetAsync(route);
                var body = await response.Content.ReadAsStringAsync();

                Assert.Equal((route, System.Net.HttpStatusCode.OK), (route, response.StatusCode));
                Assert.Contains("data-schema=\"23\"", body, StringComparison.Ordinal);
                Assert.Contains($"data-needs=\"{SchemaMigrations.LatestVersion}\"", body, StringComparison.Ordinal);
            }

            var run = await client.GetStringAsync("/screens/run");

            Assert.Contains("data-failed=\"1\"", run, StringComparison.Ordinal);
            Assert.Contains("refused before the first step: a key is missing", run, StringComparison.Ordinal);
        }

        // A store the checkout reads draws no such line.
        using var current = await FixtureExpectations.WithListings();
        using var currentHost = new Host(current.Root);
        using var currentClient = currentHost.CreateClient();

        Assert.DoesNotContain("data-schema=", await currentClient.GetStringAsync("/screens/run"), StringComparison.Ordinal);
    }

    // A registration's outcome is `registered` rather than ok, so read as a stage of the night
    // it was a stage that failed. It is a command a person ran, drawn as one.
    [Fact]
    public async Task ARegisterCommandIsDrawnAsRunByHandAndARegistrationIsNotAFailedStage()
    {
        using var store = await FixtureExpectations.WithListings();

        var registered = RegisterVerb.RunIdAt(new DateTimeOffset(2026, 9, 10, 23, 45, 0, TimeSpan.Zero));

        store.Execute(RunRow("night-20260910T233000Z", "close", "2026-09-10T23:30:00Z", "2026-09-10T23:31:00Z"));
        store.Execute(RunRow(registered, CandidateRegistrar.Stage, "2026-09-10T23:45:00Z", "2026-09-10T23:45:00Z", CandidateRegistrar.Registered));

        var stages = RunScreen.Stages(await Api(store).RunLogAsync(new DateOnly(2026, 9, 10)));

        Assert.Equal(2, stages.Count);
        Assert.Single(stages, stage => stage.ByHand && stage.Stage == CandidateRegistrar.Stage);
        Assert.Empty(RunScreen.Failed(stages));
        Assert.Contains(RegisterVerb.RunPrefix, RunScreen.RunsByHand);
        Assert.True(RunScreen.IsByHand(registered));
    }

    [Fact]
    public async Task TheOperationalHeaderDrawsEveryStageOfTheNightWithItsOwnElapsedTime()
    {
        // Section 15.10's first region. Per stage rather than in one total,
        // because a night that landed inside its limit by one step doing nothing
        // is legible only if the steps are apart.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);
        var night = ((IClock)FixedClock.At(Instant, SessionZones.UnitedStates)).SessionDateAt(Instant);
        var log = await api.RunLogAsync(night);

        // The population is the store's own rows for that night, and it is
        // floored well under what the replay writes so ordinary growth never
        // moves it. The property is the per-stage split, and a header drawn over
        // one row would satisfy every assertion below by having one stage.
        Assert.True(log.Count >= 8, $"The night carried {log.Count} stages, expected at least 8.");

        var stages = RunScreen.Stages(log);
        var header = new MarkRenderer().OperationalHeader(night, stages);

        Assert.Equal(log.Count, stages.Count);
        Assert.Contains($"data-stages=\"{stages.Count}\"", header, StringComparison.Ordinal);
        Assert.Equal(stages.Count, Regex.Matches(header, "<tr data-stage=\"[^\"]+\"").Count);

        // Every stage the store recorded is on the page, named, with the counts
        // the row carries rather than counts this test supplies. The counts are
        // read back off the row they were drawn into, so a header that printed
        // a constant would fail here rather than pass by drawing the right
        // number of rows.
        foreach (var row in log)
        {
            var drawnRow = Regex.Match(
                header,
                $"<tr data-stage=\"{Regex.Escape(row.Stage)}\"(?<attributes>[^>]*)>(?<cells>.*?)</tr>",
                RegexOptions.Singleline);

            Assert.True(drawnRow.Success, $"the header drew no row for the {row.Stage} stage");

            var attributes = drawnRow.Groups["attributes"].Value;

            // The instant the stage started, which is a different question from
            // how long it took and is the one the posting hour is read against.
            // owes: The provider's posting hour for the day's bulk file, measured from live fetches
            Assert.Contains(
                FormattableString.Invariant($"data-started=\"{row.StartedAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\""),
                attributes,
                StringComparison.Ordinal);

            Assert.Contains($"data-rows=\"{row.RowsWritten}\"", attributes, StringComparison.Ordinal);
            Assert.Contains($"data-model-calls=\"{row.ModelCalls}\"", attributes, StringComparison.Ordinal);
            Assert.Contains($"data-requests=\"{row.NetworkRequests}\"", attributes, StringComparison.Ordinal);
            Assert.Contains($"data-spend=\"{row.Spend}\"", attributes, StringComparison.Ordinal);
            Assert.Contains($"data-outcome=\"{row.Outcome}\"", attributes, StringComparison.Ordinal);

            // The cells and not only the attributes. A claim that something is
            // shown is a claim about the surface a person reads, and the two
            // channels are written by different expressions: the row's counts
            // can be right in the markup a machine reads and wrong in the cells
            // a person does, which is what the sweep found here.
            var cells = drawnRow.Groups["cells"].Value;

            Assert.Contains(FormattableString.Invariant($"<td>{row.StartedAt.UtcDateTime:HH:mm:ss}</td>"), cells, StringComparison.Ordinal);
            Assert.Contains($"<td>{row.RowsWritten}</td>", cells, StringComparison.Ordinal);
            Assert.Contains($"<td>{row.ModelCalls}</td><td>{row.NetworkRequests}</td>", cells, StringComparison.Ordinal);
            Assert.Contains($"<td>{row.Spend}</td><td>{row.Outcome}</td>", cells, StringComparison.Ordinal);
        }

        // The requests are the figure the cost rule is read against, so they are
        // asserted where they are not all the same number. One stage of this
        // night made a request and the rest made none, and a header printing a
        // constant would agree with the store on every row but that one.
        Assert.Contains(log, row => row.NetworkRequests > 0);
        Assert.Contains(log, row => row.NetworkRequests == 0);

        Assert.Contains("listings", header, StringComparison.Ordinal);
        Assert.Contains("forward-returns", header, StringComparison.Ordinal);

        // The elapsed time itself, over constructed rows, because every row a
        // fixed clock writes started and ended at the same instant and a night
        // of zeroes cannot show that the two columns are read apart.
        var timed = RunScreen.Stages(
        [
            new RunStageRow("run-1", "slow", Utc("2026-09-05T21:00:00Z"), Utc("2026-09-05T21:02:30Z"), "ok", 4, 0, 1, "0", "detail"),
            new RunStageRow("run-1", "quick", Utc("2026-09-05T21:02:30Z"), Utc("2026-09-05T21:02:31Z"), "ok", 0, 0, 0, "0", "detail"),
        ]);

        Assert.Equal([150d, 1d], [.. timed.Select(stage => stage.Seconds)]);

        var drawn = new MarkRenderer().OperationalHeader(night, timed);

        Assert.Contains("data-seconds=\"150\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-seconds=\"1\"", drawn, StringComparison.Ordinal);

        // What each stage said about itself is a cell a person reads, and not a
        // hover. Until the phase 5 sign-off it was the stage cell's title, so
        // the names a night could not compare were on the page only for a
        // pointer resting on the word "changes".
        Assert.Equal(2, Regex.Matches(drawn, "<td class=\"detail\">detail</td>").Count);
        Assert.DoesNotContain("title=", drawn, StringComparison.Ordinal);
        Assert.Contains("<th>Detail</th>", drawn, StringComparison.Ordinal);

        // The instant and the duration are drawn from different values, which is
        // what these two rows are shaped to show: the second stage starts where
        // the first ended, so a header deriving one column from the other would
        // disagree here. Asserted on the cells as well as the attributes, since
        // the instant is what a person reads off the page to bound the hour the
        // provider posts the day's file.
        Assert.Contains("data-started=\"2026-09-05T21:00:00Z\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-started=\"2026-09-05T21:02:30Z\"", drawn, StringComparison.Ordinal);
        Assert.Contains("<td>21:00:00</td>", drawn, StringComparison.Ordinal);
        Assert.Contains("<td>21:02:30</td>", drawn, StringComparison.Ordinal);

        Assert.Equal(
            [Utc("2026-09-05T21:00:00Z"), Utc("2026-09-05T21:02:30Z")],
            [.. timed.Select(stage => stage.StartedAt)]);

        // And the order is the order they ran, which is what makes a step that
        // did nothing findable beside the one before it.
        Assert.True(
            drawn.IndexOf("data-stage=\"slow\"", StringComparison.Ordinal)
                < drawn.IndexOf("data-stage=\"quick\"", StringComparison.Ordinal),
            "the header drew a later stage above an earlier one");

        // A night the log carries nothing for says so rather than drawing an
        // empty table, which reads as a night that did nothing.
        Assert.Contains(
            "carries no stage",
            new MarkRenderer().OperationalHeader(night, []),
            StringComparison.Ordinal);
    }

    static DateTimeOffset Utc(string instant) =>
        DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture);

    [Fact]
    public async Task ANightsRunLogIsTheNightsAndTheClockDecidesWhichNightARowIsOn()
    {
        // The run log has no session column and cannot have one it would agree
        // with: the run starts after the close in New York, so the UTC date it
        // carries is the session's on some evenings and the next day's on
        // others. The clock is what decides, and this is the pair of rows that
        // shows it: one written after midnight UTC that belongs to this night,
        // and one written the evening before that does not.
        using var store = await FixtureExpectations.WithReturns();

        Insert(store, RunRow("night-late", "close", "2026-09-06T01:30:00Z", "2026-09-06T01:31:00Z"));
        Insert(store, RunRow("night-before", "listings", "2026-09-04T21:00:00Z", "2026-09-04T21:00:10Z"));

        var api = Api(store);
        var night = new DateOnly(2026, 9, 5);
        var log = await api.RunLogAsync(night);

        // Half past nine in the evening in New York is half past one the next
        // morning in UTC, and it is this night. A filter on the stored date
        // would drop it.
        Assert.Contains(log, row => row.RunId == "night-late");

        // The evening before is its own night, and it is the one that would
        // widen this night's duration if the rows were read together.
        Assert.DoesNotContain(log, row => row.RunId == "night-before");

        Assert.Equal(
            ["night-before"],
            [.. (await api.RunLogAsync(new DateOnly(2026, 9, 4))).Select(row => row.RunId).Distinct()]);

        // The duration follows from the same selection, over the run that wrote
        // that night's list. Every stage the replay wrote carries one instant
        // from the fixed clock, so this night's span is zero and the evening
        // before is ten seconds. Before 5.6 the query took every run carrying a
        // listings stage and spanned the lot, so both of these read as ten
        // minutes and the date in the parameter changed nothing.
        Assert.Equal("00:00:00", await api.NightDurationAsync(night));
        Assert.Equal("00:00:10", await api.NightDurationAsync(new DateOnly(2026, 9, 4)));
        Assert.Null(await api.NightDurationAsync(new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public async Task ANightsDurationIsTheRunWhoseListTheStoreHoldsAndNotEveryRunThatReachedOne()
    {
        // A night that wrote its list, stopped at the next step and was run
        // again half an hour later wrote a list twice. Until the phase 5
        // sign-off the duration spanned both runs, from the first's start to
        // the second's end, which the by-hand runs of 2026-09-10 did.
        using var store = await FixtureExpectations.WithReturns();

        Insert(store, RunRow("night-first", "listings", "2026-09-03T21:00:00Z", "2026-09-03T21:00:10Z"));
        Insert(store, RunRow("night-first", "changes", "2026-09-03T21:00:10Z", "2026-09-03T21:00:12Z", "failed"));
        Insert(store, RunRow("night-again", "listings", "2026-09-03T21:30:00Z", "2026-09-03T21:30:05Z"));
        Insert(store, RunRow("night-again", "close", "2026-09-03T21:30:05Z", "2026-09-03T21:30:08Z"));

        var api = Api(store);

        // Both runs are the night's, which the page's stage table still shows,
        // and the duration is the second's alone.
        Assert.Equal(
            ["night-again", "night-first"],
            [.. (await api.RunLogAsync(new DateOnly(2026, 9, 3))).Select(row => row.RunId).Distinct().Order(StringComparer.Ordinal)]);
        Assert.Equal("00:00:08", await api.NightDurationAsync(new DateOnly(2026, 9, 3)));
    }

    [Fact]
    public async Task ANightsDurationIsTheRunThatWroteItsListLastWhenThatRunCarriesTheEarlierInstants()
    {
        // A night run by hand after midnight UTC and then run again for its session. The
        // second run is on the replay clock, which stamps its stages from 21:10Z on the
        // session, so the run that wrote the list the store holds carries the earlier
        // instants. The header draws that run's span, as the fired count beside it is that
        // run's list.
        using var store = await FixtureExpectations.WithReturns();

        Insert(store, RunRow("night-by-hand", "fetch", "2026-09-04T02:44:30Z", "2026-09-04T02:48:00Z"));
        Insert(store, RunRow("night-by-hand", "listings", "2026-09-04T02:50:06Z", "2026-09-04T02:50:07Z"));
        Insert(store, RunRow("night-by-hand", "close", "2026-09-04T02:50:07Z", "2026-09-04T02:50:34Z"));
        Insert(store, RunRow("night-again", "fetch", "2026-09-03T21:10:00Z", "2026-09-03T21:14:00Z"));
        Insert(store, RunRow("night-again", "listings", "2026-09-03T21:15:38Z", "2026-09-03T21:15:39Z"));
        Insert(store, RunRow("night-again", "close", "2026-09-03T21:15:39Z", "2026-09-03T21:15:52Z"));

        var api = Api(store);

        Assert.Equal(
            ["night-again", "night-by-hand"],
            [.. (await api.RunLogAsync(new DateOnly(2026, 9, 3))).Select(row => row.RunId).Distinct().Order(StringComparer.Ordinal)]);
        Assert.Equal("00:05:52", await api.NightDurationAsync(new DateOnly(2026, 9, 3)));
    }

    [Fact]
    public async Task ANightsDurationIsNotARunAgainWhoseListingsStepStoppedAndLeftTheEarlierRunsList()
    {
        // A night that wrote its list and was then run again for its session, the second run's
        // listings step failing. Its list rolled back, so the store holds the first run's, and
        // the listings row the second run holds is the stop the night recorded under the stage.
        using var store = await FixtureExpectations.WithReturns();

        Insert(store, RunRow("night-scheduled", "fetch", "2026-09-03T23:30:07Z", "2026-09-03T23:34:00Z"));
        Insert(store, RunRow("night-scheduled", "listings", "2026-09-03T23:37:12Z", "2026-09-03T23:37:13Z"));
        Insert(store, RunRow("night-scheduled", "close", "2026-09-03T23:37:13Z", "2026-09-03T23:37:40Z"));
        Insert(store, RunRow("night-again", "fetch", "2026-09-03T21:10:00Z", "2026-09-03T21:14:00Z"));
        Insert(store, StopRow("night-again", "listings", "2026-09-03T21:15:38Z", "2026-09-03T21:15:40Z", NightClose.Failed));

        Assert.Equal("00:07:33", await Api(store).NightDurationAsync(new DateOnly(2026, 9, 3)));
    }

    [Fact]
    public async Task ANightsDurationIsARunWhoseListCommittedWithACandidateFaultedInShadowAndNotAStopBeforeTheStep()
    {
        // The stage's own row reads failed in words of its own where a registered candidate went
        // unevaluated, and its list committed all the same, so that run is the one whose list
        // the store holds. A run after it stopped before the step on the allowance wrote a row
        // under the stage and wrote no list.
        using var store = await FixtureExpectations.WithReturns();

        Insert(store, RunRow("night-scheduled", "fetch", "2026-09-03T23:30:07Z", "2026-09-03T23:34:00Z"));
        Insert(store, RunRow("night-scheduled", "listings", "2026-09-03T23:37:12Z", "2026-09-03T23:37:13Z"));
        Insert(store, RunRow("night-scheduled", "close", "2026-09-03T23:37:13Z", "2026-09-03T23:37:40Z"));
        Insert(store, RunRow("night-again", "fetch", "2026-09-03T21:10:00Z", "2026-09-03T21:14:00Z"));
        Insert(store, RunRow("night-again", "listings", "2026-09-03T21:15:38Z", "2026-09-03T21:15:40Z", ShortlistBuilder.Failed));
        Insert(store, RunRow("night-again", "close", "2026-09-03T21:15:40Z", "2026-09-03T21:15:52Z"));

        var api = Api(store);

        Assert.Equal("00:05:52", await api.NightDurationAsync(new DateOnly(2026, 9, 3)));

        Insert(store, StopRow("night-allowance", "listings", "2026-09-03T21:20:00Z", "2026-09-03T21:20:00Z", NightClose.Stopped));

        Assert.Equal("00:05:52", await api.NightDurationAsync(new DateOnly(2026, 9, 3)));
    }

    [Fact]
    public async Task ANightWhoseOnlyListingsRowsAreStopsDrawsTheLastRunToReachTheStage()
    {
        // No run wrote a list for the night, so no fired count is drawn beside the duration, and
        // the span is the last run that reached the stage rather than none.
        using var store = await FixtureExpectations.WithReturns();

        Insert(store, RunRow("night-first", "fetch", "2026-09-03T23:30:00Z", "2026-09-03T23:34:00Z"));
        Insert(store, StopRow("night-first", "listings", "2026-09-03T23:34:00Z", "2026-09-03T23:34:05Z", NightClose.Failed));
        Insert(store, RunRow("night-again", "fetch", "2026-09-03T21:10:00Z", "2026-09-03T21:12:00Z"));
        Insert(store, StopRow("night-again", "listings", "2026-09-03T21:12:00Z", "2026-09-03T21:12:30Z", NightClose.Stopped));

        Assert.Equal("00:02:30", await Api(store).NightDurationAsync(new DateOnly(2026, 9, 3)));
    }

    // The outcomes the listings stage writes on its own row, which the duration reads as the
    // stage having written its list. A stop the night records under the stage writes none of them.
    [Fact]
    public void TheReadSurfaceNamesTheOutcomesTheListingsStageWritesAndNoStopWritesEither()
    {
        Assert.Equal([ShortlistBuilder.Ok, ShortlistBuilder.Failed], ReadApi.ListingsStageOutcomes);
        Assert.Empty(ReadApi.ListingsStageOutcomes.Intersect([NightClose.Failed, NightClose.Stopped, NightClose.Refused, NightClose.NoSession]));
    }

    // A row as the night's stop writes it: no rows written and the stop's own sentence.
    static string StopRow(string runId, string stage, string started, string ended, string outcome) =>
        "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, " +
        $"model_calls, network_requests, spend, detail) VALUES ('{runId}', '{stage}', '{started}', " +
        $"'{ended}', '{outcome}', 0, 0, 0, '0', 'step ''{stage}'' stopped');";

    // A read of the run log that takes its newest row by the instant the row carries, as SQL
    // ordering on the start or as the rows ordered on it.
    static readonly Regex NewestByInstant = new(
        @"ORDER\s+BY\s+started_at\s+DESC|OrderByDescending\(\s*(\w+)\s*=>\s*\1\.StartedAt\s*\)",
        RegexOptions.IgnoreCase);

    [Fact]
    public void ARunLogReadTakesItsNewestRowByTheOrderTheRowsWereWrittenOutsideTheTwoThatStateWhyTheyMayNot()
    {
        // A night run again for a named session stamps its stages from 21:10Z on that session,
        // so the newest instant is not the newest run. The two reads left on the instant are
        // named with their reason: the pass a page started is chosen at or after the press, and
        // a pass is not a night; the backfill's rows for one session are the same row, and the
        // page reads them only for a name holding no bar.
        string[] allowed = ["PassRowsForName", "BackfillRows"];

        Assert.Matches(NewestByInstant, "        ORDER BY started_at DESC;");
        Assert.Matches(NewestByInstant, ".OrderByDescending(row => row.StartedAt)");
        Assert.DoesNotMatch(NewestByInstant, "ORDER BY rowid DESC");
        Assert.DoesNotMatch(NewestByInstant, ".OrderByDescending(row => row.SessionDate)");

        var found = new List<string>();

        foreach (var file in Directory.GetFiles(Path.Combine(Repository.Root, "src", "EquityBrief.Api"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var source = Regex.Replace(File.ReadAllText(file), @"//[^\r\n]*", string.Empty);

            foreach (Match match in NewestByInstant.Matches(source))
            {
                // A query is named by the constant holding it; a read in code by its file and line.
                var constant = match.Value.Contains("started_at", StringComparison.OrdinalIgnoreCase)
                    ? Regex.Matches(source[..match.Index], @"const\s+string\s+(\w+)\s*=").LastOrDefault()
                    : null;

                found.Add(constant is null
                    ? $"{Path.GetFileName(file)} line {source[..match.Index].Count(character => character == '\n') + 1}"
                    : constant.Groups[1].Value);
            }
        }

        Assert.Equal(allowed.Order(StringComparer.Ordinal), found.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AReasonsRecordCountsEverySetupThatFiredItAndShowsNoRateBelowTheMinimum()
    {
        // Section 15.10's reason record, in the state this build is in for its
        // first year: counts, and no rate at all.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);
        var listings = await api.ListingsAsync();
        var returns = await api.ForwardReturnsAsync();
        var records = RunScreen.Records(listings, RunScreen.Resolved(returns));

        // The population, derived here from the stored rows rather than read
        // back from the projection, and stated in advance by the expectation
        // file rather than frozen from this run.
        var expected = Expected("run-page");

        Assert.Equal(expected.GetProperty("counts").GetProperty("reasons").GetInt32(), records.Count);
        Assert.Equal(ShortlistSeries.Reasons.Length, records.Count);
        Assert.Equal(ReasonVerdict.MinimumResolved, expected.GetProperty("rules").GetProperty("minimumResolvedSetups").GetInt32());
        Assert.Equal(expected.GetProperty("counts").GetProperty("resolved").GetInt32(), records.Sum(record => record.Resolved));
        Assert.Equal(expected.GetProperty("counts").GetProperty("fired").GetInt32(), records.Sum(record => record.Fired));

        // The nights, for the listings and for each record, which over rows all in the
        // corrected shape are the same count.
        var nights = expected.GetProperty("counts").GetProperty("nights").GetInt32();

        Assert.Equal(nights, RunScreen.Nights(listings));
        Assert.All(records, record => Assert.Equal(nights, record.Nights));

        var fired = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var listing in listings)
        {
            foreach (var reason in JsonDocument.Parse(listing.Reasons).RootElement.EnumerateArray()
                .Where(reason => reason.GetProperty("fired").GetBoolean())
                .Select(reason => reason.GetProperty("name").GetString()!))
            {
                fired[reason] = fired.GetValueOrDefault(reason) + 1;
            }
        }

        foreach (var record in records)
        {
            Assert.Equal(fired.GetValueOrDefault(record.Reason), record.Fired);
        }

        // Nothing the committed fixture holds has matured, so every record is
        // below the minimum and carries no rate. That is the state, not a hole:
        // its listings sit on the last stored session and nothing after them
        // exists.
        Assert.All(records, record => Assert.False(record.HasEarnedAVerdict));
        Assert.Equal(0, records.Sum(record => record.Resolved));

        // A setup belongs to every reason that fired on the night it was listed,
        // and a resolved setup for a name nobody listed belongs to none. Both
        // over constructed rows, because the fixture cannot reach either.
        var night = new DateOnly(2026, 9, 4);

        var constructed = RunScreen.Records(
            [
                new ListingRow("AAAA", night, FiredNamed(ShortlistSeries.AtEntryZone, ShortlistSeries.CrossedALevel), 2, "{}"),
                new ListingRow("BBBB", night, FiredNamed(ShortlistSeries.AtEntryZone), 1, "{}"),
            ],
            [
                new ResolvedSetup("AAAA", night, ForwardReturnSeries.Win),
                new ResolvedSetup("BBBB", night, ForwardReturnSeries.Loss),
                new ResolvedSetup("CCCC", night, ForwardReturnSeries.Win),
            ]);

        var atEntry = constructed.Single(record => record.Reason == ShortlistSeries.AtEntryZone);
        var crossed = constructed.Single(record => record.Reason == ShortlistSeries.CrossedALevel);

        // AAAA fired two reasons and won, so both carry that win. BBBB fired one
        // and lost. A setup counted against one reason alone would be a record
        // about whichever reason happened to be read first.
        Assert.Equal((1, 1), (atEntry.Won, atEntry.Lost));
        Assert.Equal((1, 0), (crossed.Won, crossed.Lost));

        // The unlisted name is in nobody's record, which is what keeps a record
        // a statement about the reason that fired. Two of the three resolved
        // setups reach a record, and one of those reaches two.
        Assert.Equal(3, constructed.Sum(record => record.Resolved));
        Assert.All(
            constructed.Where(record => record.Reason != ShortlistSeries.AtEntryZone && record.Reason != ShortlistSeries.CrossedALevel),
            record => Assert.Equal(0, record.Resolved));

        // And a stored reason the roster does not carry refuses rather than
        // being counted into whichever row is read first.
        var unknown = Assert.Throws<InvalidOperationException>(
            () => RunScreen.Records([new ListingRow("AAAA", night, FiredNamed("a seventh reason"), 1, "{}")], []));

        Assert.Contains("a seventh reason", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASetupNobodyEnteredIsDrawnInItsOwnColumnAndCountsTowardNoRate()
    {
        // 8.1's column. A setup whose price never reached the entry the plan named
        // is not a trade that went badly, so it is neither resolved nor in any
        // rate, and a reader has to be able to see how many of them there were: it
        // is the difference between a reason that fires where nothing can be
        // bought and one that fires where something can.
        // see: A setup is scored from its entry, and a target reached before the entry is never a win
        var night = new DateOnly(2026, 9, 4);

        var records = RunScreen.Records(
            [
                new ListingRow("AAAA", night, FiredNamed(ShortlistSeries.AtEntryZone), 1, "{}"),
                new ListingRow("BBBB", night, FiredNamed(ShortlistSeries.AtEntryZone), 1, "{}"),
                new ListingRow("CCCC", night, FiredNamed(ShortlistSeries.AtEntryZone), 1, "{}"),
            ],
            [
                new ResolvedSetup("AAAA", night, ForwardReturnSeries.Win),
                new ResolvedSetup("BBBB", night, ForwardReturnSeries.NeverEntered),
                new ResolvedSetup("CCCC", night, ForwardReturnSeries.NeverEntered),
            ]);

        var record = records.Single(one => one.Reason == ShortlistSeries.AtEntryZone);

        Assert.Equal(2, record.NeverEntered);
        Assert.Equal(1, record.Resolved);
        Assert.Equal((1, 0, 0), (record.Won, record.Lost, record.Unresolved));

        // Drawn in its own column on the row the run page renders, beside the
        // resolved count rather than inside it.
        var row = Assert.Single(Blocks(
            new MarkRenderer().ReasonRecords(records, RunScreen.Tracks(records), [new BaseRateLine("5", 50d)], nights: 1),
            $"<tr data-reason=\"{ShortlistSeries.AtEntryZone}\".*?</tr>"));

        Assert.Contains("data-resolved=\"1\"", row, StringComparison.Ordinal);
        Assert.Contains("data-never-entered=\"2\"", row, StringComparison.Ordinal);

        // And the track the mark draws carries no never-entered segment, since a
        // setup nobody entered is not a slice of a denominator it is not in.
        var track = RunScreen.Tracks(records).Single(one => one.Reason == ShortlistSeries.AtEntryZone);

        Assert.Equal(1, track.Total);
    }

    [Fact]
    public void ARecordBelowTheMinimumDrawsItsCountInADashedOutlineAndNeverARate()
    {
        // 15.11's first row, and the state the column is in for the first year.
        // The count against the minimum is what makes the absence readable: a
        // reader sees how far off a verdict is rather than only that there is
        // none.
        var marks = new MarkRenderer();

        // Both floors from 8.5, so a record that clears the rows and not the
        // nights is the third case rather than an oversight. The one above
        // clears both; the one below clears neither; and the middle one clears
        // the rows on 12 sessions, which is the case a single floor would have
        // drawn a verdict for.
        var below = new ReasonRecord(
            ShortlistSeries.AtEntryZone, 400, 6, 5, 40, ReasonVerdict.MinimumResolved,
            Scored: 11, Sessions: 4, SessionMinimum: ReasonVerdict.MinimumSessions,
            Withheld: ReasonVerdict.BelowTheResolvedMinimum);

        var above = new ReasonRecord(
            ShortlistSeries.CrossedALevel, 900, 150, 130, 60, ReasonVerdict.MinimumResolved,
            Scored: 280, Share: 53.57d, BreakEven: 41d, Sessions: 90, SessionMinimum: ReasonVerdict.MinimumSessions,
            Cleared: true, PValue: 0.0001d, Threshold: ReasonVerdict.Significance / ReasonVerdict.LiveFamily,
            Divisor: ReasonVerdict.LiveFamily, Withheld: ReasonVerdict.Shown, Significance: ReasonVerdict.Significance);

        var fewNights = new ReasonRecord(
            ShortlistSeries.UnusualVolume, 900, 150, 130, 60, ReasonVerdict.MinimumResolved,
            Scored: 280, Sessions: 12, SessionMinimum: ReasonVerdict.MinimumSessions,
            Withheld: ReasonVerdict.BelowTheSessionMinimum);

        Assert.False(below.HasEarnedAVerdict);
        Assert.True(above.HasEarnedAVerdict);
        Assert.False(fewNights.HasEarnedAVerdict);

        var records = new[] { below, above, fewNights };
        var drawn = marks.ReasonRecords(records, RunScreen.Tracks(records), Rates(1.2, 3.4), 60);

        // One row per reason with the reason track mark, which is what section
        // 15.10 states the region is. A table of counts with the picture gone
        // is a different region.
        Assert.Equal(records.Length, Regex.Matches(drawn, "<svg class=\"reason-track\"").Count);

        Assert.Contains("data-outline=\"dashed\"", drawn, StringComparison.Ordinal);
        Assert.Contains("11 of 250 resolved", drawn, StringComparison.Ordinal);
        Assert.Contains("data-verdict=\"none\"", drawn, StringComparison.Ordinal);

        // Which floor is short is named, because a reader who cannot tell whether
        // they are waiting for rows or for nights cannot tell how long for.
        Assert.Contains("data-short=\"resolved\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-short=\"sessions\"", drawn, StringComparison.Ordinal);
        Assert.Contains("280 of 250 resolved setups that set a bar, over 12 of 60 listing session(s)", drawn, StringComparison.Ordinal);

        // The row that cleared both draws the three together with its verdict and
        // divisor; neither of the other two draws a rate at all.
        Assert.Contains("53.57 per cent of 280 resolved", drawn, StringComparison.Ordinal);
        Assert.Contains("data-divisor=\"6\"", drawn, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(drawn, "per cent of [0-9]+ resolved"));

        // The nights the record stands on, which is what the three operating
        // obligations read on this page are counted in.
        Assert.Contains("data-nights=\"60\"", drawn, StringComparison.Ordinal);
        Assert.Contains("stands on 60 night(s)", drawn, StringComparison.Ordinal);

        // And the split is gated on the same minimum in the picture as in the
        // column. A win beside a loss below the minimum is the same figure
        // through a second channel, since a reader reads the ratio off it.
        var tracks = RunScreen.Tracks(records);

        Assert.Equal(0, tracks[0].Won);
        Assert.Equal(0, tracks[0].Lost);
        Assert.Equal(11, tracks[0].ResolvedUnsplit);
        Assert.Equal(150, tracks[1].Won);
        Assert.Equal(130, tracks[1].Lost);
        Assert.Equal(0, tracks[1].ResolvedUnsplit);

        // And the night floor gates the picture as well as the column, which is
        // the same rule one channel along: a reason with 280 resolved over 12
        // sessions has its split withheld from the mark too.
        Assert.Equal(0, tracks[2].Won);
        Assert.Equal(0, tracks[2].Lost);
        Assert.Equal(280, tracks[2].ResolvedUnsplit);
    }

    static IReadOnlyList<BaseRateLine> Rates(double? five, double? twentyOne) =>
    [
        new BaseRateLine(ForwardReturnSeries.FiveSessions, five),
        new BaseRateLine(ForwardReturnSeries.TwentyOneSessions, twentyOne),
    ];

    [Fact]
    public async Task NoForwardReturnFigureIsDrawnWithoutTheUniverseBaseRateBesideIt()
    {
        // Section 17's base rate limit, on the surface a person reads it on. The
        // filler computes the figure and stores it beside every return; the
        // claim is that no forward-return figure on this page is shown without
        // it, which is a claim about a surface.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);
        var returns = await api.ForwardReturnsAsync();
        var rates = RunScreen.BaseRates(returns);

        // One line per window that has one, and the setup horizon is not among
        // them by rule rather than by absence.
        Assert.Equal(
            [ForwardReturnSeries.FiveSessions, ForwardReturnSeries.TwentyOneSessions],
            [.. rates.Select(rate => rate.Window)]);

        // The value is the stored column and not a count taken here. Nothing in
        // the committed fixture has matured, so both windows read as not yet
        // measured rather than as zero.
        foreach (var rate in rates)
        {
            Assert.Equal(
                returns.FirstOrDefault(row => row.Horizon == rate.Window && row.BaseRate is not null)?.BaseRate,
                rate.Rate);
        }

        var records = RunScreen.Records(await api.ListingsAsync(), RunScreen.Resolved(returns));
        var marks = new MarkRenderer();
        var drawn = marks.ReasonRecords(records, RunScreen.Tracks(records), rates, 1);

        Assert.Contains("data-window=\"5\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-window=\"21\"", drawn, StringComparison.Ordinal);
        Assert.Contains("is not yet measured", drawn, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(drawn, "data-pinned=\"true\"").Count);

        // The population in the same breath as the figure, which is what turns
        // a number into one a reader can check.
        Assert.Contains("every name-night the store holds", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("data-window=\"setup\" data-base-rate=\"none\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-window=\"setup\" data-base-rate=\"none by rule\"", drawn, StringComparison.Ordinal);

        // A measured window carries its value, over constructed rows because the
        // fixture's own listings sit on the last stored session and nothing
        // after them exists.
        var measured = marks.ReasonRecords(records, RunScreen.Tracks(records), Rates(41.5, 57.25), 1);

        Assert.Contains("data-base-rate=\"41.5\"", measured, StringComparison.Ordinal);
        Assert.Contains("57.25 per cent", measured, StringComparison.Ordinal);

        // And the region refuses rather than drawing the records with no base
        // rate at all. A figure that can be shown without it is one that will
        // be, on the evening somebody passes an empty list.
        var refused = Assert.Throws<InvalidOperationException>(
            () => marks.ReasonRecords(records, RunScreen.Tracks(records), [], 1));

        Assert.Contains("base rate", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReasonTrackDrawsThreeStatesOutOfOneDenominatorAndOutlinesTheUnresolved()
    {
        // Section 15.5's sixth mark. Its third state is a dashed outline and
        // never a third colour, because unresolved is not a smaller amount of
        // losing and must not read as one.
        var track = new MarkRenderer().ReasonTrack(
        [
            new ReasonTrackRow("busy", 6, 2, 2),
            new ReasonTrackRow("quiet", 2, 1, 2),
            new ReasonTrackRow("never fired", 0, 0, 0),
        ]);

        Assert.Equal(3, Regex.Matches(track, "<g data-reason=").Count);
        Assert.Contains("data-denominator=\"10\"", track, StringComparison.Ordinal);

        // One denominator across the mark rather than one per row. The busy
        // reason's won segment is three times the quiet one's, which is the point
        // of the mark: scaled per row every bar would be full width and the
        // picture would say nothing about which reason fired on more names.
        var widths = Regex.Matches(track, "data-state=\"won\" data-count=\"(\\d+)\" x=\"[\\d.]+\" y=\"\\d+\" width=\"([\\d.]+)\"")
            .Select(match => double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(2, widths.Length);
        Assert.Equal(3d, widths[0] / widths[1], 3);

        // The unresolved segment has no fill at all and carries the dashes.
        var unresolved = Regex.Matches(track, "<rect data-state=\"unresolved\"[^/]*/>");

        Assert.Equal(2, unresolved.Count);
        Assert.All(unresolved, match => Assert.Contains("fill=\"none\"", match.Value, StringComparison.Ordinal));
        Assert.All(unresolved, match => Assert.Contains("stroke-dasharray", match.Value, StringComparison.Ordinal));

        // Hue is never the only channel: every row carries its counts in words.
        Assert.Contains("6 won, 2 lost and 2 unresolved, of 10", track, StringComparison.Ordinal);

        // A reason that fired on nothing draws its rule rather than nothing, so
        // a reason that never fires is visible as one that never fires rather
        // than absent from the picture.
        Assert.Contains("no setup on this reason yet", track, StringComparison.Ordinal);
        Assert.Contains("data-reason=\"never fired\" data-total=\"0\"", track, StringComparison.Ordinal);

        // The rule itself, and not only the words on the title. A group holding
        // nothing at all is an empty space on the picture, and an empty space is
        // what a reason absent from the mark would look like.
        var groups = Regex.Matches(track, "<g data-reason=\"(?<reason>[^\"]+)\".*?</g>", RegexOptions.Singleline);
        var empty = groups.Single(group => group.Groups["reason"].Value == "never fired").Value;

        Assert.Contains("height=\"1\" fill=\"var(--rule", empty, StringComparison.Ordinal);
        Assert.DoesNotContain("data-state=", empty, StringComparison.Ordinal);

        // And a reason that did fire draws its segments rather than a rule, so
        // the two states are told apart by the picture as well as by the words.
        Assert.DoesNotContain(
            "height=\"1\" fill=\"var(--rule",
            groups.Single(group => group.Groups["reason"].Value == "busy").Value,
            StringComparison.Ordinal);

        // Below the minimum the resolved setups are one segment rather than two,
        // and the words follow the picture.
        var gated = new MarkRenderer().ReasonTrack([new ReasonTrackRow("gated", 0, 0, 40, 11)]);

        Assert.Contains("data-state=\"resolved\" data-count=\"11\"", gated, StringComparison.Ordinal);
        Assert.DoesNotContain("data-state=\"won\"", gated, StringComparison.Ordinal);
        Assert.Contains("11 resolved and 40 unresolved, of 51", gated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryReasonOnARowCarriesItsRecordAndTheValuesThatMadeItTrue()
    {
        // Section 15.7's reasons-per-row region. The record is the reason's and
        // never the name's: it says how this reason has done across every name
        // it ever fired for, and it is not a statement about the row it sits in.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync(night);
        var universe = await api.UniverseAsync("GSPC");


        var rows = TonightScreen.Rows(
            night,
            listings,
            UniverseScreen.Rows(universe).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal),
            await api.ClosesToTheNightAsync(night));

        Assert.NotEmpty(rows);

        var records = RunScreen.Records(await api.ListingsAsync(), RunScreen.Resolved(await api.ForwardReturnsAsync()));
        var list = new MarkRenderer().TonightList(rows, SinglePageApp.TonightDrawn, records);

        // Every reason that fired on a drawn row is named on that row, with the
        // values the store holds for it rather than values this test supplies.
        foreach (var row in rows)
        {
            Assert.NotNull(row.Fired);

            foreach (var reason in row.Fired!)
            {
                Assert.Contains($"data-reason=\"{reason.Name}\"", list, StringComparison.Ordinal);

                foreach (var value in reason.Values)
                {
                    Assert.Contains($"<dt>{value.Key}</dt><dd>{value.Value}</dd>", list, StringComparison.Ordinal);
                }
            }
        }

        // The record beside each reason, in the not-yet-measured state, carrying
        // its count against the minimum rather than a rate.
        var named = rows.SelectMany(row => row.Reasons).Distinct(StringComparer.Ordinal).Count();

        Assert.True(named >= 1, $"the drawn rows named {named} reasons, expected at least 1.");
        Assert.Equal(
            rows.Sum(row => row.Reasons.Count),
            Regex.Matches(list, "class=\"record not-measured\"").Count);

        Assert.Contains($"of {ReasonVerdict.MinimumResolved} resolved setups that set a bar", list, StringComparison.Ordinal);

        // And it is drawn INSIDE the reason's own span, which is the hard rule
        // rather than a detail of the markup: the record says how this reason
        // has done across every name it ever fired for, so a record in a column
        // of its own reads as a property of the ticker on that row.
        //
        // The count above cannot see this. Move every record span out of its
        // reason and into a cell on the ticker's row and the count is unchanged,
        // every data-reason is still present and no rate appears, so all three
        // assertions stay green while the rule is broken. What has teeth is the
        // nesting, asserted both ways: every record sits inside a reason, and
        // none sits outside one.
        // see: A reason's record is displayed, beside the reason and never beside the name
        // A row's record sits inside that row's own reason disclosure, and the footer's sits
        // inside the footer's own reason span. The two are counted apart rather than by one
        // matcher, because `record` is a prefix of `record-foot`: a matcher keyed on the
        // prefix counts both and is satisfied by either, which is how it stayed green while
        // only half of them were nested.
        var onRows = Regex.Matches(list, "<span class=\"record[ \"]").Count;
        var inFoot = Regex.Matches(list, "<span class=\"record-foot[ \"]").Count;

        // The cell is the unit, because a cell holds one reason and the panel that carries the
        // record is nested inside it: bounding on the next cell says the record is inside the
        // reason it belongs to, where bounding on a closing tag would stop at the inner one.
        var nestedOnRows = Regex.Matches(
            list,
            "<div class=\"reason\"[^>]*>(?:(?!<td).)*?<span class=\"record[ \"]",
            RegexOptions.Singleline).Count;

        var nestedInFoot = Regex.Matches(
            list,
            "<span class=\"reason\"[^>]*>(?:(?!</span>).)*?<span class=\"record-foot[ \"]",
            RegexOptions.Singleline).Count;

        Assert.True(onRows >= 1, $"the rows drew {onRows} records, expected at least 1.");
        Assert.True(inFoot >= 1, $"the footer drew {inFoot} records, expected at least 1.");
        Assert.Equal(onRows, nestedOnRows);
        Assert.Equal(inFoot, nestedInFoot);

        // A row with no values stored for a reason says so rather than drawing
        // an empty hover, which reads as a reason with nothing behind it.
        var bare = new MarkRenderer().TonightList(
            [new ListingCell("ZZZZ", night, 1, 0, 10m, [ShortlistSeries.AtEntryZone])],
            SinglePageApp.TonightDrawn,
            records);

        Assert.Contains("no values stored for this reason", bare, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryRowOnTonightsListSaysWhetherTheNameHoldsResearch()
    {
        // The link on a row called itself a report whatever the store held, and the store
        // holds nothing but the key under each figure for almost every name. Read back off
        // each row against the store in both directions: a row claiming research the store
        // lacks and a row hiding research it holds both fail.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync(night);
        var universe = await api.UniverseAsync("GSPC");

        var researched = await api.ResearchedAsync();

        var rows = TonightScreen.Rows(
            night,
            listings,
            UniverseScreen.Rows(universe).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal),
            await api.ClosesToTheNightAsync(night),
            null,
            researched);

        var list = new MarkRenderer().TonightList(rows, SinglePageApp.TonightDrawn, []);
        var drawn = Regex.Matches(list, "<a class=\"open( unwritten)?\"[^>]*data-researched=\"(true|false)\"[^>]*>([^<]*)</a>");

        Assert.NotEmpty(drawn);

        var holds = researched.ToDictionary(row => row.Ticker, row => row.Written, StringComparer.Ordinal);
        var shown = rows.Take(SinglePageApp.TonightDrawn).ToArray();

        Assert.Equal(shown.Length, drawn.Count);

        foreach (var (row, at) in shown.Select((row, at) => (row, at)))
        {
            var says = drawn[at].Groups[2].Value == "true";

            Assert.Equal(holds.ContainsKey(row.Ticker), says);
            Assert.Equal(says ? "report" : "not written", drawn[at].Groups[3].Value);
        }

        // The other direction, over a row the store holds research for, so the false
        // case is not passing because every row happens to be false.
        var written = new DateOnly(2026, 9, 18);
        var pair = new MarkRenderer().TonightList(
            [
                new ListingCell("ZZZZ", night, 1, 0, 10m, [ShortlistSeries.AtEntryZone]),
                new ListingCell("YYYY", night, 1, 0, 10m, [ShortlistSeries.AtEntryZone]) { ResearchedOn = written },
            ],
            SinglePageApp.TonightDrawn,
            []);

        Assert.Contains("data-researched=\"false\" title=\"open the name, whose researched sections are not written\">not written</a>", pair, StringComparison.Ordinal);
        Assert.Contains($"data-researched=\"true\" data-researched-on=\"2026-09-18\"", pair, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachReasonOnTonightsListOpensOnWhatTheNightMeasuredItOver()
    {
        // The values a reason was measured over were held in a title attribute, which is a
        // native tooltip a reader on a touch screen cannot reach. They are drawn now, and this
        // reads them back off each reason's own panel rather than off the page as a whole: a
        // value drawn under the wrong reason answers about the wrong name, and a page-wide
        // search cannot tell the two apart.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync(night);
        var universe = await api.UniverseAsync("GSPC");

        var rows = TonightScreen.Rows(
            night,
            listings,
            UniverseScreen.Rows(universe).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal),
            await api.ClosesToTheNightAsync(night));

        var list = new MarkRenderer().TonightList(rows, SinglePageApp.TonightDrawn, []);

        // One panel per reason that fired, across every drawn row, read to the end of the cell
        // that holds it rather than to a closing tag, which the panel nests inside.
        var fired = rows.Sum(row => row.Fired!.Count);
        var drawn = Regex.Matches(list, "<div class=\"reason\" data-reason=\"([^\"]+)\" tabindex=\"0\">(.*?)</td>", RegexOptions.Singleline);

        Assert.True(fired >= 1, $"the drawn rows fired {fired} reasons, expected at least 1.");
        Assert.Equal(fired, drawn.Count);

        // And each one carries its own reason's values, keyed to the reason it is inside.
        var reasons = rows.SelectMany(row => row.Fired!).ToArray();

        Assert.Equal(fired, reasons.Length);

        for (var at = 0; at < reasons.Length; at++)
        {
            var reason = reasons[at];
            var body = drawn[at].Groups[2].Value;

            // The head carries the short word the columns are keyed by, which the verdicts
            // surface pins; what the panel adds is the reason's whole name, so a cell under
            // the pointer says which of the six it is.
            Assert.Equal(reason.Name, drawn[at].Groups[1].Value);
            Assert.Matches("<span class=\"r-head\">[a-z]+</span>", body);
            Assert.Contains($"<p class=\"why-fired\">{reason.Name}</p>", body, StringComparison.Ordinal);

            var values = Regex.Matches(body, "<dt>([^<]*)</dt><dd>([^<]*)</dd>")
                .ToDictionary(one => one.Groups[1].Value, one => one.Groups[2].Value, StringComparer.Ordinal);

            Assert.Equal(reason.Values.Count, values.Count);
            Assert.All(reason.Values, value => Assert.Equal(value.Value, values[value.Key]));

            // And none of it is left in a title attribute, which is the native tooltip this
            // replaced and the one surface the values cannot be read off. Scoped to the cell:
            // the column heads carry one, which spells out the word a column is headed by and
            // is not a value the night measured.
            Assert.DoesNotContain("title=", body, StringComparison.Ordinal);
        }

        // A reason the store holds no values for says so rather than opening on nothing.
        var bare = new MarkRenderer().TonightList(
            [new ListingCell("ZZZZ", night, 1, 0, 10m, [ShortlistSeries.AtEntryZone])],
            SinglePageApp.TonightDrawn,
            []);

        Assert.Contains("<p class=\"no-values\">no values stored for this reason</p>", bare, StringComparison.Ordinal);
        Assert.DoesNotContain("<dl class=\"reason-values\"", bare, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReasonTotalsDrawTonightsFiredNamesAndEveryOneOfThemIsUnresolved()
    {
        // Section 15.7's last region. Whether the evening is one thing happening
        // to many names or many things happening to a few, and every name on it
        // is a setup nothing has scored yet.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync(night);

        var totals = TonightScreen.Totals(listings);
        var tracks = RunScreen.Tracks(totals);
        var fired = TonightScreen.Fired(listings);
        var drawn = new MarkRenderer().ReasonTotals(tracks, fired);

        Assert.Equal(ShortlistSeries.Reasons.Length, tracks.Count);

        // Each reason drawn as its count of tonight's fired names, out of tonight's
        // fired count and with the count written on its bar. A region that lost the
        // bars would be the claim gone with the table still passing.
        // see: Tonight's reason totals are counts, and a reason's record is the run page's
        Assert.Equal(tracks.Count, Regex.Matches(drawn, "<svg class=\"reason-count\"").Count);

        // The counts are the store's, per reason, and tonight's setups have no outcome,
        // so no won or lost segment is drawn.
        foreach (var total in totals)
        {
            Assert.Contains($"data-reason=\"{total.Reason}\" data-names=\"{total.Names}\"", drawn, StringComparison.Ordinal);
            Assert.Contains($"data-count=\"{total.Names}\" data-of=\"{fired}\"", drawn, StringComparison.Ordinal);
            Assert.Contains($">{total.Names} of {fired}</text>", drawn, StringComparison.Ordinal);
            Assert.Equal(total.Names, tracks.Single(track => track.Reason == total.Reason).Unresolved);
        }

        Assert.All(tracks, track => Assert.Equal(0, track.Won + track.Lost + track.ResolvedUnsplit));
        Assert.DoesNotContain("data-state=\"won\"", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("data-state=\"lost\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-unresolved=\"all\"", drawn, StringComparison.Ordinal);

        // The fired count on the page is the fired count in the store, summed
        // over the reasons rather than over the names, because a name that fired
        // two reasons is in two of these.
        Assert.Equal(
            listings.Sum(listing => listing.FiredCount),
            tracks.Sum(track => track.Total));
    }

    [Fact]
    public async Task StaleAndFailedNamesTheStaleNamesAndTheStageThatFailed()
    {
        // Section 15.10's fourth region. The names are listed rather than
        // counted: a page that says four names are stale and does not say which
        // is a page nobody can act on.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);

        // The population is the one the night's closing stage counts over, which
        // is the index and not the names with bars. A member with no series at
        // all is stale in the way that matters, and this is the case the
        // committed fixture cannot reach: it is constructed by adding a member
        // the backfill never saw.
        var before = await api.StaleNamesAsync("GSPC");

        Insert(
            store,
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector) " +
            "VALUES ('GSPC', 'NEWW', '2026-09-01', NULL, '2026-09-05T21:10:00Z', 'Technology');");

        var stale = await api.StaleNamesAsync("GSPC");

        Assert.Equal(["NEWW"], [.. stale.Except(before, StringComparer.Ordinal)]);
        Assert.Contains("NEWW", stale);

        var failed = RunScreen.Failed(RunScreen.Stages(
        [
            new RunStageRow("run-1", "fetch", Utc("2026-09-05T21:00:00Z"), Utc("2026-09-05T21:00:05Z"), "ok", 4, 0, 1, "0", "fine"),
            new RunStageRow("run-1", "calendar", Utc("2026-09-05T21:00:05Z"), Utc("2026-09-05T21:00:06Z"), "failed", 0, 0, 3, "0", "the feed did not answer"),
        ]));

        Assert.Equal(["calendar"], [.. failed.Select(stage => stage.Stage)]);

        var region = new MarkRenderer().StaleAndFailed(stale, failed, [], []);

        Assert.Contains("data-stale=\"1\"", region, StringComparison.Ordinal);
        Assert.Contains("NEWW", region, StringComparison.Ordinal);
        Assert.Contains("data-stage=\"calendar\"", region, StringComparison.Ordinal);
        Assert.Contains("the feed did not answer", region, StringComparison.Ordinal);

        // A clean night says each thing rather than showing four empty regions.
        var clean = new MarkRenderer().StaleAndFailed([], [], [], []);

        Assert.Contains("no name is carrying yesterday's bars", clean, StringComparison.Ordinal);
        Assert.Contains("no stage of this night failed", clean, StringComparison.Ordinal);
        Assert.Contains("no document was refused by admissibility on this night", clean, StringComparison.Ordinal);
        Assert.Contains("no section fell back on this night", clean, StringComparison.Ordinal);
    }

    // ---- 6.4, the sections the claim checker left out ----

    // The status word the read surface states for a section left out, against the
    // checker's own. The surface holds no reference to the worker, so the word is
    // stated twice and held together here, which is what `RunScreen.NoSession`
    // already does for a night that did not trade.
    [Fact]
    public void TheWordThePageReadsForASectionLeftOutIsTheWordTheCheckerWrites()
    {
        Assert.Equal(EquityBrief.Worker.Research.ClaimChecker.Fallback, NameScreen.Fallback);
    }

    // A store the whole pipeline filled with the fixture's sections checked by the
    // shipped checker, so what the page draws is what the checker decided rather
    // than a status a test wrote.
    static async Task<TemporaryStore> WithCheckedSections()
    {
        var store = await ClaimAdmissibility.WithSources();
        var checker = new EquityBrief.Worker.Research.ClaimChecker(
            FixedClock.At(new DateTimeOffset(2026, 9, 8, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile);

        // Refused twice on one day, which falls back.
        ClaimAdmissibility.Pending(store, ClaimAdmissibility.SectionNamed("a poisoned paragraph"), 1);
        await checker.RunAsync("check-first");
        ClaimAdmissibility.Pending(store, ClaimAdmissibility.SectionNamed("a poisoned paragraph"), 2);
        await checker.RunAsync("check-second");

        // No admissible source, which falls back on its first check.
        ClaimAdmissibility.Pending(store, ClaimAdmissibility.SectionNamed("a section with no admissible source"), 1);

        // Refused once and waiting on its retry, which is neither written nor left
        // out and is not drawn as either.
        ClaimAdmissibility.Pending(store, ClaimAdmissibility.SectionNamed("an unsourced claim"), 1);

        await checker.RunAsync("check-rest");

        return store;
    }

    [Fact]
    public async Task ASectionTheCheckerLeftOutIsAbsentFromTheNamePageWithOneLineSayingWhy()
    {
        // Section 18's two rows about a section the checker could not accept, read
        // back off the name page's own markup: refused twice, and no admissible
        // source. Each is absent with one line saying why, and the line is the
        // reason the checker stored.
        using var store = await WithCheckedSections();

        var api = Api(store);
        var ticker = ClaimAdmissibility.Ticker;
        var states = await api.SectionStatesAsync(ticker, DateOnly.MaxValue);

        Assert.Equal(3, states.Count);

        var region = NameScreen.Region(
            new SinglePageApp(),
            new MarkRenderer(),
            ticker,
            await api.BarsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.IndicatorsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.LevelsAsync(ticker),
            await api.ProfileAsync(ticker),
            await api.LadderAsync(ticker),
            await api.NextEventAsync(ticker, DateOnly.MinValue),
            await api.MovesAsync(ticker),
            sections: states);

        Assert.Contains("data-left-out=\"2\"", region, StringComparison.Ordinal);

        // Refused twice: the line names the section and carries both the word that
        // says it was the retry and the figure the facts file did not hold.
        var twice = Regex.Match(region, "<p class=\"left-out\" data-section=\"The key under each figure\">([^<]*)</p>");

        Assert.True(twice.Success);
        Assert.Contains("rejected twice", twice.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("66.3%", twice.Groups[1].Value, StringComparison.Ordinal);

        // No admissible source: the line says so in the words section 18 uses.
        var none = Regex.Match(region, "<p class=\"left-out\" data-section=\"The two cases\">([^<]*)</p>");

        Assert.True(none.Success);
        Assert.Contains("no admissible source was found", none.Groups[1].Value, StringComparison.Ordinal);

        // The section waiting on its retry is not drawn as left out, and no prose
        // from any of the three reaches the page, because none of the three was
        // accepted and only an accepted version is drawn as a written section.
        Assert.DoesNotContain("data-section=\"What the company sells\"", region, StringComparison.Ordinal);
        Assert.DoesNotContain("Analysts expect", region, StringComparison.Ordinal);
        Assert.DoesNotContain("Keysight closed at", region, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"written-section\"", region, StringComparison.Ordinal);

        // Exactly the store's fallback rows, counted from the store rather than
        // from the page, so a region drawing one of two looks as wrong as it is.
        Assert.Equal(
            states.Count(state => state.Status == EquityBrief.Worker.Research.ClaimChecker.Fallback),
            Regex.Matches(region, "<p class=\"left-out\"").Count);
    }

    // ---- 6.5, where a name's research stands ----

    static async Task<string> NamePageWithStaleness(TemporaryStore store, string ticker)
    {
        var api = Api(store);

        return NameScreen.Region(
            new SinglePageApp(),
            new MarkRenderer(),
            ticker,
            await api.BarsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.IndicatorsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.LevelsAsync(ticker),
            await api.ProfileAsync(ticker),
            await api.LadderAsync(ticker),
            await api.NextEventAsync(ticker, DateOnly.MinValue),
            await api.MovesAsync(ticker),
            await api.FundamentalsAsync(ticker),
            sections: await api.SectionStatesAsync(ticker, DateOnly.MaxValue),
            staleness: await api.StalenessAsync(ticker));
    }

    [Fact]
    public async Task ANameWithNoResearchSaysSoBesideTheComputedSectionsRenderedWhole()
    {
        // Section 15.9's research-not-yet-written row, the two parts this checkpoint
        // draws. The line saying the sections have not been written, and the computed
        // sections beside it in full on the same markup, because a page that said
        // research was missing and drew nothing else would be a page that failed.
        using var store = await FixtureReplay.ReplayedAsync();

        var region = await NamePageWithStaleness(store, "KEYS");

        var line = Regex.Match(region, "<p class=\"research-state\" data-state=\"([a-z]+)\">([^<]*)</p>");

        Assert.True(line.Success);
        Assert.Equal("missing", line.Groups[1].Value);
        Assert.Equal(EquityBrief.Core.Research.Staleness.NotYetWritten, line.Groups[2].Value);

        foreach (var computed in new[] { "class=\"fact-strip\"", "class=\"level-chart\"", "class=\"level-summary\"", "class=\"numbers\"", "<section class=\"how-it-got-here\"" })
        {
            Assert.Contains(computed, region, StringComparison.Ordinal);
        }

        // Whole rather than present: the numbers section draws its five quarters for
        // a name the fetcher stored filings for, and the chart draws every stored
        // session, so a region that drew a placeholder beside the line would fail
        // here rather than read as rendered.
        var api = Api(store);

        // Counted inside the level chart alone: the moves region draws its own
        // twelve-month picture from the same bars, and a count over the page would
        // count both.
        var chartStart = region.IndexOf("class=\"level-chart\"", region.IndexOf("data-card=\"chart\"", StringComparison.Ordinal), StringComparison.Ordinal);
        var chart = region[chartStart..region.IndexOf("</svg>", chartStart, StringComparison.Ordinal)];

        Assert.Equal(
            (await api.BarsAsync("KEYS", DateOnly.MinValue, DateOnly.MaxValue)).Count,
            Regex.Matches(chart, "class=\"candle\"").Count);

        Assert.Contains("class=\"numbers-quarters\"", region, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStaleNameIsDrawnWithOneLineNamingTheTriggersInTheWordsTheJudgeWrites()
    {
        // Section 15.9's research-stale row, the line this checkpoint draws. KEYS's
        // stored filing is dated 2026-09-02 and its newest passed print 2026-08-18
        // after the close, so a section written on 2026-08-10 is stale on both.
        using var store = await FixtureReplay.ReplayedAsync();

        Insert(
            store,
            "INSERT INTO research_section VALUES ('KEYS', 'What the company sells', 1, '2026-08-10', 'a writer', 'accepted', 'prose', '[]', NULL);");

        var region = await NamePageWithStaleness(store, "KEYS");
        var line = Regex.Match(region, "<p class=\"research-state\" data-state=\"([a-z]+)\">([^<]*)</p>");

        Assert.True(line.Success);
        Assert.Equal("stale", line.Groups[1].Value);
        Assert.Contains("a filing dated 2026-09-02", line.Groups[2].Value, StringComparison.Ordinal);
        Assert.Contains("the earnings date 2026-08-18", line.Groups[2].Value, StringComparison.Ordinal);

        // One line, and the judge's own words: the shipped judge over the same store
        // writes the same line to the run log, so the page and the record of the
        // decision cannot describe one verdict two ways.
        var verdict = await new EquityBrief.Worker.Research.StalenessJudge(
            FixedClock.At(new DateTimeOffset(2026, 9, 12, 14, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile).JudgeAsync("KEYS", refresh: false, "judge-page");

        Assert.Equal(verdict.Line, System.Net.WebUtility.HtmlDecode(line.Groups[2].Value));
        Assert.Single(Regex.Matches(region, "class=\"research-state\""));

        // And a section rewritten after both is a record that stands, drawn as such.
        Insert(
            store,
            "INSERT INTO research_section VALUES ('KEYS', 'What the company sells', 2, '2026-09-05', 'a writer', 'accepted', 'prose', '[]', NULL);");

        Assert.Contains("data-state=\"stands\"", await NamePageWithStaleness(store, "KEYS"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSectionsThatFellBackOnANightAreDrawnOnTheRunPageWithTheirReasons()
    {
        // Section 15.10's fourth region, the part 6.4 draws. A name's sections and
        // a theme's together, each with whose it is and the reason it stored, and
        // only the night's: a section that fell back on another day is that day's.
        using var store = await WithCheckedSections();

        Insert(
            store,
            "INSERT INTO theme_section VALUES ('test and measurement', 'The industry cycle', 1, '2026-09-08', 'a writer', " +
            "'fallback', '', '[]', 'no admissible source was found', '[]');");

        Insert(
            store,
            "INSERT INTO research_section VALUES ('KEYS', 'The risks, each with what would confirm it', 1, '2026-09-07', 'a writer', " +
            "'fallback', '', '[]', 'no admissible source was found');");

        var api = Api(store);
        var night = new DateOnly(2026, 9, 8);

        var fellBack = RunScreen.FellBack(await api.FellBackAsync(night));

        Assert.Equal(3, fellBack.Count);

        var region = new MarkRenderer().StaleAndFailed([], [], [], fellBack);

        Assert.Contains("data-fell-back=\"3\"", region, StringComparison.Ordinal);
        Assert.Contains("data-subject=\"KEYS\" data-section=\"The key under each figure\"", region, StringComparison.Ordinal);
        Assert.Contains("data-subject=\"KEYS\" data-section=\"The two cases\"", region, StringComparison.Ordinal);
        Assert.Contains("data-subject=\"test and measurement\" data-section=\"The industry cycle\"", region, StringComparison.Ordinal);
        Assert.Contains("66.3%", region, StringComparison.Ordinal);

        // The day before is its own night.
        Assert.DoesNotContain("The risks, each with what would confirm it", region, StringComparison.Ordinal);
        Assert.Single(await api.FellBackAsync(new DateOnly(2026, 9, 7)));

        // And a refusal still waiting on its retry fell back on no night.
        Assert.DoesNotContain("What the company sells", region, StringComparison.Ordinal);
    }

    // A refused document as the store holds one: the row form the intake produces
    // for a document the test refused, written here rather than by a component
    // because SCHEMA gives Insert on this table to the two research runners and to
    // nobody else, and the first of them arrives at 6.8. What the row says is the
    // shipped projection's, so the page is read against values the code produced.
    static string RefusedRow(StoredDocument row) =>
        "INSERT INTO source_document (id, url, title, published_on, fetched_at, body, admissibility) VALUES ('"
        + row.Id + "', '" + row.Url + "', '" + row.Title.Replace("'", "''", StringComparison.Ordinal) + "', "
        + (row.PublishedOn is { } published
            ? "'" + published.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "'"
            : "NULL")
        + ", '" + row.FetchedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "', "
        + (row.Body is null ? "NULL" : "'" + row.Body.Replace("'", "''", StringComparison.Ordinal) + "'")
        + ", '" + row.Admissibility + "');";

    [Fact]
    public async Task TheDocumentsRefusedByAdmissibilityAreDrawnWithTheCategoryThatRefusedEach()
    {
        // Section 15.10's fourth region, third part. A refusal is kept as a row
        // precisely so it can be read on a surface, and this is the surface: the
        // category beside each document, the address a person can follow, and the
        // count per category above them.
        using var store = await FixtureExpectations.WithReturns();

        var night = new DateOnly(2026, 9, 5);

        // The rows the intake produces over the documents the fixture holds, at an
        // instant that is half past nine in the evening in New York and the next
        // day in UTC, which is the case a filter on the stored date gets wrong.
        var intake = SourceDocuments.Of(
            [.. ClaimAdmissibility.Refusable().Select(one => one.Document)],
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 9, 30),
            Utc("2026-09-06T01:30:00Z"));

        // An admitted document on the same night, which the region must not draw.
        var admitted = SourceDocuments.Of(
            [.. ClaimAdmissibility.Real()],
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 9, 30),
            Utc("2026-09-06T01:31:00Z"));

        Assert.Equal(7, intake.Refused.Count);
        Assert.Equal(6, admitted.Admitted.Count);

        foreach (var row in intake.Rows.Concat(admitted.Rows))
        {
            Insert(store, RefusedRow(row));
        }

        var read = await Api(store).RefusedDocumentsAsync(night);

        // Only the refusals, and all of them. The admitted rows are in the same
        // table on the same night, so a query that read the table would draw a
        // document a claim may rest on into the region about refusals.
        Assert.Equal(7, read.Count);
        Assert.All(read, row => Assert.NotEqual(Admissibility.Accepted, row.Category));

        var refused = RunScreen.Refused(read);
        var region = new MarkRenderer().StaleAndFailed([], [], refused, []);

        Assert.Contains("data-refused=\"7\"", region, StringComparison.Ordinal);

        // Every category the fixture's documents reach is named on the markup,
        // beside the document it refused, which is the claim the row makes.
        foreach (var document in refused)
        {
            Assert.Contains($"data-category=\"{document.Category}\"", region, StringComparison.Ordinal);
            Assert.Contains(document.Url, region, StringComparison.Ordinal);
        }

        Assert.Equal(
            Admissibility.DeniedCategories.Length + 2,
            refused.Select(document => document.Category).Distinct(StringComparer.Ordinal).Count());

        // The count per category above the list, so four of one kind reads as a
        // search returning marketing rather than as four unrelated refusals.
        Assert.Contains($"{Admissibility.MarketingPage} 2", region, StringComparison.Ordinal);
        Assert.Contains("7 document(s) refused", region, StringComparison.Ordinal);

        // And no body reaches the page, because the store holds none for a
        // refusal. The admitted documents' bodies are in the same table, so this
        // is a statement about what the region draws rather than about what the
        // store happens to hold.
        foreach (var body in admitted.Admitted.Select(row => row.Body!))
        {
            Assert.DoesNotContain(body[..40], region, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ARefusalFromAnotherNightIsNotDrawnOnThisOne()
    {
        // The night is decided by the clock over each row's own fetch instant, as
        // the run log's is, because this table has no session column either and a
        // pass runs when a name is opened rather than on a schedule.
        using var store = await FixtureExpectations.WithReturns();

        var one = ClaimAdmissibility.Refusable()[0].Document;

        var tonight = SourceDocuments.Stored(one, Admissibility.PriceForecast, Utc("2026-09-06T01:30:00Z"));

        var before = SourceDocuments.Stored(
            one with { Url = one.Url + "?page=2" },
            Admissibility.PriceForecast,
            Utc("2026-09-04T21:00:00Z"));

        Insert(store, RefusedRow(tonight));
        Insert(store, RefusedRow(before));

        var api = Api(store);

        Assert.Equal([tonight.Id], [.. (await api.RefusedDocumentsAsync(new DateOnly(2026, 9, 5))).Select(row => row.Id)]);
        Assert.Equal([before.Id], [.. (await api.RefusedDocumentsAsync(new DateOnly(2026, 9, 4))).Select(row => row.Id)]);
        Assert.Empty(await api.RefusedDocumentsAsync(new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public async Task TheUniverseAndTheStaleNamesAreTheIndexOnTheNightShownAndNotOnTheDayTheyAreRead()
    {
        // Both reads bound membership to the day the page was opened, so every
        // page about a past night read today's index: opened on 2026-09-21 before
        // that night ran, 2026-09-18's list would have lost its three leavers'
        // closes and drawn the first-ranked one's plan at zero. Found by the
        // fourth phase 5 sign-off review.
        using var store = await FixtureExpectations.WithReturns();

        var shown = (await Api(store).NewestNightAsync())!.Value;
        var leaver = FixtureExpectation.CurrentMembers.Order(StringComparer.Ordinal).First();
        var leaves = shown.AddDays(3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // A member leaving after the night shown, and a name joining after it
        // with no bars yet, each effective before the day the page is read.
        Insert(store, $"UPDATE membership SET \"left\" = '{leaves}' WHERE ticker = '{leaver}';");
        Insert(
            store,
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector) " +
            $"VALUES ('GSPC', 'JOIN', '{leaves}', NULL, '2026-09-05T21:10:00Z', 'Technology');");

        var later = new ReadApi(
            store.DatabaseFile,
            FixedClock.At(new DateTimeOffset(shown.AddDays(10).ToDateTime(new TimeOnly(21, 10)), TimeSpan.Zero), SessionZones.UnitedStates));

        // On the night shown the leaver is a member with the close that night
        // stored, and the joiner is not yet one.
        var onTheNight = await later.UniverseAsync("GSPC", shown);

        Assert.Contains(onTheNight, row => row.Ticker == leaver && row.Close is not null);
        Assert.DoesNotContain(onTheNight, row => row.Ticker == "JOIN");
        Assert.DoesNotContain("JOIN", await later.StaleNamesAsync("GSPC", shown));

        // Read on the later day with no night, which is what every route did,
        // the two change places.
        var today = await later.UniverseAsync("GSPC");

        Assert.DoesNotContain(today, row => row.Ticker == leaver);
        Assert.Contains("JOIN", await later.StaleNamesAsync("GSPC"));
    }

    [Fact]
    public void TheHarnessRegionCountsOutOfScopeApartFromUnexaminedAndSaysSoWithNoReport()
    {
        // Section 15.10's last region, and the rule in .claude/rules/checks.md about the two
        // counts: only one of them is a defect, and a page that summed them
        // would report a build that has not reached a claim as one that failed
        // to check it.
        var report = """
            { "summary": { "pass": 156, "fail": 0, "unexamined": 0, "outOfScope": 81 } }
            """;

        var counts = RunScreen.Harness(report);

        Assert.Equal(new HarnessCounts(156, 0, 0, 81), counts);

        var region = new MarkRenderer().HarnessVerdicts(counts);

        Assert.Contains("data-unexamined=\"0\"", region, StringComparison.Ordinal);
        Assert.Contains("data-out-of-scope=\"81\"", region, StringComparison.Ordinal);
        Assert.Contains("156 passed, 0 failed, 0 unexamined, 81 out of scope", region, StringComparison.Ordinal);

        // Never summed, on the surface as well as in the arithmetic.
        Assert.DoesNotContain("237 ", region, StringComparison.Ordinal);
        Assert.DoesNotContain("81 unexamined", region, StringComparison.Ordinal);

        // A machine with no report says so rather than showing four zeros,
        // which would read as a build nothing failed.
        Assert.Null(RunScreen.Harness(null));
        Assert.Null(RunScreen.Harness("{}"));

        var absent = new MarkRenderer().HarnessVerdicts(null);

        Assert.Contains("no phase report has been written", absent, StringComparison.Ordinal);
        Assert.DoesNotContain("data-passed", absent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRunPageDrawsEveryRegionSectionFifteenTenNamesAndStatesTheTwoThatAreAbsent()
    {
        // The seven regions in the order that section states them. The shadow
        // region was stated as absent until 8.4 built it, because an empty
        // region reads as a night that produced nothing; it is now drawn, and
        // what it states over a store with no registration is that nothing is
        // registered rather than that nothing ran.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync();
        var returns = await api.ForwardReturnsAsync();
        var stages = RunScreen.Stages(await api.RunLogAsync(night));
        var records = RunScreen.Records(listings, RunScreen.Resolved(returns));

        var page = new SinglePageApp().RunRegion(
            new MarkRenderer(),
            night,
            stages,
            RunScreen.Failed(stages),
            records,
            RunScreen.Tracks(records),
            RunScreen.BaseRates(returns),
            RunScreen.Nights(listings),
            await api.StaleNamesAsync("GSPC"),
            RunScreen.Refused(await api.RefusedDocumentsAsync(night)),
            RunScreen.FellBack(await api.FellBackAsync(night)),
            RunScreen.Queue(await api.QueueRowsAsync(), night, _ => true),
            RunScreen.Harness(null),
            RunScreen.Shadow(await api.RegisteredCandidatesAsync(), Utc("2026-09-08T22:00:00Z")),
            orders: RunScreen.Orders(listings, night));

        foreach (var region in new[] { "operational", "reason-records", "shadow-candidates", "tonights-order", "stale-and-failed", "overnight-queue", "harness" })
        {
            Assert.Contains($"class=\"{region}\"", page, StringComparison.Ordinal);
        }

        // In that order, so the evidence page reads as section 15.10 states it.
        var at = new[] { "operational", "reason-records", "shadow-candidates", "tonights-order", "stale-and-failed", "overnight-queue", "harness" }
            .Select(region => page.IndexOf($"class=\"{region}\"", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal([.. at.Order()], at);

        // The register holds nothing here, so the region says so with the
        // maximum the family may reach and never with an empty row.
        Assert.Contains("data-shadow=\"0\"", page, StringComparison.Ordinal);
        Assert.Contains("no candidate condition is registered", page, StringComparison.Ordinal);
        Assert.Contains("withheld until it is promoted", page, StringComparison.Ordinal);

        // And the route is a link, which is what makes the page shareable.
        Assert.Contains(SinglePageApp.RunRoute, new SinglePageApp().Shell("EquityBrief"), StringComparison.Ordinal);
        Assert.Contains("/screens/run/", new SinglePageApp().Shell("EquityBrief"), StringComparison.Ordinal);
    }

    // 5.8's own assertions. Nine parts of five section 15 rows were stated by
    // the document and drawn by no page, each sitting under a row-level PASS
    // until the fifth phase 5 sign-off review found that a row's parts were the
    // reader's rather than the document's. Every one of them is a claim about a
    // surface, so every one is asserted off the markup the page draws rather
    // than off the model behind it.
    // owes: The screens' parts stated in section 15 and not drawn

    [Fact]
    public async Task TonightsListDrawsTheDayChangeTheTrendStateAndTheDistanceMarkBesideEachName()
    {
        // Section 15.7's list row states six things and the page drew three of
        // them. These are the other three.
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync(night);
        var universe = await api.UniverseAsync(Index, night);
        var closes = await api.ClosesToTheNightAsync(night);


        var rows = TonightScreen.Rows(
            night,
            listings,
            UniverseScreen.Rows(universe).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal),
            closes);

        Assert.NotEmpty(rows);

        var list = new MarkRenderer().TonightList(rows, SinglePageApp.TonightDrawn);

        // The day change, against the store rather than against the projection.
        // The two closes are read back out of the bars the night wrote, and the
        // percentage the markup carries is the one those two closes make.
        var drawn = 0;

        foreach (var row in rows)
        {
            var bars = await api.BarsAsync(row.Ticker, DateOnly.MinValue, night);

            Assert.True(bars.Count >= 2, $"{row.Ticker} holds {bars.Count} sessions, expected at least 2.");

            var today = bars[^1].Close;
            var before = bars[^2].Close;
            var change = Statistic.FromPrice((today - before) / before) * 100;
            var reads = change.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

            Assert.Contains(
                $"data-ticker=\"{row.Ticker}\" data-fired-count=\"{row.FiredCount}\" data-strength=\"{row.Strength}\" data-day-change=\"{reads}\"",
                list,
                StringComparison.Ordinal);

            // And in words, with its sign, because the sign is the only channel
            // the direction has: the two hues belong to support and resistance
            // and a day's change is not allowed either of them.
            Assert.Contains($">{reads}%</td>", list, StringComparison.Ordinal);

            // The trend state in a word, which is the ladder's own label.
            var ladder = await api.LadderAsync(row.Ticker);

            Assert.Contains(
                $"data-trend-state=\"{ladder?.TrendState ?? "not classified"}\"",
                list,
                StringComparison.Ordinal);
            Assert.Contains(
                $"<td class=\"trend-state\">{(ladder?.TrendState ?? "not classified").Replace('_', ' ')}</td>",
                list,
                StringComparison.Ordinal);

            drawn++;
        }

        // The distance row mark, one per drawn row and each carrying that row's
        // own ticker, so the column is the mark and not one shape repeated.
        Assert.Equal(drawn, Regex.Matches(list, "<svg class=\"distance-row\"").Count);

        foreach (var row in rows)
        {
            Assert.NotNull(row.Distance);
            Assert.Contains(
                $"<svg class=\"distance-row\" role=\"img\" viewBox=\"-30 0 146 26\" width=\"146\" height=\"26\" data-ticker=\"{row.Ticker}\"",
                list,
                StringComparison.Ordinal);
        }

        // Each of the three is an absence stated rather than a zero. A name with
        // no bar on the night, no ladder row and no bands says so three times,
        // and says nothing that reads as a value. Over a constructed cell here,
        // and over the read path in the two tests below, which is the half this
        // could not see: a hand-built cell carries whatever the test puts in it,
        // so it proves the renderer and never the figures reaching it.
        var bare = new MarkRenderer().TonightList(
            [new ListingCell("ZZZZ", night, 1, 0, null, [ShortlistSeries.AtEntryZone])],
            SinglePageApp.TonightDrawn);

        Assert.Contains("data-day-change=\"none\"", bare, StringComparison.Ordinal);
        Assert.Contains("data-absence=\"no-bar\"", bare, StringComparison.Ordinal);
        Assert.Contains("data-trend-state=\"not classified\"", bare, StringComparison.Ordinal);
        Assert.Contains("data-distance=\"none\"", bare, StringComparison.Ordinal);
        Assert.DoesNotContain("data-day-change=\"0.00\"", bare, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg class=\"distance-row\"", bare, StringComparison.Ordinal);

        // A name with a close and no session before it is the other absence, and
        // it is a different sentence.
        var first = new MarkRenderer().TonightList(
            [new ListingCell("ZZZZ", night, 1, 0, 10m, [ShortlistSeries.AtEntryZone])],
            SinglePageApp.TonightDrawn);

        Assert.Contains("data-absence=\"no-earlier-close\"", first, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNightHeaderStatesTheHarnessVerdictAndSaysSoWhereNoReportHasBeenWritten()
    {
        // Section 15.7 puts the harness verdict in the night header. It stood on
        // the run page alone until 5.8, which is a different screen answering a
        // different question.
        var marks = new MarkRenderer();
        var night = new DateOnly(2026, 9, 5);

        var header = marks.NightHeader(night, 4, 2, "00:00:01", new HarnessCounts(211, 0, 0, 87));

        Assert.Contains("class=\"harness-verdict\"", header, StringComparison.Ordinal);
        Assert.Contains("data-passed=\"211\"", header, StringComparison.Ordinal);
        Assert.Contains("data-failed=\"0\"", header, StringComparison.Ordinal);
        Assert.Contains("data-unexamined=\"0\"", header, StringComparison.Ordinal);
        Assert.Contains("data-out-of-scope=\"87\"", header, StringComparison.Ordinal);
        Assert.Contains("211 passed, 0 failed, 0 unexamined, 87 out of scope", header, StringComparison.Ordinal);

        // Four counts and no total, which is the rule the run page's own region
        // states: out of scope is counted apart from unexamined and only one of
        // them is a defect. A header that summed them would read 87 as a failure
        // to check.
        Assert.DoesNotContain("298", header, StringComparison.Ordinal);

        // A machine with no report says so rather than drawing four zeros, which
        // would read as a build nothing has ever checked.
        var none = marks.NightHeader(night, 4, 2, "00:00:01", null);

        Assert.Contains("data-report=\"none\"", none, StringComparison.Ordinal);
        Assert.Contains("no phase report has been written", none, StringComparison.Ordinal);
        Assert.DoesNotContain("data-passed=\"0\"", none, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSelectedNameIsWhicheverRowTheReaderPickedAndCarriesTheLevelSummary()
    {
        // Section 15.7's selected-name region is for whichever row is selected.
        // The composition fixed it at the first row until 5.8, which answered a
        // question the reader had not asked, and drew the plan column without
        // the level summary the row states beside it.
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync(night);
        var universe = await api.UniverseAsync(Index, night);


        var rows = TonightScreen.Rows(
            night,
            listings,
            UniverseScreen.Rows(universe).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal),
            await api.ClosesToTheNightAsync(night));

        Assert.True(rows.Count >= 2, $"the night listed {rows.Count} names, and telling a selection from a default needs two.");

        // The reader's pick, and it is not the first row, which is the whole
        // point: a region fixed at the first satisfies every count on this page.
        var picked = rows[^1];

        Assert.NotEqual(rows[0].Ticker, picked.Ticker);
        Assert.Equal(picked.Ticker, TonightScreen.Selected(rows, picked.Ticker)?.Ticker);

        // No pick, and a pick that is not on tonight's list, both fall back to
        // the first row rather than to nothing: the page opens with no name in
        // the hash and an absent region would make the common case the empty one.
        Assert.Equal(rows[0].Ticker, TonightScreen.Selected(rows, null)?.Ticker);
        Assert.Equal(rows[0].Ticker, TonightScreen.Selected(rows, "NOSUCH")?.Ticker);

        var page = new SinglePageApp();
        var marks = new MarkRenderer();
        var summary = await api.LevelsAsync(picked.Ticker);

        var region = NameScreen.PlanRegion(
            page,
            marks,
            picked.Ticker,
            await api.LadderAsync(picked.Ticker),
            picked.Close ?? 0m,
            summary);

        // The region is about the name the reader picked and about no other.
        Assert.Contains($"class=\"selected-name\" data-ticker=\"{picked.Ticker}\"", region, StringComparison.Ordinal);
        Assert.DoesNotContain($"data-ticker=\"{rows[0].Ticker}\"", region, StringComparison.Ordinal);

        // The level summary beside the plan column, carrying the stored bands.
        Assert.NotEmpty(summary);
        Assert.Contains($"data-bands=\"{summary.Count}\"", region, StringComparison.Ordinal);
        Assert.Contains("level-summary", region, StringComparison.Ordinal);

        foreach (var band in summary)
        {
            Assert.Contains(band.LowEdge.ToString(CultureInfo.InvariantCulture), region, StringComparison.Ordinal);
        }

        // And the page states which row it is drawn for, so the selection is
        // legible on the surface rather than only in the hash.
        var tonight = page.TonightRegion(
            marks, night, universe.Count, TonightScreen.Fired(listings), "00:00:01",
            rows, [], region, null, picked.Ticker);

        Assert.Contains($"data-selected=\"{picked.Ticker}\"", tonight, StringComparison.Ordinal);

        // Every row is selectable, which is what makes "whichever row" true: a
        // row with no way to pick it is a row the region can never be about.
        var list = marks.TonightList(rows, SinglePageApp.TonightDrawn);

        foreach (var row in rows)
        {
            Assert.Contains($"data-selects=\"{row.Ticker}\"", list, StringComparison.Ordinal);
            Assert.Contains($"?name={row.Ticker}\"", list, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheAppCarriesSelectionInTheHashBesideItsRoutingAndItsFilters()
    {
        // Section 15.4 says the app is the single page with routing, filters and
        // selection. The first two were drawn and the third was not, so the row
        // passed while a third of what it names did not exist.
        var shell = new SinglePageApp().Shell("EquityBrief");

        // The hash is split into a path and a query before anything routes, so
        // routing is the path and filters and selection are the query. Written
        // the other way, each route sliced the whole hash and a front page
        // carrying a selection matched no route at all.
        Assert.Contains("const cut = hash.indexOf('?')", shell, StringComparison.Ordinal);
        Assert.Contains("const path = cut < 0 ? hash : hash.slice(0, cut)", shell, StringComparison.Ordinal);
        Assert.Contains("const query = cut < 0 ? '' : hash.slice(cut + 1)", shell, StringComparison.Ordinal);

        // And the selection reaches the server, which is what makes it a link
        // rather than a thing the browser keeps to itself.
        Assert.Contains("'/screens/tonight' + night + (query ? '?' + query : '')", shell, StringComparison.Ordinal);
        Assert.Contains("'/screens/universe' + (query ? '?' + query : '')", shell, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheUniverseTableIsPagedAndEveryPageLinkKeepsTheFiltersItWasDrawnUnder()
    {
        // Section 15.8 says the table is paged. It drew every name in the index
        // on one page until 5.8, which on five hundred rows carrying a mark each
        // is a screen nobody can name a place in.
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var cells = UniverseScreen.Rows(await api.UniverseAsync(Index, await api.NewestNightAsync()));

        Assert.NotEmpty(cells);

        // The page size is asserted over constructed rows, because the committed
        // fixture holds four names and four names are one page however it is cut.
        var many = Enumerable.Range(0, 120)
            .Select(at => new UniverseCell($"N{at:000}", at % 2 == 0 ? "Tech" : "Health", 100m, "uptrend", null, null, null, null, at))
            .ToArray();

        Assert.Equal(UniverseScreen.PageSize, UniverseScreen.Page(many, 1).Count);
        Assert.Equal("N000", UniverseScreen.Page(many, 1)[0].Ticker);
        Assert.Equal($"N{UniverseScreen.PageSize:000}", UniverseScreen.Page(many, 2)[0].Ticker);

        // The pages partition the rows: every row is on exactly one page, in
        // order, and none is on two. A slice that overlapped or skipped would
        // still draw a plausible page.
        var pages = (many.Length + UniverseScreen.PageSize - 1) / UniverseScreen.PageSize;

        Assert.Equal(
            [.. many.Select(row => row.Ticker)],
            [.. Enumerable.Range(1, pages).SelectMany(at => UniverseScreen.Page(many, at)).Select(row => row.Ticker)]);

        // A page number past the end gives the last page rather than an empty
        // table, because an empty table is the picture a filter matching nothing
        // draws and the two are different states.
        Assert.Equal(pages, UniverseScreen.PageOf(many.Length, 99));
        Assert.Equal(1, UniverseScreen.PageOf(many.Length, 0));
        Assert.NotEmpty(UniverseScreen.Page(many, 99));

        var marks = new MarkRenderer();
        var nav = marks.UniversePaging(many.Length, 2, UniverseScreen.PageSize, "uptrend", "Tech");

        Assert.Contains($"data-page=\"2\" data-pages=\"{pages}\"", nav, StringComparison.Ordinal);
        Assert.Contains($"data-rows=\"{many.Length}\"", nav, StringComparison.Ordinal);
        Assert.Contains($"page 2 of {pages}", nav, StringComparison.Ordinal);

        // Every link carries the filters it was drawn under. A next-page link
        // that dropped the chips would take a reader from a filtered page 1 to
        // an unfiltered page 2 and look exactly like paging.
        var links = Regex.Matches(nav, "href=\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToArray();

        Assert.Equal(2, links.Length);

        foreach (var href in links)
        {
            Assert.Contains("trend=uptrend", href, StringComparison.Ordinal);
            Assert.Contains("sector=Tech", href, StringComparison.Ordinal);
            Assert.StartsWith(SinglePageApp.UniverseRoute + "?page=", href, StringComparison.Ordinal);
        }

        // The first page has no previous and the last has no next, and each says
        // so rather than linking to a page that is not there.
        Assert.Contains("data-page=\"none\" rel=\"prev\"", marks.UniversePaging(many.Length, 1, UniverseScreen.PageSize, null, null), StringComparison.Ordinal);
        Assert.Contains("data-page=\"none\" rel=\"next\"", marks.UniversePaging(many.Length, pages, UniverseScreen.PageSize, null, null), StringComparison.Ordinal);

        // The filters and the cut are one call, and the order between them is the
        // property: the page is taken from what the filters left and never from
        // the whole table. Cut the other way round and page 2 of a filter that
        // matches half the names is half a page of the wrong names, which is a
        // plausible screen no count on it contradicts.
        var half = UniverseScreen.Rows(many, null, "Tech", 2);

        Assert.Equal(many.Length / 2, half.Rows);
        Assert.NotEmpty(half.Page);
        Assert.All(half.Page, row => Assert.Equal("Tech", row.Sector));
        Assert.Equal(
            [.. many.Where(row => row.Sector == "Tech").Skip(UniverseScreen.PageSize).Take(UniverseScreen.PageSize).Select(row => row.Ticker)],
            [.. half.Page.Select(row => row.Ticker)]);

        // Unfiltered, the same page is a different set of names, which is what
        // makes the assertion above about the order and not about paging.
        Assert.NotEqual(
            [.. UniverseScreen.Rows(many, null, null, 2).Page.Select(row => row.Ticker)],
            [.. half.Page.Select(row => row.Ticker)]);

        // A filter matching nothing leaves no rows rather than the first page of
        // everything, and the page it reports is the first.
        var none = UniverseScreen.Rows(many, null, "NoSuchSector", 3);

        Assert.Empty(none.Page);
        Assert.Equal(0, none.Rows);
        Assert.Equal(1, none.At);

        // And the region draws the page it was handed rather than every filtered
        // row, which is the half that makes the table paged on the screen and
        // not only in the projection.
        var region = new SinglePageApp().UniverseRegion(
            marks, many, UniverseScreen.Sectors(many), null, null, UniverseScreen.Page(many, 2), 2, UniverseScreen.PageSize);

        Assert.Contains($"data-shown=\"{many.Length}\" data-drawn=\"{UniverseScreen.PageSize}\" data-page=\"2\"", region, StringComparison.Ordinal);
        Assert.Equal(UniverseScreen.PageSize, Regex.Matches(region, "<tr data-ticker=\"").Count);
        Assert.DoesNotContain("<tr data-ticker=\"N000\"", region, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheUniverseTableStatesTheSessionsUntilEachNamesNextDatedEvent()
    {
        // Section 15.8 names the column. It did not exist until 5.8, and the
        // row's PASS covered it because the reader chose the row's parts.
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var events = (await api.NextEventsAsync(night))
            .ToDictionary(row => row.Ticker, row => row.EventDate, StringComparer.Ordinal);

        Assert.NotEmpty(events);

        var cells = UniverseScreen.Rows(await api.UniverseAsync(Index, night), null, events, night);
        var table = new MarkRenderer().UniverseTable(cells);

        // The date agrees with the per-name read the fact strip uses, so the
        // column and the strip cannot name two different events for one name.
        foreach (var cell in cells)
        {
            var perName = await api.NextEventAsync(cell.Ticker, night);

            Assert.Equal(perName?.EventDate, cell.NextEvent);

            Assert.Contains(
                $"data-sessions=\"{(cell.SessionsUntilEarnings is { } until ? until.ToString(CultureInfo.InvariantCulture) : "none")}\" " +
                $"data-next-event=\"{(cell.NextEvent is { } dated ? dated.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none")}\"",
                table,
                StringComparison.Ordinal);
        }

        // Each count against the one the listings expectation walks by hand over weekdays
        // less the closures, for the night that file names, so a count that is not the
        // exchange's disagrees with it.
        var walked = Expected("listings").GetProperty("earningsSoon");

        Assert.Equal(walked.GetProperty("night").GetString(), night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        foreach (var cell in cells)
        {
            var expected = walked.GetProperty(cell.Ticker);

            Assert.Equal(expected.GetProperty("nextDatedEvent").GetString(), cell.NextEvent?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ShortlistSeries.NotOnFile);
            Assert.Equal(expected.GetProperty("sessions").GetString(), cell.SessionsUntilEarnings?.ToString(CultureInfo.InvariantCulture) ?? ShortlistSeries.NotOnFile);
        }

        Assert.Contains(cells, cell => cell.SessionsUntilEarnings is > 0);

        // The count itself, over dates the closure table answers for. A Friday
        // to the Monday after it is one session, the weekend asking nothing of
        // the table; a day to itself is none; and the week holding Thanksgiving
        // is four sessions rather than five, which is what makes this the
        // exchange's calendar rather than a subtraction.
        Assert.Equal(0, ExchangeClosures.SessionsUntil(new DateOnly(2026, 9, 11), new DateOnly(2026, 9, 11)));
        Assert.Equal(1, ExchangeClosures.SessionsUntil(new DateOnly(2026, 9, 11), new DateOnly(2026, 9, 14)));
        Assert.Equal(4, ExchangeClosures.SessionsUntil(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 27)));

        // Two absences, stated as two. A name with no dated event has nothing to
        // count to; a name whose event is past the end of the closure table has
        // an event nobody can count the sessions to, and the table refuses to
        // guess the weekdays past its end rather than answering with a number.
        Assert.Null(ExchangeClosures.SessionsUntil(new DateOnly(2026, 9, 11), ExchangeClosures.CoveredThrough.AddDays(1)));

        var neither = new UniverseCell("ZZZZ", "Tech", 10m, "uptrend", null, null, null, null, 0);
        var beyond = neither with
        {
            Ticker = "YYYY",
            NextEvent = ExchangeClosures.CoveredThrough.AddDays(1),
            EventBeyondTheTable = true,
        };

        var stated = new MarkRenderer().UniverseTable([neither, beyond]);

        Assert.Contains("no dated event", stated, StringComparison.Ordinal);
        Assert.Contains("past the end of the closure table", stated, StringComparison.Ordinal);
        Assert.DoesNotContain("data-sessions=\"0\" data-next-event=\"none\"", stated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HowItGotHereDrawsTheTwelveMonthPictureAboveTheTableOfMoves()
    {
        // Section 15.9 puts the twelve-month picture in this region, above the
        // table of the biggest moves. The region drew the table alone until 5.8.
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var bars = await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);
        var moves = await api.MovesAsync(Name);
        var year = NameScreen.TwelveMonths(bars);

        Assert.NotEmpty(year);
        Assert.NotEmpty(moves);

        var region = new MarkRenderer().MovesTable(
            Name,
            [.. moves.Select(move => new MoveCell(move.SessionDate, move.Sessions, move.ChangePct, move.Rank))],
            year);

        // The picture is inside the region the document puts it in, and above
        // the table, which is the order the region is read in: the year first,
        // then which moves made it.
        Assert.Contains("class=\"twelve-months\"", region, StringComparison.Ordinal);
        Assert.True(
            region.IndexOf("class=\"twelve-months\"", StringComparison.Ordinal)
                < region.IndexOf("<tr data-session-date=", StringComparison.Ordinal),
            "the twelve-month picture is drawn below the table of moves.");

        // It is the level chart mark rather than a drawing of its own, and it is
        // given the year and no bands: nothing on any screen is a one-off
        // drawing, and what this region asks is what the year did.
        Assert.Contains($"data-picture-sessions=\"{year.Count}\"", region, StringComparison.Ordinal);
        Assert.Contains($"class=\"level-chart\" data-ticker=\"{Name}\" data-sessions=\"{year.Count}\"", region, StringComparison.Ordinal);

        // And no bands, which is what separates this picture from the chart
        // region below it: that one is about where the levels are, this one is
        // about what the year did.
        Assert.DoesNotContain("class=\"level-bands\"", region, StringComparison.Ordinal);

        // The window is twelve months back from the newest stored session and
        // not from the machine clock, so a page opened on a Sunday draws the
        // year to Friday.
        Assert.Equal(bars[^1].SessionDate, year[^1].SessionDate);
        Assert.All(year, bar => Assert.True(bar.SessionDate > bars[^1].SessionDate.AddYears(-1)));
        Assert.Contains(bars[^1].SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), region, StringComparison.Ordinal);

        // A session older than the window is outside the picture. The committed
        // fixture holds a year and no more, so the boundary is constructed: one
        // bar two years back and one on the newest session, and the picture is
        // the second alone.
        var older = NameScreen.TwelveMonths(
            [
                new BarRow(Name, bars[^1].SessionDate.AddYears(-2), 1m, 2m, 0.5m, 1.5m, 10),
                bars[^1],
            ]);

        Assert.Equal([bars[^1].SessionDate], [.. older.Select(bar => bar.SessionDate)]);

        // The window is measured from the newest stored session and from nothing
        // else, asserted over a series that ended years ago. Measured from the
        // machine clock this is empty, and over the committed fixture the two
        // readings cannot be told apart: its newest session is days from today,
        // so both windows hold the same bars and a picture drawn to the wrong
        // year looks exactly right.
        var settled = new DateOnly(2020, 6, 30);

        var stale = NameScreen.TwelveMonths(
            [
                new BarRow(Name, settled.AddYears(-1).AddDays(-1), 1m, 2m, 0.5m, 1.5m, 10),
                new BarRow(Name, settled.AddYears(-1).AddDays(1), 1m, 2m, 0.5m, 1.5m, 10),
                new BarRow(Name, settled, 1m, 2m, 0.5m, 1.5m, 10),
            ]);

        Assert.Equal(
            [settled.AddYears(-1).AddDays(1), settled],
            [.. stale.Select(bar => bar.SessionDate)]);

        // And a name with fewer sessions than a chart needs gets the sentence
        // stating its count rather than a picture drawn through nothing, which
        // is how every mark here degrades.
        var sparse = new MarkRenderer().MovesTable("NOSUCH", [], [.. year.Take(MarkRenderer.FewestBars - 1)]);

        Assert.Contains($"data-picture-sessions=\"{MarkRenderer.FewestBars - 1}\"", sparse, StringComparison.Ordinal);
        Assert.Contains("and a chart needs at least", sparse, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"level-chart\"", sparse, StringComparison.Ordinal);

        // A name with no stored session at all is the same shape rather than a
        // caption naming a date nobody has.
        Assert.Contains("no stored session", new MarkRenderer().MovesTable("NOSUCH", [], []), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APastNightsRowCarriesThatNightsCloseAndTheChangeAcrossThatNightsOwnSessions()
    {
        // The sixth phase 5 sign-off review's blocking finding, over the read
        // path that produced it. 5.8 took the close from the universe row, whose
        // close column is the name's newest bar whatever night is asked for, and
        // the previous close from the newest bar before the night. On a past
        // night that subtracts two sessions which are neither consecutive nor
        // the night's, and the number drawn is wrong rather than absent.
        //
        // The committed fixture holds listings on one night, so the case is
        // constructed: the same names listed again on an earlier session the
        // store already holds bars for. Nothing else in the suite reaches a
        // second night, which is why nothing saw this.
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var newest = (await api.NewestNightAsync())!.Value;
        var bars = await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);

        // A session with a session before it, and far enough back that the
        // name's newest close is not the close on it.
        var earlier = bars[^3].SessionDate;

        Assert.True(earlier < newest, $"the earlier session {earlier} is not before the night {newest}.");

        var on = Stamp(earlier);

        Insert(
            store,
            "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) " +
            $"SELECT ticker, '{on}', reasons, fired_count, plan_at_listing, shadow_reasons " +
            $"FROM listing WHERE session_date = '{Stamp(newest)}';");

        var listings = await api.ListingsAsync(earlier);

        Assert.NotEmpty(listings);

        var universe = await api.UniverseAsync(Index, earlier);

        var rows = TonightScreen.Rows(
            earlier,
            listings,
            UniverseScreen.Rows(universe).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal),
            await api.ClosesToTheNightAsync(earlier));

        Assert.NotEmpty(rows);

        var list = new MarkRenderer().TonightList(rows, SinglePageApp.TonightDrawn);
        var discriminating = 0;

        foreach (var row in rows)
        {
            var held = await api.BarsAsync(row.Ticker, DateOnly.MinValue, earlier);

            Assert.True(held.Count >= 2, $"{row.Ticker} holds {held.Count} sessions to {earlier}, expected at least 2.");
            Assert.Equal(earlier, held[^1].SessionDate);

            // The close is that night's, not the name's newest.
            Assert.Equal(held[^1].Close, row.Close);

            var reads = (Statistic.FromPrice((held[^1].Close - held[^2].Close) / held[^2].Close) * 100)
                .ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

            Assert.Contains($"data-day-change=\"{reads}\"", list, StringComparison.Ordinal);

            // And the two figures are about one session, which is the property
            // the defect broke: the change is the one this night's close makes
            // against the session before it, and both come from the same pair.
            var newestClose = (await api.BarsAsync(row.Ticker, DateOnly.MinValue, DateOnly.MaxValue))[^1].Close;

            if (newestClose != held[^1].Close)
            {
                discriminating++;

                Assert.NotEqual(newestClose, row.Close);

                var wrong = (Statistic.FromPrice((newestClose - held[^2].Close) / held[^2].Close) * 100)
                    .ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

                Assert.DoesNotContain($"data-day-change=\"{wrong}\"", list, StringComparison.Ordinal);
            }
        }

        // Stated in advance, because a sweep that finds nothing is a sweep that
        // proves nothing: at least one name has to have moved between the night
        // and its newest session, or the two readings cannot be told apart.
        Assert.True(
            discriminating >= 1,
            $"{discriminating} of {rows.Count} rows have a newest close differing from the night's, expected at least 1.");
    }

    [Fact]
    public async Task ANameWithNoBarOnTheNightDrawsNoCloseAndNoDayChangeRatherThanZero()
    {
        // The second shape the sixth review found, and this one fires on the
        // newest night. Both legs of the change resolved to the name's last
        // stored bar, so a stale member drew exactly 0.00: a figure saying the
        // price did not move, on a night the store holds no price for.
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync(night);
        var made = listings.First(listing => listing.FiredCount > 0).Ticker;

        // Made stale by removing its bar on the night, which is what a name the
        // day's file carried nothing for looks like in the store. The facts row
        // goes with it, because a facts file for a session with no bar is the
        // state the 5.3 guard refuses.
        var on = Stamp(night);

        Insert(store, $"DELETE FROM facts WHERE ticker = '{made}' AND session_date = '{on}';");
        Insert(store, $"DELETE FROM bar WHERE ticker = '{made}' AND session_date = '{on}';");

        var held = await api.BarsAsync(made, DateOnly.MinValue, night);

        Assert.NotEmpty(held);
        Assert.NotEqual(night, held[^1].SessionDate);

        var universe = await api.UniverseAsync(Index, night);

        var rows = TonightScreen.Rows(
            night,
            listings,
            UniverseScreen.Rows(universe).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal),
            await api.ClosesToTheNightAsync(night));

        var stale = Assert.Single(rows, row => row.Ticker == made);

        Assert.Null(stale.DayChangePct);
        Assert.Null(stale.Close);

        // The universe row still carries the name's newest close, which is the
        // column the defect read. The row does not, and that is the fix: the two
        // are different questions and only one of them is about this night.
        Assert.Equal(held[^1].Close, universe.Single(row => row.Ticker == made).Close);

        var list = new MarkRenderer().TonightList(rows, SinglePageApp.TonightDrawn);
        var cell = Regex.Match(list, $"<tr data-ticker=\"{made}\".*?</tr>", RegexOptions.Singleline).Value;

        Assert.NotEmpty(cell);
        Assert.Contains("data-day-change=\"none\"", cell, StringComparison.Ordinal);
        Assert.Contains("data-absence=\"no-bar\"", cell, StringComparison.Ordinal);
        Assert.Contains("not computed", cell, StringComparison.Ordinal);
        Assert.DoesNotContain("data-day-change=\"0.00\"", cell, StringComparison.Ordinal);
        Assert.DoesNotContain("0.00%", cell, StringComparison.Ordinal);

        // The names that do have a bar on the night still draw a change, so the
        // absence is this name's and not the whole column going quiet.
        Assert.Contains(rows, row => row.Ticker != made && row.DayChangePct is not null);
    }

    static ChartBar[] Bars(IReadOnlyList<BarRow> served) =>
        [.. served.Select(bar => new ChartBar(bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume))];

    // A session as the store writes it, formatted once against the invariant
    // culture rather than interpolated into each statement. A hole carrying a
    // date format is a hole rendered in the machine's locale, which is the form
    // `clock-usage` reads off the literal and refuses.
    static string Stamp(DateOnly session) =>
        session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---- the routes, hosted in process, owed at 6.0 ----
    //
    // The phase 5 sign-off found that no test in the suite hosted a route, so
    // every route body in the API's own file was unreached and each screen's
    // PASS sat at the helper the route calls. It showed it by pointing the
    // tonight route at the day before the one asked for and leaving all 556
    // tests green. 6.0's ruling is that the suite hosts the API in process
    // against a throwaway store, so a route's verdict sits at the route.
    //
    // In process rather than over a port. A listener on a real socket is a
    // second thing that can fail, and a bound-time bound is a claim about the
    // machine, which is the shape this corpus has already caught twice.
    // Keyed on a public type from the API's own assembly rather than on its
    // `Program`, which the factory only uses to find the assembly. The suite
    // has a `Program` of its own for the phase report command, and both are in
    // the global namespace, so naming that one is ambiguous and the compiler
    // says so.
    internal sealed class Host(string root) : WebApplicationFactory<ReadApi>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting(StoreLocation.DataRootKey, root);
    }

    // The night the store's listings are for, read off the store rather than
    // written here, so a fixture whose year moves takes this with it.
    static string NightIn(TemporaryStore store)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(session_date) FROM listing;";

        return (string)command.ExecuteScalar()!;
    }

    [Fact]
    public async Task TheTonightRouteServesTheNightItWasAskedForAndNotTheDayBefore()
    {
        // The mutation the sign-off demonstrated with, closed. Both directions:
        // the night asked for is drawn and the day before it is not, so a route
        // shifted by a day fails rather than serving a page that looks right.
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var night = NightIn(store);
        var before = DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .AddDays(-1)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var body = await client.GetStringAsync($"/screens/tonight/{night}");

        Assert.Contains(night, body, StringComparison.Ordinal);
        Assert.DoesNotContain(before, body, StringComparison.Ordinal);

        // A name that fired on that night, so what is asserted is the listings
        // the route served and not the date it echoed back.
        //
        // 6.0's own mutation sweep found the weaker form: pointed at the day
        // before, the route finds no listings for it and falls back to section
        // 18's banner, which draws the night that was asked for. So both
        // assertions above held and the mutation the sign-off demonstrated with
        // survived a test written to catch it. A date in the markup is the one
        // thing a wrong night still produces.
        Assert.Contains(FiredOn(store, night), body, StringComparison.Ordinal);
    }

    // A ticker that fired on one night, read off the store.
    static string FiredOn(TemporaryStore store, string night)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT ticker FROM listing WHERE session_date = $n AND fired_count > 0 ORDER BY ticker LIMIT 1;";
        command.Parameters.AddWithValue("$n", night);

        var ticker = command.ExecuteScalar() as string;

        Assert.NotNull(ticker);

        return ticker;
    }

    [Fact]
    public async Task TheRunRouteServesTheNightItWasAskedFor()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var night = NightIn(store);
        var body = await client.GetStringAsync($"/screens/run/{night}");

        Assert.Contains(night, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNameRouteServesTheNameItWasAskedForAndNotAnother()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var body = await client.GetStringAsync($"/screens/name/{Name}");

        Assert.Contains(Name, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheUniverseRouteCarriesItsFilterThroughToWhatItDraws()
    {
        // A filter passed on the query string and honoured by the route. A route
        // that dropped it would draw the whole index and read as correct.
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var all = await client.GetStringAsync("/screens/universe");
        var filtered = await client.GetStringAsync("/screens/universe?trend=downtrend");

        Assert.NotEqual(all, filtered);
    }

    // A ticker the provider has listed under two join dates holds two open spans, and every
    // screen draws it once, from the span the provider listed most recently.
    [Fact]
    public async Task ATickerListedUnderTwoJoinDatesIsDrawnOnceFromTheSpanListedMostRecently()
    {
        using var store = await FixtureExpectations.WithListings();

        var sector = Rows(store, "SELECT sector FROM membership WHERE ticker = 'AAPL' AND \"left\" IS NULL;").Single()[0];

        // The span the provider listed before it re-dated a member, and a name with no bars
        // listed under two join dates, which is stale on every night.
        store.Execute(
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector) VALUES " +
            "('GSPC', 'AAPL', '2026-08-18', NULL, '2000-01-01T00:00:00Z', 'an earlier span'), " +
            "('GSPC', 'ZZZZ', '2001-12-03', NULL, '2000-01-01T00:00:00Z', NULL), " +
            "('GSPC', 'ZZZZ', '2026-08-18', NULL, '2000-01-02T00:00:00Z', NULL);");

        var universe = await Api(store).UniverseAsync(Index);

        Assert.Equal([.. FixtureExpectation.CurrentMembers, "ZZZZ"], universe.Select(row => row.Ticker));
        Assert.Equal(sector, universe.Single(row => row.Ticker == "AAPL").Sector);
        Assert.Equal(["ZZZZ"], await Api(store).StaleNamesAsync(Index));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        foreach (var route in new[] { "/screens/tonight", "/screens/universe", "/screens/name/AAPL", "/screens/run" })
        {
            var response = await client.GetAsync(route);

            Assert.Equal((route, System.Net.HttpStatusCode.OK), (route, response.StatusCode));
        }

        Assert.Contains(
            $"data-names=\"{FixtureExpectation.CurrentMembers.Length + 1}\"",
            await client.GetStringAsync("/screens/universe"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMarkRouteHandsBackSvgForTheNameItWasAskedFor()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var response = await client.GetAsync($"/marks/level-chart/{Name}");

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<svg", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRootRouteServesTheShell()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var response = await client.GetAsync("/");

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TheRunPageOpensOnTheNightWrittenLastAndNotOnTheEarliestInstant()
    {
        // 6.0's repair for what the phase 5 sign-off found. A replay stamps the
        // run log's instant from 21:10Z on the session it was given, so a night
        // replayed for an older session after tonight's ran carries the older
        // instant. Ordered by that instant, the page opened on a night whose
        // list the store may not hold. The write order is the rowid, which is
        // the one thing here that nothing can stamp.
        //
        // Constructed rows, because the thing under test is the ordering and a
        // replay that produced this shape would be two full nights to build a
        // two-row case.
        using var store = await FixtureExpectations.WithListings();

        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, " +
            "network_requests, spend, detail) VALUES " +
            "('night-later-session', 'close', '2026-09-04T21:10:00Z', '2026-09-04T21:11:00Z', 'ok', 1, 0, 0, '0', 'x');");

        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, " +
            "network_requests, spend, detail) VALUES " +
            "('night-earlier-session', 'close', '2026-08-31T21:10:00Z', '2026-08-31T21:11:00Z', 'ok', 1, 0, 0, '0', 'x');");

        var night = await Api(store).RunNightAsync();

        // The row written last, whose instant is the earlier of the two.
        Assert.Equal(new DateOnly(2026, 8, 31), night);
    }

    // The numbers section, 6.1's visible output.
    //
    // A store the fetcher filled rather than rows this test wrote, because what is
    // being asserted is that the figures on the page are the figures in the store,
    // and a test that wrote both would be comparing itself with itself.
    static async Task<TemporaryStore> WithFundamentals(bool withTheArchive = false)
    {
        var store = await WithLadders();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        await new FundamentalsFetcher(
            RecordedFundamentalsFeed.FromFolder(FixtureFolder()),
            clock,
            store.DatabaseFile,
            withTheArchive ? new RecordedFilingsArchiveFeed(FixtureFolder()) : null)
            .RunAsync(Name, null, "run-fundamentals");

        return store;
    }

    static string StoredPayload(TemporaryStore store, string ticker)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT payload FROM fundamentals WHERE ticker = $ticker ORDER BY filing_date DESC LIMIT 1;";
        command.Parameters.AddWithValue("$ticker", ticker);

        return (string)command.ExecuteScalar()!;
    }

    [Fact]
    public async Task TheNumbersSectionDrawsTheStoredFiguresAndReadsBackAgainstTheStore()
    {
        using var store = await WithFundamentals();

        var region = NameScreen.Numbers(await Api(store).FundamentalsAsync(Name));

        using var payload = JsonDocument.Parse(StoredPayload(store, Name));

        var quarter = payload.RootElement.GetProperty("quarter");

        // Read off the markup against the store, figure by figure, rather than
        // against the object the section was handed. Each figure sits on its own
        // attribute, so a section drawing the right count of wrong numbers fails.
        foreach (var name in new[] { "revenue", "netIncome", "grossMargin", "netMargin" })
        {
            Assert.Contains(
                $"data-{name}=\"{quarter.GetProperty(name).GetString()}\"",
                region,
                StringComparison.Ordinal);
        }

        var sheet = payload.RootElement.GetProperty("balanceSheet");

        foreach (var name in new[] { "totalAssets", "totalLiabilities", "equity", "cash", "netDebt" })
        {
            Assert.Contains(
                $"data-{name}=\"{sheet.GetProperty(name).GetString()}\"",
                region,
                StringComparison.Ordinal);
        }

        // The valuation on each earnings basis, with the basis drawn beside the
        // ratio: a multiple without the earnings figure it was struck on is a
        // number nobody can check.
        var valuation = payload.RootElement.GetProperty("valuation");
        var bases = payload.RootElement.GetProperty("epsBases");

        Assert.Contains($"data-trailingPe=\"{valuation.GetProperty("trailingPe").GetString()}\"", region, StringComparison.Ordinal);
        Assert.Contains($"data-forwardPe=\"{valuation.GetProperty("forwardPe").GetString()}\"", region, StringComparison.Ordinal);
        Assert.Contains($"data-trailing=\"{bases.GetProperty("trailing").GetString()}\"", region, StringComparison.Ordinal);
        Assert.Contains("data-basis=\"trailing\"", region, StringComparison.Ordinal);
        Assert.Contains("data-basis=\"forward\"", region, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSectionShowsFiveQuartersWhileTheStoreHoldsTwelve()
    {
        using var store = await WithFundamentals();

        var filings = await Api(store).FundamentalsAsync(Name);
        var region = NameScreen.Numbers(filings);

        // The read surface hands back every filing, because which of them a
        // section shows is the section's statement rather than the surface's.
        Assert.Equal(FundamentalsFetcher.StoredFilings, filings.Count);

        Assert.Contains($"data-filings-held=\"{FundamentalsFetcher.StoredFilings}\"", region, StringComparison.Ordinal);
        Assert.Contains($"data-quarters-shown=\"{NameScreen.QuartersShown}\"", region, StringComparison.Ordinal);

        // Five rows drawn, and they are the five most recent. A section drawing all
        // twelve would still hold every figure this test's sibling asserts.
        var drawn = Regex.Matches(region, "data-period-end=\"[0-9-]+\"").Count;

        Assert.Equal(NameScreen.QuartersShown, drawn);

        foreach (var filing in filings.Take(NameScreen.QuartersShown))
        {
            Assert.Contains(FormattableString.Invariant($"data-filed=\"{filing.FilingDate:yyyy-MM-dd}\""), region, StringComparison.Ordinal);
        }

        // And the sixth is not drawn, which is what makes the count a selection.
        Assert.DoesNotContain(
            FormattableString.Invariant($"data-filed=\"{filings[NameScreen.QuartersShown].FilingDate:yyyy-MM-dd}\""),
            region,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatTheArchiveWasNotReadForIsMarkedAbsentWithThatReasonAndNeverDrawnBlank()
    {
        using var store = await WithFundamentals();

        var region = NameScreen.Numbers(await Api(store).FundamentalsAsync(Name));

        // Section 18's row: a filing not parsed for a name shows what the provider
        // has and marks the rest absent, because a blank cell reads as a zero. This
        // fetch had no archive, so the two parts it supplies are absent and the
        // reason drawn beside each is that the archive was not read, which is not
        // the same statement as a provider that files none.
        Assert.Contains("data-absent=\"segments\"", region, StringComparison.Ordinal);
        Assert.Contains("data-absent=\"guidance\"", region, StringComparison.Ordinal);
        Assert.Contains(
            FormattableString.Invariant($"data-reason=\"{FundamentalsFetcher.NotRead}\""),
            region,
            StringComparison.Ordinal);

        Assert.DoesNotContain(FundamentalsFetcher.Archive, region, StringComparison.Ordinal);

        // And the consensus estimate is named for what it is rather than presented
        // as a guide, which it is not: one is what analysts expect and the other is
        // what management said.
        Assert.Contains("consensus estimate", region, StringComparison.Ordinal);
        Assert.Contains("data-estimated-quarter=", region, StringComparison.Ordinal);

        // No empty cell anywhere in the section, which is the property rather than
        // the two absences above: a figure the filing does not carry says so.
        Assert.DoesNotContain("<td></td>", region, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSegmentTableIsDrawnFromTheStoreForTheQuarterTheTableStates()
    {
        using var store = await WithFundamentals(withTheArchive: true);

        var region = NameScreen.Numbers(await Api(store).FundamentalsAsync(Name));

        using var payload = JsonDocument.Parse(StoredPayload(store, Name));

        var segments = payload.RootElement.GetProperty("segments");

        // Drawn from the store rather than computed: the report it came from, the
        // period it covers and every figure are the stored ones.
        Assert.Contains(
            FormattableString.Invariant($"data-report=\"{segments.GetProperty("report").GetString()}\""),
            region,
            StringComparison.Ordinal);

        Assert.Contains("data-months=\"3\"", region, StringComparison.Ordinal);
        Assert.DoesNotContain("data-absent=\"segments\"", region, StringComparison.Ordinal);

        // Every group the table carries, in the order it carries them, because two
        // groups of one captured table share a member and two labels repeat: a
        // screen keyed on the label would draw one and drop the other.
        foreach (var group in segments.GetProperty("groups").EnumerateArray())
        {
            Assert.Contains(
                FormattableString.Invariant($"data-segment=\"{group.GetProperty("label").GetString()}\""),
                region,
                StringComparison.Ordinal);
        }

        // And the quarter's own figures, matched against the store figure by figure
        // for the period drawn.
        var newest = segments.GetProperty("periods").EnumerateArray()
            .Where(period => period.GetProperty("months").GetInt32() == 3)
            .Max(period => period.GetProperty("ended").GetString())!;

        var drawn = segments.GetProperty("consolidated").EnumerateArray()
            .Where(figure => figure.GetProperty("months").GetInt32() == 3
                && figure.GetProperty("ended").GetString() == newest
                && figure.GetProperty("value").ValueKind == JsonValueKind.String)
            .ToArray();

        Assert.NotEmpty(drawn);
        Assert.All(drawn, figure => Assert.Contains(figure.GetProperty("value").GetString()!, region, StringComparison.Ordinal));

        // The nine-month column is not drawn, which is what makes the period a
        // selection: one end date under two spans is two columns, and showing both
        // would put three quarters of a year beside one quarter with nothing saying
        // which was which.
        Assert.Contains(
            FormattableString.Invariant($"data-period-end=\"{newest}\""),
            region,
            StringComparison.Ordinal);

        Assert.Contains("line(s).", region, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GuidanceIsDrawnAsManagementsWordsAndTheExhibitItCameFrom()
    {
        // The filer whose release states none under any heading, which is the
        // absence this surface must not draw as a company that guided nothing.
        // see: Guidance is stored as management's own prose, and the facts file carries each figure the passage states as the claim checker reads it
        using var store = await WithFundamentals(withTheArchive: true);

        var region = NameScreen.Numbers(await Api(store).FundamentalsAsync(Name));

        using var payload = JsonDocument.Parse(StoredPayload(store, Name));

        var guidance = payload.RootElement.GetProperty("guidance");

        Assert.False(guidance.GetProperty("located").GetBoolean());
        Assert.Contains("data-guidance=\"not located\"", region, StringComparison.Ordinal);
        Assert.Contains(
            FormattableString.Invariant($"data-exhibit=\"{guidance.GetProperty("document").GetString()}\""),
            region,
            StringComparison.Ordinal);

        // The distinction on the surface a person reads, in words: the exhibit is
        // stored and no heading located a passage in it.
        Assert.Contains("is stored and no heading in it locates a guidance passage", region, StringComparison.Ordinal);
        Assert.DoesNotContain("data-guidance=\"located\"", region, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANameWithNoStoredFilingSaysSoRatherThanDrawingAnEmptyTable()
    {
        using var store = await WithLadders();

        var region = NameScreen.Numbers(await Api(store).FundamentalsAsync(Name));

        Assert.Contains("data-filings-held=\"0\"", region, StringComparison.Ordinal);
        Assert.Contains("data-absent=\"fundamentals\"", region, StringComparison.Ordinal);
        Assert.DoesNotContain("numbers-quarters", region, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANameHoldingFewerFilingsThanTheSectionStatesCarriesItsCount()
    {
        using var store = await WithLadders();

        // Three filings rather than twelve, constructed because every captured
        // name holds more. A reading over three quarters and one over twelve are
        // different readings, and the surface says which.
        foreach (var filed in new[] { "2026-07-31", "2026-04-30", "2026-01-30" })
        {
            Insert(
                store,
                "INSERT INTO fundamentals (ticker, filing_date, fetched_at, payload, source) VALUES " +
                $"('{Name}', '{filed}', '2026-09-05T21:10:00Z', " +
                "'{\"periodEnd\":\"2026-06-30\",\"currency\":\"USD\",\"quarter\":{\"revenue\":\"1\"}}', '{}');");
        }

        var region = NameScreen.Numbers(await Api(store).FundamentalsAsync(Name));

        Assert.Contains("data-filings-held=\"3\"", region, StringComparison.Ordinal);
        Assert.Contains("data-quarters-shown=\"3\"", region, StringComparison.Ordinal);
        Assert.Contains("data-short-window=\"3\"", region, StringComparison.Ordinal);
        Assert.Contains($"fewer than the {NameScreen.QuartersShown} quarters", region, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNameRouteServesTheNumbersSection()
    {
        using var store = await WithFundamentals();

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync($"/screens/name/{Name}");

        // On the route rather than on the projection alone, because a claim that
        // something is visible is a claim about a surface and the route is the
        // surface a person opens.
        Assert.Contains("class=\"numbers\"", page, StringComparison.Ordinal);
        Assert.Contains($"data-quarters-shown=\"{NameScreen.QuartersShown}\"", page, StringComparison.Ordinal);

        using var payload = JsonDocument.Parse(StoredPayload(store, Name));

        Assert.Contains(
            $"data-revenue=\"{payload.RootElement.GetProperty("quarter").GetProperty("revenue").GetString()}\"",
            page,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFactStripStatesEveryPartItsRowEnumeratesAndEachAgainstTheStore()
    {
        using var store = await WithFundamentals();

        var api = Api(store);
        var bars = await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);
        var indicators = await api.IndicatorsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);
        var filings = await api.FundamentalsAsync(Name);
        var extremes = await api.MoveExtremesAsync(Name);

        var strip = NameScreen.FactStrip(
            Name,
            bars[^1].Close,
            (await api.NextEventAsync(Name, bars[^1].SessionDate))?.EventDate,
            extremes,
            filings,
            indicators);

        // Seven parts, each on its own attribute, so a strip drawing six of them
        // fails rather than passing on the row. The row's own decomposition is what
        // makes each a claim; this is what gives each a verdict.
        Assert.Contains(FormattableString.Invariant($"data-close=\"{bars[^1].Close}\""), strip, StringComparison.Ordinal);

        using var payload = JsonDocument.Parse(StoredPayload(store, Name));

        var capitalisation = payload.RootElement.GetProperty("marketCapitalisation").GetString();
        var valuation = payload.RootElement.GetProperty("valuation");

        Assert.Contains($"data-market-capitalisation=\"{capitalisation}\"", strip, StringComparison.Ordinal);
        Assert.Contains($"data-trailing-multiple=\"{valuation.GetProperty("trailingPe").GetString()}\"", strip, StringComparison.Ordinal);
        Assert.Contains($"data-forward-multiple=\"{valuation.GetProperty("forwardPe").GetString()}\"", strip, StringComparison.Ordinal);

        // The high and the low of the sessions the move spans, against the bars of
        // exactly those sessions read by a second path.
        Assert.NotNull(extremes);

        var span = bars
            .Where(bar => bar.SessionDate <= extremes!.Ended)
            .OrderByDescending(bar => bar.SessionDate)
            .Take(extremes!.Sessions)
            .ToArray();

        Assert.Equal(span.Max(bar => bar.High), extremes.High);
        Assert.Equal(span.Min(bar => bar.Low), extremes.Low);
        Assert.Contains(FormattableString.Invariant($"data-move-high=\"{extremes.High}\""), strip, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"data-move-low=\"{extremes.Low}\""), strip, StringComparison.Ordinal);

        // The next dated event, kept from what the strip said before this row was
        // decomposed, so the part that already worked still reads the same way.
        Assert.Contains("data-next-event=", strip, StringComparison.Ordinal);
        Assert.Contains("next dated event:", strip, StringComparison.Ordinal);

        // The averages, and then momentum and the typical daily move, each against
        // the indicator row the store holds for the last session.
        var latest = indicators
            .Where(row => row.SessionDate == indicators.Max(other => other.SessionDate))
            .ToDictionary(row => row.Name, row => row.Value, StringComparer.Ordinal);

        foreach (var name in new[]
        {
            IndicatorSeries.Sma20, IndicatorSeries.Sma50, IndicatorSeries.Sma200,
            IndicatorSeries.Rsi14, IndicatorSeries.Macd, IndicatorSeries.MacdSignal,
            IndicatorSeries.MacdHist, IndicatorSeries.Atr14,
        })
        {
            var stated = latest.TryGetValue(name, out var value) && value is { } reading
                ? reading.ToString("0.######", CultureInfo.InvariantCulture)
                : "none";

            Assert.Contains($"data-{name}=\"{stated}\"", strip, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheFactStripSaysWhatIsNotOnFileRatherThanDrawingABlank()
    {
        // A name with no filing and no move: five of the seven parts have no value,
        // and each says so. A blank attribute would read as a zero, and a zero
        // market capitalisation is a figure a reader would act on.
        using var store = await Populated();

        var api = Api(store);
        var bars = await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);

        var strip = NameScreen.FactStrip(Name, bars[^1].Close, null, null, [], []);

        Assert.Contains("data-market-capitalisation=\"none\"", strip, StringComparison.Ordinal);
        Assert.Contains("data-trailing-multiple=\"none\"", strip, StringComparison.Ordinal);
        Assert.Contains("data-forward-multiple=\"none\"", strip, StringComparison.Ordinal);
        Assert.Contains("data-move-high=\"none\"", strip, StringComparison.Ordinal);
        Assert.Contains("data-move-low=\"none\"", strip, StringComparison.Ordinal);
        Assert.Contains("data-next-event=\"none\"", strip, StringComparison.Ordinal);
        Assert.Contains("not on file", strip, StringComparison.Ordinal);

        // And the close, which this name does have, is not drawn as an absence.
        Assert.DoesNotContain("data-close=\"none\"", strip, StringComparison.Ordinal);

        // No empty attribute anywhere, which is the property rather than the six
        // above: a part with no value says so in a word.
        Assert.DoesNotContain("=\"\"", strip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNameRouteServesTheFactStripWithItsSevenParts()
    {
        using var store = await WithFundamentals();

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync($"/screens/name/{Name}");

        // On the route, because a claim that something is visible is a claim about
        // a surface and the route is the surface a person opens.
        Assert.Contains("class=\"fact-strip\"", page, StringComparison.Ordinal);
        Assert.Contains("data-market-capitalisation=", page, StringComparison.Ordinal);
        Assert.Contains("data-trailing-multiple=", page, StringComparison.Ordinal);
        Assert.Contains("data-move-high=", page, StringComparison.Ordinal);
        Assert.Contains("data-sma200=", page, StringComparison.Ordinal);
        Assert.Contains("data-atr14=", page, StringComparison.Ordinal);
    }
}
