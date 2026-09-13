using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Core.Facts;

namespace EquityBrief.Core.Research;

// What a figure in prose is read as.
//
// Only the first is a number the facts file has to hold. The others are numbers
// that are not figures: a year standing alone, a quarter's label, the length of a
// window an average is taken over, and a date, each of which is checked in its
// own way or exempted by rule, and the last is a figure written so nothing can
// match it, which is refused rather than passed.
public enum FigureKind
{
    Figure,
    Year,
    Label,
    Window,
    Date,
    Unmatchable,
}

// One number read out of a sentence.
//
// `Magnitude` is the number as written with its separators removed and its sign
// dropped, `Decimals` is the precision the prose itself states, and `Scale` is
// the multiplier its unit word carries. Precision is read off the prose rather
// than chosen, because what the rule allows is a rounding of a computed value at
// the precision a sentence states and never a number nobody computed.
public sealed record ProseFigure(
    string Text,
    FigureKind Kind,
    decimal Magnitude,
    int Decimals,
    decimal Scale,
    bool Percent,
    DateOnly? Date,
    int? Month,
    int? Day);

// One sentence and the documents it cites, by their position in the section's
// own source list.
public sealed record ProseSentence(string Text, IReadOnlyList<int> Citations);

// One thing wrong with a section, with the text a person needs to see.
public sealed record ClaimFinding(string Sentence, string Offending, string Reason);

// The checker's reading of one section.
public sealed record ClaimVerdict(IReadOnlyList<ClaimFinding> Findings, bool NoAdmissibleSource)
{
    public bool Passes => Findings.Count == 0 && !NoAdmissibleSource;

    // The line a surface draws and the reason a row stores, in one place so the
    // two cannot describe one refusal differently.
    public string Reason =>
        NoAdmissibleSource
            ? ClaimRules.NoAdmissibleSource
            : string.Join("; ", Findings.Select(finding => $"{finding.Reason}: {finding.Offending}"));
}

// The claim checker's rules, as functions of the text, the facts file and the
// stored documents a section cites.
//
// Two rules and they fail apart. Every figure in the prose is a rounding of a
// value the facts file holds, at the precision the prose states.
// see: Every number in written prose must exist in the facts file
// And every sentence of a researched section names a stored document that
// admissibility admitted, by a marker the section's source list resolves.
// see: Every researched claim must name a stored source document
// see: A claim is a sentence, and every sentence in a researched section names the document it rests on
//
// The checker reads the admissibility verdict on the row and never applies the
// test again, which 6.0 ruled: the test runs on a document as it is fetched, and
// the runner is what fetches.
public static class ClaimRules
{
    // ---- the sections ----

    // Figure 12.2's lane table, in its own order and by its own names, which is
    // what the `section` column holds. `claim-admissibility` reads the table back
    // against this list in both directions, so a section added to the figure and
    // not here, or named differently in either, is a failure rather than a
    // section the checker holds to the wrong rule.
    public static readonly string[] Sections =
    [
        "The cause of each large move",
        "What the company sells",
        "The segment commentary",
        "The key under each figure",
        "The industry cycle",
        "The dated calendar items",
        "The two cases",
        "The risks, each with what would confirm it",
        "The short version",
    ];

    // The one section held to the number rule alone. The lane table says it is a
    // fixed explanation over known values with nothing to weigh, so a sentence in
    // it rests on the facts file rather than on a document, and asking it to cite
    // one would be asking it to invent a source for arithmetic.
    public const string ComputedSection = "The key under each figure";

    public static bool IsResearched(string section) =>
        !string.Equals(section, ComputedSection, StringComparison.Ordinal);

    // The one section held to a third rule, because every sentence in it is about a
    // move the facts file dates. A document published months after a move cannot
    // be what caused it, and the first draft a model wrote for this section said
    // exactly that about a February move and an August release.
    // see: A cause of a move rests only on a document published inside that move
    public const string CauseSection = "The cause of each large move";

    // The one section whose every date is a document's rather than the facts file's,
    // because what it lists is events a filing or an article dates that fall after the
    // night the file was computed for, and the file carries none of them. A date in it is
    // held to a document the sentence cites and to that night.
    // see: A dated calendar item's date rests on a document the sentence cites and falls after the night the facts were computed for
    public const string CalendarSection = "The dated calendar items";

