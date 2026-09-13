using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 6.5: the staleness judge over the fixture's own stored
// dates, section 17's research staleness row read off the document, and figure
// 12.1's three questions.
//
// One test per trigger, which is what section 17's assertion column asks for, each
// with the case that must not fire beside the case that must, because a trigger
// that fired on everything would pass a test that only asked whether it fired.
public partial class FixtureExpectations
{
    static readonly DateOnly StaleNight = new(2026, 9, 8);

    static SectionStanding Accepted(string section, string asOf) =>
        new(section, DateOnly.ParseExact(asOf, "yyyy-MM-dd", CultureInfo.InvariantCulture), Staleness.Accepted);

    static PulseSession[] QuietThenSpike(DateOnly night, params int[] spike)
    {
        // Sixty sessions of one article, then the spike ending on the night. Weekdays
        // only, because the counter writes a row per trading session.
        var sessions = new List<DateOnly>();

        for (var day = night; sessions.Count < 60 + spike.Length; day = day.AddDays(-1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                sessions.Add(day);
            }
        }

        sessions.Reverse();

        return
        [
            .. sessions.Select((session, index) => new PulseSession(
                session,
                index < 60 ? 1 : spike[index - 60])),
        ];
    }

    // ---- one per trigger ----

    [Fact]
    public void ANewFilingMakesStaleTheSectionsWrittenBeforeItAndNoOthers()
    {
        var filed = new DateOnly(2026, 9, 2);

        var verdict = Staleness.Judge(
            StaleNight,
            [Accepted("What the company sells", "2026-08-25"), Accepted("The two cases", "2026-09-05")],
            filed,
            [],
            [],
            refresh: false);

        Assert.Equal(ResearchState.Stale, verdict.State);
        Assert.Equal(["What the company sells"], verdict.StaleSections);
        Assert.Equal(StalenessTrigger.NewFiling, Assert.Single(verdict.Fired).Trigger);

        // A filing on the section's own date is not after it, and a filing dated
        // after the night is not one the night could have known.
        Assert.Equal(ResearchState.Stands, Staleness.Judge(StaleNight, [Accepted("The two cases", "2026-09-02")], filed, [], [], false).State);
        Assert.Equal(ResearchState.Stands, Staleness.Judge(StaleNight, [Accepted("The two cases", "2026-08-01")], new DateOnly(2026, 9, 9), [], [], false).State);
    }

    [Fact]
    public void AnEarningsDatePassingMakesStaleTheSectionsWrittenBeforeThePrint()
    {
        EarningsEvent[] earnings = [new(new DateOnly(2026, 8, 18), "after"), new(new DateOnly(2026, 11, 20), "after")];

        var verdict = Staleness.Judge(StaleNight, [Accepted("The risks, each with what would confirm it", "2026-08-10")], null, earnings, [], false);

        Assert.Equal(ResearchState.Stale, verdict.State);
        Assert.Equal(StalenessTrigger.EarningsPassed, Assert.Single(verdict.Fired).Trigger);
        Assert.Equal(new DateOnly(2026, 8, 18), verdict.Fired[0].Since);

        // A section written after the print stands, and the print still ahead is not
        // one that has passed.
        Assert.Equal(ResearchState.Stands, Staleness.Judge(StaleNight, [Accepted("The risks, each with what would confirm it", "2026-08-19")], null, earnings, [], false).State);

        // The same day: an after-close print was most likely after the section was
        // written, and a print filed before the open was not.
        Assert.True(Staleness.PrintedAfter(new EarningsEvent(new DateOnly(2026, 8, 18), "after"), new DateOnly(2026, 8, 18)));
        Assert.True(Staleness.PrintedAfter(new EarningsEvent(new DateOnly(2026, 8, 18), "unstated"), new DateOnly(2026, 8, 18)));
        Assert.False(Staleness.PrintedAfter(new EarningsEvent(new DateOnly(2026, 8, 18), Staleness.BeforeTheOpen), new DateOnly(2026, 8, 18)));
    }

    [Fact]
    public void TheNewsPulseFiresOnARunAboveItsOwnBaselineAndIsDatedByTheSessionTheRunBegan()
    {
        // Sixty quiet sessions, a baseline window sum of five, then six a day for
        // three sessions. The window ending two sessions before the night holds ten,
        // under three times five; the window ending the session before holds fifteen,
        // which is above; the night's holds twenty. So the run began the session
        // before the night.
        var pulse = QuietThenSpike(StaleNight, 6, 6, 6);
        var reading = Staleness.Pulse(StaleNight, pulse);

        Assert.True(reading.Above);
        Assert.Equal(20, reading.WindowArticles);
        Assert.Equal(5m, reading.Baseline);
        Assert.True(reading.BaselineWindows > 30);

        var began = pulse[^2].Session;

        Assert.Equal(began, reading.Since);

        // A section written before the run began is stale, and one written on the
        // session it began is not, which is the story breaking over several days not
        // firing twice.
        Assert.Equal(
            ResearchState.Stale,
            Staleness.Judge(StaleNight, [Accepted("The short version", pulse[^3].Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))], null, [], pulse, false).State);

