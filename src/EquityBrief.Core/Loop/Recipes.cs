using System.Globalization;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Loop;

// From 24.30 a reading recipe: a new reading made from fields the store holds, one line of a closed set of operations a
// session at a time: a change over a number of sessions, a ratio of two terms, an average over a number of sessions, a
// rank within the index or the industry, and a difference from its own history. A term is a field of the ledger's
// catalogue or an operation over terms, at most two operations deep, each span from a closed list. A recipe stands apart
// from the pinned catalogue: no rule's version reads it, and it is computed into a table of its own as a reading on test.
// see: A reading recipe is a closed set of operations over stored fields and is kept apart from the pinned catalogue
public abstract record RecipeTerm
{
    public abstract int Depth { get; }

    public abstract string Canonical { get; }

    // The fields the term reads, each once.
    public abstract IEnumerable<string> Fields { get; }

    // Whether the term ranks within the industry anywhere.
    public abstract bool ByIndustry { get; }
}

public sealed record RecipeField(string Column) : RecipeTerm
{
    public override int Depth => 0;

    public override string Canonical => Column;

    public override IEnumerable<string> Fields => [Column];

    public override bool ByIndustry => false;
}

public sealed record RecipeOperation(string Operation, RecipeTerm First, RecipeTerm? Second, int Span, string? Within) : RecipeTerm
{
    public override int Depth => 1 + Math.Max(First.Depth, Second?.Depth ?? 0);

    public override string Canonical => Operation switch
    {
        Recipes.Ratio => $"{Operation}({First.Canonical},{Second!.Canonical})",
        Recipes.Rank => $"{Operation}({First.Canonical},{Within})",
        _ => string.Create(CultureInfo.InvariantCulture, $"{Operation}({First.Canonical},{Span})"),
    };

    public override IEnumerable<string> Fields => First.Fields.Concat(Second?.Fields ?? []).Distinct(StringComparer.Ordinal);

    public override bool ByIndustry => (Operation == Recipes.Rank && Within == Recipes.Industry) || First.ByIndustry || (Second?.ByIndustry ?? false);
}

// What a recipe's line was read as: the term, or why it was refused, and each field it names that the store does not
// hold, which is listed for the operator and blocks nothing.
public sealed record RecipeRead(RecipeTerm? Term, string? Why, IReadOnlyList<string> Lacking);

// The members' readings a recipe is computed over, a session at a time: for each session of the calendar, whether each
// member stood in the index then, and its figure of each field the recipe reads, by the field's place in the catalogue;
// and its industry as it stood where one is held. A panel cut after a session holds nothing later, which the check reads
// a recipe's figure again over.
public sealed class RecipePanel(
    IReadOnlyList<DateOnly> calendar,
    IReadOnlyDictionary<string, bool[]> members,
    IReadOnlyDictionary<int, IReadOnlyDictionary<string, double?[]>> fields,
    Func<string, int, string?>? industryOf = null)
{
    public IReadOnlyList<DateOnly> Calendar => calendar;

    public IEnumerable<string> Members => members.Keys;

    public bool HoldsIndustries => industryOf is not null;

    public string? IndustryOf(string ticker, int session) => industryOf?.Invoke(ticker, session);

    // A panel from each member's catalogue readings on each session, none on a session it stood in no index.
    public static RecipePanel Of(IReadOnlyList<DateOnly> calendar, IReadOnlyDictionary<string, IReadOnlyList<double?[]?>> rows, Func<string, int, string?>? industryOf = null) => new(
        calendar,
        rows.ToDictionary(pair => pair.Key, pair => Enumerable.Range(0, calendar.Count).Select(at => at < pair.Value.Count && pair.Value[at] is not null).ToArray(), StringComparer.Ordinal),
        Enumerable.Range(0, LedgerReadings.Count).ToDictionary(
            place => place,
            place => (IReadOnlyDictionary<string, double?[]>)rows.ToDictionary(pair => pair.Key, pair => Enumerable.Range(0, calendar.Count).Select(at => at < pair.Value.Count ? pair.Value[at]?[place] : null).ToArray(), StringComparer.Ordinal)),
        industryOf);

    // A member's reading of a field on a session, none where it was no member then or the field holds none.
    public double? Value(string ticker, int place, int session) =>
        MemberOn(ticker, session) && fields.TryGetValue(place, out var byMember) && byMember.TryGetValue(ticker, out var figures) && session < figures.Length
            ? figures[session]
            : null;

    public bool MemberOn(string ticker, int session) =>
        session >= 0 && session < calendar.Count && members.TryGetValue(ticker, out var on) && session < on.Length && on[session];

    // The same panel with every session after the one named removed.
    public RecipePanel Through(int session) => new(
        [.. calendar.Take(session + 1)],
        members.ToDictionary(pair => pair.Key, pair => pair.Value.Take(session + 1).ToArray(), StringComparer.Ordinal),
        fields.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, double?[]>)pair.Value.ToDictionary(one => one.Key, one => one.Value.Take(session + 1).ToArray(), StringComparer.Ordinal)),
        industryOf is null ? null : (ticker, at) => at <= session ? industryOf(ticker, at) : null);
}

