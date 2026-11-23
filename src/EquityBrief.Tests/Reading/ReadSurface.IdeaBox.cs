using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The idea box on the Improvement loop page, read back over a constructed store: the box and its press; each idea typed,
// newest first, waiting for the model, read into nothing, refused with its cause, understood above its line with a Confirm,
// confirmed and tested first on Sunday, and tested with its verdict; a press or a confirm from anything but a page of this
// tool refused with nothing written; a press with nothing typed refused; a confirm written once, and only of an understood
// idea; and a store holding none saying so.
// see: An idea typed in the box is read into one grammar line by the local model and joins the week's tests only on the operator's confirm
public partial class ReadSurface
{
    internal static string[] IdeaBoxRows =>
    [
        CheckReach.Key("15.20 The Loop page", "The idea box"),
    ];

    const string BoxLine = "event pullback(600) · if rsi above tenth 3 · buy next open · stop 1.5 typical days · take profit whole at 2 risks · sell after 63 sessions · cap 5 a night by strength highest";

    [Fact]
    public async Task TheLoopPagesIdeaBoxDrawsEachIdeaWhereItStandsAndTakesAPressOrAConfirmFromItsOwnPageAlone()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO idea_box (id, words, pressed_at) VALUES "
            + "(1, 'tested idea', '2026-11-20T10:00:00Z'), (2, 'confirmed idea', '2026-11-21T10:00:00Z'), (3, 'understood idea', '2026-11-22T10:00:00Z'), "
            + "(4, 'refused idea', '2026-11-23T10:00:00Z'), (5, 'no idea at all', '2026-11-24T10:00:00Z'), (6, 'waiting idea', '2026-11-25T10:00:00Z');");
        store.Execute(
            "INSERT INTO idea_box_answer (press_id, attempt, outcome, cause, line, reason, prompt_key, pin, version, answer, seconds, asked_at) VALUES "
            + $"(1, 1, 'understood', NULL, '{BoxLine.Replace("rsi", "volume_ratio", StringComparison.Ordinal)}', 'A tested reading.', 'k', 1, 'v', '{{}}', 3.1, '2026-11-20T10:01:00Z'), "
            + $"(2, 1, 'understood', NULL, '{BoxLine.Replace("1.5", "2", StringComparison.Ordinal)}', 'A confirmed reading.', 'k', 1, 'v', '{{}}', 3.1, '2026-11-21T10:01:00Z'), "
            + "(3, 1, 'refused', 'a number in its sentence', NULL, NULL, 'k', 1, 'v', '{}', 3.1, '2026-11-22T10:01:00Z'), "
            + $"(3, 2, 'understood', NULL, '{BoxLine}', 'Keep the small cap pullbacks whose momentum has not faded.', 'k', 1, 'v', '{{}}', 3.1, '2026-11-22T10:02:00Z'), "
            + "(4, 1, 'refused', 'an idea the grammar cannot read', NULL, NULL, 'k', 1, 'v', '{}', 3.1, '2026-11-23T10:01:00Z'), "
            + "(5, 1, 'none', NULL, NULL, NULL, 'k', 1, 'v', '{}', 3.1, '2026-11-24T10:01:00Z');");
        store.Execute(
            "INSERT INTO idea_box_confirm (press_id, line, confirmed_at) VALUES "
            + $"(1, '{BoxLine.Replace("rsi", "volume_ratio", StringComparison.Ordinal)}', '2026-11-20T11:00:00Z'), (2, '{BoxLine.Replace("1.5", "2", StringComparison.Ordinal)}', '2026-11-21T11:00:00Z');");
        store.Execute($"INSERT INTO notebook (entered_at, source, index_code, family, words, line, verdict, why, tested_on, run_id) VALUES ('2026-11-22T15:00:00Z', 'the idea box', 'SML', 'pullback', 'w', '{BoxLine.Replace("rsi", "volume_ratio", StringComparison.Ordinal)}', 'failed', 'Made nothing over the benchmark on the years it was chosen on.', '2026-11-20', 'r');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        string Box(string page) => Blocks(WebUtility.HtmlDecode(page), "<section class=\"card\" data-card=\"loop-idea-box\">.*?</section>").Single();

        var box = Box(await client.GetStringAsync("/screens/loop?universe=600"));

        Assert.Contains("<h2>Your ideas</h2>", box, StringComparison.Ordinal);
        Assert.Contains($"<form class=\"idea-press\" method=\"post\" action=\"{SinglePageApp.IdeaBoxRoute}\" data-idea-key=\"idea-box\"><label for=\"idea-words\">Your idea</label><textarea id=\"idea-words\" name=\"words\" rows=\"3\" maxlength=\"1000\" required></textarea><button type=\"submit\">Ask the model</button></form>", box, StringComparison.Ordinal);
        Assert.Equal(["waiting", "none", "refused", "understood", "confirmed", "tested"], [.. Regex.Matches(box, "data-idea=\"([a-z]+)\"").Select(match => match.Groups[1].Value)]);
        Assert.Contains("Waiting for the local model, which reads it once the night, a local run, the labeller or the picker lets it go.", box, StringComparison.Ordinal);
        Assert.Contains("Not read: an idea the grammar cannot read.", box, StringComparison.Ordinal);
        Assert.Contains($"<p>Understood as: Keep the small cap pullbacks whose momentum has not faded.</p><code class=\"idea-line\">{BoxLine}</code><form class=\"idea-press\" method=\"post\" action=\"{SinglePageApp.IdeaConfirmRoute}3\" data-idea-key=\"idea|3\"><button type=\"submit\">Confirm</button></form>", box, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(box, ">Confirm</button>"));
        Assert.Contains("Confirmed 21 Nov 2026: tested first in the next Sunday run.", box, StringComparison.Ordinal);
        Assert.Contains("<b>Failed</b> Made nothing over the benchmark on the years it was chosen on.", box, StringComparison.Ordinal);

        // A press or a confirm from anything but a page of this tool is refused and writes nothing.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(SinglePageApp.IdeaBoxRoute, new FormUrlEncodedContent([new("words", "a scripted idea")]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(SinglePageApp.IdeaConfirmRoute + "3", null)).StatusCode);
        Assert.Equal((6L, 2L), (Scalar(store, "SELECT COUNT(*) FROM idea_box;"), Scalar(store, "SELECT COUNT(*) FROM idea_box_confirm;")));

        async Task<HttpResponseMessage> Pressed(string route, IEnumerable<KeyValuePair<string, string>>? fields = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = new FormUrlEncodedContent(fields ?? []) };

            request.Headers.Add(SinglePageApp.PassHeader, SinglePageApp.PassHeaderValue);

            return await client.SendAsync(request);
        }

        // A press with nothing typed is refused; one with words writes them.
        Assert.Equal(HttpStatusCode.Conflict, (await Pressed(SinglePageApp.IdeaBoxRoute, [new("words", "   ")])).StatusCode);

        var asked = await Pressed(SinglePageApp.IdeaBoxRoute, [new("words", "  buy dips in the strongest sectors  ")]);

        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.Equal(["buy dips in the strongest sectors"], Query(store, "SELECT words FROM idea_box WHERE id = 7;"));

        // A confirm is written once, of the understood idea's newest line alone; one of an idea not understood writes nothing.
        Assert.Equal(HttpStatusCode.OK, (await Pressed(SinglePageApp.IdeaConfirmRoute + "3")).StatusCode);
        Assert.Equal([BoxLine], Query(store, "SELECT line FROM idea_box_confirm WHERE press_id = 3;"));
        Assert.Equal(HttpStatusCode.Conflict, (await Pressed(SinglePageApp.IdeaConfirmRoute + "3")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Pressed(SinglePageApp.IdeaConfirmRoute + "4")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Pressed(SinglePageApp.IdeaConfirmRoute + "7")).StatusCode);
        Assert.Equal(3L, Scalar(store, "SELECT COUNT(*) FROM idea_box_confirm;"));

        // A store holding none says so.
        using var empty = new TemporaryStore().Migrated();
        using var emptyHost = new Host(empty.Root);
        using var emptyClient = emptyHost.CreateClient();

        Assert.Contains("No idea has been typed yet.", Box(await emptyClient.GetStringAsync("/screens/loop?universe=400")), StringComparison.Ordinal);
    }
}
