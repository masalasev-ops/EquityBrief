using System.Globalization;
using System.Text;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Research;

// One document as a prompt carries it: the position the prose cites it by, and
// what it says.
public sealed record PromptDocument(string Id, string Title, DateOnly? PublishedOn, string Body);

// What a model is asked to write for one section, built from the facts file and
// the documents handed in and nothing else.
//
// The model never fetches, so everything it may say arrives here. And the claim
// checker's rules are what the section will be read against, so the instructions
// are those rules in the words a writer needs rather than a style: a figure is
// copied or rounded from a fact listed, every sentence of a researched section
// cites a listed document, and numbers from eleven up are written in digits.
// see: The model never fetches; components fetch and hand it documents
// see: A claim is a sentence, and every sentence in a researched section names the document it rests on
public static class SectionPrompt
{
    public const string Lane = "local";

    // The lane a paid section is asked in, which is part of its recording's key, so the
    // same section over the same evidence asked of either model is two recordings.
    public const string PaidLane = "paid";

    // What any section is written under. Held as constants because they are part of
    // every recording's key, so a word changed here is a pass nothing recorded.
    //
    // The rounding sentence arrived with the first pass a model wrote from a facts
    // file, whose accepted key under each figure quoted "revenue of 1846000000.00"
    // and "a gross margin of 0.658722": right, and copied as the store holds them.
    // A margin is named rather than every fraction below one, because the MACD
    // histogram is one too and a percentage of it would pass the number rule while
    // saying something nobody computed.
    public const string Instructions =
        "You write one section of an equity research report. Write plain prose with no headings, no lists, no markdown and no preamble. "
        + "Every figure you write must be one of the facts listed, copied or rounded, and written in digits. Write no figure that is not listed, and no date that is not listed. "
        + "Round an amount of money to millions or billions and write the unit, write a margin as a percentage, and write any other figure to at most two decimal places. "
        + "Write a count from one to ten in words and anything larger in digits. "
        + "If the facts and documents do not support the section, write nothing.";

    // The citation instruction, given only where documents are listed. It was part of
    // every call until the first replay, where a section handed no document put a
    // citation to a first document on every sentence, having been told how to cite
    // one, and was refused for citing past a source list that held nothing.
    public const string Citing =
        "End every sentence with the document it rests on, written as [D1] for the first document listed, [D2] for the second, and so on. Write no sentence that no listed document supports.";

    // The instructions for a call, which carry the citation rule where there is
    // something to cite.
    public static string System(IReadOnlyList<PromptDocument> documents) =>
        documents.Count > 0 ? Instructions + " " + Citing : Instructions;

