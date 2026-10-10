using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;

namespace EquityBrief.Tests.Checks;

// The paid lane's answers to the requests the 6.4 correction changed, constructed with no paid call from the
// answers the provider gave to the same draft of the same section in the same pass before [N] was taught. Each
// says so in its first key and names the capture it was made from, and is that capture with [N] marked on each
// sentence stating a figure, or on Claude's wire each sentence's facts flag set to whether it states one, and
// nothing else changed, so a replay over it prices and reads every call as the capture did.
// see: A sentence names the night's stored figures by [N] and a document by its marker, and a figure in a sentence citing documents alone is one they state
public partial class FixtureExpectations
{
    [Fact]
    public void EachConstructedPaidAnswerIsTheCaptureItNamesWithTheNightsMarkAddedAndNothingElse()
    {
        var constructed = Constructed().Where(one => !Since184(one.Node)).ToArray();

        // Thirteen on DeepSeek's wire and seven on Claude's: each paid request the instructions moved in the default pass,
        // the lane comparison's pass and the pass asked of Claude, the short version's retries among them, while the
        // risks, asked under the fields' own citing line, and every request handed no document keep their captures; and
        // from 18.2 one on each wire for what management said, a section no capture answers. From 18.4 ten and five: the
        // short version's three on DeepSeek's wire and two on Claude's are taken out, since 18.4 writes it from the
        // accepted claims and no request asks for them, and the requests 18.4 moved are answered by those below.
        Assert.Equal(10, constructed.Count(one => one.Node.ContainsKey("choices")));
        Assert.Equal(5, constructed.Count(one => one.Node.ContainsKey("response")));

        var sources = new List<string>();
        var unanswered = constructed.Where(one => one.Node["constructed"]!.GetValue<string>().Contains(NoCaptureAnswers, StringComparison.Ordinal)).ToArray();

        Assert.Equal(2, unanswered.Length);
        WrittenForASectionNoCaptureAnswers(unanswered);

        foreach (var (file, node) in constructed.Except(unanswered))
        {
            var said = node["constructed"]!.GetValue<string>();
            var source = Regex.Match(said, @"recorded as (research-call-[0-9a-f]{32}\.json)").Groups[1].Value;
            var stated = int.Parse(Regex.Match(said, @"(?:each of its|true on the) (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);

            Assert.True(File.Exists(Path.Combine(Folder(), source)), $"{file} names {source}, which the fixture does not hold.");
            sources.Add(source);

            var capture = JsonNode.Parse(File.ReadAllText(Path.Combine(Folder(), source)))!.AsObject();

            Assert.False(capture.ContainsKey("constructed"), $"{file} names {source}, which is constructed and not a capture.");

            var unmarked = node.DeepClone().AsObject();

            unmarked.Remove("constructed");

            if (node.ContainsKey("choices"))
            {
                var message = unmarked["choices"]![0]!["message"]!.AsObject();
                var content = message["content"]!.GetValue<string>();

                Assert.DoesNotContain("[N]", capture["choices"]![0]!["message"]!["content"]!.GetValue<string>(), StringComparison.Ordinal);

                // [N] on each sentence stating a figure and on no other, as many as the file says.
                var sentences = content.Split("\n\n").SelectMany(ClaimRules.Sentences).ToArray();

                Assert.All(sentences, sentence => Assert.True(
                    sentence.CitesNight == StatesAFigure(sentence.Text),
                    $"{file}: \"{sentence.Text}\" {(sentence.CitesNight ? "carries [N] and states no figure" : "states a figure and carries no [N]")}."));
                Assert.Equal(stated, sentences.Count(sentence => sentence.CitesNight));

                message["content"] = content.Replace(" [N]", string.Empty, StringComparison.Ordinal);
                Assert.True(JsonNode.DeepEquals(capture, unmarked), $"{file} differs from {source} beyond the [N] it marks.");
            }
            else
            {
                var flagged = 0;

                foreach (var answer in Answers(unmarked))
                {
                    foreach (var sentence in answer["paragraphs"]!.AsArray().SelectMany(paragraph => paragraph!.AsArray()).Select(one => one!.AsObject()))
                    {
                        var facts = sentence["facts"]!.GetValue<bool>();

                        Assert.Equal(StatesAFigure(sentence["sentence"]!.GetValue<string>()), facts);
                        flagged += facts ? 1 : 0;
                        sentence.Remove("facts");
                    }
                }

                Assert.Equal(stated, flagged);

                var read = capture.DeepClone().AsObject();

                Assert.All(Answers(read), answer => Assert.All(
                    answer["paragraphs"]!.AsArray().SelectMany(paragraph => paragraph!.AsArray()),
                    sentence => Assert.False(sentence!.AsObject().ContainsKey("facts"))));
                Assert.True(JsonNode.DeepEquals(read, unmarked), $"{file} differs from {source} beyond the facts flags it sets.");
            }
        }

        // Each from a capture of its own.
        Assert.Equal(constructed.Length - unanswered.Length, sources.Distinct(StringComparer.Ordinal).Count());
    }

    const string NoCaptureAnswers = "written for a section no capture answers";

    // Every constructed answer the fixture holds, each with the note its first key carries.
    static (string File, JsonObject Node)[] Constructed() =>
    [
        .. Directory.GetFiles(Folder(), RecordedResearchModelFeed.FilePrefix + "*.json")
            .Select(file => (File: Path.GetFileName(file), Node: JsonNode.Parse(File.ReadAllText(file))!.AsObject()))
            .Where(one => one.Node.FirstOrDefault().Key == "constructed")
            .OrderBy(one => one.File, StringComparer.Ordinal),
    ];

    // An answer constructed for a request 18.4 moved says so in its note.
    static bool Since184(JsonObject node) => node["constructed"]!.GetValue<string>().Contains("18.4", StringComparison.Ordinal);

    // The answers to the requests 18.4 moved, constructed with no paid call: a capture re-keyed whole, its content and usage
    // as recorded; the risks' capture with the two columns written beside its risks; the two cases' capture with each figure
    // another region draws said in words; and the short version's and the headline's written by hand in a capture's shell
    // for requests no capture answers. Each names its capture and is held to it for every part its note does not say it
    // changed.
    // see: The short version and the headline are written last from the accepted claims alone, and code writes each where none is accepted
    // see: The risks close on what would turn the stock up and what would make it worse, each column opening on a line the plan computes
    // see: Each figure of the facts file is owned by one region of the page, and a written section writes only the figures its own region owns
    [Fact]
    public void EachAnswerConstructedFor184IsTheCaptureItNamesChangedOnlyWhereItsNoteSays()
    {
        var since = Constructed().Where(one => Since184(one.Node)).ToArray();

        static string Note(JsonObject node) => node["constructed"]!.GetValue<string>();

        var rekeyed = since.Where(one => Note(one.Node).Contains("nothing else changed", StringComparison.Ordinal)).ToArray();
        var columns = since.Where(one => Note(one.Node).Contains("the two columns written by hand", StringComparison.Ordinal)).ToArray();
        var inWords = since.Where(one => Note(one.Node).Contains("held each figure to the region of the page drawing it", StringComparison.Ordinal)).ToArray();
        var byHand = since.Where(one => Note(one.Node).Contains("written by hand for a request no capture answers", StringComparison.Ordinal)).ToArray();

        // Stated in advance: fourteen re-keyed, two with the risks' columns, two of the two cases said in words and thirteen
        // written by hand, every one of the thirty-one of one kind.
        Assert.Equal((14, 2, 2, 13), (rekeyed.Length, columns.Length, inWords.Length, byHand.Length));
        Assert.Equal(since.Length, rekeyed.Length + columns.Length + inWords.Length + byHand.Length);

        JsonObject Source(string file, string note, string pattern)
        {
            var named = Regex.Match(note, pattern + @" (research-call-[0-9a-f]{32}\.json)").Groups[1].Value;

            Assert.True(File.Exists(Path.Combine(Folder(), named)), $"{file} names {named}, which the fixture does not hold.");

            var source = JsonNode.Parse(File.ReadAllText(Path.Combine(Folder(), named)))!.AsObject();

            source.Remove("constructed");

            return source;
        }

        static JsonObject Unmarked(JsonObject node)
        {
            var copy = node.DeepClone().AsObject();

            copy.Remove("constructed");

            return copy;
        }

        // The text an answer gives on its own wire, and its usage, which prices it.
        static string Said(JsonObject node) => node.ContainsKey("choices")
            ? node["choices"]![0]!["message"]!["content"]!.GetValue<string>()
            : node["response"]!["content"]!.AsArray().Single(block => block!["type"]!.GetValue<string>() == "text")!["text"]!.GetValue<string>();

        static JsonNode Usage(JsonObject node) => node.ContainsKey("choices") ? node["usage"]! : node["response"]!["usage"]!;

        // Re-keyed: the capture whole, but for the note.
        foreach (var (file, node) in rekeyed)
        {
            Assert.True(JsonNode.DeepEquals(Source(file, Note(node), "recorded as"), Unmarked(node)), $"{file} differs from the capture it names.");
        }

        // The risks: the capture's risks as recorded, two lines in each column resting on a listed document and stating no
        // figure, and the capture's usage.
        foreach (var (file, node) in columns)
        {
            var source = Source(file, Note(node), "recorded as");
            var answer = JsonNode.Parse(Said(node))!.AsObject();

            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Said(source))!["risks"], answer["risks"]), $"{file}'s risks differ from the capture's.");
            Assert.True(JsonNode.DeepEquals(Usage(source), Usage(node)), $"{file}'s usage differs from the capture's.");

            foreach (var column in new[] { "turnsUp", "makesWorse" })
            {
                var lines = answer[column]!.AsArray();

                Assert.Equal(2, lines.Count);
                Assert.All(lines, line => Assert.InRange(line!["document"]!.GetValue<int>(), 1, 7));
                Assert.All(lines, line => Assert.False(StatesAFigure(line!["what"]!.GetValue<string>()), $"{file}: \"{line!["what"]}\" states a figure."));
            }
        }