    // The one section written per theme rather than per name, into the theme store, and
    // read by every name whose industry the theme is. A theme has no facts file, so every
    // figure in it is refused, which is the rule and not a gap in it.
    // see: Industry research is per theme, not per name
    // see: A theme section states no figure, because nothing the store holds is computed for an industry
    public const string CycleSection = "The industry cycle";

    // ---- the reasons ----

    public const string UnmatchedFigure = "a figure the facts file does not hold";
    public const string UnmatchableFigure = "a figure written in words, which nothing can match";
    public const string UnknownWindow = "a window the facts file names no figure for";
    public const string UnknownDate = "a date the facts file does not hold";
    public const string Uncited = "a sentence naming no document";
    public const string CitationOutOfRange = "a citation past the section's own source list";
    public const string NotStored = "a citation to a document that is not stored";
    public const string RefusedSource = "a citation to a document admissibility refused";
    public const string NoAdmissibleSource = "no admissible source was found";
    public const string CauseNamingNoMove = "a cause naming the session of no move the facts file holds";
    public const string CauseOutsideItsMove = "a citation to a document published outside the move it gives the cause of";
    public const string DateNoCitedDocumentCarries = "a date no document the sentence cites carries";
    public const string DateNotAfterTheNight = "a date on or before the night the facts file was computed for";

    // ---- the check ----

    // One section against its facts file and the stored rows its source list
    // resolves to, in the list's order, with a null where an id resolves to no
    // stored row.
    public static ClaimVerdict Check(
        string section,
        string prose,
        IReadOnlyList<Fact> facts,
        IReadOnlyList<StoredDocument?> sources,
        DateOnly? night = null)
    {
        var researched = IsResearched(section);

        // A researched section whose source list holds nothing admitted has no
        // sentence that could be written, so it is not read at all. It goes to
        // fallback on its first check rather than being handed a retry, because a
        // rewrite cannot create a source.
        if (researched && !sources.Any(source => source is { Admitted: true }))
        {
            return new ClaimVerdict([], NoAdmissibleSource: true);
        }

        var findings = new List<ClaimFinding>();
        var cause = string.Equals(section, CauseSection, StringComparison.Ordinal);
        var calendar = string.Equals(section, CalendarSection, StringComparison.Ordinal);
        var moves = cause ? MoveWindows.In(facts) : [];

        foreach (var sentence in Sentences(prose))
        {
            if (cause)
            {
                findings.AddRange(CauseFindings(sentence, moves, sources));
            }

            if (researched && sentence.Citations.Count == 0)
            {
                findings.Add(new ClaimFinding(sentence.Text, sentence.Text, Uncited));
            }

            foreach (var cited in sentence.Citations)
            {
                if (cited < 1 || cited > sources.Count)
                {
                    findings.Add(new ClaimFinding(sentence.Text, $"D{cited}", CitationOutOfRange));
                }
                else if (sources[cited - 1] is not { } source)
                {
                    findings.Add(new ClaimFinding(sentence.Text, $"D{cited}", NotStored));
                }
                else if (!source.Admitted)
                {
                    findings.Add(new ClaimFinding(sentence.Text, $"D{cited} ({source.Admissibility})", RefusedSource));
                }
            }

            foreach (var figure in Figures(sentence.Text))
            {
                var reason = figure.Kind switch
                {
                    FigureKind.Figure when !Matches(figure, facts) => UnmatchedFigure,
                    FigureKind.Window when !IsAWindow(figure, facts) => UnknownWindow,
                    FigureKind.Date when calendar => CalendarDate(figure, sentence, sources, night),
                    FigureKind.Date when !IsADate(figure, facts) => UnknownDate,
                    FigureKind.Unmatchable => UnmatchableFigure,
                    _ => null,
                };

                if (reason is not null)
                {
                    findings.Add(new ClaimFinding(sentence.Text, figure.Text, reason));
                }
            }
        }

        return new ClaimVerdict(findings, NoAdmissibleSource: false);
    }

    // ---- the cause of a move ----

