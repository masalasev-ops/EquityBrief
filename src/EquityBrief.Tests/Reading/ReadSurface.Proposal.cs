using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Reading;

// read-surface, 12.4: the run page's shape proposal, each figure read back off the page, and the blocks
// it draws beside the proposal being the count the shape command holds a later acceptance to.
public partial class ReadSurface
{
    [Fact]
    public async Task TheProposalIsDrawnGateByGateWithItsFindingAndWhatAcceptingItRestarts()
    {
        using var store = new TemporaryStore().Migrated();

        // The proposal the known nights give, stored as the proposer stores it: trend and strength 2/3 to
        // 0.46 with its median 0 to 40, the setup 1 to 1.05 with 10 to 15, the trigger's 6 drawn with no
        // threshold, the trade's 0 with none bringing it inside and the finding naming it, the list 0.
        var proposal = ShapeProposals.Propose([.. Enumerable.Range(0, 60).Select(FixtureExpectations.KnownNight)], FilterSettings.Proposed);

        store.Execute(
            "INSERT INTO shape_proposal (proposed_at, session_date, version, ordinary, current_settings, settings, levers, list_now, list_proposed, findings) " +
            $"VALUES ('2026-03-31T22:00:00Z', '2026-03-31', 'none', 60, '{FilterSettings.Proposed.Write()}', '{proposal.Settings.Write()}', '{JsonSerializer.Serialize(proposal.Levers).Replace("'", "''", StringComparison.Ordinal)}', 0, 0, '{JsonSerializer.Serialize(proposal.Findings).Replace("'", "''", StringComparison.Ordinal)}');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-03-31"));
        var region = Regex.Match(page, "<section class=\"shape-proposal\">(.*?)</section>", RegexOptions.Singleline).Groups[1].Value;

