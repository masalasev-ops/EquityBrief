using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Cards;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.4: paper trading over the fixture's whole night, read off the page with an account's settings
// kept, a family at a time. The night lists nothing, so each family's one account, its page's list, holds no trade: its
// tile counts none and has no day to count from, its row among its family's accounts reads not tested yet with too few of
// its count and no variant beside it, its scorecard holds no test year and its record since it went live no trade, no
// account has a session to draw, and the operator's own trades say none is taken.
// see: A rule's scorecard draws its test years and its record since it went live as two halves, and never adds them together
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheFixturesPaperAccountsEachHoldNoTradeAndReadNotTestedYetAndTooFewOfTheirCount()
    {
        var expected = Expected("paper-accounts");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Equal(["gate_result"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!));

        var night = expected.GetProperty("paperNight").GetString()!;
        var verdict = expected.GetProperty("headVerdict").GetString()!;
        var accounts = expected.GetProperty("paperAccounts").EnumerateArray()
            .Select(account => (
                Family: account.GetProperty("family").GetString()!,
                Key: account.GetProperty("key").GetString()!,
                Standing: account.GetProperty("standing").GetString()!,
                TooFew: account.GetProperty("tooFew").GetString()!,
                SinceLive: account.GetProperty("sinceLive").ValueKind == JsonValueKind.Null ? null : account.GetProperty("sinceLive").GetString()))
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

        // The page with an account's settings kept: a tile a family in the one family order with its standing, none
        // holding a return, an open or a closed trade or a day to count from; no session to draw; and no trade of yours.
        AccountFile.Write(store.Root, new AccountSettings(10_000m, 1m, 0.2m));

        using var host = new Reading.ReadSurface.Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper"));
        var tiles = Regex.Matches(page, "<div class=\"paper-tile\" data-family=\"([^\"]+)\" data-standing=\"([^\"]+)\" data-return=\"([^\"]+)\" data-open=\"(\\d+)\" data-closed=\"(\\d+)\" data-since=\"([^\"]+)\"")
            .Select(match => (Family: match.Groups[1].Value, Standing: match.Groups[2].Value, Figures: string.Join("|", match.Groups[3].Value, match.Groups[4].Value, match.Groups[5].Value, match.Groups[6].Value)))
            .ToArray();

        Assert.Contains($"<section class=\"paper\" data-universe=\"500\" data-night=\"{night}\"", page, StringComparison.Ordinal);
        Assert.Equal(accounts.Select(account => (account.Family, account.Standing)), tiles.Select(tile => (tile.Family, tile.Standing)));
        Assert.All(tiles, tile => Assert.Equal("none|0|0|none", tile.Figures));
        Assert.Contains("<p class=\"degraded\" data-paper-chart=\"none\">No account has a session to draw yet.</p>", page, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"degraded\" data-yours=\"{expected.GetProperty("yours").GetString()}\">", page, StringComparison.Ordinal);

        // Each family chosen for its variants and its scorecard: its one account with no trade, the verdict the derivation
        // gives and its record's label, no variant beside it; no test year; and its record since it went live.
        foreach (var (family, key, standing, tooFew, sinceLive) in accounts)
        {
            var chosen = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/paper?variants={family}&rule={family}"));
            var table = Regex.Match(chosen, "<table class=\"paper-variants\".*?</table>", RegexOptions.Singleline).Value;
            var row = Assert.Single(Regex.Matches(table, "<tr data-account=.*?</tr>", RegexOptions.Singleline)).Value;
            var score = Regex.Match(chosen, "<div class=\"score\" .*", RegexOptions.Singleline).Value;
            var since = score[score.IndexOf("<div class=\"score-half live\"", StringComparison.Ordinal)..];

            Assert.StartsWith($"<tr data-account=\"{key}\" data-standing=\"{standing}\" data-trades=\"0\" data-shared=\"0\" data-only=\"0\">", row, StringComparison.Ordinal);
            Assert.Contains($"<span class=\"pv-verdict\" data-verdict=\"{verdict}\">{verdict}</span>", row, StringComparison.Ordinal);
            Assert.Contains($"<span class=\"too-few\">{tooFew}</span>", row, StringComparison.Ordinal);
            Assert.Contains("<p class=\"degraded\" data-variants=\"none\">", chosen, StringComparison.Ordinal);
            Assert.StartsWith($"<div class=\"score\" data-family=\"{family}\" data-standing=\"{standing}\" data-verdict=\"{verdict}\">", score, StringComparison.Ordinal);
            Assert.Contains("<p class=\"degraded\" data-history=\"none\">No monthly test has read this rule on this index yet.</p>", score, StringComparison.Ordinal);
            Assert.Contains(sinceLive is { } said ? $"data-counted=\"0\" data-needed=\"{said.Split(' ')[2]}\">{said}</p>" : $"<p class=\"sh-line\" data-record=\"none\">{MarkRenderer.NoRecordLine}</p>", since, StringComparison.Ordinal);
        }
    }
}
