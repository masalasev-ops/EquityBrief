using System.Text.Json;
using EquityBrief.Core.News;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Local;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, from 20.5: the news labels compared on the local model. Over a constructed store, every article
// the paid profile labelled under the instruction's version that the store still holds is labelled again by the local
// model under the labeller's own instruction, an answer that cannot be read asked once more, and the agreement on the
// kind, on the direction and on both and the reasons holding no digit worked by hand; the file written under the data
// root, the store's rows as they were, the local lock waited for and the runtime left as it was found.
// see: A comparison of the local model against stored paid work writes no store row and switches nothing
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheLabelComparisonLabelsEachPaidLabelledArticleAgainAndStatesTheAgreementWritingNoStoreRow()
    {
        using var store = new TemporaryStore().Migrated();

        // Two members, the first named and the second not, so its prompt names its ticker as the labeller's does.
        store.Execute(
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, name) VALUES " +
            "('GSPC', 'AAA', NULL, NULL, '2026-10-01T00:00:00Z', 'Alpha Corp'), ('GSPC', 'BBB', NULL, NULL, '2026-10-01T00:00:00Z', NULL);");

        foreach (var (ticker, id, title) in new[]
        {
            ("AAA", "a1", "Alpha beats"), ("AAA", "a2", "Alpha sued"), ("AAA", "a3", "Alpha unread"), ("AAA", "a4", "Alpha by another"),
            ("AAA", "a5", "Alpha under another version"), ("BBB", "b1", "Beta buys"), ("BBB", "b2", "Beta opinion"),
        })
        {
            store.Execute(
                "INSERT INTO news_article (ticker, article_id, link, title, source, published_at, text, length, admissibility, session_date) VALUES " +
                $"('{ticker}', '{id}', 'https://example.com/{id}', '{title}', 'wire', '2026-10-04T12:00:00Z', 'The text of {title}.', 20, 'accepted', '2026-10-05');");
        }

        // The paid profile's labels: four it labelled under the instruction's version, oldest first by when; one it could
        // not read, one another profile wrote, one under another version and one whose article the store no longer holds,
        // none of those four compared.
        store.Execute(
            "INSERT INTO news_label (ticker, article_id, profile, instruction_version, model, outcome, cause, kind, direction, reason, labelled_at, run_id) VALUES " +
            "('BBB', 'b2', 'deepseek', 1, 'deepseek-flash', 'labelled', NULL, 'opinion or promotional', 'neutral', 'An opinion piece on Beta.', '2026-10-05T01:03:00Z', 'news-1'), " +
            "('AAA', 'a1', 'deepseek', 1, 'deepseek-flash', 'labelled', NULL, 'results', 'positive', 'Alpha beat what was expected.', '2026-10-05T01:00:00Z', 'news-1'), " +
            "('BBB', 'b1', 'deepseek', 1, 'deepseek-flash', 'labelled', NULL, 'deal', 'positive', 'Beta agreed to buy a rival.', '2026-10-05T01:02:00Z', 'news-1'), " +
            "('AAA', 'a2', 'deepseek', 1, 'deepseek-flash', 'labelled', NULL, 'legal or regulatory', 'negative', 'A lawsuit weighs on Alpha.', '2026-10-05T01:01:00Z', 'news-1'), " +
            "('AAA', 'a3', 'deepseek', 1, 'deepseek-flash', 'unreadable', 'not JSON', NULL, NULL, NULL, '2026-10-05T01:04:00Z', 'news-1'), " +
            "('AAA', 'a4', 'claude-haiku', 1, 'claude-haiku-4-5', 'labelled', NULL, 'other', 'neutral', 'Another profile read it.', '2026-10-05T01:05:00Z', 'news-1'), " +
            "('AAA', 'a5', 'deepseek', 2, 'deepseek-flash', 'labelled', NULL, 'other', 'neutral', 'Another version read it.', '2026-10-05T01:06:00Z', 'news-1'), " +
            "('CCC', 'c1', 'deepseek', 1, 'deepseek-flash', 'labelled', NULL, 'other', 'neutral', 'Its article is gone.', '2026-10-05T01:07:00Z', 'news-1');");

        var before = RowsByTable(store);
        var settings = new LocalModelSettings("gemma-4", "http://127.0.0.1:1234/v1/", Gemma, 30, 50176, 30);
        var runtime = AnsweringRuntime(loaded: false, chat: _ => throw new InvalidOperationException("the comparison's answers are the test's own"));
        var asked = new Dictionary<string, int>(StringComparer.Ordinal);

        // The local model's answers by the article's title: the first agrees on both, the second on the kind alone, the
        // third states a figure in its reason and agrees on both when asked once more, and the fourth is no object twice.
        var feed = new LabelAnsweringFeed(ReadingFeedOver(runtime, settings), request =>
        {
            var title = new[] { "Alpha beats", "Alpha sued", "Beta buys", "Beta opinion" }.Single(title => request.Text.Contains(title, StringComparison.Ordinal));
            var times = asked[title] = asked.GetValueOrDefault(title) + 1;

            return Reply(title switch
            {
                "Alpha beats" => "{\"kind\": \"results\", \"direction\": \"positive\", \"reason\": \"Alpha beat expectations.\"}",
                "Alpha sued" => "{\"kind\": \"legal or regulatory\", \"direction\": \"neutral\", \"reason\": \"A lawsuit was filed against Alpha.\"}",
                "Beta buys" when times == 1 => "{\"kind\": \"deal\", \"direction\": \"positive\", \"reason\": \"Beta agreed to pay 5 billion for a rival.\"}",
                "Beta buys" => "{\"kind\": \"deal\", \"direction\": \"positive\", \"reason\": \"Beta agreed to buy a rival.\"}",
                _ => "I cannot label this article.",
            });
        });

        // The local lock held by another process when it starts, given up the first time the comparison waits for it.
        var folder = Path.Combine(store.Root, EquityBrief.Core.Research.WorkerDrainLauncher.CopiesFolder);

        Directory.CreateDirectory(folder);

        using var holder = new FileStream(Path.Combine(folder, LocalLocks.FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var waits = 0;

        Task Released(TimeSpan _, CancellationToken __)
        {
            waits++;
            holder.Dispose();

            return Task.CompletedTask;
        }

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var said = new StringWriter();
        var outcome = await new LocalComparison(feed, settings, clock, store.DatabaseFile, store.Root, Released).LabelsAsync("deepseek", 2000, said);

        // Worked by hand: four compared and three labelled, the fourth unreadable as no object; the kind agreeing on all three,
        // the direction on the first and the third, both on those two; four reasons stated, the first answer for the third
        // holding a figure, and the fourth's answers stating none.
        var agreement = outcome.Agreement;

        Assert.Equal((4, 3, 3, 2, 2, 4, 3), (agreement.Compared, agreement.Labelled, agreement.Kind, agreement.Direction, agreement.Both, agreement.Reasons, agreement.ReasonsWithoutDigit));
        Assert.Equal(NewsInstruction.NotJson, Assert.Single(agreement.Unreadable).Key);
        Assert.Equal(1, agreement.Unreadable[NewsInstruction.NotJson]);
        Assert.Equal((Gemma, $"{Runtime256} {RuntimeVersion}", (string?)null), (outcome.Model, outcome.Runtime, outcome.Stopped));

        // Each article asked in the paid labels' order, under the labeller's own instruction for the company the labeller
        // names, at the readings' seed and the budget handed in, and the third and fourth asked once more.
        Assert.Equal(
            ["Alpha Corp (AAA) Alpha beats", "Alpha Corp (AAA) Alpha sued", "BBB (BBB) Beta buys", "BBB (BBB) Beta buys", "BBB (BBB) Beta opinion", "BBB (BBB) Beta opinion"],
            feed.Asked.Select(request => request.Text.Split('\n')[0]["Company: ".Length..] + " " + request.Text.Split('\n')[1]["Title: ".Length..]));
        Assert.All(feed.Asked, request => Assert.Equal((Gemma, NewsInstruction.System, ReadingSettings.Seed, 2000), (request.Model, request.Instruction, request.Seed, request.AnswerTokens)));
        Assert.Equal(NewsInstruction.Prompt("Alpha Corp", "AAA", "Alpha beats", "The text of Alpha beats."), feed.Asked[0].Text);

        // It waited for the lock and said so, loaded the model it found not loaded and unloaded it at its end.
        Assert.Equal(1, waits);
        Assert.Contains("compare-labels: another local process is calling the local model, so this waits for it to end.", said.ToString(), StringComparison.Ordinal);
        Assert.Contains(runtime.Asked, request => request.StartsWith("POST /api/v1/models/load", StringComparison.Ordinal));
        Assert.Contains(runtime.Asked, request => request.StartsWith("POST /api/v1/models/unload", StringComparison.Ordinal));

        // Its line, and its file under the data root named by its instant, holding the same figures and each article.
        Assert.Contains(
            "compare-labels: 4 articles deepseek labelled are still stored; google/gemma-4-26b-a4b-qat on llama.cpp-win-x86_64-nvidia-cuda12-avx2 2.56.0 labelled 3 of the 4 it was asked and could not label 1 (not JSON 1); " +
            "of its 3 labels, the kind agreed on 3, the direction on 2 and both on 2; 3 of the 4 reasons its answers stated held no digit; written to comparisons/labels-20261019T120000Z.json under the data root, and nothing to the store.",
            said.ToString(),
            StringComparison.Ordinal);
        Assert.Equal("comparisons/labels-20261019T120000Z.json", outcome.File);

        using (var written = JsonDocument.Parse(File.ReadAllText(Path.Combine(store.Root, "comparisons", "labels-20261019T120000Z.json"))))
        {
            var root = written.RootElement;

            Assert.Equal(
                ("deepseek", "gemma-4", 4, 4, 3, 3, 2, 2, 4, 3),
                (root.GetProperty("paidProfile").GetString(), root.GetProperty("localProfile").GetString(), root.GetProperty("held").GetInt32(), root.GetProperty("compared").GetInt32(),
                 root.GetProperty("labelled").GetInt32(), root.GetProperty("kind").GetInt32(), root.GetProperty("direction").GetInt32(), root.GetProperty("both").GetInt32(),
                 root.GetProperty("reasons").GetInt32(), root.GetProperty("reasonsWithoutDigit").GetInt32()));
            Assert.Equal(["a1", "a2", "b1", "b2"], root.GetProperty("articles").EnumerateArray().Select(article => article.GetProperty("articleId").GetString()));

            var fourth = root.GetProperty("articles")[3];

            Assert.Equal((JsonValueKind.Null, NewsInstruction.NotJson, 2), (fourth.GetProperty("local").ValueKind, fourth.GetProperty("cause").GetString(), fourth.GetProperty("answers").GetArrayLength()));
        }

        // And the store holds every row it held, in every table.
        Assert.Equal(before, RowsByTable(store));

        // A store holding no paid label asks nothing, takes no lock and loads nothing, and its file says none was compared.
        using var empty = new TemporaryStore().Migrated();
        var idle = AnsweringRuntime(loaded: false, chat: _ => throw new InvalidOperationException("nothing is asked"));
        var none = await new LocalComparison(new LabelAnsweringFeed(ReadingFeedOver(idle, settings), _ => throw new InvalidOperationException("nothing is asked")), settings, clock, empty.DatabaseFile, empty.Root, NoWait)
            .LabelsAsync("deepseek", 2000, TextWriter.Null);

        Assert.Equal((0, 0), (none.Agreement.Compared, none.Agreement.Reasons));
        Assert.Empty(idle.Asked);
        Assert.True(File.Exists(Path.Combine(empty.Root, "comparisons", "labels-20261019T120000Z.json")));
    }

    [Fact]
    public void AnAnswersReasonIsReadFromItsObjectAsTheLabellersReaderFindsIt()
    {
        Assert.Equal("Alpha beat expectations.", LabelAgreement.ReasonOf("Here it is: {\"kind\": \"results\", \"direction\": \"positive\", \"reason\": \" Alpha beat expectations. \"} done"));
        Assert.Null(LabelAgreement.ReasonOf("no object here"));
        Assert.Null(LabelAgreement.ReasonOf("{\"kind\": \"results\", \"reason\": 5}"));
        Assert.Null(LabelAgreement.ReasonOf("{\"kind\": \"results\", \"reason\": \"  \"}"));
        Assert.Null(LabelAgreement.ReasonOf("{\"reason\": \"unclosed\""));
    }

    // Every table's rows, counted, so a comparison is shown to write none.
    static Dictionary<string, long> RowsByTable(TemporaryStore store)
    {
        using var connection = store.Open();
        var tables = new List<string>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var rows = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();

            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
            rows[table] = (long)command.ExecuteScalar()!;
        }

        return rows;
    }

    // A feed whose list, load and unload go to the scripted runtime and whose answers the test gives by the request,
    // keeping every request it was asked.
    sealed class LabelAnsweringFeed(LmStudioReadingFeed inner, Func<ReadingRequest, ReadingReply> answer) : IReadingModelFeed
    {
        public List<ReadingRequest> Asked { get; } = [];

        public int Requests => Asked.Count;

        public Task<bool> PrepareAsync(CancellationToken cancellation = default) => inner.PrepareAsync(cancellation);

        public Task ReleaseAsync(bool loadedHere, CancellationToken cancellation = default) => inner.ReleaseAsync(loadedHere, cancellation);

        public Task<ReadingReply> AskAsync(ReadingRequest request, CancellationToken cancellation = default)
        {
            Asked.Add(request);

            return Task.FromResult(answer(request));
        }
    }
}
