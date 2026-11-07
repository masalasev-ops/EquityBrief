using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.History;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Reading;

// 23.3's History page: each index's members back to 2005 read back over a constructed store, by how its holdings were matched
// or why left out, the lower coverage label on the S&P 400's and 600's years to 2008 alone, and the companies left out by name.
// see: The S&P 400's and 600's figures for 2004 to 2008 carry the lower coverage label
public partial class ReadSurface
{
    internal static string[] LongMembershipPageRows => [CheckReach.Key("15.25 History", "Members back to 2005")];

    [Fact]
    public async Task TheHistoryPageDrawsEachIndexsMembersBackTo2005ByHowTheyWereMatchedOrWhyLeftOut()
    {
        using var store = new TemporaryStore().Migrated();

        using (var host = new Host(store.Root))
        {
            using var client = host.CreateClient();
            var empty = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/history"));

            Assert.Contains("The membership back to 2005 has not been written.", empty, StringComparison.Ordinal);
        }

        // The S&P 500 in 2008 at one quarter end: three holdings, two matched, one of them by hand, and Countrywide left out as
        // bought out at 200 of the 400 held. The S&P 400 in 2008 and 2009: a holding matched and one failed, 50 of 150.
        using (var connection = store.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO long_snapshot (index_code, period, accession, form, holdings, value_usd, run_id) VALUES
                    ('GSPC', '2008-06-30', 'a', 'N-CSRS', 3, '400', 'long-members-1'),
                    ('MID', '2008-12-31', 'b', 'N-Q', 2, '150', 'long-members-1'),
                    ('MID', '2009-03-31', 'c', 'N-CSR', 1, '100', 'long-members-1');
                INSERT INTO long_holding (index_code, period, holding, name, industry, shares, value_usd, ticker, matched_by, stood_between, checked, left_as, left_read, run_id) VALUES
                    ('GSPC', '2008-06-30', 0, 'Alpha Co.', 'Things', '10', '100', 'ALPH', 'name', '[""ALPH""]', NULL, NULL, NULL, 'long-members-1'),
                    ('GSPC', '2008-06-30', 1, 'Beta Co.', 'Things', '10', '100', 'BETA', 'hand', '[""BETA""]', 'matched by hand: constructed', NULL, NULL, 'long-members-1'),
                    ('GSPC', '2008-06-30', 2, 'Countrywide Financial Corp.', 'Things', '10', '200', NULL, NULL, NULL, NULL, 'bought out', 'named by the operator', 'long-members-1'),
                    ('MID', '2008-12-31', 0, 'Gamma Co.', 'Things', '10', '100', 'GAMA', 'price', '[""GAMA""]', NULL, NULL, NULL, 'long-members-1'),
                    ('MID', '2008-12-31', 1, 'Delta Co.', 'Things', '10', '50', NULL, NULL, NULL, NULL, 'failed', 'an 8-K carrying item 1.03', 'long-members-1'),
                    ('MID', '2009-03-31', 0, 'Gamma Co.', 'Things', '10', '100', 'GAMA', 'price', '[""GAMA""]', NULL, NULL, NULL, 'long-members-1');";
            command.ExecuteNonQuery();
        }

        using var written = new Host(store.Root);
        using var reader = written.CreateClient();
        var page = WebUtility.HtmlDecode(await reader.GetStringAsync("/screens/history"));

        Assert.Contains("data-card=\"long-membership\"", page, StringComparison.Ordinal);
        Assert.Contains("long-members-1", page, StringComparison.Ordinal);

        string Row(string index, int year) =>
            Regex.Match(page, $"<table[^>]*data-index=\"{index}\".*?</table>", RegexOptions.Singleline) is { Success: true } table
            && Regex.Match(table.Value, $"<tr data-year=\"{year}\".*?</tr>", RegexOptions.Singleline) is { Success: true } row
                ? row.Value
                : string.Empty;

        static string[] Numbers(string row) => [.. Regex.Matches(row, "<td class=\"n\">([^<]*)</td>").Select(match => match.Groups[1].Value)];

        // The S&P 500's 2008: one quarter end, three holdings, two matched, one by hand, one left out, bought out half the value.
        Assert.Equal(["1", "3", "2", "1", "1", "50.00 %", "0.00 %", "0.00 %", "0.00 %"], Numbers(Row("GSPC", 2008)));
        Assert.DoesNotContain(LongFloors.LowerLabel, Row("GSPC", 2008), StringComparison.Ordinal);

        // The S&P 400's 2008 carries the lower coverage label and 2009 does not: 50 of 150 failed in 2008.
        Assert.Equal(["1", "2", "1", "0", "1", "0.00 %", "33.33 %", "0.00 %", "0.00 %"], Numbers(Row("MID", 2008)));
        Assert.Contains(LongFloors.LowerLabel, Row("MID", 2008), StringComparison.Ordinal);
        Assert.Contains("data-lower=\"1\"", Row("MID", 2008), StringComparison.Ordinal);
        Assert.DoesNotContain(LongFloors.LowerLabel, Row("MID", 2009), StringComparison.Ordinal);

        // The companies left out by name, with how each is named, and no table for an index the run wrote nothing of.
        Assert.Contains("Left out by name: Countrywide Financial Corp., S&P 500, bought out.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-index=\"SML\"", page, StringComparison.Ordinal);
    }
}
