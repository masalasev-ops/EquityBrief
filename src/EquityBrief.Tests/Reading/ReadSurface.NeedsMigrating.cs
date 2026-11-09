using System.Net;
using EquityBrief.Data.Migrations;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The 23.7 correction: every page over a store behind the checkout opens with "The store needs migrating." as a banner, and
// the merge step runs the migrations it carries: `tools/merge` merges, fast-forwards main and migrates, in that order, and
// its Windows wrapper hands the work to it.
// see: A merge carrying a migration applies it in the merge step, and every page over a store behind says it needs migrating
public partial class ReadSurface
{
    [Fact]
    public async Task EveryPageOverAStoreBehindSaysTheStoreNeedsMigrating()
    {
        using var store = new TemporaryStore();

        new MigrationRunner([.. SchemaMigrations.All.Where(migration => migration.Version <= 23)]).Apply(store.DatabaseFile);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        foreach (var route in new[] { "/screens/tonight", "/screens/history", "/screens/loop", "/screens/home", "/screens/marking" })
        {
            var body = WebUtility.HtmlDecode(await client.GetStringAsync(route));

            Assert.Contains("<p class=\"banner\" role=\"status\" data-schema=\"23\"", body, StringComparison.Ordinal);
            Assert.Contains("<b>" + SinglePageApp.NeedsMigrating + "</b>", body, StringComparison.Ordinal);
        }

        Assert.Equal("The store needs migrating.", SinglePageApp.NeedsMigrating);

        var merge = File.ReadAllText(Path.Combine(Repository.Root, "tools", "merge"));
        var steps = new[] { "night.lock", "gh pr merge \"$number\" --merge --delete-branch", "git checkout main", "git pull --ff-only", "-- migrate" }
            .Select(step => merge.IndexOf(step, StringComparison.Ordinal))
            .ToArray();

        Assert.All(steps, at => Assert.True(at > 0));
        Assert.Equal(steps.Order(), steps);
        Assert.Contains("run-bash.ps1') 'merge' @args", File.ReadAllText(Path.Combine(Repository.Root, "tools", "merge.ps1")), StringComparison.Ordinal);
    }
}