        Assert.Contains("<p class=\"proposal\" data-proposal=\"1\" data-version=\"none\" data-ordinary=\"60\" data-decision=\"none\">Proposal 1, written on the night of 2026-03-31 over 60 ordinary nights under filter version none, is waiting on your decision: shape --accept 1 opens it as the next filter version, and shape --reject 1 --reason records why not.</p>", region, StringComparison.Ordinal);
        Assert.Contains("<td>trend and strength</td><td>strengthFloor</td><td class=\"num\">0.67</td><td class=\"num\">0.46</td><td class=\"num\">0</td><td class=\"num\">40</td><td class=\"num\">38 to 347</td>", region, StringComparison.Ordinal);
        Assert.Contains("<td>setup</td><td>dryUpCeiling</td><td class=\"num\">1.00</td><td class=\"num\">1.05</td><td class=\"num\">10</td><td class=\"num\">15</td><td class=\"num\">14 to 126</td>", region, StringComparison.Ordinal);
        Assert.Contains("<td>trigger</td><td>no threshold</td><td></td><td></td><td class=\"num\">6</td><td class=\"num\">6</td><td class=\"num\">6 to 56</td>", region, StringComparison.Ordinal);
        Assert.Contains("<td>trade</td><td>rewardToRiskFloor</td><td class=\"num\">2.00</td><td class=\"num\">none brings it inside</td><td class=\"num\">0</td><td class=\"num\">0</td><td class=\"num\">1 to 12</td>", region, StringComparison.Ordinal);
        Assert.Contains("<td>the list</td><td></td><td></td><td></td><td class=\"num\">0</td><td class=\"num\">0</td><td class=\"num\">1 to 9</td>", region, StringComparison.Ordinal);
        Assert.Contains("<ul class=\"proposal-findings\" data-findings=\"1\"><li>no rewardToRiskFloor between 1.00 and 4.00 brings the trade's median inside 1 to 12, and it stays 0</li></ul>", region, StringComparison.Ordinal);
        Assert.Contains("<p class=\"restarts\" data-live=\"none\" data-accepted-while-live=\"0\" data-blocks=\"0\">No live filter candidate is registered, so accepting restarts nothing.</p>", region, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALaterAcceptanceWhileTheListIsLiveIsHeldToTheBlocksThePageDrawsBesideTheProposal()
    {
        using var store = new TemporaryStore().Migrated();

        // Acceptance zero before the family registers, the live candidate registered, and the first
        // acceptance while it stands, which retires it for version 2 of the live filter and states nothing.
        Assert.Equal(0, (await FixtureExpectations.ShapeVerb(store, new DateTimeOffset(2025, 1, 1, 22, 0, 0, TimeSpan.Zero), "--settings", "strengthFloor=0.6", "--evidence", "the ruling")).Code);
        await FixtureExpectations.RegisterLive(store, new DateTimeOffset(2025, 1, 2, 22, 0, 0, TimeSpan.Zero));
        Assert.Equal(0, (await FixtureExpectations.ShapeVerb(store, new DateTimeOffset(2025, 3, 3, 22, 0, 0, TimeSpan.Zero), "--settings", "dryUpCeiling=0.85", "--evidence", "the first live trigger")).Code);

        var live = SwingFamily.LiveCandidate("2");

        // The live candidate's nights. It is first evaluated on 2025-03-04, which is session 0 of its
        // blocks; it fires on 03-10, session 4, in block 0, and on 03-11, whose setup has no outcome yet;
        // on 06-16, session 72, in block 1; and on 10-15, session 156, in block 2. The newest night is
        // 2025-12-01, session 208: block 1 closes once 188 sessions have traded, and block 2 not before
        // 251, so worked by hand the clock has run 2 non-empty blocks.
        (string Night, bool Fired, string? Outcome)[] nights =
        [
            ("2025-03-04", false, null),
            ("2025-03-10", true, ForwardReturnSeries.Win),
            ("2025-03-11", true, null),
            ("2025-06-16", true, ForwardReturnSeries.Loss),
            ("2025-10-15", true, ForwardReturnSeries.Win),
            ("2025-12-01", false, null),
        ];

        foreach (var (night, fired, outcome) in nights)
        {
            store.Execute(
                "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) " +
                $"VALUES ('AAA', '{night}', '{CurrentReasons(false)}', 0, '{{}}', '{{\"candidates\":[{{\"candidate\":\"{live}\",\"fired\":{(fired ? "true" : "false")}}}],\"skipped\":[]}}');");

            if (outcome is not null)
            {
                store.Execute(
                    "INSERT INTO forward_return (ticker, session_date, horizon, outcome, return_pct, null_win, null_win_at_sensitivity, break_even, planned_risk, on_earnings) " +
                    $"VALUES ('AAA', '{night}', '{ForwardReturnSeries.Setup}', '{outcome}', 1.0, 0.4, 0.38, 0.35, 1.0, 0);");
            }
        }

        FixtureExpectations.StoreProposal(store, "2", FilterSettings.Proposed with { StrengthFloor = 0.6, DryUpCeiling = 0.85, RewardToRiskFloor = 1.5 });

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2025-12-01"));
        var restarts = Regex.Match(page, "<p class=\"restarts\" data-live=\"([^\"]*)\" data-accepted-while-live=\"(\\d+)\" data-blocks=\"(\\d+)\">([^<]*)</p>");

        Assert.True(restarts.Success);
        Assert.Equal((live, "1", "2"), (restarts.Groups[1].Value, restarts.Groups[2].Value, restarts.Groups[3].Value));
        Assert.Equal(
            $"Accepting it restarts the 2 non-empty block(s) '{live}' has run, of the {EquityBrief.Core.Returns.Blocks.Floor} its first look reads. Shape is frozen after one acceptance while the list is live, so the command states them: shape --accept 1 --restarts 2.",
            restarts.Groups[4].Value);

        var drawn = restarts.Groups[3].Value;
        var at = new DateTimeOffset(2025, 12, 2, 22, 0, 0, TimeSpan.Zero);
        var before = FixtureExpectations.ShapeTables(store);

        // Without the count, and at a count other than the page's, the acceptance is refused and nothing changes.
        var (unstated, saidUnstated) = await FixtureExpectations.ShapeVerb(store, at, "--accept", "1");

        Assert.Equal(1, unstated);
        Assert.Contains("--restarts 2", saidUnstated, StringComparison.Ordinal);
        Assert.Equal(1, (await FixtureExpectations.ShapeVerb(store, at.AddMinutes(1), "--accept", "1", "--restarts", "3")).Code);
        Assert.Equal(1, (await FixtureExpectations.ShapeVerb(store, at.AddMinutes(2), "--accept", "1", "--restarts", "1")).Code);
        Assert.Equal(before, FixtureExpectations.ShapeTables(store));

        // At the count read off the page it is accepted, and the live candidate restarts.
        var (accepted, said) = await FixtureExpectations.ShapeVerb(store, at.AddMinutes(3), "--accept", "1", "--restarts", drawn);

        Assert.Equal(0, accepted);
        Assert.EndsWith("restarting 2 non-empty block(s)" + Environment.NewLine, said, StringComparison.Ordinal);

        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json_array((SELECT version FROM filter_version WHERE closed_at IS NULL), (SELECT candidate FROM candidate_register ORDER BY id DESC LIMIT 1), (SELECT retires FROM candidate_register ORDER BY id DESC LIMIT 1 OFFSET 1));";

        Assert.Equal($"[\"3\",\"{SwingFamily.LiveCandidate("3")}\",\"{live}\"]", (string)command.ExecuteScalar()!);
    }
}