        Assert.Equal(
            ResearchState.Stands,
            Staleness.Judge(StaleNight, [Accepted("The short version", began.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))], null, [], pulse, false).State);

        // The floor alone does not fire: a name steadily written about four times a
        // session has twenty articles in every window, over the floor and never three
        // times its own baseline.
        var steady = pulse.Select(day => day with { Articles = 4 }).ToArray();
        var steadyReading = Staleness.Pulse(StaleNight, steady);

        Assert.True(steadyReading.WindowArticles >= Staleness.ArticleFloor);
        Assert.False(steadyReading.Above);

        // Three times the baseline and under the floor does not fire either: a name
        // with no coverage that gets four articles has not had a story.
        var silent = QuietThenSpike(StaleNight, 2, 1, 1).Select((day, index) => index < 60 ? day with { Articles = 0 } : day).ToArray();

        Assert.False(Staleness.Pulse(StaleNight, silent).Above);

        // And no earlier window is no baseline and no reading, which is what the
        // fixture's one night of pulse is.
        var tonight = Staleness.Pulse(StaleNight, [new PulseSession(StaleNight, 40)]);

        Assert.False(tonight.Above);
        Assert.Null(tonight.Baseline);
        Assert.Equal(0, tonight.BaselineWindows);
    }

    [Fact]
    public void ARefreshMakesEverySectionStaleWhateverItsDate()
    {
        var verdict = Staleness.Judge(StaleNight, [Accepted("The two cases", "2026-09-08"), Accepted("The short version", "2026-09-07")], null, [], [], refresh: true);

        Assert.Equal(ResearchState.Stale, verdict.State);
        Assert.Equal(["The short version", "The two cases"], verdict.StaleSections);
        Assert.Equal(StalenessTrigger.Refresh, Assert.Single(verdict.Fired).Trigger);

        Assert.Equal(ResearchState.Stands, Staleness.Judge(StaleNight, [Accepted("The two cases", "2026-09-08")], null, [], [], refresh: false).State);
    }

    [Fact]
    public void ANameWithNoAcceptedSectionIsMissingRatherThanStale()
    {
        // No sections, and sections that were only ever left out, are both a name
        // with nothing written to go stale, even with every trigger firing.
        var nothing = Staleness.Judge(StaleNight, [], new DateOnly(2026, 9, 2), [new EarningsEvent(new DateOnly(2026, 8, 18), "after")], [], refresh: true);

        Assert.Equal(ResearchState.Missing, nothing.State);
        Assert.Empty(nothing.Fired);
        Assert.Equal(Staleness.NotYetWritten, nothing.Line);

        var leftOut = Staleness.Judge(StaleNight, [new SectionStanding("The two cases", new DateOnly(2026, 8, 1), "fallback")], new DateOnly(2026, 9, 2), [], [], true);

        Assert.Equal(ResearchState.Missing, leftOut.State);
    }

    // ---- the judge over the fixture ----

    static async Task<TemporaryStore> WithJudgedSections(IEnumerable<(string Section, string AsOf)> sections)
    {
        var store = await FixtureReplay.ReplayedAsync();

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        foreach (var (section, asOf) in sections)
        {
            using var command = connection.CreateCommand();

            command.CommandText =
                "INSERT INTO research_section VALUES ('KEYS', $section, 1, $as_of, 'a writer', 'accepted', 'prose', '[]', NULL);";
            command.Parameters.AddWithValue("$section", section);
            command.Parameters.AddWithValue("$as_of", asOf);
            command.ExecuteNonQuery();
        }

        return store;
    }

    [Fact]
    public async Task TheJudgeReachesTheVerdictsTheFixturesOwnStalenessExpectationStates()
    {
        var expected = Expected("staleness");
        var sections = expected.GetProperty("sections");
        var ticker = expected.GetProperty("ticker").GetString()!;

        // The fixture's own dates, asserted first, so the verdicts below are about
        // the rules rather than about dates this file assumed.
        using var store = await WithJudgedSections(
            sections.EnumerateObject().Select(entry => (entry.Name, entry.Value.GetProperty("asOf").GetString()!)));

        Assert.Equal(expected.GetProperty("night").GetString(), StaleScalar(store, "SELECT MAX(session_date) FROM facts WHERE ticker = 'KEYS';"));
        Assert.Equal(expected.GetProperty("newestFiling").GetString(), StaleScalar(store, "SELECT MAX(filing_date) FROM fundamentals WHERE ticker = 'KEYS';"));

        var earnings = expected.GetProperty("newestPassedEarnings");

        Assert.Equal(
            earnings.GetProperty("timing").GetString(),
            StaleScalar(store, $"SELECT timing FROM calendar WHERE ticker = 'KEYS' AND event_date = '{earnings.GetProperty("date").GetString()}';"));

        var clock = FixedClock.At(new DateTimeOffset(2026, 9, 12, 14, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var verdict = await new StalenessJudge(clock, store.DatabaseFile).JudgeAsync(ticker, refresh: false, "judge-fixture");

        Assert.Equal(expected.GetProperty("night").GetString(), verdict.Night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.Equal(expected.GetProperty("recordState").GetString(), verdict.State.ToString());

        foreach (var entry in sections.EnumerateObject())
        {
            Assert.Equal(entry.Value.GetProperty("stale").GetBoolean(), verdict.StaleSections.Contains(entry.Name));

            // Which triggers made this section stale, worked out from the rules one
            // at a time so each fired trigger is attributed to the sections it reaches.
            var asOf = DateOnly.ParseExact(entry.Value.GetProperty("asOf").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var stated = entry.Value.GetProperty("firedBy").EnumerateArray().Select(trigger => trigger.GetString()!).ToArray();

            var alone = Staleness.Judge(
                verdict.Night,
                [new SectionStanding(entry.Name, asOf, Staleness.Accepted)],
                DateOnly.ParseExact(expected.GetProperty("newestFiling").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                [new EarningsEvent(DateOnly.ParseExact(earnings.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture), earnings.GetProperty("timing").GetString()!)],
                [],
                false);

            Assert.Equal<string>(stated, [.. alone.Fired.Select(fired => fired.Trigger.ToString())]);
        }

        // With only the section written after both, and with a refresh.
        using (var onlyAfter = await WithJudgedSections([("written after both", "2026-09-05")]))
        {
            var judge = new StalenessJudge(clock, onlyAfter.DatabaseFile);

            Assert.Equal(expected.GetProperty("withOnlyTheSectionWrittenAfterBoth").GetString(), (await judge.JudgeAsync(ticker, false, "judge-after")).State.ToString());
            Assert.Equal(expected.GetProperty("withARefresh").GetString(), (await judge.JudgeAsync(ticker, true, "judge-refresh")).State.ToString());
        }

        var missing = expected.GetProperty("aNameWithNoRecord");

        Assert.Equal(
            missing.GetProperty("state").GetString(),
            (await new StalenessJudge(clock, store.DatabaseFile).JudgeAsync(missing.GetProperty("ticker").GetString()!, true, "judge-missing")).State.ToString());

        var pulse = expected.GetProperty("pulseOverTheFixture");

        Assert.Equal(pulse.GetProperty("sessions").GetInt32(), verdict.Pulse.WindowSessions);
        Assert.Equal(pulse.GetProperty("baselineWindows").GetInt32(), verdict.Pulse.BaselineWindows);
        Assert.Equal(pulse.GetProperty("above").GetBoolean(), verdict.Pulse.Above);

        // Deciding not to spend spent nothing, asserted over the judge's own run log
        // rows rather than over a night, and over the class, which holds no client
        // and declares no feed to have made a request with.
        var spent = expected.GetProperty("spentByAJudgement");

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), SUM(network_requests), SUM(model_calls), MIN(spend), MAX(spend) FROM run_log WHERE stage = $stage;";
        command.Parameters.AddWithValue("$stage", StalenessJudge.Stage);

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal(spent.GetProperty("requests").GetInt32(), reader.GetInt32(1));
        Assert.Equal(spent.GetProperty("modelCalls").GetInt32(), reader.GetInt32(2));
        Assert.Equal("0", reader.GetString(3));
        Assert.Equal("0", reader.GetString(4));

        Assert.Empty(StalenessJudge.Access.Feeds);
        Assert.All(
            typeof(StalenessJudge).GetConstructors().SelectMany(constructor => constructor.GetParameters()),
            parameter => Assert.DoesNotContain("Http", parameter.ParameterType.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void SectionSeventeensTriggerFiguresAreTheOnesTheRulesUse()
    {
        // The row against the code, read off the row. Numbers written as words in the
        // row are read as the words the constants spell.
        var expected = Expected("staleness").GetProperty("sectionSeventeen");
        var row = ClaimAdmissibility.Row(Scope.LimitsTable, "Research staleness triggers");
        var value = row[1];

        string Word(int number) => number switch { 3 => "three", 5 => "five", _ => number.ToString(CultureInfo.InvariantCulture) };

        Assert.Contains($"at least {Word(Staleness.ArticleFloor)} articles in the last {Word(Staleness.WindowSessions)} sessions", value, StringComparison.Ordinal);
        Assert.Contains($"at least {Word(Staleness.BaselineMultiple)} times the name's own trailing {Staleness.BaselineDays}-day median for a {Word(Staleness.WindowSessions)}-session window", value, StringComparison.Ordinal);

        Assert.Equal(Staleness.WindowSessions, expected.GetProperty("windowSessions").GetInt32());
        Assert.Equal(Staleness.ArticleFloor, expected.GetProperty("articleFloor").GetInt32());
        Assert.Equal(Staleness.BaselineMultiple, expected.GetProperty("baselineMultiple").GetInt32());
        Assert.Equal(Staleness.BaselineDays, expected.GetProperty("baselineDays").GetInt32());

        // And the four triggers the row names are the four the rules carry.
        Assert.Equal(4, Enum.GetValues<StalenessTrigger>().Length);

        foreach (var phrase in new[] { "a new filing", "the earnings date passing", "or a manual refresh" })
        {
            Assert.Contains(phrase, value, StringComparison.Ordinal);
        }
    }

    static string? StaleScalar(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar() as string;
    }
}