    // One sentence of the cause section against the moves the facts file dates.
    //
    // A sentence names a move by the session it ended on, which is how the section
    // is asked for and the only date the file held for a move before 6.6. A sentence
    // naming no move's session is about nothing a cause can be checked against, so
    // it is refused rather than passed on its citation alone. And every admitted
    // document it cites has to have been published inside every move it names,
    // because a sentence naming two moves says the document caused both.
    //
    // Only admitted documents are read here. A citation that resolves to nothing, or
    // to a refusal, is already a finding of the citation rule, and a second finding
    // for the same marker would state one fault twice.
    static IEnumerable<ClaimFinding> CauseFindings(
        ProseSentence sentence,
        IReadOnlyList<MoveWindow> moves,
        IReadOnlyList<StoredDocument?> sources)
    {
        var dates = Figures(sentence.Text).Where(figure => figure.Kind == FigureKind.Date).ToArray();

        var named = moves
            .Where(move => dates.Any(date => date.Date is { } full
                ? full == move.To
                : date.Month == move.To.Month && date.Day == move.To.Day))
            .ToArray();

        if (named.Length == 0)
        {
            yield return new ClaimFinding(sentence.Text, sentence.Text, CauseNamingNoMove);

            yield break;
        }

        foreach (var cited in sentence.Citations.Distinct())
        {
            if (cited < 1 || cited > sources.Count || sources[cited - 1] is not { Admitted: true } source)
            {
                continue;
            }

            foreach (var move in named.Where(move => source.PublishedOn is not { } published || !MoveWindows.Holds(move, published)))
            {
                var published = source.PublishedOn is { } on ? Iso(on) : "on no stated date";
                var ended = Iso(move.To);

                var span = move.From is { } from
                    ? "the move ending " + ended + " measured from " + Iso(from)
                    : "the move ending " + ended + ", whose start the facts file does not carry";

                yield return new ClaimFinding(
                    sentence.Text,
                    "D" + cited.ToString(CultureInfo.InvariantCulture) + " published " + published + ", outside " + span,
                    CauseOutsideItsMove);
            }
        }
    }

    // ---- a dated calendar item ----

    // One date of the calendar section, against the documents its sentence cites and
    // the night the facts file was computed for.
    //
    // The date has to be one an admitted document the sentence cites states, read by the
    // reader that reads the prose, so a date the model wrote from nowhere is refused
    // however plausible, and a date in the form the document writes it matches in the
    // form the sentence writes it. And a full date has to fall after the night, because
    // the section lists what is coming: the first draft a model wrote for it named the
    // day an article was published beside the days of the conferences it announced.
    // A month and a day with no year are held to the documents alone, since which year
    // they fall in is what they do not say.
    // see: A dated calendar item's date rests on a document the sentence cites and falls after the night the facts were computed for
    static string? CalendarDate(ProseFigure figure, ProseSentence sentence, IReadOnlyList<StoredDocument?> sources, DateOnly? night)
    {
        var carried = sentence.Citations
            .Distinct()
            .Where(cited => cited >= 1 && cited <= sources.Count)
            .Select(cited => sources[cited - 1])
            .OfType<StoredDocument>()
            .Where(source => source.Admitted && source.Body is { Length: > 0 })
            .Any(source => DatesIn(source.Body!).Any(date => figure.Date is { } full
                ? date.Date == full || date.Date is null && date.Month == full.Month && date.Day == full.Day
                : date.Month == figure.Month && date.Day == figure.Day));

        if (!carried)
        {
            return DateNoCitedDocumentCarries;
        }

        return figure.Date is { } stated && night is { } computed && stated <= computed ? DateNotAfterTheNight : null;
    }

    // Every date a document's text states, read by the prose's own date reader.
    static IEnumerable<ProseFigure> DatesIn(string body) =>
        Figures(body).Where(figure => figure.Kind == FigureKind.Date);

    // ---- sentences and citations ----

    // A citation is a D and the document's position in the section's source list,
    // in square brackets. A position rather than an id, because a model copying a
    // thirty-two character id into prose is a model given the chance to get one
    // character wrong, and the list is stored beside the prose in the order the
    // prose cites it.
    static readonly Regex Citation = new(@"\[D(\d+)\]", RegexOptions.Compiled);

