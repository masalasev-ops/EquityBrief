using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Releases;

// Who a release is from, as the anonymiser reads it: the filer's names, current and former, as the SEC and the store
// hold them; its tickers; and the people the store knows for it, each as the insiders' filings name them, surname
// first.
public sealed record ReleaseFiler(IReadOnlyList<string> Names, IReadOnlyList<string> Tickers, IReadOnlyList<string> People);

// A results release with every name and date in it replaced by a neutral word, so the model reads what the release says
// and not whose it is or when. Contact details first, since an address or a file name carries the company and the day;
// then the dates and the fiscal labels, so "Q3" or "September 30" is never read as a name; then the dateline's place,
// the filer's names and tickers, and its people. A name of several words is matched whatever its case, its words joined
// by a space, a hyphen or an ampersand alike and a short capitalised word written with points or without; a single
// word, a company name's first word or a surname, only as written with its capital and never as a sentence's first
// word, and a company name's first word only where the common words below do not hold it; a ticker as written, wherever
// it stands. Product names stay. The version is the pin of this file, and it is part of the reading's prompt version, so
// a changed rule is a new reading version.
// see: A release is read with every name and date in it replaced by a neutral word, and product names kept
public static class ReleaseAnonymiser
{
    public const string VersionDeclaration = "public const string Version =";

    public const string Version = "ef9867cce471";

    public const string Company = "[company]";
    public const string Ticker = "[ticker]";
    public const string Place = "[place]";
    public const string Year = "[year]";
    public const string Date = "[date]";
    public const string Quarter = "[quarter]";
    public const string Address = "[address]";
    public const string Phone = "[phone]";

    public static string Person(int number) => FormattableString.Invariant($"[person {number}]");

    // Ordinary English and business words a company's name may open on that a release also writes with a capital in
    // their ordinary sense, in its headings, its segments and its tables, each left in place where it stands alone. A
    // word a release capitalises only as the company, as Regions or Target, is not one.
    public static IReadOnlyList<string> CommonWords { get; } =
    [
        "Advanced", "Affiliated", "Air", "American", "Atlantic", "Bank", "Best", "Big", "Black",
        "Blue", "Bright", "Builders", "Capital", "Central", "Century", "City", "Clean", "Clear", "Commerce", "Commercial",
        "Community", "Consolidated", "Construction", "Core", "Credit", "Customers", "Digital", "East", "Eastern",
        "Employers", "Energy", "Enterprise", "Equity", "Essential", "Extra", "Federal", "Fifth", "Financial", "First",
        "Five", "Four", "Gaming", "General", "Global", "Golden", "Grand", "Great", "Green", "Group", "Health",
        "Healthcare", "Home", "Independent", "Interactive", "International", "Life", "Live", "Medical", "Merchants",
        "Mid", "Minerals", "Mobility", "National", "New", "News", "North", "Northern", "Northwest", "Old", "One",
        "Pacific", "Packaging", "People", "Performance", "Power", "Preferred", "Premier", "Prime", "Public", "Red",
        "Safety", "Science", "Second", "Select", "Service", "Services", "Silver", "Six", "Smart", "South", "Southern",
        "Southwest", "Standard", "Star", "State", "Steel", "Strategic", "Sun", "Third", "Three", "Trade", "Travel",
        "Two", "Union", "United", "Universal", "Us", "Valley", "Waste", "Water", "West", "Western", "White", "World",
    ];

    // The legal words a name ends on, each taken off in turn to make the shorter names a release writes.
    public static IReadOnlyList<string> LegalSuffixes { get; } =
    [
        "Inc", "Incorporated", "Corp", "Corporation", "Co", "Company", "Companies", "Ltd", "Limited", "LLC", "L.L.C",
        "LP", "L.P", "PLC", "N.V", "NV", "S.A", "SA", "AG", "SE", "Holdings", "Holding", "Group", "Bancorp",
        "Bancorporation", "Trust",
    ];

    // The states the SEC writes bare after a legal word, as RENT A CENTER INC DE.
    static readonly string[] States =
    [
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DC", "DE", "FL", "GA", "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA",
        "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ", "NM", "NY", "NC", "ND", "OH", "OK", "OR",
        "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY",
    ];

