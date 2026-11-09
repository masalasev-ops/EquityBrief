using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Reading;

// 23.8's row of the long history on the Loop page: each family's newest search over the long history and each price family's
// newest buy and sell points on the index, a family's newest by the order written, read back over a constructed store, and
// the line drawn where neither is stored.
// see: Every price family's traded plan's buy and sell points are tested over the long history and one combination is recommended a family an index
public partial class ReadSurface
{
    internal static string[] LongSearchPageRows => [CheckReach.Key(Scope.LoopPage, "The long history")];

    [Fact]
    public async Task TheLoopPageDrawsEachFamilysNewestLongSearchAndPlanPoints()
    {
        using var store = new TemporaryStore().Migrated();

        // Two breakout searches on the S&P 500, the later naming a setting and passing; one on the S&P 400, which the S&P 500's
        // page does not draw; and the pullback's buy and sell points beside the traded plan's.
        store.Execute(@"
            INSERT INTO long_search (run_id, index_code, family, through, first_test_year, words, current_words, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, stable_folds, searches, passed, years, fund_units, fund_edge, current_fund_edge) VALUES
                ('r1', 'GSPC', 'breakout', '2026-11-06', 2008, NULL, 'rule today', 100, 20, 0.5, 0, 0, 10, 4, 0.1, 0, 0, 7, 0, '[]', 0, NULL, NULL),
                ('r2', 'GSPC', 'breakout', '2026-11-06', 2008, 'a wider stop', 'rule today', 1619, 60, 0.0123, 1, 1, 14, 9, 2.5, 1, 12, 7, 1, '[]', 1600, 0.4, 0.1),
                ('r3', 'MID', 'drift', '2026-11-06', 2016, 'other', 'rule today', 50, 10, 0.9, 0, 0, 5, 1, 0.1, 0, 0, 4, 0, '[]', 0, NULL, NULL);
            INSERT INTO plan_point (run_id, index_code, family, through, setups, combination, picks, fills, fill_rate, hit_rate, break_even, result_per_pick, edge_per_pick, edge_per_fill, days_held, worst_drawdown, traded_fills, traded_edge_per_pick, later_fills, later_edge_per_pick, traded_later_edge_per_pick, fund_edge_per_pick, traded_fund_edge_per_pick, later_fund_edge_per_pick, traded_later_fund_edge_per_pick) VALUES
                ('p1', 'GSPC', 'pullback', '2026-11-06', 3202, 'the second zone within 20 sessions, the rule''s stop, the time limit alone', 3185, 2048, 0.643, 0.5, 0.4, 0.3, 0.292, 0.45, 30.5, 12.0, 3162, -0.135, 900, 0.05, -0.02, 0.15, -0.1, -0.03, -0.2);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop"));

        string Row(string table, string family) => Regex.Replace(
            Regex.Match(Regex.Match(page, $"<table[^>]*data-table=\"{table}\".*?</table>", RegexOptions.Singleline).Value, $"<tr data-family=\"{family}\"[^>]*>(.*?)</tr>", RegexOptions.Singleline).Groups[1].Value,
            "<[^>]+>",
            "|");

        Assert.Contains("data-card=\"loop-long\"", page, StringComparison.Ordinal);
        Assert.Equal("|Breakout||2008||a wider stop||against rule today|||1,619||About 1 in 81||9 of 14||+0.400||today +0.100|||yes|", Row("long-search", "breakout"));
        Assert.Equal(string.Empty, Row("long-search", "drift"));
        // The page reads every stored word through the reader's phrases, sessions among them.
        Assert.Equal("|Pullback||the second zone within 20 trading days, the rule's stop, the time limit alone||2,048||64%||+0.292||-0.135||+0.050||-0.020||+0.150||from 2019 -0.030||", Row("plan-points", "pullback"));
    }

    [Fact]
    public async Task TheLoopPageSaysWhereNoLongSearchIsStored()
    {
        using var store = new TemporaryStore().Migrated();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop"));

        Assert.Contains("data-long-search=\"none\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-table=\"long-search\"", page, StringComparison.Ordinal);
    }
}