    // Words a period ends without ending a sentence. The four captured articles
    // carry "vs." in a title and "Inc." in a company name, and a split after either
    // leaves a fragment with no citation, which the citation rule would refuse
    // for being a sentence it is not.
    static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "vs", "Inc", "Corp", "Co", "Ltd", "Plc", "St", "Mr", "Mrs", "Ms", "Dr", "No", "approx", "est",
        "e.g", "i.e", "U.S", "U.K", "Jan", "Feb", "Mar", "Apr", "Jun", "Jul", "Aug", "Sep", "Sept",
        "Oct", "Nov", "Dec",
    };

    public static IReadOnlyList<ProseSentence> Sentences(string prose)
    {
        var sentences = new List<ProseSentence>();
        var start = 0;

        foreach (Match boundary in Regex.Matches(prose, @"[.!?](?:\s*\[D\d+\])*(?=\s+|$)"))
        {
            var end = boundary.Index + boundary.Length;

            // A period after an abbreviation or a single initial does not end the
            // sentence it sits in.
            if (prose[boundary.Index] == '.' && EndsWithAbbreviation(prose[start..boundary.Index]))
            {
                continue;
            }

            Add(sentences, prose[start..end]);
            start = end;
        }

        Add(sentences, prose[start..]);

        return sentences;
    }

    static void Add(List<ProseSentence> sentences, string text)
    {
        var trimmed = text.Trim();

        if (trimmed.Length == 0)
        {
            return;
        }

        var citations = Citation.Matches(trimmed)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        // A run of markers standing alone after a terminator belongs to the
        // sentence it follows, which is where a writer who puts the citation after
        // the full stop means it to go.
        if (Citation.Replace(trimmed, string.Empty).Trim().Length == 0 && sentences.Count > 0)
        {
            var previous = sentences[^1];

            sentences[^1] = previous with
            {
                Text = previous.Text + " " + trimmed,
                Citations = [.. previous.Citations, .. citations],
            };

            return;
        }

        sentences.Add(new ProseSentence(trimmed, citations));
    }

    static bool EndsWithAbbreviation(string before)
    {
        var word = Regex.Match(before, @"([A-Za-z][A-Za-z.]*)$");

        if (!word.Success)
        {
            return false;
        }

        var token = word.Groups[1].Value.TrimEnd('.');

        return Abbreviations.Contains(token) || (token.Length == 1 && char.IsUpper(token[0]));
    }

    // ---- figures ----

    // Proper names that carry digits and are not figures. Stated rather than
    // matched on a shape, because the shape of an index name is the shape of a
    // number and a rule keyed on capital letters before digits would exempt
    // "Q3 revenue of 94" along with it.
    public static readonly string[] NamesCarryingDigits = ["S&P 500"];

    // Phrases carrying a number word that name a period rather than state a
    // quantity. One, and it is here because both filed releases the fixture holds
    // report "the twelve months ended", which is the standard reporting period and
    // not a count anybody computed.
    public static readonly string[] PeriodsInWords = ["twelve months", "Twelve months"];

    static readonly Regex IsoDate = new(@"\b(\d{4})-(\d{2})-(\d{2})\b", RegexOptions.Compiled);

    static readonly Regex MonthDate = new(
        @"\b(January|February|March|April|May|June|July|August|September|October|November|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)\.?\s+(\d{1,2})(?:st|nd|rd|th)?(?:,\s*(\d{4}))?\b",

        // Case sensitive, because "may" is a verb far more often than it is a
        // month, and a sentence saying revenue may 5 per cent would otherwise read
        // as a date.
        RegexOptions.Compiled);

    // A quarter or half label, or a fiscal year. What these carry is which period
    // a sentence is about, and a wrong period is caught by the figure beside it
    // failing to match the period's value rather than by the label.
    //
    // A time of day is a label too. Both filed releases announce their call at an
    // hour, and "2:00 p.m." read as two figures was the "00" the measurement found.
    static readonly Regex Label = new(
        @"\b(?:Q[1-4]|H[12]|FY\s?\d{2,4}|fiscal\s+\d{4}|\d{1,2}:\d{2}(?:\s?[ap]\.?m\.?)?)(?![A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // A number, with what the prose writes around it. Separators inside the digits
    // are read as separators, a unit word attached or following is a scale, and a
    // letter scale counts only where it is attached, because "5 m" is as likely to
    // be a sentence about a metre as about a million.
    //
    // A period is a unit of time as the other three are, from 6.6: the first key a
    // model wrote from a facts file called the relative strength index "14 periods",
    // the reader took the 14 for a figure, and an unrelated move of 13.89 per cent
    // rounded to it, which is the coincidence a whole number is weakest against,
    // passed for the wrong reason.
    //
    // A number is read only where it stands as one. Digits inside a name, the 17
    // of a phone or the 100 of a chip, are part of the name, so a number may not
    // start after a letter, a digit or a decimal point, and may not end before a
    // letter or a digit unless what follows is one of the suffixes read here.
    static readonly Regex Number = new(
        @"(?<![A-Za-z0-9.,])(?<currency>[$€£])?(?<digits>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)"
        + @"(?:(?<attached>tn|bn|t|k|m|b|x)(?![A-Za-z0-9])|\s?(?<percent>%|per\s?cent\b)|\s(?<word>trillion|billion|million|thousand)\b)?"
        + @"(?<window>-(?:day|session|week|period)s?\b|\s(?:day|session|week|period)s?\b)?"
        + @"(?<ordinal>st|nd|rd|th)?"
        + @"(?![A-Za-z0-9]|\.\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // A number written in words, which no rule can compare with a stored value.
    // From eleven up and the scale words on their own. One to ten and their
    // ordinals are ordinary language and are not read at all, which is a stated
    // limit rather than a gap nobody saw: "two segments" where there are three
    // passes this rule.
    static readonly Regex NumberWord = new(
        @"\b(?:eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|hundreds?|thousands?|millions?|billions?|trillions?|dozens?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<ProseFigure> Figures(string sentence)
    {
        var text = Citation.Replace(sentence, " ");

        foreach (var name in NamesCarryingDigits.Concat(PeriodsInWords))
        {
            text = text.Replace(name, new string(' ', name.Length), StringComparison.Ordinal);
        }

        var figures = new List<ProseFigure>();

        // Dates first, and each is blanked once read, so the digits inside a date
        // are never read again as a year, a day and a figure.
        text = Blank(text, IsoDate, match =>
        {
            figures.Add(new ProseFigure(
                match.Value, FigureKind.Date, 0m, 0, 1m, false,
                DateOnly.TryParseExact(match.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : null,
                null,
                null));
        });

        text = Blank(text, MonthDate, match =>
        {
            var month = MonthOf(match.Groups[1].Value);
            var day = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var year = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;

            DateOnly? date = year > 0 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
                ? new DateOnly(year, month, day)
                : null;

            figures.Add(new ProseFigure(match.Value, FigureKind.Date, 0m, 0, 1m, false, date, month, day));
        });

        text = Blank(text, Label, match =>
            figures.Add(new ProseFigure(match.Value, FigureKind.Label, 0m, 0, 1m, false, null, null, null)));

        text = Blank(text, Number, match =>
        {
            var digits = match.Groups["digits"].Value.Replace(",", string.Empty, StringComparison.Ordinal);
            var magnitude = decimal.Parse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            var point = digits.IndexOf('.', StringComparison.Ordinal);
            var decimals = point < 0 ? 0 : digits.Length - point - 1;
            var percent = match.Groups["percent"].Success;
            var scale = ScaleOf(match.Groups["attached"].Value, match.Groups["word"].Value);

            // A window is a bare count before a unit of time. A percentage or a
            // scaled amount before one is a figure about that period, which is
            // what "10.86% week over week" is, and the live measurement found five
            // of those read as windows before this was written.
            var window = match.Groups["window"].Success
                && !percent
                && scale == 1m
                && !match.Groups["currency"].Success
                && !match.Groups["attached"].Success;

            var kind = window
                ? FigureKind.Window
                : IsAYear(match, magnitude, decimals, percent, scale)
                    ? FigureKind.Year
                    : match.Groups["ordinal"].Success && decimals == 0 && magnitude is >= 1m and <= 10m
                        ? FigureKind.Label
                        : FigureKind.Figure;

            figures.Add(new ProseFigure(match.Value.Trim(), kind, magnitude, decimals, scale, percent, null, null, null));
        });

        Blank(text, NumberWord, match =>
            figures.Add(new ProseFigure(match.Value, FigureKind.Unmatchable, 0m, 0, 1m, false, null, null, null)));

        return figures;
    }

    // A four-digit whole number between 1900 and 2100 with nothing written around
    // it that makes it a quantity. A year on its own carries no claim about a
    // value, and the figure that would carry one, the revenue of that year, is
    // read and matched on its own.
    static bool IsAYear(Match match, decimal magnitude, int decimals, bool percent, decimal scale) =>
        decimals == 0
        && !percent
        && scale == 1m
        && !match.Groups["currency"].Success
        && !match.Groups["attached"].Success
        && !match.Groups["digits"].Value.Contains(',', StringComparison.Ordinal)
        && magnitude is >= 1900m and <= 2100m;

    static decimal ScaleOf(string attached, string word) =>
        (attached.ToLowerInvariant(), word.ToLowerInvariant()) switch
        {
            ("tn", _) or (_, "trillion") => 1_000_000_000_000m,
            ("bn", _) or ("b", _) or (_, "billion") => 1_000_000_000m,
            ("m", _) or (_, "million") => 1_000_000m,
            ("k", _) or (_, "thousand") => 1_000m,
            _ => 1m,
        };

    static readonly string[] MonthNames =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    static int MonthOf(string name) =>
        Array.IndexOf(MonthNames, name[..3].ToLowerInvariant()) + 1;

    static string Blank(string text, Regex pattern, Action<Match> read) =>
        pattern.Replace(text, match =>
        {
            read(match);

            return new string(' ', match.Length);
        });

    // ---- matching ----

    // Whether a figure is a rounding of a value the facts file holds.
    //
    // The tolerance is half a unit at the precision the prose states, scaled by
    // the prose's own unit word, so "$109.4 billion" is a rounding of
    // 109417000000 and "$109 billion" is too, while "$110 billion" is not. A
    // percentage is also tried against a fraction, because the margins are
    // stored as fractions of one and written as percentages.
    //
    // It matches the magnitude and not the sign. The sign of a move is carried by
    // the words around it, "fell 3.2%", and a match on the signed value would
    // refuse every such sentence written from a stored negative. The cost is
    // stated: "rose 3.2%" written from a stored fall passes this rule.
    //
    // And it matches existence rather than attribution. A figure that happens to
    // equal an unrelated fact passes, and how often that happens over real prose
    // is measured in 6.4's record rather than assumed to be rare.
    public static bool Matches(ProseFigure figure, IReadOnlyList<Fact> facts)
    {
        var target = figure.Magnitude * figure.Scale;
        var tolerance = 0.5m * Unit(figure.Decimals) * figure.Scale;

        foreach (var value in NumericValues(facts))
        {
            if (Math.Abs(value - target) <= tolerance)
            {
                return true;
            }

            // Only a value stored as a fraction is also read as a percentage, and
            // what marks one is a fractional part below ten. Without that, a
            // strength of 2 matched "200%", which is one of the seven coincident
            // matches the measurement found in four captured articles.
            if (figure.Percent
                && value < 10m
                && value != decimal.Truncate(value)
                && Math.Abs(value * 100m - target) <= tolerance)
            {
                return true;
            }
        }

        return false;
    }

    // A window is a number the facts file carries in the name of a figure, the
    // 200 of `sma200`, or as a count it holds, the five sessions of the largest
    // move. "The 100-day average" names no figure the file holds and is refused.
    public static bool IsAWindow(ProseFigure figure, IReadOnlyList<Fact> facts) =>
        facts.Any(fact => Regex.Matches(fact.Name, @"\d+")
                .Any(digits => decimal.Parse(digits.Value, CultureInfo.InvariantCulture) == figure.Magnitude))
            || NumericValues(facts).Any(value => value == figure.Magnitude);

    // A full date has to be one the file holds. A month and day with no year have
    // to be the month and day of one it holds.
    public static bool IsADate(ProseFigure figure, IReadOnlyList<Fact> facts) =>
        DateValues(facts).Any(date => figure.Date is { } full
            ? date == full
            : date.Month == figure.Month && date.Day == figure.Day);

    static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static decimal Unit(int decimals)
    {
        var unit = 1m;

        for (var index = 0; index < decimals; index++)
        {
            unit /= 10m;
        }

        return unit;
    }

    static IEnumerable<decimal> NumericValues(IReadOnlyList<Fact> facts)
    {
        foreach (var fact in facts)
        {
            if (decimal.TryParse(
                    fact.Value,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                yield return Math.Abs(value);
            }
        }
    }

    static IEnumerable<DateOnly> DateValues(IReadOnlyList<Fact> facts)
    {
        foreach (var fact in facts)
        {
            if (DateOnly.TryParseExact(fact.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                yield return date;
            }
        }
    }
}
