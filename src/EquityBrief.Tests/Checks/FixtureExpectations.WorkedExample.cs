using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Filter;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.8: section 11's worked example. The first three figures are drawn from what the
// code computes over the committed fixture, regenerated here and held to the document word for word, and
// their numbers are held to the fixture's expectations; the last two are illustrative and say so.
public partial class FixtureExpectations
{
    static readonly DateOnly WorkedNight = new(2026, 9, 4);

    // The setting the counts replay an earlier session at: section 17's proposed values with the trade read
    // from the swing trade's own plan, the plan the figures draw.
    const string WorkedSetting = "market 50%, trade from the swing trade";

    internal sealed record WorkedFigures(string Funnel, string Name, string Resolution, NameFigure NameData, ResolutionFigure ResolutionData, IReadOnlyList<(string Gate, int Passed)> Steps, int Members);

    internal static async Task<WorkedFigures> WorkedFiguresAsync()
    {
        using var store = await WithTwoNights();
        var night = WorkedNight.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Figure 11.1: the night's rows through the gates in order.
        var flags = Query(store, $"SELECT market, trend, setup, trigger_pass, trade, passed FROM gate_result WHERE session_date = '{night}' ORDER BY ticker;")
            .Select(row => row.Split('|').Select(flag => flag == "1").ToArray())
            .ToArray();

        var steps = SwingGates.Order
            .Select((gate, at) => (gate, flags.Count(row => row.Take(at + 1).All(held => held))))
            .Append(("not excluded", flags.Count(row => row[5])))
            .ToArray();

        // Figure 11.2: the member passing the most gates, the first by ticker among equals.
        var rows = Query(store, $"SELECT ticker, gates, swing_entry, swing_stop, swing_target, swing_reward_to_risk FROM gate_result WHERE session_date = '{night}' ORDER BY ticker;")
            .Select(row => row.Split('|'))
            .Select(row =>
            {
                using var document = JsonDocument.Parse(row[1]);

                var gates = document.RootElement.GetProperty("gates").EnumerateArray()
                    .Select(gate => (Gate: gate.GetProperty("gate").GetString()!, Passed: gate.GetProperty("passed").GetBoolean(), Values: gate.GetProperty("values").EnumerateObject().ToDictionary(value => value.Name, value => value.Value.GetString()!)))
                    .ToArray();

                return (Ticker: row[0], Gates: gates, Entry: row[2], Stop: row[3], Target: row[4], Ratio: row[5]);
            })
            .ToArray();

        var chosen = rows.OrderByDescending(row => row.Gates.Count(gate => gate.Passed)).ThenBy(row => row.Ticker, StringComparer.Ordinal).First();
        var setup = chosen.Gates.Single(gate => gate.Gate == SwingGates.Setup).Values;
        var reading = Query(store, $"SELECT recent_high, depth FROM swing_reading WHERE ticker = '{chosen.Ticker}' AND session_date = '{night}';").Single().Split('|');
        var closes = Query(store, $"SELECT session_date, close FROM bar WHERE ticker = '{chosen.Ticker}' AND session_date <= '{night}' ORDER BY session_date DESC LIMIT 40;")
            .Select(row => row.Split('|'))
            .Select(row => (Session: DateOnly.ParseExact(row[0], "yyyy-MM-dd", CultureInfo.InvariantCulture), Close: decimal.Parse(row[1], CultureInfo.InvariantCulture)))
            .Reverse()
            .ToArray();

        var name = new NameFigure(
            chosen.Ticker,
            WorkedNight,
            closes,
            decimal.Parse(setup["band low"], CultureInfo.InvariantCulture),
            decimal.Parse(setup["band high"], CultureInfo.InvariantCulture),
            decimal.Parse(reading[0], CultureInfo.InvariantCulture),
            double.Parse(reading[1], CultureInfo.InvariantCulture),
            decimal.Parse(chosen.Entry, CultureInfo.InvariantCulture),
            decimal.Parse(chosen.Stop, CultureInfo.InvariantCulture),
            decimal.Parse(chosen.Target, CultureInfo.InvariantCulture),
            decimal.Parse(chosen.Ratio, CultureInfo.InvariantCulture),
            [.. chosen.Gates.Select(gate => (gate.Gate, gate.Passed))]);

        // Figure 11.3: the counts replay every session of the stored year; the latest at least twenty sessions
        // before the night at which a member stood at a setup, passing the setup gate, and its swing plan
        // resolves on the closes the store holds after it, so the figure draws a setup's plan and the path it
        // took, the member passing the most gates among equals and then the first by ticker. The setup gate
        // rather than the trade gate, because the figure shows how a setup is scored on closes, and the
        // fixture's year holds no plan past the trade gate far enough before its night to resolve.
        var replayed = new List<(DateOnly Session, GateResult Result)>();
        var twentyBefore = Query(store, $"SELECT DISTINCT session_date FROM bar WHERE session_date <= '{night}' ORDER BY session_date DESC LIMIT 1 OFFSET 20;").Single();
        var cutoff = DateOnly.ParseExact(twentyBefore, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        await new FilterCounts(store.DatabaseFile).CountAsync(
            Index,
            year: true,
            evaluated: (session, setting, results) =>
            {
                if (setting == WorkedSetting)
                {
                    replayed.AddRange(results
                        .Where(result => result.SwingTrade is { Entry: not null, Stop: not null, Target: not null } && result.Gates.Single(gate => gate.Name == SwingGates.Setup).Passed)
                        .Select(result => (session, result)));
                }
            });

        ResolutionFigure? resolved = null;

        foreach (var (session, result) in replayed.Where(pair => pair.Session <= cutoff).OrderByDescending(pair => pair.Session).ThenByDescending(pair => pair.Result.Gates.Count(gate => gate.Passed)).ThenBy(pair => pair.Result.Ticker, StringComparer.Ordinal))
        {
            var after = Query(store, $"SELECT session_date, close FROM bar WHERE ticker = '{result.Ticker}' AND session_date > '{session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}' ORDER BY session_date LIMIT {ForwardReturnSeries.SetupSessionCap};")
                .Select(row => row.Split('|'))
                .Select(row => new ReturnBar(DateOnly.ParseExact(row[0], "yyyy-MM-dd", CultureInfo.InvariantCulture), decimal.Parse(row[1], CultureInfo.InvariantCulture)))
                .ToArray();

            var plan = result.SwingTrade;

            if (plan.Stop >= plan.Target)
            {
                continue;
            }

            var outcome = ForwardReturnSeries.OverSetup(after, plan.Stop, plan.Target, null, plan.Entry, plan.Entry, ForwardReturnSeries.Swing, ForwardReturnSeries.SetupSessionCap);

            if (outcome.Outcome is ForwardReturnSeries.Win or ForwardReturnSeries.Loss && outcome.ResolvedOn is { } on)
            {
                var drawn = after.TakeWhile(bar => bar.SessionDate <= on).Concat(after.SkipWhile(bar => bar.SessionDate <= on).Take(5)).ToArray();

                resolved = new ResolutionFigure(
                    result.Ticker,
                    session,
                    [(session, plan.Entry!.Value), .. drawn.Select(bar => (bar.SessionDate, bar.Close))],
                    plan.Entry!.Value,
                    plan.Stop!.Value,
                    plan.Target!.Value,
                    outcome.Outcome,
                    on);

                break;
            }
        }

        Assert.NotNull(resolved);

        return new WorkedFigures(
            WorkedExample.Funnel(WorkedNight, flags.Length, steps),
            WorkedExample.Name(name),
            WorkedExample.Resolution(resolved),
            name,
            resolved,
            steps,
            flags.Length);
    }

    [Fact]
    public async Task TheWorkedExamplesFirstThreeFiguresAreTheFixtureAsTheCodeComputesItAndTheLastTwoSayTheyAreIllustrative()
    {
        var figures = await WorkedFiguresAsync();
        var drawn = new[] { figures.Funnel, figures.Name, figures.Resolution, WorkedExample.Clocks(), WorkedExample.NearMisses() };

        // Each figure as the code draws it, written beside the phase report so a figure the document has fallen
        // behind can be placed from what the code computes rather than typed.
        var folder = Path.Combine(Repository.Root, "artifacts", "worked-example");

        Directory.CreateDirectory(folder);

        for (var at = 0; at < drawn.Length; at++)
        {
            File.WriteAllText(Path.Combine(folder, $"figure-11-{at + 1}.html"), drawn[at]);
        }

        var architecture = File.ReadAllText(Repository.Architecture).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.All(drawn, figure => Assert.Contains(figure, architecture, StringComparison.Ordinal));

        // The numbers, held to the fixture's expectation of the night's gates worked by hand: the funnel's
        // counts through each gate in order, the member drawn being the one passing the most, and its plan.
        var expected = Expected("gate-results").GetProperty("nights").GetProperty("2026-09-04");
        var members = expected.EnumerateObject().Select(member => (Ticker: member.Name, Gates: SwingGates.Order.Select(gate => member.Value.GetProperty("gates").GetProperty(gate).GetBoolean()).ToArray(), Member: member.Value)).ToArray();

        Assert.Equal(members.Length, figures.Members);
        Assert.Equal(
            [.. SwingGates.Order.Select((gate, at) => members.Count(member => member.Gates.Take(at + 1).All(held => held)))],
            figures.Steps.Take(SwingGates.Order.Length).Select(step => step.Passed));
        Assert.Equal(members.Count(member => member.Member.GetProperty("passed").GetBoolean()), figures.Steps[^1].Passed);

        var most = members.OrderByDescending(member => member.Gates.Count(held => held)).ThenBy(member => member.Ticker, StringComparer.Ordinal).First();

        Assert.Equal(most.Ticker, figures.NameData.Ticker);
        Assert.Equal(most.Gates, figures.NameData.Gates.Select(gate => gate.Passed));
        Assert.Equal(most.Member.GetProperty("swingStop").GetString(), figures.NameData.Stop.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(most.Member.GetProperty("swingTarget").GetString(), figures.NameData.Target.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(most.Member.GetProperty("swingRewardToRisk").GetString(), figures.NameData.RewardToRisk.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(most.Member.GetProperty("worked").GetProperty("depth").GetDouble(), figures.NameData.Depth, 9);

        // The replayed setup, held to the expectation the fixture states for it, and its outcome walked here close
        // by close over the captured bars themselves rather than over the store the replay read.
        var example = Expected("gate-results").GetProperty("workedExample");
        var setup = figures.ResolutionData;

        Assert.Equal(
            (example.GetProperty("ticker").GetString(), example.GetProperty("session").GetString(), example.GetProperty("entry").GetString(), example.GetProperty("stop").GetString(), example.GetProperty("target").GetString(), example.GetProperty("outcome").GetString(), example.GetProperty("resolvedOn").GetString()),
            (setup.Ticker, setup.Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), setup.Entry.ToString(CultureInfo.InvariantCulture), setup.Stop.ToString(CultureInfo.InvariantCulture), setup.Target.ToString(CultureInfo.InvariantCulture), setup.Outcome, setup.ResolvedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        var captured = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), $"bars-{setup.Ticker}.json"))).RootElement.EnumerateArray()
            .Select(bar => (Session: DateOnly.ParseExact(bar.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture), Close: bar.GetProperty("adjusted_close").GetDecimal()))
            .Where(bar => bar.Session > setup.Session)
            .OrderBy(bar => bar.Session)
            .ToArray();

        var decided = captured.First(bar => bar.Close < setup.Stop || bar.Close >= setup.Target);

        Assert.Equal((decided.Close >= setup.Target ? "win" : "loss", decided.Session), (setup.Outcome, setup.ResolvedOn));

        // The two illustrative figures say so in their drawing, their caption and their key.
        foreach (var figure in drawn.Skip(3))
        {
            Assert.Contains("Illustrative", figure, StringComparison.Ordinal);
            Assert.Contains("illustrative.</figcaption>", figure, StringComparison.Ordinal);
            Assert.Contains("<p><b>Key.</b> Illustrative", figure, StringComparison.Ordinal);
        }

        // And the first three name the fixture's date in their keys.
        Assert.All(drawn.Take(3), figure => Assert.Matches("<p><b>Key\\.</b> [^<]*committed fixture[^<]*20[0-9]{2}-[0-9]{2}-[0-9]{2}", figure));
    }
}