public static class Recipes
{
    public const string Change = "change";
    public const string Ratio = "ratio";
    public const string Average = "average";
    public const string Rank = "rank";
    public const string History = "history";

    public const string Index = "index";
    public const string Industry = "industry";

    // The spans a change, an average or a difference from its own history may read, in sessions.
    public static IReadOnlyList<int> Spans { get; } = [5, 10, 21, 63, 126, 252];

    // The most operations a recipe nests.
    public const int MostDepth = 2;

    public static IReadOnlyList<string> Operations { get; } = [Change, Ratio, Average, Rank, History];

    // The fields a recipe may read: the ledger's catalogue, which the readings of every member on every session hold.
    public static IReadOnlyList<string> Fields { get; } = [.. LedgerReadings.All.Select(reading => reading.Column)];

    // What the store lacks for ranking within an industry: each member's industry as it stood on each session.
    public const string IndustryAsItStood = "each member's industry as it stood on each session";

    // A line read into a term: spaces and case set aside, every operation of the closed list with its operands and span,
    // at most two deep; a field the store does not hold refuses the line and is listed beside it.
    public static RecipeRead Parse(string text)
    {
        var reader = new Reader(text.ToLowerInvariant());
        var lacking = new List<string>();
        RecipeTerm? term;
        string? why = null;

        try
        {
            term = reader.Term(lacking);
            reader.End();
        }
        catch (FormatException refused)
        {
            return new RecipeRead(null, refused.Message, lacking.Distinct(StringComparer.Ordinal).ToArray());
        }

        if (lacking.Count > 0)
        {
            why = "it names a field the store does not hold: " + string.Join(", ", lacking.Distinct(StringComparer.Ordinal));
            term = null;
        }
        else if (term.Depth > MostDepth)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"it nests {term.Depth} operations, and a recipe nests at most {MostDepth}");
            term = null;
        }
        else if (term is RecipeField)
        {
            why = "it is a field of the catalogue already, and a recipe makes a new reading from one";
            term = null;
        }

        return new RecipeRead(term, why, lacking.Distinct(StringComparer.Ordinal).ToArray());
    }

    // What a recipe needs that the store lacks, beside the fields its line names: each member's industry as it stood where
    // it ranks within the industry and the panel holds none.
    public static IReadOnlyList<string> Needs(RecipeTerm term, bool holdsIndustries) =>
        term.ByIndustry && !holdsIndustries ? [IndustryAsItStood] : [];

    // A member's figure of a term on a session, read from that session and those before it alone.
    public static double? Evaluate(RecipeTerm term, RecipePanel panel, string ticker, int session)
    {
        if (!panel.MemberOn(ticker, session))
        {
            return null;
        }

        switch (term)
        {
            case RecipeField field:
                return panel.Value(ticker, LedgerReadings.IndexOf(field.Column), session);

            case RecipeOperation { Operation: Change } change:
                return session - change.Span >= 0 && Evaluate(change.First, panel, ticker, session) is { } now && Evaluate(change.First, panel, ticker, session - change.Span) is { } then
                    ? now - then
                    : null;

            case RecipeOperation { Operation: Ratio } ratio:
                return Evaluate(ratio.First, panel, ticker, session) is { } top && Evaluate(ratio.Second!, panel, ticker, session) is { } bottom && bottom != 0
                    ? top / bottom
                    : null;

            case RecipeOperation { Operation: Average } average:
                return Mean(average.First, panel, ticker, session - average.Span + 1, session);

            case RecipeOperation { Operation: History } history:
                return Evaluate(history.First, panel, ticker, session) is { } today && Mean(history.First, panel, ticker, session - history.Span, session - 1) is { } before
                    ? today - before
                    : null;

            case RecipeOperation { Operation: Rank } rank:
                return RankOf(rank, panel, ticker, session);

            default:
                return null;
        }
    }

    // The mean of a term over the sessions from one to another, none where any of them holds none.
    static double? Mean(RecipeTerm term, RecipePanel panel, string ticker, int from, int to)
    {
        if (from < 0)
        {
            return null;
        }

        var sum = 0.0;

        for (var at = from; at <= to; at++)
        {
            if (Evaluate(term, panel, ticker, at) is not { } value)
            {
                return null;
            }

            sum += value;
        }

        return sum / (to - from + 1);
    }

    // A member's place among the members holding the term on the session, within the index or its own industry as it
    // stood: the share of the others standing below it, a tie counting half, from 0 at the bottom to 1 at the top; none
    // where fewer than two hold it, or where it ranks within an industry the panel holds none of.
    static double? RankOf(RecipeOperation rank, RecipePanel panel, string ticker, int session)
    {
        if (Evaluate(rank.First, panel, ticker, session) is not { } own)
        {
            return null;
        }

        string? industry = null;

        if (rank.Within == Industry && (industry = panel.IndustryOf(ticker, session)) is null)
        {
            return null;
        }

        var below = 0.0;
        var others = 0;

        foreach (var peer in panel.Members)
        {
            if (string.Equals(peer, ticker, StringComparison.Ordinal) || (industry is not null && !string.Equals(panel.IndustryOf(peer, session), industry, StringComparison.Ordinal)))
            {
                continue;
            }

            if (Evaluate(rank.First, panel, peer, session) is not { } value)
            {
                continue;
            }

            others++;
            below += value < own ? 1 : value == own ? 0.5 : 0;
        }

        return others == 0 ? null : below / others;
    }

    // Every member's figure of a term on every session of the panel at once, worked a node at a time as Evaluate works one
    // figure, so a rank reads each member's figure on the session once.
    public static IReadOnlyDictionary<string, double?[]> All(RecipeTerm term, RecipePanel panel)
    {
        var sessions = panel.Calendar.Count;
        string[] members = [.. panel.Members];

        double?[] Masked(string ticker, Func<int, double?> figure)
        {
            var figures = new double?[sessions];

            for (var at = 0; at < sessions; at++)
            {
                figures[at] = panel.MemberOn(ticker, at) ? figure(at) : null;
            }

            return figures;
        }

        static double? MeanOf(double?[] figures, int from, int to)
        {
            if (from < 0)
            {
                return null;
            }

            var sum = 0.0;

            for (var at = from; at <= to; at++)
            {
                if (figures[at] is not { } value)
                {
                    return null;
                }

                sum += value;
            }

            return sum / (to - from + 1);
        }

        if (term is RecipeField field)
        {
            var place = LedgerReadings.IndexOf(field.Column);

            return members.ToDictionary(ticker => ticker, ticker => Masked(ticker, at => panel.Value(ticker, place, at)), StringComparer.Ordinal);
        }

        var operation = (RecipeOperation)term;
        var first = All(operation.First, panel);

        switch (operation.Operation)
        {
            case Change:
                return members.ToDictionary(ticker => ticker, ticker => Masked(ticker, at => at - operation.Span >= 0 && first[ticker][at] is { } now && first[ticker][at - operation.Span] is { } then ? now - then : null), StringComparer.Ordinal);

            case Ratio:
                var second = All(operation.Second!, panel);

                return members.ToDictionary(ticker => ticker, ticker => Masked(ticker, at => first[ticker][at] is { } top && second[ticker][at] is { } bottom && bottom != 0 ? top / bottom : null), StringComparer.Ordinal);

            case Average:
                return members.ToDictionary(ticker => ticker, ticker => Masked(ticker, at => MeanOf(first[ticker], at - operation.Span + 1, at)), StringComparer.Ordinal);

            case History:
                return members.ToDictionary(ticker => ticker, ticker => Masked(ticker, at => first[ticker][at] is { } today && MeanOf(first[ticker], at - operation.Span, at - 1) is { } before ? today - before : null), StringComparer.Ordinal);

            default:
                var ranks = members.ToDictionary(ticker => ticker, _ => new double?[sessions], StringComparer.Ordinal);

                for (var at = 0; at < sessions; at++)
                {
                    var session = at;
                    var groups = members
                        .Where(ticker => panel.MemberOn(ticker, session) && first[ticker][session] is not null)
                        .GroupBy(ticker => operation.Within == Industry ? panel.IndustryOf(ticker, session) : Index, StringComparer.Ordinal);

                    foreach (var group in groups.Where(group => group.Key is not null))
                    {
                        string[] held = [.. group];

                        foreach (var ticker in held)
                        {
                            var own = first[ticker][session]!.Value;
                            var below = 0.0;

                            foreach (var peer in held)
                            {
                                if (!string.Equals(peer, ticker, StringComparison.Ordinal))
                                {
                                    var value = first[peer][session]!.Value;

                                    below += value < own ? 1 : value == own ? 0.5 : 0;
                                }
                            }

                            ranks[ticker][session] = held.Length < 2 ? null : below / (held.Length - 1);
                        }
                    }
                }

                return ranks;
        }
    }

    // Whether a figure reads nothing after its session: worked again over the panel with every later session removed, and
    // the same, a figure or none on both.
    public static bool ReadsNothingLater(Func<RecipePanel, string, int, double?> figure, RecipePanel panel, string ticker, int session) =>
        (figure(panel, ticker, session), figure(panel.Through(session), ticker, session)) switch
        {
            (null, null) => true,
            ({ } full, { } cut) => full.Equals(cut),
            _ => false,
        };

    sealed class Reader(string text)
    {
        int at;

        public RecipeTerm Term(List<string> lacking)
        {
            var name = Name();

            if (!Peek('('))
            {
                if (!Fields.Contains(name, StringComparer.Ordinal))
                {
                    lacking.Add(name);
                }

                return new RecipeField(name);
            }

            if (!Operations.Contains(name, StringComparer.Ordinal))
            {
                throw new FormatException($"'{name}' is no operation of the recipes' closed list: {string.Join(", ", Operations)}");
            }

            Expect('(');

            var first = Term(lacking);

            Expect(',');

            RecipeOperation operation;

            switch (name)
            {
                case Ratio:
                    operation = new RecipeOperation(name, first, Term(lacking), 0, null);
                    break;

                case Rank:
                    var within = Name();

                    if (within is not (Index or Industry))
                    {
                        throw new FormatException($"a rank is read within the {Index} or the {Industry}, not '{within}'");
                    }

                    operation = new RecipeOperation(name, first, null, 0, within);
                    break;

                default:
                    var span = Number();

                    if (!Spans.Contains(span))
                    {
                        throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"a span of {span} sessions is not one of the closed list: {string.Join(", ", Spans)}"));
                    }

                    operation = new RecipeOperation(name, first, null, span, null);
                    break;
            }

            Expect(')');

            return operation;
        }

        public void End()
        {
            Skip();

            if (at != text.Length)
            {
                throw new FormatException($"the line goes on past its recipe at '{text[at..]}'");
            }
        }

        string Name()
        {
            Skip();

            var start = at;

            while (at < text.Length && (char.IsAsciiLetterLower(text[at]) || char.IsAsciiDigit(text[at]) || text[at] == '_'))
            {
                at++;
            }

            if (at == start)
            {
                throw new FormatException(at < text.Length ? $"a field or an operation was expected at '{text[at..]}'" : "the line ends where a field or an operation was expected");
            }

            return text[start..at];
        }

        int Number()
        {
            Skip();

            var start = at;

            while (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                at++;
            }

            return at > start && int.TryParse(text[start..at], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number
                : throw new FormatException("a span of sessions was expected");
        }

        bool Peek(char wanted)
        {
            Skip();

            return at < text.Length && text[at] == wanted;
        }

        void Expect(char wanted)
        {
            if (!Peek(wanted))
            {
                throw new FormatException(at < text.Length ? $"'{wanted}' was expected at '{text[at..]}'" : $"'{wanted}' was expected where the line ends");
            }

            at++;
        }

        void Skip()
        {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }
        }
    }
}