    // The titles beside which a run of capitalised words is a person's name.
    public static IReadOnlyList<string> Titles { get; } =
    [
        "Chief Executive Officer", "Chief Financial Officer", "Chief Operating Officer", "Chief Accounting Officer",
        "Chief Technology Officer", "Chief Commercial Officer", "Chief Strategy Officer", "Chief Investment Officer",
        "Chief Risk Officer", "Chief Administrative Officer", "Chief Legal Officer", "Chief Marketing Officer",
        "Chief Information Officer", "Chief Medical Officer", "Chief Scientific Officer", "Chief Revenue Officer",
        "Executive Vice President", "Senior Vice President", "Vice President", "Executive Chairman", "Vice Chairman",
        "Vice Chair", "General Counsel", "Managing Director", "Chairman", "Chairwoman", "Chairperson", "Chair",
        "President", "Treasurer", "Secretary", "Founder", "Co-Founder", "Director", "Controller", "CEO", "CFO", "COO",
        "CTO", "CAO", "EVP", "SVP",
    ];

    // The words a person's name is said beside.
    public static IReadOnlyList<string> Sayings { get; } = ["said", "stated", "commented", "added"];

    public static IReadOnlyList<string> Honorifics { get; } = ["Mr", "Ms", "Mrs", "Dr"];

    // Words that are no part of a person's name though they are written with a capital beside a title.
    static readonly HashSet<string> NotNames = new(
        [
            "Investor", "Investors", "Relations", "Media", "Communications", "Corporate", "Finance", "Financial",
            "Treasury", "Strategy", "Operations", "Development", "Affairs", "Marketing", "Sales", "Business", "Global",
            "Group", "Division", "Segment", "Board", "Directors", "Company", "Officer", "Officers", "Chief", "Executive",
            "Senior", "Vice", "Management", "Team", "Contact", "Contacts", "Our", "The", "We", "This", "That", "These",
            "It", "In", "On", "For", "And", "Of", "As", "At", "By", "To", "With", "From", "Revenue", "Results", "Quarter",
            "Banking", "Bank", "Markets", "Market", "Lending", "Investment", "Investments", "Wealth", "Consumer",
            "Commercial", "Retail", "Products", "Product", "Services", "Service", "Solutions", "Systems", "Technology",
            "Technologies", "Health", "Healthcare", "Energy", "Americas", "America", "Europe", "Asia", "International",
            "Unit", "Region", "Supply", "Chain", "Customer", "Customers", "Brand", "Brands", "Accounting", "Legal",
            "Human", "Resources", "Information", "Data", "Digital", "Engineering", "Research", "Manufacturing",
            "Quality", "Risk", "Compliance", "Audit", "Tax", "Capital", "Public", "Press", "News", "Release",
        ],
        StringComparer.Ordinal);

    static readonly string[] MonthNames =
    [
        "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November",
        "December",
    ];

    static readonly string[] MonthAbbreviations = ["Jan", "Feb", "Mar", "Apr", "Jun", "Jul", "Aug", "Sep", "Sept", "Oct", "Nov", "Dec"];

    public static string Anonymise(string text, ReleaseFiler filer)
    {
        var read = Contacts(text);

        read = Dates(read);
        read = Dateline(read);
        read = Companies(read, filer.Names);
        read = Tickers(read, filer.Tickers);
        read = People(read, filer.People);

        return read;
    }

    // ---- contact details --------------------------------------------------------

    static readonly Regex Email = new(@"[\w.+\-]+@[\w\-]+(?:\.[\w\-]+)+", RegexOptions.CultureInvariant);

