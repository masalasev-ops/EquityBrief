using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Cards;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.4: paper trading over the fixture's whole night, read off the page with an account's settings
// kept. The night lists nothing, so each family's one account, its page's list, holds no trade, reads not tested yet and
// too few of its count, and no account has a session to draw; the operator's own reads that the test does not read it.
// see: A paper account draws its test years verdict and its record since registration apart, and never merges them
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheFixturesPaperAccountsEachHoldNoTradeAndReadNotTestedYetAndTooFewOfTheirCount()
    {
        var expected = Expected("paper-accounts");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Equal(["gate_result"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!));

        var night = expected.GetProperty("paperNight").GetString()!;
        var accounts = expected.GetProperty("paperAccounts").EnumerateArray()
            .Select(account => (Key: account.GetProperty("key").GetString()!, Standing: account.GetProperty("standing").GetString()!, TooFew: account.GetProperty("tooFew").ValueKind == JsonValueKind.Null ? null : account.GetProperty("tooFew").GetString()))
            .ToArray();

        var (store, code, _, _) = await FixtureReplay.NightAsync();
        using var held = store;

        Assert.Equal(0, code);

        // The derivation's ground: every member's stored gate result on the night fails its trigger, no list or book
        // kept a trade, nothing is registered and no per-rule test has run.
        var triggers = Query(store, $"SELECT gates FROM gate_result WHERE session_date = '{night}';")
            .Select(gates => JsonDocument.Parse(gates).RootElement.GetProperty("gates").EnumerateArray().Single(gate => gate.GetProperty("gate").GetString() == "trigger").GetProperty("passed").GetBoolean())
            .ToArray();

        Assert.Equal(4, triggers.Length);
        Assert.All(triggers, Assert.False);
        Assert.Equal(["0|0|0|0|0"], Query(store, "SELECT (SELECT COUNT(*) FROM index_family_trade) || '|' || (SELECT COUNT(*) FROM heavyweight_holding) || '|' || (SELECT COUNT(*) FROM candidate_register) || '|' || (SELECT COUNT(*) FROM loop_rule) || '|' || (SELECT COUNT(*) FROM taken_trade);"));

        // The page with an account's settings kept: each account in the one family order, its standing, the verdict the
        // derivation gives and its record's label.
        AccountFile.Write(store.Root, new AccountSettings(10_000m, 1m, 0.2m));

        using var host = new Reading.ReadSurface.Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper"));
        var rows = Regex.Matches(page, "<tr data-account=\"([^\"]+)\" data-family=\"[^\"]+\" data-standing=\"([^\"]+)\" data-verdict=\"([^\"]+)\" data-trades=\"(\\d+)\"").Select(match => (Key: match.Groups[1].Value, Standing: match.Groups[2].Value, Verdict: match.Groups[3].Value, Trades: match.Groups[4].Value)).ToArray();

        Assert.Contains($"<section class=\"paper\" data-universe=\"500\" data-night=\"{night}\"", page, StringComparison.Ordinal);
        Assert.Equal(accounts.Select(account => (account.Key, account.Standing)), rows.Select(row => (row.Key, row.Standing)));
        Assert.All(rows, row => Assert.Equal("0", row.Trades));
        Assert.All(rows.SkipLast(1), row => Assert.Equal(expected.GetProperty("headVerdict").GetString(), row.Verdict));
        Assert.Equal(expected.GetProperty("yourVerdict").GetString(), rows[^1].Verdict);

        foreach (var (key, _, tooFew) in accounts.Where(account => account.TooFew is not null))
        {
            var at = page.IndexOf($"<tr data-account=\"{key}\"", StringComparison.Ordinal);

            Assert.Contains($"data-too-few=\"{tooFew}\"", page[at..page.IndexOf("</tr>", at, StringComparison.Ordinal)], StringComparison.Ordinal);
        }

        Assert.Contains("<p class=\"degraded\" data-paper-chart=\"none\">No account has a session to draw yet.</p>", page, StringComparison.Ordinal);
    }
}
