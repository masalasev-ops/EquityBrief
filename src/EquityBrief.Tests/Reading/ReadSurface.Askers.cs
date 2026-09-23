using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Passes;
using EquityBrief.Web.App;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface, the 11.4 correction: the queue page and section 15's paragraph on it name every
// asker the store admits, read off the store's own check on who asked.
public partial class ReadSurface
{
    [Fact]
    public void TheQueuePageAndItsParagraphNameEveryAskerTheStoreAdmits()
    {
        using var store = new TemporaryStore().Migrated();
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'research_request';";

        var check = Regex.Match((string)command.ExecuteScalar()!, @"asked_from\s+TEXT\s+NOT\s+NULL\s+CHECK\s*\(\s*asked_from\s+IN\s*\(([^)]*)\)");

        Assert.True(check.Success, "The request table holds no check on who asked.");

        var admitted = check.Groups[1].Value.Split(',').Select(value => value.Trim().Trim('\'')).ToArray();

        // The words the page and the paragraph name each asker in. An asker the store comes to
        // admit with no words here is refused below rather than passed over.
        var words = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ResearchRequests.FromList] = "from a row on tonight's list",
            [ResearchRequests.FromName] = "from a name's own page",
            [RequestDrain.FromNight] = "by the night for the first name its list draws",
        };

        var page = WebUtility.HtmlDecode(new SinglePageApp().QueueRegion([]));
        var paragraph = WebUtility.HtmlDecode(Regex.Match(File.ReadAllText(Repository.Architecture), "<p>A request is asked for [^<]*").Value);

        Assert.True(admitted.Length >= 3, $"Read {admitted.Length} askers off the store's check, expected at least 3.");
        Assert.NotEmpty(paragraph);

        Assert.All(admitted, asker =>
        {
            Assert.True(words.TryGetValue(asker, out var named), $"The store admits '{asker}' as asking for a report, and nothing says how the queue page names it.");
            Assert.Contains(named, page, StringComparison.Ordinal);
            Assert.Contains(named, paragraph, StringComparison.Ordinal);
        });
    }
}
