using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The reading recipes on the Improvement loop page, read back over a constructed store: every recipe newest first, the one a
// run worked on the index with its figures, stocks, span and checked sessions, one waiting for data the store lacks and one
// not worked yet; each piece of data asked for with who asked, the day and the line, said to block nothing; another index's
// page drawing none worked; and a store holding none saying so.
// see: A reading recipe is a closed set of operations over stored fields and is kept apart from the pinned catalogue
public partial class ReadSurface
{
    internal static string[] RecipeRows =>
    [
        CheckReach.Key("15.20 The Loop page", "Reading recipes"),
    ];

    [Fact]
    public async Task TheLoopPageDrawsTheReadingRecipesNewestFirstWithTheirRunsAndTheDataTheStoreLacks()
    {
        using var store = new TemporaryStore().Migrated();
        var industry = Recipes.IndustryAsItStood.Replace("'", "''", StringComparison.Ordinal);

        store.Execute(
            "INSERT INTO recipe (id, line, words, source, entered_at, run_id) VALUES "
            + "(1, 'change(rsi,5)', 'rsi''s change over a week', 'the operator', '2026-11-27T15:00:00Z', 'recipe-a'), "
            + "(2, 'rank(rsi,industry)', 'rsi against its industry', 'the researcher', '2026-11-28T10:00:00Z', 'recipe-b'), "
            + "(3, 'average(volume_ratio,21)', 'volume over a month', 'the operator', '2026-11-29T10:00:00Z', 'recipe-c');");
        store.Execute("INSERT INTO recipe_run (recipe_id, index_code, figures, members, first_session, last_session, checked, ran_at, run_id) VALUES (1, 'SML', 12345, 598, '2005-01-03', '2026-11-25', 20, '2026-11-29T12:00:00Z', 'recipe-SML-x');");
        store.Execute(
            "INSERT INTO data_request (id, need, line, words, source, entered_at, run_id) VALUES "
            + "(1, 'the field days_to_cover', 'change(days_to_cover, 21)', 'short interest', 'the researcher', '2026-11-28T09:00:00Z', 'recipe-d'), "
            + $"(2, '{industry}', 'rank(rsi,industry)', 'rsi against its industry', 'the researcher', '2026-11-28T10:00:00Z', 'recipe-b');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var small = Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=600")), "<section class=\"card\" data-card=\"loop-recipes\">.*?</section>").Single();

        Assert.Contains("<h2>New readings on test</h2>", small, StringComparison.Ordinal);
        Assert.Contains("<p data-summary data-recipes=\"count\">3 recipes, newest first; 1 worked on the S&P 600.</p>", small, StringComparison.Ordinal);
        Assert.Equal(["3", "2", "1"], [.. Regex.Matches(small, "<tr data-recipe=\"(\\d+)\">").Select(match => match.Groups[1].Value)]);
        Assert.Contains("<td data-h=\"The recipe\">volume over a month<code class=\"idea-line\">average(volume_ratio,21)</code></td><td data-h=\"On this index\">" + SinglePageApp.RecipesNotRun + "</td>", small, StringComparison.Ordinal);
        Assert.Contains("<td data-h=\"From\">The researcher</td><td data-h=\"The recipe\">rsi against its industry<code class=\"idea-line\">rank(rsi,industry)</code></td><td data-h=\"On this index\">waits for " + Recipes.IndustryAsItStood + "</td>", small, StringComparison.Ordinal);
        Assert.Contains("<td data-h=\"Entered\" class=\"num\">27 Nov 2026</td><td data-h=\"From\">You</td><td data-h=\"The recipe\">rsi's change over a week<code class=\"idea-line\">change(rsi,5)</code></td><td data-h=\"On this index\">12,345 figures of 598 stocks, 3 Jan 2005 to 25 Nov 2026; checked on 20 sessions to read nothing later</td>", small, StringComparison.Ordinal);

        // The data the store lacks, newest first, each with who asked and the line, said to block nothing.
        Assert.Contains("<h4>Data the store lacks</h4><p class=\"lm-small\">" + SinglePageApp.RequestsLede + "</p><ol class=\"data-requests\" data-requests=\"2\">", small, StringComparison.Ordinal);
        Assert.Contains("<li><b>Each member's industry as it stood on each session</b>, asked by the researcher on 28 Nov 2026 for <code class=\"idea-line\">rank(rsi,industry)</code></li><li><b>The field days_to_cover</b>, asked by the researcher on 28 Nov 2026 for <code class=\"idea-line\">change(days_to_cover, 21)</code></li>", small, StringComparison.Ordinal);

        // The S&P 500's page draws the same recipes, none worked there.
        var large = Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=500")), "<section class=\"card\" data-card=\"loop-recipes\">.*?</section>").Single();

        Assert.Contains("3 recipes, newest first; 0 worked on the S&P 500.", large, StringComparison.Ordinal);
        Assert.Contains("<code class=\"idea-line\">change(rsi,5)</code></td><td data-h=\"On this index\">" + SinglePageApp.RecipesNotRun + "</td>", large, StringComparison.Ordinal);

        using var empty = new TemporaryStore().Migrated();
        using var emptyHost = new Host(empty.Root);
        using var emptyClient = emptyHost.CreateClient();

        Assert.Contains("No recipe has been entered yet.", WebUtility.HtmlDecode(await emptyClient.GetStringAsync("/screens/loop?universe=400")), StringComparison.Ordinal);
    }
}