        // The two cases: the capture's usage, and no sentence stating a figure, since their region draws none.
        foreach (var (file, node) in inWords)
        {
            var source = Source(file, Note(node), "recorded as");

            Assert.True(JsonNode.DeepEquals(Usage(source), Usage(node)), $"{file}'s usage differs from the capture's.");

            var sentences = node.ContainsKey("choices")
                ? Said(node).Split("\n\n").SelectMany(ClaimRules.Sentences).Select(sentence => sentence.Text).ToArray()
                : [.. JsonNode.Parse(Said(node))!["paragraphs"]!.AsArray().SelectMany(paragraph => paragraph!.AsArray()).Select(sentence => sentence!["sentence"]!.GetValue<string>())];

            Assert.NotEmpty(sentences);
            Assert.All(sentences, sentence => Assert.False(StatesAFigure(sentence), $"{file}: \"{sentence}\" states a figure."));
        }

        // Written by hand: the shell's usage, every sentence citing the claims alone, and a headline one sentence carrying
        // no digit.
        foreach (var (file, node) in byHand)
        {
            var source = Source(file, Note(node), "those of");

            Assert.True(JsonNode.DeepEquals(Usage(source), Usage(node)), $"{file}'s usage differs from its shell's.");

            var sentences = Said(node).Split("\n\n").SelectMany(ClaimRules.Sentences).ToArray();

            Assert.NotEmpty(sentences);
            Assert.All(sentences, sentence => Assert.True(sentence.ClaimsCited.Count > 0 && sentence.Citations.Count == 0 && !sentence.CitesNight, $"{file}: \"{sentence.Text}\" cites more than the claims."));

            if (Note(node).Contains("The headline", StringComparison.Ordinal))
            {
                Assert.Single(sentences);
                Assert.DoesNotMatch(@"\d", ClaimRules.Unmarked(sentences[0].Text));
            }
        }
    }

    // An answer constructed for a section no capture answers holds what the section's ask and the checker hold it to, read
    // against the two releases the pass hands it: every quotation word for word in the newest release, every sentence
    // stating a figure marked as the night's on DeepSeek's wire and flagged on Claude's and no other, Claude's the same
    // sentences as DeepSeek's, and the prompt's size the one the file states.
    static void WrittenForASectionNoCaptureAnswers(IReadOnlyList<(string File, JsonObject Node)> unanswered)
    {
        var release = new StoredDocument(
            "r", "https://www.sec.gov/Archives/edgar/data/1601046/release.htm", "Results release", new DateOnly(2026, 8, 18), DateTimeOffset.UnixEpoch,
            SecEdgarArchive.Plain(Captured("release-KEYS.htm")), Admissibility.Accepted);

        void QuotedFromTheRelease(string file, string sentence) =>
            Assert.All(ClaimRules.Quotations(sentence), quoted => Assert.True(
                ClaimRules.QuotedByACitedDocument(quoted, new ProseSentence(sentence, [1], false), [release]),
                $"{file}: \"{quoted}\" is not in the newest release word for word."));

        static void Stated(string said, long usage) =>
            Assert.Equal(usage, long.Parse(Regex.Match(said, @"counted over the same two releases, (\d+) tokens").Groups[1].Value, CultureInfo.InvariantCulture));

        var deepSeek = unanswered.Single(one => one.Node.ContainsKey("choices"));
        var claude = unanswered.Single(one => one.Node.ContainsKey("response"));

        var content = deepSeek.Node["choices"]![0]!["message"]!["content"]!.GetValue<string>();
        var sentences = content.Split("\n\n").SelectMany(ClaimRules.Sentences).ToArray();

        Assert.Equal(ClaimRules.GuidanceOpens, content[..ClaimRules.GuidanceOpens.Length]);
        Assert.All(sentences, sentence => Assert.True(
            sentence.CitesNight == StatesAFigure(sentence.Text),
            $"{deepSeek.File}: \"{sentence.Text}\" {(sentence.CitesNight ? "carries [N] and states no figure" : "states a figure and carries no [N]")}."));
        Assert.All(sentences, sentence => QuotedFromTheRelease(deepSeek.File, sentence.Text));
        Assert.Equal(4, sentences.Sum(sentence => ClaimRules.Quotations(sentence.Text).Count));
        Stated(deepSeek.Node["constructed"]!.GetValue<string>(), deepSeek.Node["usage"]!["prompt_tokens"]!.GetValue<long>());

        var claudes = Answers(claude.Node.DeepClone().AsObject())
            .SelectMany(answer => answer["paragraphs"]!.AsArray().SelectMany(paragraph => paragraph!.AsArray()))
            .Select(sentence => sentence!.AsObject())
            .ToArray();

        Assert.All(claudes, sentence => Assert.Equal(StatesAFigure(sentence["sentence"]!.GetValue<string>()), sentence["facts"]!.GetValue<bool>()));
        Assert.All(claudes, sentence => QuotedFromTheRelease(claude.File, sentence["sentence"]!.GetValue<string>()));
        Assert.Equal(
            sentences.Select(sentence => Regex.Replace(sentence.Text, @"\s*\[(?:D\d+|N)\]", string.Empty)),
            claudes.Select(sentence => sentence["sentence"]!.GetValue<string>()));
        Stated(claude.Node["constructed"]!.GetValue<string>(), claude.Node["response"]!["usage"]!["input_tokens"]!.GetValue<long>());
    }

    static bool StatesAFigure(string sentence) =>
        ClaimRules.Figures(sentence).Any(figure => figure.Kind == FigureKind.Figure);

    // Each text block on Claude's wire holding an answer as sentences, read in place as the object it holds, so two
    // answers compare by what they say and not by how their text was spaced.
    static IReadOnlyList<JsonObject> Answers(JsonObject wrapper)
    {
        var answers = new List<JsonObject>();

        foreach (var block in wrapper["response"]!["content"]!.AsArray().Select(one => one!.AsObject()))
        {
            if (block["type"]?.GetValue<string>() == "text" && block["text"]?.GetValue<string>() is { } text && text.TrimStart().StartsWith('{'))
            {
                var answer = JsonNode.Parse(text)!.AsObject();

                block["text"] = answer;
                answers.Add(answer);
            }
        }

        return answers;
    }
}