    // What each section asks for beyond the instructions, by figure 12.2's names.
    public static readonly IReadOnlyDictionary<string, string> Asks = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Which document sits inside which move is worked out by code and listed, and
        // the model is asked for the words. A model asked only for a cause per move
        // cited an August release for a February move; told each span and asked to
        // compare the dates itself, it cited the same release for five moves in
        // February and March and wrote nothing for the one move the release falls
        // inside. Comparing two dates is arithmetic, and code owns it.
        // see: A cause of a move rests only on a document published inside that move
        // see: Code owns every number
        ["The cause of each large move"] =
            "For each move listed under Moves with a document beside it, write one sentence that begins with the session the move ended on "
            + "and says what that document gives as the cause of the move, ending with that document's marker. "
            + "Write nothing about any move not listed there.",
        ["What the company sells"] =
            "Say what the company sells and to whom, in two to four sentences, using only the documents listed.",
        ["The segment commentary"] =
            "Write one sentence for each business segment whose figures are listed, saying what that segment reported for the quarter, using only the segment facts listed.",
        ["The key under each figure"] =
            "In three to five sentences, say what the close, the averages, the latest quarter and the valuation listed in the facts show, for a reader who has not seen the figures.",
        ["The industry cycle"] =
            "Say where the industry's own prices are in their cycle and the three things capping or driving them, using only the documents listed.",
        ["The dated calendar items"] =
            "List, one sentence each, the dated events the documents name that fall after the session stated below, each with the date a listed document gives for it and ending with that document's marker. "
            + "Write no event dated on or before that session, and no date no listed document states.",
        ["The two cases"] =
            "Write the bull case and the bear case side by side, each ending in what it needs to see at the next report.",
        ["The risks, each with what would confirm it"] =
            "Name each risk to the company that the documents support, with the figure or event that would confirm it.",
        ["The short version"] =
            "In three or four paragraphs, say what is true, what the market is arguing about, and what the plan therefore is.",
    };

    public static string Prompt(
        string ticker,
        string section,
        IReadOnlyList<Fact> facts,
        IReadOnlyList<PromptDocument> documents,
        string? refusedBecause = null,
        IReadOnlyList<(string Section, string Prose)>? written = null,
        DateOnly? night = null)
    {
        if (!Asks.TryGetValue(section, out var ask))
        {
            throw new InvalidOperationException(
                $"'{section}' is not a section figure 12.2 names, so there is nothing to ask a model to write for it.");
        }

        var prompt = new StringBuilder();

        prompt.Append("Company: ").Append(ticker).Append('\n');
        prompt.Append("Section: ").Append(section).Append('\n');
        prompt.Append(ask).Append("\n\n");

        // The night the facts were computed for, given to the one section whose dates are
        // held after it, because nothing in the facts file states which night that is.
        if (night is { } computed && string.Equals(section, ClaimRules.CalendarSection, StringComparison.Ordinal))
        {
            prompt.Append("Session: ").Append(computed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\n\n");
        }

        if (string.Equals(section, ClaimRules.CauseSection, StringComparison.Ordinal))
        {
            prompt.Append("Moves, each with the listed documents published inside it:\n");

            foreach (var (move, markers) in MovesWithDocuments(facts, documents))
            {
                prompt.Append("- ").Append(move.Name)
                    .Append(", from ").Append(move.From!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    .Append(" to ").Append(move.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    .Append(": ").Append(string.Join(", ", markers)).Append('\n');
            }

            prompt.Append('\n');
        }

        prompt.Append("Facts:\n");

        foreach (var fact in facts)
        {
            prompt.Append("- ").Append(fact.Name).Append(" = ").Append(fact.Value).Append('\n');
        }

        if (documents.Count > 0)
        {
            prompt.Append("\nDocuments:\n");

            for (var index = 0; index < documents.Count; index++)
            {
                var document = documents[index];

                prompt.Append("[D").Append((index + 1).ToString(CultureInfo.InvariantCulture)).Append("] ")
                    .Append(document.Title);

                if (document.PublishedOn is { } published)
                {
                    prompt.Append(" (").Append(published.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(')');
                }

                prompt.Append('\n').Append(document.Body.Trim()).Append("\n\n");
            }
        }

        // The sections the checker has already accepted, handed to the one section figure
        // 12.2 says is written last and from them. Absent from every other prompt, so a
        // recording made before any section was written is still the request it answers.
        if (written is { Count: > 0 })
        {
            prompt.Append("\nSections already written:\n");

            foreach (var (name, prose) in written)
            {
                prompt.Append(name).Append(":\n").Append(prose.Trim()).Append("\n\n");
            }
        }

        // The one retry, told why the first draft was refused, because a second draft
        // written blind would fail for the same reason.
        if (refusedBecause is { Length: > 0 })
        {
            prompt.Append("\nYour previous draft of this section was refused by the checker for: ")
                .Append(refusedBecause)
                .Append(". Write it again so that it is not.\n");
        }

        return prompt.ToString();
    }

    public static ModelRequest Request(
        string model,
        string ticker,
        string section,
        IReadOnlyList<Fact> facts,
        IReadOnlyList<PromptDocument> documents,
        string? refusedBecause = null,
        IReadOnlyList<(string Section, string Prose)>? written = null,
        DateOnly? night = null) =>
        new(Lane, section, model, [.. documents.Select(document => document.Id)], System(documents), Prompt(ticker, section, facts, documents, refusedBecause, written, night));

    // The same request asked in the paid lane.
    public static ModelRequest PaidRequest(
        string model,
        string ticker,
        string section,
        IReadOnlyList<Fact> facts,
        IReadOnlyList<PromptDocument> documents,
        string? refusedBecause = null,
        IReadOnlyList<(string Section, string Prose)>? written = null,
        DateOnly? night = null) =>
        Request(model, ticker, section, facts, documents, refusedBecause, written, night) with { Lane = PaidLane };

    // Each stored move that a handed document was published inside, with the markers
    // of those documents in the order the prompt lists them. A move no document falls
    // inside is left out, and so is one whose start the facts file does not carry,
    // since nothing can be shown to fall inside a span with no start.
    public static IReadOnlyList<(MoveWindow Move, IReadOnlyList<string> Markers)> MovesWithDocuments(
        IReadOnlyList<Fact> facts,
        IReadOnlyList<PromptDocument> documents)
    {
        var paired = new List<(MoveWindow, IReadOnlyList<string>)>();

        foreach (var move in MoveWindows.In(facts))
        {
            var markers = new List<string>();

            for (var index = 0; index < documents.Count; index++)
            {
                if (documents[index].PublishedOn is { } published && MoveWindows.Holds(move, published))
                {
                    markers.Add("D" + (index + 1).ToString(CultureInfo.InvariantCulture));
                }
            }

            if (markers.Count > 0)
            {
                paired.Add((move, markers));
            }
        }

        return paired;
    }

    // ---- what the machine can hold ----

    // How many tokens a call's prompt comes to, estimated in two halves.
    //
    // Measured as the runtime's own prompt token count against what was sent, over
    // the seven section calls recorded at 6.6 on qwen/qwen3.5-9b, one ratio does not
    // hold: 3.57 characters a token where a prompt carries a release and 2.36 where
    // it carries a facts file alone. What moves it is the figures, because this
    // model's tokeniser reads a number a digit at a time. A constant under the lowest
    // ratio would refuse every document-sized prompt a third of the way to the
    // context; one above it undercounts a facts file.
    //
    // So a digit is counted as a token, and so is every byte of a character outside
    // ASCII. Neither can come to more than one token a byte in a tokeniser that works
    // on bytes, so that half is a ceiling rather than a measurement. The rest of the
    // text is counted at a ratio set under the lowest measured over that text alone,
    // which ran from 3.16 on the shortest prompts, where the chat template's own
    // tokens weigh most, to 4.54.
    //
    // It remains an estimate, and what catches one that runs low is the runtime
    // rather than a fluent answer: a prompt past the loaded context was captured
    // refused with a 400 naming its own token count before any of it was read, which
    // the feed passes on as the reason. A test holds the estimate at or above the
    // runtime's count for every recorded call.
    public const decimal CharactersPerToken = 2.75m;

    public static int EstimatedTokens(ModelRequest request)
    {
        var counted = 0;
        var rest = 0;

        foreach (var rune in (request.System + request.Prompt).EnumerateRunes())
        {
            if (!rune.IsAscii)
            {
                counted += rune.Utf8SequenceLength;
            }
            else if (Rune.IsDigit(rune))
            {
                counted++;
            }
            else
            {
                rest++;
            }
        }

        return counted + (int)Math.Ceiling(rest / CharactersPerToken);
    }

    // Whether the machine can hold a call: its prompt and the answer's budget inside
    // the context the local model is loaded with. Asked before the call is made,
    // because attempting one that does not fit produces either an out-of-memory
    // failure or a fluent answer from a model given half its evidence.
    public static bool Fits(ModelRequest request, int contextTokens) =>
        EstimatedTokens(request) + OpenAiCompatibleModelFeed.AnswerTokens <= contextTokens;
}
