using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// 15.4: the answer a sweep run states, recorded by the command run after it, and the rule a card draws its line by.
// see: No family on any index is set aside or hidden by a test result without the operator's word
public partial class FixtureExpectations
{
    static RecordedAnswer Answered(string run, string? design, string answer, int hour) =>
        new(run, "MID", "heavyweight", design, answer, new DateTimeOffset(2026, 10, 2, hour, 0, 0, TimeSpan.Zero));

    [Fact]
    public void ACardsLineIsDrawnWhereEveryDesignsNewestAnswerSaysNonePassedAndNoFreezeCameAfterIt()
    {
        var at = (int hour) => new DateTimeOffset(2026, 10, 2, hour, 0, 0, TimeSpan.Zero);

        // No answer recorded: nothing to draw.
        Assert.False(SweepLine.Drawn([], null));

        // One design, none passed, no freeze: drawn; a newer run passing takes it away, and an older one passing does not.
        Assert.True(SweepLine.Drawn([Answered("r1", null, SweepAnswer.NonePassedWord, 10)], null));
        Assert.False(SweepLine.Drawn([Answered("r1", null, SweepAnswer.NonePassedWord, 10), Answered("r2", null, SweepAnswer.PassedWord, 11)], null));
        Assert.True(SweepLine.Drawn([Answered("r1", null, SweepAnswer.PassedWord, 9), Answered("r2", null, SweepAnswer.NonePassedWord, 10)], null));

        // Two designs: drawn only where both newest say none passed, one design passing being a result even where the
        // other's run after it found none.
        Assert.False(SweepLine.Drawn([Answered("a1", "a", SweepAnswer.NonePassedWord, 10), Answered("b1", "b", SweepAnswer.PassedWord, 10)], null));
        Assert.False(SweepLine.Drawn([Answered("a1", "a", SweepAnswer.PassedWord, 10), Answered("b1", "b", SweepAnswer.NonePassedWord, 11)], null));
        Assert.True(SweepLine.Drawn([Answered("a1", "a", SweepAnswer.NonePassedWord, 10), Answered("b1", "b", SweepAnswer.PassedWord, 9), Answered("b2", "b", SweepAnswer.NonePassedWord, 11)], null));

        // A freeze registered after the newest answer takes it away; one at its very instant does too, the freeze being
        // what was done on the answer; one a second before it does not.
        Assert.False(SweepLine.Drawn([Answered("r1", null, SweepAnswer.NonePassedWord, 10)], at(11)));
        Assert.False(SweepLine.Drawn([Answered("r1", null, SweepAnswer.NonePassedWord, 10)], at(10)));
        Assert.True(SweepLine.Drawn([Answered("r1", null, SweepAnswer.NonePassedWord, 10)], at(10).AddSeconds(-1)));
    }

    [Fact]
    public void AnAnswerIsStatedAsItsRunWroteItAndReadBackWhole()
    {
        var answer = new SweepAnswer("SML", "heavyweight", "b", false);

        Assert.Equal("{\"index\":\"SML\",\"family\":\"heavyweight\",\"design\":\"b\",\"passed\":false}", answer.Json());
        Assert.Equal(answer, SweepAnswer.Read(answer.Json()));
        Assert.Equal(SweepAnswer.NonePassedWord, answer.Word);
        Assert.Equal(SweepAnswer.PassedWord, (answer with { Passed = true }).Word);
    }

    [Fact]
    public async Task TheCommandRecordsTheAnswerARunStatesAndRefusesARunStatingNoneWithNothingWritten()
    {
        using var store = new TemporaryStore().Migrated();
        using var sweeps = new TemporaryDirectory();
        var clock = new SweepClock(new DateTimeOffset(2026, 10, 6, 1, 30, 0, TimeSpan.Zero));
        var said = new StringWriter();
        var command = new SweepAnswers(clock, store.DatabaseFile, sweeps.Path, said);

        void Run(string name, string? answer)
        {
            Directory.CreateDirectory(Path.Combine(sweeps.Path, name));

            if (answer is not null)
            {
                File.WriteAllText(Path.Combine(sweeps.Path, name, SweepAnswer.File), answer);
            }
        }

        IReadOnlyList<string> Rows()
        {
            using var connection = store.Open();
            using var read = connection.CreateCommand();

            read.CommandText = "SELECT run || ' ' || index_code || ' ' || family || ' ' || coalesce(design, '-') || ' ' || answer || ' ' || recorded_at FROM sweep_answer ORDER BY run;";

            using var reader = read.ExecuteReader();
            var rows = new List<string>();

            while (reader.Read())
            {
                rows.Add(reader.GetString(0));
            }

            return rows;
        }

        long Logged()
        {
            using var connection = store.Open();
            using var read = connection.CreateCommand();

            read.CommandText = "SELECT count(*) FROM run_log WHERE stage = 'sweep-answer';";

            return (long)read.ExecuteScalar()!;
        }

        Run("20261006T004046Z", new SweepAnswer("SML", "breakout", null, false).Json());
        Run("20261006T004044Z", new SweepAnswer("MID", "heavyweight", "a", true).Json());
        Run("20261005T211903Z", null);
        Run("20261006T004050Z", new SweepAnswer("SML", "leader", null, true).Json());
        Run("20261006T004051Z", new SweepAnswer("NDX", "drift", null, true).Json());
        Run("20261006T004052Z", "not an answer");

        // Two runs recorded a minute apart, each a row and a run log row; one recorded again a minute later writes its row
        // again with the instant it was recorded at, and a run log row of its own.
        Assert.Equal(0, await command.RecordAsync("20261006T004046Z"));
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        Assert.Equal(0, await command.RecordAsync("20261006T004044Z"));
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        Assert.Equal(0, await command.RecordAsync("20261006T004044Z"));
        Assert.Equal(
            ["20261006T004044Z MID heavyweight a passed 2026-10-06T01:32:00Z", "20261006T004046Z SML breakout - none passed 2026-10-06T01:30:00Z"],
            Rows());
        Assert.Equal(3, Logged());
        Assert.Contains("recorded run 20261006T004046Z: SML breakout, none passed", said.ToString(), StringComparison.Ordinal);

        var logged = Logged();

        // Each refused with why, and nothing written for any of them.
        Assert.Equal(2, await command.RecordAsync("20261005T211903Z"));
        Assert.Equal(2, await command.RecordAsync("20261006T004050Z"));
        Assert.Equal(2, await command.RecordAsync("20261006T004051Z"));
        Assert.Equal(2, await command.RecordAsync("20261006T004052Z"));
        Assert.Equal(2, await command.RecordAsync("20261006T004053Z"));
        Assert.Equal(2, await command.RecordAsync("yesterday"));
        Assert.Equal(2, Rows().Count);
        Assert.Equal(logged, Logged());

        var refusals = said.ToString();

        Assert.Contains("run 20261005T211903Z states no answer, being a run made before runs stated one or the pullback's base, which searches nothing; run its sweep again from this build; nothing was recorded", refusals, StringComparison.Ordinal);
        Assert.Contains("run 20261006T004050Z names the family 'leader', which no card draws", refusals, StringComparison.Ordinal);
        Assert.Contains("run 20261006T004051Z names the index 'NDX', which no page reads", refusals, StringComparison.Ordinal);
        Assert.Contains("run 20261006T004052Z's answer cannot be read", refusals, StringComparison.Ordinal);
        Assert.Contains("the sweep's folder holds no run 20261006T004053Z", refusals, StringComparison.Ordinal);
        Assert.Contains("'yesterday' is not a run's name", refusals, StringComparison.Ordinal);
    }
}
