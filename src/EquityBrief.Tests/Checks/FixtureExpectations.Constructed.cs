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
        var constructed = Directory.GetFiles(Folder(), RecordedResearchModelFeed.FilePrefix + "*.json")
            .Select(file => (File: Path.GetFileName(file), Node: JsonNode.Parse(File.ReadAllText(file))!.AsObject()))
            .Where(one => one.Node.FirstOrDefault().Key == "constructed")
            .OrderBy(one => one.File, StringComparer.Ordinal)
            .ToArray();

        // Twelve on DeepSeek's wire and six on Claude's: each paid request the instructions moved in the default pass,
        // the lane comparison's pass and the pass asked of Claude, the short version's retries among them, while the
        // risks, asked under the fields' own citing line, and every request handed no document keep their captures.
        Assert.Equal(12, constructed.Count(one => one.Node.ContainsKey("choices")));
        Assert.Equal(6, constructed.Count(one => one.Node.ContainsKey("response")));

        var sources = new List<string>();

        foreach (var (file, node) in constructed)
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
        Assert.Equal(constructed.Length, sources.Distinct(StringComparer.Ordinal).Count());
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
