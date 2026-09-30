using System.Globalization;
using System.Text;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Research;

// One document as a prompt carries it: the position the prose cites it by, what it
// says, and the kind of document it is where that was read from its address.
public sealed record PromptDocument(string Id, string Title, DateOnly? PublishedOn, string Body, string? Kind = null)
{
    // A stored document as a prompt lists it, its kind read from its address.
    // see: The research prompt repeats each section's ask after the documents, names its reader and marks each document by kind
    public static PromptDocument From(StoredDocument document, string? body = null) =>
        new(document.Id, document.Title, document.PublishedOn, body ?? document.Body ?? string.Empty, DocumentKinds.Of(document.Url));
}

// What a model is asked to write for one section, built from the facts file and
// the documents handed in and nothing else.
//
// The model never fetches, so everything it may say arrives here. And the claim
// checker's rules are what the section will be read against, so the instructions
// are those rules in the words a writer needs rather than a style: a figure is
// copied or rounded from a fact listed, every sentence of a researched section
// cites a listed document, and numbers from eleven up are written in digits.
// see: The model never fetches; components fetch and hand it documents
// see: A claim is a sentence, every sentence in a researched section names the document it rests on, and a window written in words is read as its number
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
    //
    // A section describes and never prescribes, because the trade plan is computed by code
    // and drawn in a region of its own, and a written plan beside it can contradict it. The
    // corpus's two prose rules are stated because the checker refuses a sentence breaking
    // either. And nothing to write is no text at all: a writer told only to write nothing
    // answered with an invisible character and, asked again, with sentences saying so.
    // see: A written section describes the company and never proposes a trade, a holding or a plan for income
    //
    // Two sentences name what to do beside what not to do: what a figure a document gives and the facts do not shows
    // is said without the number, and so is a change no listed figure sizes, since a writer told only what to leave
    // out wrote the figure anyway, and one told to state it in words spelled the number out, which the checker
    // refuses as it refuses the figure. And the reader is named, so a section chooses what could move the stock over the weeks a swing trade is
    // held for and leaves out what would not, which is still a description and never a plan.
    // see: The research prompt repeats each section's ask after the documents, names its reader and marks each document by kind
    public const string Instructions =
        "You write one section of an equity research report. Write plain prose with no headings, no lists, no markdown and no preamble. "
        + "Every figure you write must be one of the facts listed, copied or rounded, and written in digits. Write no figure that is not listed, and no date that is not listed. "
        + "Where a document gives a figure that is not listed under Facts, say what it shows without the number, such as that orders rose sharply or that most of the growth came from a single segment, and never write it as a figure or spell the number out; describe a change no listed figure sizes the same way, against its base. "
        + "Round an amount of money to millions or billions and write the unit, write a margin as a percentage, and write any other figure to at most two decimal places. "
        + "Write a count from one to ten in words and anything larger in digits. "
        + "The reader is deciding whether to hold a swing trade in this stock over the next weeks to few months, so choose what could move the stock before and at its next report and leave out what would not. "
        + "Describe the company and never prescribe: propose no trade, no holding, no adding or trimming and no plan for income, because the trade plan is computed separately. "
        + "Write no em dash, and never call a statement or a view candid or frank. "
        + "If the facts and documents do not support the section, answer with no text at all: no character, no mark and no sentence saying so.";

    // The citation instruction, given only where documents are listed. It was part of
    // every call until the first replay, where a section handed no document put a
    // citation to a first document on every sentence, having been told how to cite
    // one, and was refused for citing past a source list that held nothing. The sentence
    // opening a paragraph is named because those were the ones a writer left uncited: a
    // risk's own sentence ahead of the confirmation it cited, and a short version's framing.
    public const string Citing =
        "End every sentence with the document it rests on, written as [D1] for the first document listed, [D2] for the second, and so on, "
        + "the sentence opening a paragraph as well as the rest. Write no sentence that no listed document supports, and none about what you are writing or leaving out. "
        + Kinds;

    // Each document is listed with its kind, and a point rests on what a company filed or a reporter reported: an
    // opinion or a promotion is what its writer argues, and a section citing one says that it is.
    // see: The research prompt repeats each section's ask after the documents, names its reader and marks each document by kind
    public const string Kinds =
        "Each document is marked as a " + DocumentKinds.Filed + ", a " + DocumentKinds.News + " or an " + DocumentKinds.Opinion + ". "
        + "Rest a point on a filing, a release or a news report, and cite an opinion or promotional piece only for what its writer argues, saying that it is their argument.";

    // The instructions for a call, which carry the citation rule where there is
    // something to cite.
    public static string System(IReadOnlyList<PromptDocument> documents) =>
        documents.Count > 0 ? Instructions + " " + Citing : Instructions;

    // The risks are answered as fields, so their call opens on the answer's form in place of plain prose and
    // names each document by its number in the field beside the words, where code puts the marker.
    // see: Each risk is returned as fields and confirmed by a listed fact or an event of one kind, and no two risks share either
    public const string AsFields =
        "You write one section of an equity research report as the JSON object the section asks for, with no markdown, no code fence and no preamble.";

    public const string CitingFields =
        "Name the listed document each field rests on by its number, 1 for the first document listed, 2 for the second, and so on, and write no marker inside a field. "
        + Kinds;

    public static string System(IReadOnlyList<PromptDocument> documents, string section) =>
        RiskFields.IsRisks(section)
            ? AsFields + Instructions[Instructions.IndexOf(" Every figure", StringComparison.Ordinal)..] + (documents.Count > 0 ? " " + CitingFields : string.Empty)
            : System(documents);

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
            + "Where no document beside a move gives a cause for it going the way it went, write nothing for that move. "
            + "Write nothing about any move not listed there.",
        ["What the company sells"] =
            "Say what the company sells and to whom, in two to four sentences, using only the documents listed.",
        // The segments that moved and why, and never every segment, which the numbers table
        // already draws line by line.
        ["The segment commentary"] =
            "Name the business segments whose figures moved most or changed direction, at most four, one sentence each, saying what each reported for the quarter "
            + "and what the documents listed give as the cause. Never go through every segment, because the numbers table already shows each of them. "
            + "Use only the segment facts and the documents listed.",
        // Handed its facts as a reader reads them and asked to copy them, because asked to
        // round them the model cut digits off, and asked nothing it copied six places.
        // see: The key under each figure is handed its facts as a reader reads them, rounded by code
        ["The key under each figure"] =
            "In three to five sentences, say what the close, the averages, the latest quarter and the valuation listed in the facts show, for a reader who has not seen the figures. "
            + "Write every number in digits. Each figure listed is already rounded, so copy it and its name as they are listed. Write no figure you work out yourself.",
        // Asked with no figure and no full date, because a theme has no facts file and every
        // figure in a theme section is refused: which way the industry's prices are moving
        // and why, in words, each sentence resting on a document.
        // see: A theme section states no figure, because nothing the store holds is computed for an industry
        ["The industry cycle"] =
            "Say where the industry's own prices are in their cycle and the three things capping or driving them, using only the documents listed. "
            + "No facts are listed for an industry, so write no figure and no date: say in words which way prices are moving and what is moving them.",
        ["The dated calendar items"] =
            "List, one sentence each, the dated events of the company named in the facts that the documents name and that fall after the session stated below, each with the date a listed document gives for it and ending with that document's marker. "
            + "Write no event dated on or before that session, and no date no listed document states.",
        // The two cases and the risks are asked for in the shape the name page reads its parts by:
        // each case a paragraph opening on its own name, a point to a sentence, and each risk a
        // paragraph opening on its ordinal with what would confirm it in a sentence of its own. A
        // writer that answers in another shape is still drawn, a claim to a row, since the page
        // cuts only where the prose says a part ends. A point that could be said of any company
        // tells the reader nothing about this one, and a confirmation that is a bare threshold,
        // or one the next quarter crosses by construction, confirms nothing about the risk.
        // see: A written section is drawn a claim to a row, and the two cases and the risks are asked for in the parts the page draws
        // Each point a reason and not a figure restated, a change with its size so a small one reads as small,
        // and a fact on one side only. No change figure is listed, so a size is given by the two listed figures
        // or in words against the base, since a difference the writer works out is a figure no fact holds.
        // see: The two cases are asked to argue a fact on one side only, and a draft doing otherwise is counted rather than refused
        ["The two cases"] =
            "Write the bull case as one paragraph that begins \"The bull case\", then the bear case as a second paragraph that begins \"The bear case\". "
            + "Give each point its own sentence, and end each case with a sentence saying what it needs to see at the next report. "
            + "Make each point specific to this company and its latest documents, and leave out any sentence that could be said of any company. "
            + "Make each point a reason, not a figure restated. Describe a change with its size, so a small change reads as small, using the listed figures at both ends or words against its base, "
            + "and never a difference you work out yourself. Let each fact argue one side only: a figure used in the bull case does not appear in the bear case, closing sentences included. "
            + Examples.TwoCases,
        // The risks are asked for as fields, which code composes into the paragraphs the page draws, so that
        // what confirms each is a listed fact or an event of one kind and code can tell two risks apart. A
        // confirmation is what would be seen if the risk came true, and three risks confirmed by one miss of
        // guidance are one risk.
        // see: Each risk is returned as fields and confirmed by a listed fact or an event of one kind, and no two risks share either
        ["The risks, each with what would confirm it"] =
            "Name each risk to the company that the documents support, and answer with one JSON object and nothing else, shaped as "
            + "{\"risks\":[{\"risk\":\"...\",\"riskDocument\":1,\"confirm\":{\"fact\":\"...\"},\"direction\":\"rises above\",\"level\":\"...\",\"why\":\"...\",\"whyDocument\":1}]}. "
            + "Write risk as words completing \"The first risk is\", such as \"that supply stays short of demand\", and riskDocument as the number of the listed document that states it. "
            + "Let confirm name what would be seen if the risk came true: a listed fact wherever one could show it, its name copied exactly as it is listed under Facts, "
            + "with direction \"rises above\" or \"falls below\" and level a figure listed under Facts, copied or rounded. "
            + "Only where no listed fact could show it, such as a lawsuit, a regulator's decision or a competitor's launch, write confirm as {\"event\":\"...\",\"kind\":\"...\"} "
            + "with the event in words and kind one of legal, regulatory, competitive, acquisition, management, supply or other, and give it no direction and no level. "
            + "A valuation risk is confirmed by the multiple falling, not by it staying high, and three risks confirmed by the same miss of guidance cannot be told apart, "
            + "so no two risks name the same fact and no two event risks share a kind. Never use a level the next quarter crosses by construction. "
            + "Write why as words completing \"because\", saying why that movement means the risk is coming true, and whyDocument as the number of the listed document that says so. "
            + "List a real risk even where no figure can show it. "
            + Examples.Risks,
        // What is true and what is argued about, and never a plan: the plan is computed by code
        // and drawn in its own region. The one section that sums the others up is the one a
        // writer opens paragraphs in with a short signpost citing nothing, so it is told that a
        // sentence which only introduces or sums up is not one to write.
        // see: A written section describes the company and never proposes a trade, a holding or a plan for income
        ["The short version"] =
            "In three or four paragraphs, say what the documents and the sections already written show to be true of the company and what the market is arguing about. "
            + "Every sentence, however short, states something a listed document says and ends with its marker, so write no sentence that only introduces, joins or sums up the others. "
            + "Describe it and propose nothing: the trade plan is computed and drawn separately, so write no trade, no holding, no adding or trimming and no plan for income.",
    };

    // A weak point and a strong one for the two sections a writer most often fills with points that could be said of
    // any company, written with no digit, so nothing in them can be copied into a draft or refused in one.
    // see: The research prompt repeats each section's ask after the documents, names its reader and marks each document by kind
    public static class Examples
    {
        public const string TwoCases =
            "For example, a weak point restates that sales grew; a strong one says the growth came from a one-off order that will not repeat, citing the document that says so.";

        public const string Risks =
            "For example, a weak risk says that competition is intense; a strong one names the rival's launch a document describes and what it would take from the company, citing where.";
    }

    // Where the ask is said again: after the documents and the sections already written, which can run long enough
    // that the ask at the top is far behind where the model writes.
    public const string NowWrite = "Now write the section:";

    // The segment commentary over a table that files no quarter. The quarter's ask above
    // stays word for word, since every recording of it is keyed on it.
    // see: A segment figure held for a period longer than a quarter is asked for by that period and refused where its sentence names a period of another length
    public static string SegmentsOverALongerPeriod(int months, string ended) =>
        "Name the business segments whose figures moved most or changed direction, at most four, one sentence each, saying what each reported for the "
        + months.ToString(CultureInfo.InvariantCulture) + " months to " + ended
        + " and what the documents listed give as the cause. Never go through every segment, because the numbers table already shows each of them. "
        + "Each segment figure listed is named with the period it covers, so name a period by its months and never as a quarter, a half or any other period. "
        + "Use only the segment facts and the documents listed.";

    static string AskFor(string section, IReadOnlyList<Fact> facts) =>
        string.Equals(section, Evidence.Segments, StringComparison.Ordinal) && SegmentPeriods.LongerPeriodIn(facts) is { } longer
            ? SegmentsOverALongerPeriod(longer.Months, longer.Ended)
            : Asks[section];

    public static string Prompt(
        string ticker,
        string section,
        IReadOnlyList<Fact> facts,
        IReadOnlyList<PromptDocument> documents,
        string? refusedBecause = null,
        IReadOnlyList<(string Section, string Prose)>? written = null,
        DateOnly? night = null) =>
        Build("Company", ticker, section, facts, documents, refusedBecause, written, night);

    // The industry cycle for a theme, which is the one section asked of an industry rather
    // than of a company, with no facts listed because a theme has none.
    public static string ThemePrompt(string industry, IReadOnlyList<PromptDocument> documents, string? refusedBecause = null) =>
        Build("Industry", industry, ClaimRules.CycleSection, [], documents, refusedBecause, null, null);

    static string Build(
        string label,
        string subject,
        string section,
        IReadOnlyList<Fact> facts,
        IReadOnlyList<PromptDocument> documents,
        string? refusedBecause,
        IReadOnlyList<(string Section, string Prose)>? written,
        DateOnly? night)
    {
        if (!Asks.ContainsKey(section))
        {
            throw new InvalidOperationException(
                $"'{section}' is not a section figure 12.2 names, so there is nothing to ask a model to write for it.");
        }

        var ask = AskFor(section, facts);

        var prompt = new StringBuilder();

        prompt.Append(label).Append(": ").Append(subject).Append('\n');
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
                    .Append(" to ").Append(move.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                // Which way the move went, since the newest documents inside a fall can be about the
                // rise before it.
                if (Went(facts, move) is { } went)
                {
                    prompt.Append(", ").Append(went);
                }

                prompt.Append(": ").Append(string.Join(", ", markers)).Append('\n');
            }

            prompt.Append('\n');
        }

        prompt.Append("Facts:\n");

        var read = string.Equals(section, ClaimRules.ComputedSection, StringComparison.Ordinal);

        foreach (var stored in facts)
        {
            var fact = read ? FactReading.Read(stored) : stored;

            prompt.Append("- ").Append(fact.Name).Append(" = ").Append(fact.Value).Append('\n');
        }

        if (documents.Count > 0)
        {
            prompt.Append("\nDocuments:\n");

            for (var index = 0; index < documents.Count; index++)
            {
                var document = documents[index];

                prompt.Append("[D").Append((index + 1).ToString(CultureInfo.InvariantCulture)).Append("] ");

                if (document.Kind is { Length: > 0 } kind)
                {
                    prompt.Append(kind).Append(": ");
                }

                prompt.Append(document.Title);

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

        // The ask again, word for word, after everything handed in and before any retry's brief.
        // see: The research prompt repeats each section's ask after the documents, names its reader and marks each document by kind
        prompt.Append('\n').Append(NowWrite).Append('\n').Append(ask).Append('\n');

        // The one retry, told each thing the first draft was refused for and what to do about it, because
        // a second draft written blind would fail for the same reason and one told only the reason wrote
        // new figures in place of the ones refused. The brief is written by `RetryBrief` from the refused
        // draft and never carries the draft itself.
        // see: A retry names each thing the check refused, and a second draft repeating one is left out
        if (refusedBecause is { Length: > 0 })
        {
            prompt.Append('\n').Append(refusedBecause.Trim()).Append('\n');
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
        new(Lane, section, model, [.. documents.Select(document => document.Id)], System(documents, section), Prompt(ticker, section, facts, documents, refusedBecause, written, night));

    // A theme's industry cycle, asked in the paid lane, which figure 12.2 puts it in.
    public static ModelRequest ThemeRequest(string model, string industry, IReadOnlyList<PromptDocument> documents, string? refusedBecause = null) =>
        new(PaidLane, ClaimRules.CycleSection, model, [.. documents.Select(document => document.Id)], System(documents), ThemePrompt(industry, documents, refusedBecause));

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

    // Which way a move went and by how much, as its change fact is written in the facts file:
    // "down 13.98 per cent" for a change written with a minus sign, "up" otherwise, and nothing
    // where the file carries no change for the move.
    public static string? Went(IReadOnlyList<Fact> facts, MoveWindow move) =>
        facts.FirstOrDefault(fact => fact.Name == move.Name + " " + MoveWindows.PerCent)?.Value is { Length: > 0 } change
            ? (change.StartsWith('-') ? "down " + change[1..] : "up " + change) + " " + MoveWindows.PerCent
            : null;

    // The largest move of each episode that a handed document was published inside, with the
    // markers of those documents in the order the prompt lists them. The other moves of an
    // episode are the same run of the price and are not listed, so its cause is asked for once.
    // A move no document falls inside is left out, and so is one whose start the facts file
    // does not carry, since nothing can be shown to fall inside a span with no start.
    // see: A research pass reads a name's news from the last three months alone, inside each stored move and since the company's own filing, and hands each section the documents code picks from it
    public static IReadOnlyList<(MoveWindow Move, IReadOnlyList<string> Markers)> MovesWithDocuments(
        IReadOnlyList<Fact> facts,
        IReadOnlyList<PromptDocument> documents)
    {
        var paired = new List<(MoveWindow, IReadOnlyList<string>)>();

        foreach (var move in MoveWindows.Episodes(facts).Select(episode => episode.Largest))
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