    static readonly Regex Web = new(@"\b(?:https?://|www\.)[^\s)\]>""”’,;]*[^\s)\]>""”’,;.]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    static readonly Regex Domain = new(
        @"\b[\w\-]+(?:\.[\w\-]+)*\.(?:com|net|org|io|co|us|biz|info)\b(?:/[^\s)\]>""”’,;]*[^\s)\]>""”’,;.])?",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    static readonly Regex FileName = new(
        @"\b[\w\-]+(?:\.[\w\-]+)*\.(?:htm|html|txt|xml|pdf|jpg|jpeg|gif|png|xsd)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    static readonly Regex PhoneNumber = new(
        @"(?<![\w\-])(?:\+?1[\s.\-])?(?:\(\d{3}\)\s?|\d{3}[\s.\-])\d{3}[\s.\-]\d{4}(?![\w\-])",
        RegexOptions.CultureInvariant);

    static string Contacts(string text)
    {
        var read = Email.Replace(text, Address);

        read = Web.Replace(read, Address);
        read = Domain.Replace(read, Address);
        read = FileName.Replace(read, Address);

        return PhoneNumber.Replace(read, Phone);
    }

    // ---- dates and fiscal labels ------------------------------------------------

    static readonly string Month = "(?:" + string.Join("|", MonthNames.Concat(MonthNames.Select(name => name.ToUpperInvariant())))
        + "|" + string.Join("|", MonthAbbreviations.Concat(MonthAbbreviations.Select(name => name.ToUpperInvariant())).Select(name => name + @"\.?")) + ")";

    static readonly Regex MonthDay = new(@"\b" + Month + @"\s+\d{1,2}(?:st|nd|rd|th)?\b(?:,?\s+\d{4}\b)?", RegexOptions.CultureInvariant);

    // A month after its day ends its word, so "99.1 Marsh" or "1 Mayor" holds no date.
    static readonly Regex DayMonth = new(@"\b\d{1,2}(?:st|nd|rd|th)?\s+" + Month + @"(?!\p{L})(?:,?\s+\d{4}\b)?", RegexOptions.CultureInvariant);

    static readonly Regex MonthYear = new(@"\b" + Month + @",?\s+\d{4}\b", RegexOptions.CultureInvariant);

    static readonly Regex SlashDate = new(@"\b(?:0?[1-9]|1[0-2])/(?:0?[1-9]|[12]\d|3[01])/(?:\d{4}|\d{2})\b", RegexOptions.CultureInvariant);

    static readonly Regex IsoDate = new(@"\b(?:19|20)\d{2}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12]\d|3[01])\b", RegexOptions.CultureInvariant);

    static readonly Regex OrdinalQuarter = new(
        @"\b(?:first|second|third|fourth|1st|2nd|3rd|4th)(?:[\s\-]+fiscal)?[\s\-]+quarter\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    static readonly Regex Half = new(@"\b(?:first|second)[\s\-]+half\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    static readonly Regex QuarterLabel = new(@"\b(?:Q[1-4]|[1-4]Q)(?=\b|['’]|FY|\d)", RegexOptions.CultureInvariant);

    static readonly Regex FiscalYear = new(@"(?<=\bFY\s?['’]?)(?:\d{4}|\d{2})\b", RegexOptions.CultureInvariant);

    static readonly Regex ShortYear = new(@"(?<=(?:['’]|\[quarter\]\s?))\d{2}\b", RegexOptions.CultureInvariant);

    static readonly Regex FullYear = new(@"\b(?:199\d|20[0-3]\d)\b", RegexOptions.CultureInvariant);

    static string Dates(string text)
    {
        var read = MonthDay.Replace(text, Date);

        read = DayMonth.Replace(read, Date);
        read = MonthYear.Replace(read, Date);
        read = SlashDate.Replace(read, Date);
        read = IsoDate.Replace(read, Date);
        read = OrdinalQuarter.Replace(read, Quarter);
        read = Half.Replace(read, Quarter);
        read = QuarterLabel.Replace(read, Quarter);
        read = FiscalYear.Replace(read, Year);
        read = ShortYear.Replace(read, Year);

        return FullYear.Replace(read, Year);
    }

    // ---- the dateline's place ---------------------------------------------------

    // A place written before the release's date at its head, as "SANTA ROSA, Calif., [date]" or "NEW YORK, [date]".
    static readonly Regex DatelinePlace = new(
        @"(?<=^|[\s>|])(?:[A-Z][A-Za-z.'\-]*)(?:\s+[A-Z][A-Za-z.'\-]*){0,3}(?:,\s+[A-Z][A-Za-z.]{1,15})?(?=,\s+\[date\])",
        RegexOptions.CultureInvariant);

    // A dateline's place before its date with a wire's name, a dash, a bracket, a semicolon or spaces between, as
    // "LITTLE ROCK, Ark.--(BUSINESS WIRE)-- [date]", "CAMP HILL, PA ([date])" or "Danvers, Mass. — [date]", or after its
    // date, as "[date], LOUISVILLE, Ky. –": a town in capitals, with its state or country or none, or a town in mixed
    // case with its state or country, the first a release writes alone, since a dateline opens a release once; a word of
    // a heading or a table written in capitals is no town.
    static readonly string[] DatelineStates =
    [
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DC", "DE", "FL", "GA", "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA",
        "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ", "NM", "NY", "NC", "ND", "OH", "OK", "OR",
        "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY",
        "Ala.", "Ariz.", "Ark.", "Calif.", "Colo.", "Conn.", "Del.", "D.C.", "Fla.", "Ga.", "Ill.", "Ind.", "Kan.", "Kans.",
        "Ky.", "La.", "Md.", "Mass.", "Mich.", "Minn.", "Miss.", "Mo.", "Mont.", "Neb.", "Nev.", "N.H.", "N.J.", "N.M.",
        "N.Y.", "N.C.", "N.D.", "Okla.", "Ore.", "Pa.", "R.I.", "S.C.", "S.D.", "Tenn.", "Tex.", "Vt.", "Va.", "Wash.",
        "W.Va.", "Wis.", "Wyo.",
        "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut", "Delaware", "Florida",
        "Georgia", "Hawaii", "Idaho", "Illinois", "Indiana", "Iowa", "Kansas", "Kentucky", "Louisiana", "Maine",
        "Maryland", "Massachusetts", "Michigan", "Minnesota", "Mississippi", "Missouri", "Montana", "Nebraska", "Nevada",
        "New Hampshire", "New Jersey", "New Mexico", "New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma",
        "Oregon", "Pennsylvania", "Rhode Island", "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah",
        "Vermont", "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming",
        "United Kingdom", "England", "Ireland", "Canada", "Bermuda", "Israel", "Netherlands", "Switzerland", "Luxembourg",
        "Germany", "France", "Japan", "Singapore", "Australia",
    ];

    static readonly string[] NotPlaces =
    [
        "FOR", "IMMEDIATE", "RELEASE", "PRESS", "NEWS", "DATED", "DATE", "ENDED", "ENDING", "QUARTER", "QUARTERS", "YEAR",
        "YEARS", "MONTH", "MONTHS", "WEEK", "WEEKS", "PERIOD", "PERIODS", "AS", "OF", "AT", "ON", "BY", "THROUGH", "FROM",
        "TO", "AND", "THE", "RESULTS", "REPORTS", "REPORT", "REPORTED", "FISCAL", "GUIDANCE", "EARNINGS", "DIVIDEND",
        "PAYABLE", "EXHIBIT", "DOCUMENT", "CONTACT", "CONTACTS", "UPDATE", "UPDATES", "OUTLOOK", "FULL", "HALF",
        "BALANCE", "BALANCES", "TOTAL", "INVESTOR", "INVESTORS", "MEDIA", "EFFECTIVE", "ISSUED", "RECORD", "SINCE",
        "UNTIL", "BEFORE", "AFTER", "BEGINNING", "END", "STATEMENT", "STATEMENTS", "ANNOUNCES", "ANNOUNCED", "DECLARES",
        "DECLARED", "PAGE", "TABLE", "NOTE", "NOTES", "UNAUDITED", "AUDITED", "THREE", "SIX", "NINE", "TWELVE",
    ];

    // A state or a country as written, in capitals as well where it is more than its code, with a point after or none.
    static readonly string DatelineState = "(?:" + string.Join("|", DatelineStates.Concat(DatelineStates.Where(state => state.Length > 3).Select(state => state.ToUpperInvariant())).Distinct(StringComparer.Ordinal).OrderByDescending(state => state.Length).Select(Regex.Escape)) + @")\.?(?![\p{L}])";

    const string Wire = @"(?:BUSINESS\s+WIRE|Business\s+Wire|GLOBE\s+NEWSWIRE|Globe\s*Newswire|GlobeNewswire|PR\s*NEWSWIRE|PR\s*Newswire|PRNewswire|ACCESS\s*WIRE|ACCESSWIRE|ACCESS\s+Newswire|Marketwired|MARKETWIRED)";

    const string ToTheDate = @"(?=\s*[,;.:]{0,2}\s*(?:[-–—]{1,2}\s*)?(?:\(?\s*" + Wire + @"\s*\)?\s*[,;.:]?\s*(?:[-–—]{1,2}\s*)?)?\(?\s*\[date\])";

    static readonly string CapitalsWord = @"(?!(?:" + string.Join("|", NotPlaces) + @")(?![\p{L}]))[A-Z][A-Z'’\-]*\.?(?![\p{Ll}])";

    static readonly Regex CapitalsDateline = new(
        @"(?<![\p{L}\p{N}\]])" + CapitalsWord + @"(?:\s+" + CapitalsWord + @"){0,3}(?:\s*,\s*" + DatelineState + ")?" + ToTheDate,
        RegexOptions.CultureInvariant);

    static readonly Regex TownDateline = new(
        @"(?<![\p{L}\p{N}\]])[A-Z][a-z]+(?:[\s\-][A-Z][a-z]+){0,2}\s*,\s*" + DatelineState + ToTheDate,
        RegexOptions.CultureInvariant);

    static readonly Regex DateThenPlace = new(
        @"(?<=\[date\]\s*,\s*)" + CapitalsWord + @"(?:\s+" + CapitalsWord + @"){0,3}\s*,\s*" + DatelineState + @"(?=\s*[-–—(])",
        RegexOptions.CultureInvariant);

    static string Dateline(string text)
    {
        var read = text;
        var first = new[] { CapitalsDateline.Match(read), TownDateline.Match(read) }.Where(match => match.Success).MinBy(match => match.Index);

        if (first is not null)
        {
            read = read[..first.Index] + Place + read[(first.Index + first.Length)..];
        }

        read = DateThenPlace.Replace(read, Place, 1);

        return DatelinePlace.Replace(read, match => match.Value.StartsWith('[') ? match.Value : Place);
    }

    // ---- the filer's names and tickers ------------------------------------------

    static string Companies(string text, IReadOnlyList<string> names)
    {
        var variants = names
            .SelectMany(Variants)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(variant => variant.Length)
            .ToArray();

        var read = text;

        foreach (var variant in variants.Where(variant => Words(variant).Length > 1))
        {
            read = Regex.Replace(read, NamePattern(variant), Company, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        var singles = variants
            .Where(variant => Words(variant).Length == 1)
            .Concat(names.Select(FirstWord).Where(word => word.Length > 0))
            .Where(word => word.Length > 1 && !CommonWords.Contains(Titled(word), StringComparer.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(word => word.Length);

        foreach (var single in singles)
        {
            read = ReplaceWord(read, WordPattern(single), Company, anyCase: true);
        }

        return read;
    }

    // A name as filed and each shorter name a release writes for it: a state the SEC adds between slashes or backslashes,
    // or bare after a legal word, and a leading "The" taken off, and each legal word at its end in turn, with the comma or
    // the ampersand before it.
    public static IReadOnlyList<string> Variants(string name)
    {
        var filed = Regex.Replace(name, @"\s*[/\\]\s*[A-Z]{1,5}[/\\]?\s*$", string.Empty, RegexOptions.CultureInvariant).Trim();

        filed = Regex.Replace(filed, @"\s+", " ", RegexOptions.CultureInvariant);

        if (filed.Split(' ') is { Length: > 2 } parts && States.Contains(parts[^1], StringComparer.Ordinal) && LegalSuffixes.Contains(parts[^2].Trim(',', '.'), StringComparer.OrdinalIgnoreCase))
        {
            filed = string.Join(' ', parts[..^1]);
        }

        var variants = new List<string>();
        var current = filed;

        while (current.Length > 0)
        {
            variants.Add(current);

            if (current.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
            {
                variants.Add(current[4..]);
            }

            var words = current.Split(' ');
            var last = words[^1].Trim(',', '.').Trim();

            if (words.Length < 2 || !LegalSuffixes.Contains(last, StringComparer.OrdinalIgnoreCase))
            {
                break;
            }

            current = string.Join(' ', words[..^1]).TrimEnd(',', '&', ' ');

            if (current.EndsWith(" and", StringComparison.OrdinalIgnoreCase))
            {
                current = current[..^4];
            }
        }

        if (variants.Count == 0)
        {
            return [];
        }

        // A name of three words or more is also written by its first two, as "First Financial" for First Financial
        // Bankshares, where neither is a joining word.
        var shortest = Words(variants[^1].StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? variants[^1][4..] : variants[^1]);

        if (shortest.Length > 2 && !Joining.Contains(shortest[1], StringComparer.OrdinalIgnoreCase) && char.IsLetter(shortest[1][0]))
        {
            variants.Add(shortest[0] + " " + shortest[1].TrimEnd(','));
        }

        return [.. variants.Where(variant => variant.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    static readonly string[] Joining = ["&", "and", "of", "the", "for", "de", "du", "a", "an"];

    static string FirstWord(string name)
    {
        var variants = Variants(name);
        var shortest = variants.Count > 0 ? variants[^1] : name;
        var bare = shortest.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? shortest[4..] : shortest;
        var words = Words(bare);

        return words.Length > 0 ? words[0].Trim(',', '.') : string.Empty;
    }

    static string[] Words(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // A name of several words, matched word by word whatever its case: a comma or spaces between them, a hyphen, or an
    // ampersand or "and" the release may write or leave out, so BROWN FORMAN reads Brown-Forman and MARSH & MCLENNAN reads
    // Marsh McLennan; no space after a word's point, as A.O. Smith; a legal word's closing point optional and its short
    // and long forms alike, as CORP for Corporation.
    static string NamePattern(string variant)
    {
        var words = Words(variant).Select(word => word.Trim(',')).Where(word => !Ampersands.Contains(word, StringComparer.OrdinalIgnoreCase));
        var body = string.Join(Joined, words.Select(word =>
            LegalForms.FirstOrDefault(forms => forms.Contains(word.TrimEnd('.'), StringComparer.OrdinalIgnoreCase)) is { } forms
                ? "(?:" + string.Join("|", forms.Select(form => Regex.Escape(form) + @"\.?")) + ")"
                : WordPattern(word)));

        return @"(?<![\p{L}\p{N}])" + body + @"(?![\p{L}\p{N}])";
    }

    static readonly string[] Ampersands = ["&", "and"];

    // What may stand between two words of a name: spaces after an optional comma with an ampersand or "and" between, a
    // hyphen, or nothing after a word's point.
    const string Joined = @"(?:,?\s+(?:(?:&|and)\s+)?|\s*&\s*|\s*[-‐‑‒–]\s*|(?<=\.))";

    static readonly string[][] LegalForms = [["Inc", "Incorporated"], ["Corp", "Corporation"], ["Co", "Company"], ["Ltd", "Limited"]];

    // One word of a name as a release may write it: either apostrophe for the other, any hyphen for a hyphen, an
    // apostrophe before a closing s the SEC leaves out, as DOMINOS for Domino's, and a closing point optional; a word of
    // one to three capital letters with a point after each or none, as U.S. for US.
    static string WordPattern(string word)
    {
        var bare = word.EndsWith('.') ? word[..^1] : word;

        if (bare.Length is > 0 and <= 3 && bare.All(char.IsAsciiLetterUpper))
        {
            return string.Concat(bare.Select(letter => Regex.Escape(letter.ToString()) + @"\.?"));
        }

        var body = string.Concat(bare.Select(letter => letter switch
        {
            '\'' or '’' => "['’]",
            '-' or '‐' or '‑' or '‒' or '–' => "[-‐‑‒–]",
            _ => Regex.Escape(letter.ToString()),
        }));

        if (bare.Length > 3 && bare.EndsWith('s') && !bare.Contains('\'', StringComparison.Ordinal) && !bare.Contains('’', StringComparison.Ordinal)
            || bare.Length > 3 && bare.EndsWith('S') && !bare.Contains('\'', StringComparison.Ordinal) && !bare.Contains('’', StringComparison.Ordinal))
        {
            body = body[..^1] + "['’]?" + body[^1];
        }

        return word.EndsWith('.') ? body + @"\.?" : body;
    }

    static string Titled(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();

    // A class's ticker the SEC files with a hyphen, as BF-B, and a release writes with a point, a slash, a space or
    // nothing, as BF.B or BFB.
    static IEnumerable<string> TickerForms(string ticker) =>
        ticker.Contains('-', StringComparison.Ordinal)
            ? [ticker, ticker.Replace('-', '.'), ticker.Replace('-', '/'), ticker.Replace('-', ' '), ticker.Replace("-", string.Empty, StringComparison.Ordinal)]
            : [ticker];

    static string Tickers(string text, IReadOnlyList<string> tickers)
    {
        var read = text;

        foreach (var ticker in tickers.Where(ticker => ticker.Length > 0).SelectMany(TickerForms).Distinct(StringComparer.Ordinal).OrderByDescending(ticker => ticker.Length))
        {
            read = Regex.Replace(
                read,
                @"\b(?:NYSE(?:\s+American|\s+Arca|\s+MKT)?|Nasdaq(?:\s+(?:Global\s+Select\s+Market|Global\s+Market|Capital\s+Market|GS|GM|CM))?|Cboe\s+BZX)\s*:\s*" + Regex.Escape(ticker) + @"(?![\p{L}\p{N}])",
                Ticker,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            // A ticker of one letter is a word of the language, so it is replaced in its exchange's form alone; a longer one
            // as written wherever it stands, a sentence's first word among them, since no sentence opens on it as an
            // ordinary word.
            if (ticker.Length > 1)
            {
                read = Regex.Replace(read, @"(?<![\p{L}\p{N}\[])" + Regex.Escape(ticker) + @"(?![\p{L}\p{N}])", Ticker, RegexOptions.CultureInvariant);
            }
        }

        // A document's name carrying the filer's ticker or name with its figures, as the exhibit's own name in the
        // filing's envelope, which says whose release it is and when.
        var keys = tickers.Where(ticker => ticker.Length > 2)
            .Select(ticker => ticker.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return keys.Length == 0
            ? read
            : DocumentName.Replace(read, match => keys.Any(key => match.Value.Contains(key, StringComparison.OrdinalIgnoreCase)) ? Address : match.Value);
    }

    static readonly Regex DocumentName = new(@"(?<![\p{L}\p{N}\[])(?=[\p{L}\p{N}_\-]*\d)(?=[\p{L}\p{N}_\-]*\p{L})[\p{L}\p{N}_\-]{6,}(?![\p{L}\p{N}])", RegexOptions.CultureInvariant);

    // ---- people -----------------------------------------------------------------

    static readonly string CapitalWord = @"[A-Z][\p{L}’'\-]*\p{L}";

    static readonly string Initial = @"[A-Z]\.";

    static readonly string NameWord = "(?:" + CapitalWord + "|" + Initial + ")";

    static readonly string Run = NameWord + @"(?:\s+" + NameWord + "){1,3}";

    static readonly string TitleAlternation = string.Join("|", Titles.Select(Regex.Escape));

    static readonly Regex TitleThenName = new(
        @"\b(?:" + TitleAlternation + @")(?:\s*(?:,|and|&)\s*(?:" + TitleAlternation + @"))*\s*,?\s+(?<run>" + Run + ")",
        RegexOptions.CultureInvariant);

    // A name before its title with a comma or an article between, as "Jane Roe, Controller" or "Jane Roe, our Chair", and
    // never a run running straight into a title's word, as a product's "Automated Impella Controller".
    static readonly Regex NameThenTitle = new(
        @"(?<run>" + Run + @")(?:\s*,\s+(?:the\s+|our\s+|\[company\]['’]s\s+)?|\s+(?:the|our)\s+|\s+\[company\]['’]s\s+)(?:" + TitleAlternation + @")\b",
        RegexOptions.CultureInvariant);

    static readonly Regex AfterContact = new(@"\bContacts?\s*:\s*(?<run>" + Run + ")", RegexOptions.CultureInvariant);

    static readonly Regex AfterSaying = new(
        @"\b(?:" + string.Join("|", Sayings) + @")\s*,?\s+(?:(?:" + TitleAlternation + @")\s+)?(?<run>" + Run + ")",
        RegexOptions.CultureInvariant);

    static readonly Regex AfterHonorific = new(
        @"\b(?:" + string.Join("|", Honorifics) + @")\.?\s+(?<run>" + CapitalWord + ")",
        RegexOptions.CultureInvariant);

    static readonly Regex Token = new(@"\[[a-z]+(?: \d+)?\]|[\p{L}\p{N}][\p{L}\p{N}’'\-.&]*", RegexOptions.CultureInvariant);

    static string People(string text, IReadOnlyList<string> insiders)
    {
        var surnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var insider in insiders)
        {
            var surname = insider.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            if (surname is { Length: > 1 } && char.IsLetter(surname[0]))
            {
                surnames.Add(surname);
            }
        }

        foreach (var pattern in new[] { TitleThenName, NameThenTitle, AfterSaying, AfterContact })
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (SurnameOf(match.Groups["run"].Value, 2) is { } surname)
                {
                    surnames.Add(surname);
                }
            }
        }

        foreach (Match match in AfterHonorific.Matches(text))
        {
            if (SurnameOf(match.Groups["run"].Value, 1) is { } surname)
            {
                surnames.Add(surname);
            }
        }

        if (surnames.Count == 0)
        {
            return text;
        }

        var tokens = Token.Matches(text).ToArray();
        var numbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var spans = new List<(int Start, int End, string Surname)>();
        var taken = 0;

        for (var at = 0; at < tokens.Length; at++)
        {
            var word = Possessive(tokens[at].Value);

            if (!char.IsUpper(word[0]) || !surnames.Contains(word))
            {
                continue;
            }

            // The name's earlier words: capitalised words and initials just before it, a space apart, that are no
            // title and no ordinary word, and no part of a name already taken.
            var first = at;

            while (first > taken && at - first < 3)
            {
                var before = tokens[first - 1];
                var gap = text[(before.Index + before.Length)..tokens[first].Index];

                if (gap != " " || !Namelike(before.Value))
                {
                    break;
                }

                first--;
            }

            if (first == at && AtSentenceStart(text, tokens[at].Index))
            {
                continue;
            }

            spans.Add((tokens[first].Index, tokens[at].Index + word.Length, word));
            taken = at + 1;
        }

        foreach (var span in spans)
        {
            numbers.TryAdd(span.Surname, numbers.Count + 1);
        }

        var built = new StringBuilder(text.Length);
        var from = 0;

        foreach (var span in spans)
        {
            built.Append(text, from, span.Start - from);
            built.Append(Person(numbers[span.Surname]));
            from = span.End;
        }

        built.Append(text, from, text.Length - from);

        return built.ToString();
    }

    // A run's surname: its last word, where the run holds the words a name needs once its titles are left out. A month in a
    // run is a first name, as June, since a month with its day was read as a date before any name.
    static string? SurnameOf(string run, int least)
    {
        var words = Words(run).Where(word => !Titles.Contains(word, StringComparer.Ordinal)).ToArray();

        if (words.Length < least || words.Any(word => NotNames.Contains(Possessive(word))))
        {
            return null;
        }

        var surname = Possessive(words[^1]);

        return surname.Length > 1 && !surname.EndsWith('.') && !CommonWords.Contains(surname, StringComparer.Ordinal) ? surname : null;
    }

    // A word that may open a person's name before its surname: written with a capital, no title, honorific, ordinary word
    // or word of the business, and no neutral word already written.
    static bool Namelike(string word) =>
        !word.StartsWith('[')
        && char.IsUpper(word[0])
        && !Titles.Contains(word, StringComparer.Ordinal)
        && !Honorifics.Contains(word.TrimEnd('.'), StringComparer.Ordinal)
        && !NotNames.Contains(word)
        && !CommonWords.Contains(word, StringComparer.Ordinal);

    static string Possessive(string word) =>
        word.EndsWith("’s", StringComparison.Ordinal) || word.EndsWith("'s", StringComparison.Ordinal) ? word[..^2] : word.TrimEnd('.', ',');

    // ---- single words -----------------------------------------------------------

    // A single word replaced whole and never as a sentence's first word: a ticker only as written, and a name's word
    // whatever its case but only where it opens on a capital or a figure, so "Keysight", "KEYSIGHT" and
    // "UnitedHealth" are read as the name and "keysight" is not.
    static string ReplaceWord(string text, string pattern, string with, bool anyCase) =>
        Regex.Replace(
            text,
            @"(?<![\p{L}\p{N}\[])" + pattern + @"(?![\p{L}\p{N}])",
            match => char.IsLower(match.Value[0]) || AtSentenceStart(text, match.Index) ? match.Value : with,
            anyCase ? RegexOptions.CultureInvariant | RegexOptions.IgnoreCase : RegexOptions.CultureInvariant);

    // Whether the word at an index opens a sentence: the text's start, or a sentence's end or a list's mark before it,
    // with only spaces, quotation marks or an opening bracket between. A point closing an honorific, an initial or a
    // legal word ends no sentence.
    static bool AtSentenceStart(string text, int index)
    {
        var at = index - 1;

        while (at >= 0 && (char.IsWhiteSpace(text[at]) || "\"'“‘([".Contains(text[at], StringComparison.Ordinal)))
        {
            at--;
        }

        if (at < 0)
        {
            return true;
        }

        if (text[at] == '.')
        {
            var start = at;

            while (start > 0 && char.IsLetter(text[start - 1]))
            {
                start--;
            }

            var word = text[start..at];

            if (word.Length == 1 || Honorifics.Contains(word, StringComparer.Ordinal) || Abbreviations.Contains(word, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return ".!?•◦▪·|".Contains(text[at], StringComparison.Ordinal);
    }

    static readonly string[] Abbreviations = ["Inc", "Corp", "Co", "Ltd", "Jr", "Sr", "St", "No", "vs"];
}
