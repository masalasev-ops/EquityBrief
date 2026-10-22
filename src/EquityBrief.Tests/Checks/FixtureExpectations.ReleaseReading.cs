using EquityBrief.Core;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Local;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, from 20.3: a release read with its names and dates removed, and whether the reading reads well.
// The fixture's release anonymised as worked by hand, every name, ticker, person, date and fiscal label replaced and its
// segment's name kept, the filer's first word left where it opens a sentence; a filer named with a common first word and
// an insider whose surname is an ordinary word, both left where they are not the name; "Q3" and "September 30" read as
// dates before any name; the anonymiser's version the pin of its source and part of the prompt's; the fixture release's
// recorded answer read through the local run over a constructed store into its four words, a refused answer kept refused
// and the pin's history marked complete; which release answers for a print, a second either side of the late slot's
// start and across a weekend; the agreement, the probes' and the tone variant's arithmetic over constructed rows; the
// sample drawn at its seed; the history read pausing for a slot; the filers' names read off their submissions; and the
// accuracy report naming a reading written before its label.
// see: A release is read with every name and date in it replaced by a neutral word wherever it stands, and product names kept
// see: A release reading is readable from the first night whose late slot starts after the release was accepted
public partial class FixtureExpectations
{
    // The rows 20.3 adds that this check reaches: section 17's row of the samples and section 18's row of a stored text
    // that is not its hash.
    internal static readonly string[] ReleaseReadingClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Release reading's samples"),
        CheckReach.Key(Scope.FailureTable, "A stored release's text does not read back to its hash"),
    ];

    [Fact]
    public async Task TheFixtureReleaseIsReadWithItsNamesAndDatesRemovedAsWorkedOutByHand()
    {
        var expected = Expected("release-reading");

        Assert.Equal("release-reading", expected.GetProperty("stage").GetString());
        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Contains("read by hand", expected.GetProperty("derivedFrom").GetString(), StringComparison.Ordinal);
        Assert.Equal(["source_document"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()));

        // The release as the replay's research pass stores it.
        var text = SecEdgarArchive.Plain(Captured("release-KEYS.htm"));

        Assert.Equal(expected.GetProperty("characters").GetInt32(), text.Length);

        using (var replayed = await FixtureReplay.ResearchedAsync())
        {
            Assert.Contains(text, Query(replayed, "SELECT body FROM source_document WHERE body IS NOT NULL AND title LIKE 'Results release, %';"));
        }

        // The filer its submissions name, and no insider.
        var (_, names, tickers) = ResultsReleases.FilerOf(Captured("filings-KEYS.json"));
        var filer = expected.GetProperty("filer");

        Assert.Equal(filer.GetProperty("names").EnumerateArray().Select(name => name.GetString()), names);
        Assert.Equal(filer.GetProperty("tickers").EnumerateArray().Select(ticker => ticker.GetString()), tickers);

        var read = ReleaseAnonymiser.Anonymise(text, new ReleaseFiler(names, tickers, []));

        Assert.Equal(expected.GetProperty("anonymisedCharacters").GetInt32(), read.Length);

        foreach (var passage in expected.GetProperty("passages").EnumerateArray())
        {
            Assert.Contains(passage.GetProperty("stored").GetString()!, text, StringComparison.Ordinal);
            Assert.Contains(passage.GetProperty("read").GetString()!, read, StringComparison.Ordinal);
        }

        // Nothing of the filer, its people, its place or its dates is left, from the 20.3 correction of 2026-10-19 its first
        // word where a sentence opens on it among them.
        foreach (var opening in expected.GetProperty("readAtASentenceStart").EnumerateArray())
        {
            Assert.Contains(opening.GetString()!, read, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Keysight", read, StringComparison.Ordinal);

        foreach (var gone in new[] { "KEYS", "Dhanasekaran", "Satish", "SANTA ROSA", "Calif.", "2026", "2025", "August", "July", "keysight.com", "585-542" })
        {
            Assert.DoesNotContain(gone, read, StringComparison.Ordinal);
        }

        // The recorded answer over that text, read by hand into its four words.
        var reply = LmStudioReadingFeed.Parse(Captured("local-reading-KEYS-anonymised.json"), 2.51);
        var answer = expected.GetProperty("answer");
        var words = ReleaseAnswers.Read(reply.Content, reply.FinishReason).Answer!;

        Assert.Equal(
            (answer.GetProperty("results").GetString(), answer.GetProperty("outlook").GetString(), answer.GetProperty("tone").GetString(), answer.GetProperty("demand").GetString()),
            (words.Results, words.Outlook, words.Tone, words.Demand));
        Assert.Equal(expected.GetProperty("promptTokens").GetInt32(), reply.PromptTokens);

        // And the local run reads it so: the history read over a constructed store holding the release, its filer's names
        // and no insider asks the reading of exactly that text, keeps the four words with the prompt's tokens, and marks
        // the pin's history complete, every release being read.
        using var store = ReleaseStore();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(Utc(2026, 10, 18, 9, 0, 0), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);
        KeepRelease(store, KeysAugust, KeysCik, Utc(2026, 8, 18, 20, 9, 53), text);
        store.Execute($"INSERT INTO release_filer (cik, names, tickers, read_at, run_id) VALUES ('{KeysCik}', '[\"Keysight Technologies, Inc.\"]', '[\"KEYS\"]', '2026-08-19T12:05:00Z', 'constructed');");

        var asked = new List<string>();
        var feed = new BodyAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("the feed answers")), settings), body =>
        {
            asked.Add(body);

            return reply;
        });
        var outcome = await new LocalRun(feed, settings, clock, store.DatabaseFile, store.Root, NoWait).ReadHistoryAsync(null, TextWriter.Null);

        Assert.Equal([read], asked);
        Assert.Equal((LocalRun.Finished, 1), (outcome.Outcome, outcome.Read));
        Assert.Equal([$"{KeysAugust}|1|read|null|better|raised|confident|stronger|{expected.GetProperty("promptTokens").GetInt32()}"], Query(store, "SELECT accession, pin, outcome, cause, results, outlook, tone, demand, prompt_tokens FROM release_reading;"));
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM reading_pin WHERE history_completed_at IS NOT NULL;"));
    }

    [Fact]
    public void AFilerNamedWithACommonWordAndAnInsiderWithAnOrdinarySurnameAreLeftWhereTheyAreNotTheName()
    {
        // A filer opening on General, a common word, and an insider whose surname, Young, is an ordinary word the
        // common-word list does not hold. Worked by hand: the name in its long legal form, the ticker in its exchange's
        // form, the quarter, the day and the fiscal year replaced; General standing alone and the product Widgets kept;
        // Young kept in lower case, and from the 20.3 correction of 2026-10-19 replaced where it opens a sentence as well
        // as the chief executive's name and after Mr.
        const string Text =
            "General Widgets Corporation (NYSE: GWX) reported its third quarter results on October 14, 2026. General and administrative expenses fell. " +
            "Widgets shipped rose, and general demand improved. Sales of General Purpose widgets rose. Young said the quarter was strong. " +
            "“We grew,” said Robert Young, Chief Executive Officer. Mr. Young added that young customers grew in FY26.";

        Assert.Equal(
            "[company] ([ticker]) reported its [quarter] results on [date]. General and administrative expenses fell. " +
            "Widgets shipped rose, and general demand improved. Sales of General Purpose widgets rose. [person 1] said the quarter was strong. " +
            "“We grew,” said [person 1], Chief Executive Officer. Mr. [person 1] added that young customers grew in FY[year].",
            ReleaseAnonymiser.Anonymise(Text, new ReleaseFiler(["GENERAL WIDGETS CORP"], ["GWX"], ["Young Robert A"])));

        // The same sentences for a filer whose first word no common word holds: replaced on its own where it opens on a
        // capital, a sentence's first word among them from the 20.3 correction of 2026-10-19, and kept in lower case.
        Assert.Equal(
            "[company] grew. Sales of [company] Purpose widgets rose, and acme demand improved.",
            ReleaseAnonymiser.Anonymise("Acme grew. Sales of Acme Purpose widgets rose, and acme demand improved.", new ReleaseFiler(["ACME WIDGETS CORP"], ["AWX"], [])));

        // "Q3" and "September 30" are read as dates before any name is, so neither is taken for one, and June in a run
        // after a verb of saying is a first name.
        Assert.Equal(
            "Revenue for [quarter], the quarter ended [date], rose, said [person 1], Chief Financial Officer.",
            ReleaseAnonymiser.Anonymise("Revenue for Q3, the quarter ended September 30, rose, said June Smith, Chief Financial Officer.", new ReleaseFiler(["ACME HOLDINGS INC"], ["ACM"], [])));

        // A name's forms: a state the SEC adds after a slash, a leading article and each legal word taken off, the first two
        // words of a longer name, an apostrophe the SEC leaves out, and a document's name carrying the ticker; the name
        // opening a sentence replaced from the 20.3 correction of 2026-10-19.
        Assert.Equal(["WELLS FARGO & COMPANY", "WELLS FARGO"], ReleaseAnonymiser.Variants("WELLS FARGO & COMPANY/MN"));
        Assert.Equal(["The Home Depot, Inc.", "Home Depot, Inc.", "The Home Depot", "Home Depot"], ReleaseAnonymiser.Variants("The Home Depot, Inc."));
        Assert.Contains("FIRST FINANCIAL", ReleaseAnonymiser.Variants("FIRST FINANCIAL BANKSHARES INC"));
        Assert.Equal(
            "[company] reported. [company] grew, while [company] stores rose; see [address].",
            ReleaseAnonymiser.Anonymise("Domino’s Pizza, Inc. reported. Domino’s grew, while Domino’s stores rose; see earningsrelease3q26dpz.", new ReleaseFiler(["DOMINOS PIZZA INC"], ["DPZ"], [])));
    }

    [Fact]
    public void AFilersNameIsReadAcrossItsJoinsPointsAndStateAndATickerWhereverItStands()
    {
        // From the 20.3 correction of 2026-10-18, each worked by hand from the reference set's own releases. A state the
        // SEC writes between backslashes, and "U.S." for the US it files.
        Assert.Equal(["US BANCORP", "US"], ReleaseAnonymiser.Variants("US BANCORP \\DE\\"));
        Assert.Equal(
            "[company] ([ticker]) reported. Net income at [company] rose, and [ticker] grew.",
            ReleaseAnonymiser.Anonymise("U.S. Bancorp (NYSE: USB) reported. Net income at U.S. Bancorp rose, and USB grew.", new ReleaseFiler(["US BANCORP \\DE\\"], ["USB"], [])));

        // A state written bare after a legal word, a name's words joined by hyphens, and no first two words read where the
        // second is an article, so "rent a sofa" stays.
        Assert.Equal(["RENT A CENTER INC", "RENT A CENTER"], ReleaseAnonymiser.Variants("RENT A CENTER INC DE"));
        Assert.Equal(
            "[company] grew. Sales at [company] rose, as customers rent a sofa.",
            ReleaseAnonymiser.Anonymise("Rent-A-Center grew. Sales at Rent-A-Center rose, as customers rent a sofa.", new ReleaseFiler(["UPBOUND GROUP, INC.", "RENT A CENTER INC DE"], ["UPBD"], [])));

        // A name filed with spaces and written with a hyphen, and a class's ticker filed with a hyphen and written with a
        // point or nothing.
        Assert.Equal(
            "[company] ([ticker], [ticker]) reported. [company] grew, and [ticker] rose.",
            ReleaseAnonymiser.Anonymise("Brown-Forman Corporation (NYSE: BFA, BFB) reported. Brown-Forman grew, and BF.B rose.", new ReleaseFiler(["BROWN FORMAN CORP"], ["BF-B", "BF-A"], [])));

        // A name filed with an ampersand and written with it or without it, and a figure before a word opening on a month's
        // short name read as no date, as "Exhibit 99.1 Marsh" was read as "1 Mar".
        Assert.Equal(
            "[company] ([ticker]) reported. [company] grew. Exhibit 99.1 [company] reports, on [date].",
            ReleaseAnonymiser.Anonymise("Marsh McLennan (NYSE: MMC) reported. Marsh & McLennan Companies grew. Exhibit 99.1 Marsh McLennan reports, on 16 April 2026.", new ReleaseFiler(["MARSH & MCLENNAN COMPANIES, INC."], ["MMC"], [])));

        // A ticker opening a sentence, which no ordinary word is.
        Assert.Equal(
            "[ticker] had a strong quarter. [ticker] grew.",
            ReleaseAnonymiser.Anonymise("AIG had a strong quarter. AIG grew.", new ReleaseFiler(["AMERICAN INTERNATIONAL GROUP, INC."], ["AIG"], [])));

        // A product's name running into a title's word is no person, as "Automated Impella Controller" was taken for one;
        // a name before its title with a comma between is.
        Assert.Equal(
            "Shipments of the Automated Widget Controller rose. “We grew,” said [person 1], Controller.",
            ReleaseAnonymiser.Anonymise("Shipments of the Automated Widget Controller rose. “We grew,” said Jane Roe, Controller.", new ReleaseFiler(["ACME WIDGETS CORP"], ["AWX"], [])));

        // A dateline's place with a wire's name, a bracket or a dash before its date, in mixed case with its state, and
        // after its date; a heading's capitals before a date and a period's end left as they stand.
        var acme = new ReleaseFiler(["ACME WIDGETS CORP"], ["AWX"], []);
        var dash = char.ConvertFromUtf32(0x2014);

        Assert.Equal("Results grew. [place]--(BUSINESS WIRE)-- [date] --Sales rose.", ReleaseAnonymiser.Anonymise("Results grew. LITTLE ROCK, Ark.--(BUSINESS WIRE)-- May 30, 2019 --Sales rose.", acme));
        Assert.Equal("Sales rose. [place] ([date]) - Results grew.", ReleaseAnonymiser.Anonymise("Sales rose. CAMP HILL, PA (February 24, 2022) - Results grew.", acme));
        Assert.Equal($"[place] {dash} [date] – Sales rose.", ReleaseAnonymiser.Anonymise($"Danvers, Mass. {dash} April 28, 2022 – Sales rose.", acme));
        Assert.Equal("[date], [place] – Sales rose.", ReleaseAnonymiser.Anonymise("June 4, 2025, LOUISVILLE, Ky. – Sales rose.", acme));
        Assert.Equal($"Results rose. [place], [date] {dash} Sales grew.", ReleaseAnonymiser.Anonymise($"Results rose. PHOENIX, AZ., November 5, 2025 {dash} Sales grew.", acme));
        Assert.Equal(
            "FOR IMMEDIATE RELEASE [date]. Sales for the Three Months Ended [date] rose. THREE MONTHS ENDED [date].",
            ReleaseAnonymiser.Anonymise("FOR IMMEDIATE RELEASE May 30, 2019. Sales for the Three Months Ended May 30, 2019 rose. THREE MONTHS ENDED May 30, 2019.", acme));
    }

    [Fact]
    public void AContactLineAJointFilingsOwnersAFounderAPhoneAndALoneMonthAreReadAsTheDecisionNamesThem()
    {
        // From the second 20.3 correction of 2026-10-18, each worked by hand from a release the reference set holds. A
        // Windows code page's quotation marks, bullet and dash read as that page's, so a dateline's dash is read before its
        // date.
        var acme = new ReleaseFiler(["ACME WIDGETS CORP"], ["AWX"], []);
        var dash = char.ConvertFromUtf32(0x2014);

        Assert.Equal(
            $"[place] {dash} [date] {dash} “We grew,” said [person 1], Controller.",
            ReleaseAnonymiser.Anonymise($"SAN JOSE, Calif. {(char)0x97} May 30, 2019 {(char)0x97} {(char)0x93}We grew,{(char)0x94} said Jane Roe, Controller.", acme));

        // A phone written with its country's code after a plus, and one with its area's brackets between hyphens, with
        // its country's code and without it.
        Assert.Equal(
            "Call [phone], [phone] or [phone].",
            ReleaseAnonymiser.Anonymise("Call +86 (138) 1079-1480, +1-(978)-256-1300 or (978)-256-1300.", acme));

        // A month named alone, a fiscal label in the plural and a year the archive split after a date, while a month
        // before a surname is a first name and May opening a sentence may be the verb.
        Assert.Equal(
            "Sales rose in [date], and its [date] guidance held for the [quarter] Ended; paid on [date], [year] to holders. May not recur, said [person 1], Chief Financial Officer.",
            ReleaseAnonymiser.Anonymise("Sales rose in February, and its November guidance held for the Second Quarters Ended; paid on June 4, 20 26 to holders. May not recur, said June Smith, Chief Financial Officer.", acme));

        // A dateline whose date the release sets elsewhere, before the filer's name, and a town under a signature's
        // title read from its first word that is no title's.
        Assert.Equal("[place] - [company] ([ticker]) reported.", ReleaseAnonymiser.Anonymise("BIRMINGHAM, Ala. - Acme Widgets Corp. (NYSE: AWX) reported.", acme));
        Assert.Equal(
            "[place] – [date] – Sales rose. Signed, Chief Executive Officer [place], [date].",
            ReleaseAnonymiser.Anonymise("Woonsocket, R.I. – May 1, 2019 – Sales rose. Signed, Chief Executive Officer WOONSOCKET, R.I., May 30, 2019.", acme));

        // A ticker the release states in an exchange's form after the filer's name, the filer since renamed, read wherever
        // it stands, and a preferred stock's ticker opening on the filer's.
        Assert.Equal(
            "[company] ([ticker]) reported. [ticker] rose, and its Series J Preferred Stock ([ticker]) paid.",
            ReleaseAnonymiser.Anonymise("Acme Widgets Corp. (NYSE: OLD) reported. OLD rose, and its Series J Preferred Stock (NYSE: AWXPrJ) paid.", acme));

        // A contact line's label in capitals, in lower case, without its colon and running into the next label, the name
        // after each read up to its title's words.
        Assert.Equal(
            "INVESTOR CONTACTS: [person 1] Vice President, Investor Relations. For more information contact: [person 2] - VP. Investor Contact [person 3] [address] Investor Contact: PR Contact: [person 4] [phone] Media CONTACT [person 5]",
            ReleaseAnonymiser.Anonymise("INVESTOR CONTACTS: Yvonne Perron Vice President, Investor Relations. For more information contact: Kate Lowrey - VP. Investor Contact James Baglanis ir@acme.com Investor Contact: PR Contact: Suzanne Schmidt 212-555-0100 Media CONTACT Denise Pacioni", acme));

        // Each owner of a joint filing read and a fund that files beside them no person; a name after an honorific read to
        // its surname; a given name a filing names first and its surname one person; a surname's particles; a generation
        // that is no surname; and a place a fund's name opens on left as it stands.
        Assert.Equal(
            "Trian Fund Management holds shares, and [person 1] joined the board.",
            ReleaseAnonymiser.Anonymise("Trian Fund Management holds shares, and Joshua D. Frank joined the board.", new ReleaseFiler(["ACME WIDGETS CORP"], ["AWX"], ["TRIAN FUND MANAGEMENT, L.P.; Frank Joshua D."])));
        Assert.Equal(
            "said Dr. [person 1], President. Dr. [person 1] added that Shanghai and David Corp grew, said [person 2], Chief Executive Officer. Mr. [person 2] agreed.",
            ReleaseAnonymiser.Anonymise(
                "said Dr. David H. Wang, President. Dr. Wang added that Shanghai and David Corp grew, said Jason Fox, Chief Executive Officer. Mr. Fox agreed.",
                new ReleaseFiler(["ACME WIDGETS CORP"], ["AWX"], ["Shanghai Science & Technology Venture Capital (Group) Co., Ltd.; Shanghai Pudong Science & Technology Investment Group Co., Ltd.", "Jason Fox"])));
        Assert.Equal(
            "said [person 1], Chief Financial Officer. Mr. van der [person 1] added. Corporate Contact: [person 2] II Chief Corporate Affairs Officer. The EPIONE II study enrolled.",
            ReleaseAnonymiser.Anonymise("said Jaco van der Walt, Chief Financial Officer. Mr. van der Walt added. Corporate Contact: AJ Jones II Chief Corporate Affairs Officer. The EPIONE II study enrolled.", acme));

        // A founder whose surname is the filer's one-word name, beside a title and after an honorific, while the name
        // standing on its own is the filer.
        Assert.Equal(
            "“We grew,” said [person 1], Chairman. [company] thanks Mr. [person 1].",
            ReleaseAnonymiser.Anonymise("“We grew,” said Michael Dellware, Chairman. Dellware Inc. thanks Mr. Dellware.", new ReleaseFiler(["DELLWARE INC"], ["DLW"], [])));
    }

    [Fact]
    public void ANameOpeningASentenceOrAListsItemANameWithACapitalAfterItsFirstLetterAndAShortFormTheReleaseDefinesAreTheFilers()
    {
        // From the 20.3 correction of 2026-10-19, on the operator's rulings of that day, each worked by hand from a release
        // the reference set holds. (a) The filer's one-word name opening a quotation and a list's item and a sentence, as
        // Mosaic's release writes it, and a surname a title found opening a sentence, as Tractor Supply's writes Lawton,
        // are replaced as anywhere else.
        Assert.Equal(
            "“[company] delivered record EBITDA in [year], and we expect strong performance to continue in [year],” said [person 1], President and CEO. "
            + "• [company] expects to initiate an accelerated share repurchase (ASR) of $400 million in [date]. [company] remains on track to complete the ramp up of K3 by the end of the [quarter] of [year].",
            ReleaseAnonymiser.Anonymise(
                "“Mosaic delivered record EBITDA in 2021, and we expect strong performance to continue in 2022,” said Joc O’Rourke, President and CEO. "
                + "• Mosaic expects to initiate an accelerated share repurchase (ASR) of $400 million in February. Mosaic remains on track to complete the ramp up of K3 by the end of the first quarter of 2022.",
                new ReleaseFiler(["MOSAIC CO", "GLOBAL NUTRITION SOLUTIONS INC"], ["MOS"], [])));
        Assert.Equal(
            "dedication to serving our customers every day,” said [person 1], President and Chief Executive Officer of [company]. [person 1] continued, “We are updating our fiscal [year] outlook",
            ReleaseAnonymiser.Anonymise(
                "dedication to serving our customers every day,” said Hal Lawton, President and Chief Executive Officer of Tractor Supply. Lawton continued, “We are updating our fiscal 2026 outlook",
                new ReleaseFiler(["TRACTOR SUPPLY CO /DE/"], ["TSCO"], [])));

        // An insider's surname that is an ordinary word the common-word list holds stays where it opens a sentence, as
        // RGA's insider To My Chi beside "To learn more", on the operator's answer of the same day; and "the Company's"
        // stays.
        Assert.Contains("To", ReleaseAnonymiser.OrdinarySurnames);
        Assert.Equal(
            "and financial solutions. To learn more about [ticker] and its businesses, visit the Company’s website at [address] .",
            ReleaseAnonymiser.Anonymise(
                "and financial solutions. To learn more about RGA and its businesses, visit the Company’s website at www.rgare.com .",
                new ReleaseFiler(["REINSURANCE GROUP OF AMERICA INC"], ["RGA"], ["To My Chi"])));

        // (b) A name's word with a capital after its first letter, as nVent's release writes its name, in that exact case
        // wherever it stands, where no short form defines it; and with its name, its ticker and the short form it defines
        // after them in its dateline.
        var nvent = new ReleaseFiler(["nVent Electric plc"], ["NVT"], []);

        Assert.Equal(
            "EARNINGS CONFERENCE CALL [company]’s management team will discuss the company’s [quarter] performance on a conference call",
            ReleaseAnonymiser.Anonymise("EARNINGS CONFERENCE CALL nVent’s management team will discuss the company’s second quarter performance on a conference call", nvent));
        Assert.Equal(
            "[place] – [date] – [company] ([ticker]) (“[company]”), a global leader in electrical connection and protection solutions",
            ReleaseAnonymiser.Anonymise("LONDON, UNITED KINGDOM – July 31, 2026 – nVent Electric plc (NYSE:NVT) (“nVent”), a global leader in electrical connection and protection solutions", nvent));

        // (c) The filer's initials, BD, replaced wherever they recur, a heading's and a list's among them, in the part of
        // BD's release that defines no short form, so the initials alone read them; and in the whole of it, where BD also
        // stands straight before a bracket holding its name.
        Assert.Equal("BD", ReleaseAnonymiser.InitialsOf("BECTON DICKINSON & CO"));
        Assert.Equal(
            "[company] ANNOUNCES FISCAL [quarter] AND FULL YEAR [year] RESULTS; PROVIDES FISCAL [year] GUIDANCE • [company]’s COVID-19 testing sales totaled more than $440 million in the [quarter].",
            ReleaseAnonymiser.Anonymise(
                "BD ANNOUNCES FISCAL FOURTH QUARTER AND FULL YEAR 2020 RESULTS; PROVIDES FISCAL 2021 GUIDANCE • BD’s COVID-19 testing sales totaled more than $440 million in the fourth fiscal quarter.",
                new ReleaseFiler(["BECTON DICKINSON & CO"], ["BDX"], [])));
        Assert.Equal(
            "[company] ANNOUNCES FISCAL [quarter] AND FULL YEAR [year] RESULTS; PROVIDES FISCAL [year] GUIDANCE • Fiscal [quarter] revenues of $4.784 billion grew 4.4% on a reported and currency-neutral basis. "
            + "• [company]’s COVID-19 testing sales totaled more than $440 million in the [quarter]. [place] ([date]) - [company] ([company]) ([ticker]), a leading global medical technology company",
            ReleaseAnonymiser.Anonymise(
                "BD ANNOUNCES FISCAL FOURTH QUARTER AND FULL YEAR 2020 RESULTS; PROVIDES FISCAL 2021 GUIDANCE • Fiscal fourth quarter revenues of $4.784 billion grew 4.4% on a reported and currency-neutral basis. "
                + "• BD’s COVID-19 testing sales totaled more than $440 million in the fourth fiscal quarter. Franklin Lakes, NJ (November 5, 2020) - BD (Becton, Dickinson and Company) (NYSE: BDX), a leading global medical technology company",
                new ReleaseFiler(["BECTON DICKINSON & CO"], ["BDX"], [])));

        // Short forms in quotes in a bracket after the name and its ticker, as GCE's release writes "GCE" beside "the
        // Company", which the common-word list holds and so stays; and after the name alone, as AeroVironment's writes
        // "AV", whose one-word name holds no initials.
        var dash = char.ConvertFromUtf32(0x2014);

        Assert.Equal(
            $"[place], [date] {dash} [company] ([ticker]), (“[company]” or the “Company”), is a publicly traded education services company that currently provides services to 20 university partners. [company] provides a full array of support services",
            ReleaseAnonymiser.Anonymise(
                $"PHOENIX, AZ., November 5, 2025 {dash} Grand Canyon Education, Inc. (NASDAQ: LOPE), (“GCE” or the “Company”), is a publicly traded education services company that currently provides services to 20 university partners. GCE provides a full array of support services",
                new ReleaseFiler(["Grand Canyon Education, Inc."], ["LOPE"], [])));
        Assert.Equal(string.Empty, ReleaseAnonymiser.InitialsOf("AEROVIRONMENT INC"));
        Assert.Equal(
            "ABOUT [company] ​ [company] (“[company]”) ([ticker]) is a defense technology leader",
            ReleaseAnonymiser.Anonymise("ABOUT AEROVIRONMENT, INC. ​ AeroVironment (“AV”) (NASDAQ: AVAV) is a defense technology leader", new ReleaseFiler(["AEROVIRONMENT INC"], ["AVAV"], [])));

        // A short form in capitals in a bracket after the name, as PSEG's release writes it, which its initials are as
        // well, and a code page's bullet before it read as that page's; and the bullet alone, where the initials alone
        // read it.
        var pseg = new ReleaseFiler(["PUBLIC SERVICE ENTERPRISE GROUP INC"], ["PEG"], []);

        Assert.Equal("PSEG", ReleaseAnonymiser.InitialsOf("PUBLIC SERVICE ENTERPRISE GROUP INC"));
        Assert.Equal(
            "• [company]’s solid [quarter] and year-to-date operating results. About [company] [company] ([company]) ([ticker]) is a predominantly regulated infrastructure company",
            ReleaseAnonymiser.Anonymise(
                $"{(char)0x95} PSEG’s solid third quarter and year-to-date operating results. About PSEG Public Service Enterprise Group (PSEG) (NYSE: PEG) is a predominantly regulated infrastructure company",
                pseg));
        Assert.Equal(
            "• [company]’s solid [quarter] and year-to-date operating results.",
            ReleaseAnonymiser.Anonymise($"{(char)0x95} PSEG’s solid third quarter and year-to-date operating results.", pseg));

        // Constructed, since in the reference set every short form in capitals in a bracket after the name and every one
        // before a bracket holding it is the filer's initials as well: each read by its own form alone, the initials AW.
        var acme = new ReleaseFiler(["ACME WIDGETS CORP"], ["AWX"], []);

        Assert.Equal("AW", ReleaseAnonymiser.InitialsOf("ACME WIDGETS CORP"));
        Assert.Equal("[company] ([company]) reported. [company] grew.", ReleaseAnonymiser.Anonymise("Acme Widgets Corp. (ACW) reported. ACW grew.", acme));
        Assert.Equal("[company] ([company]) reported. [company] grew.", ReleaseAnonymiser.Anonymise("WidgetCo (Acme Widgets Corp.) reported. WidgetCo grew.", acme));

        // Constructed as well, since the reference set's "the Company" is a legal word too and its two banks define "the
        // Bank" after no name: a defined term the common-word list alone holds stays where the release defines it.
        Assert.Equal(
            "[company] (the “Bank”) reported. The Bank grew.",
            ReleaseAnonymiser.Anonymise("Acme Bancorp, Inc. (the “Bank”) reported. The Bank grew.", new ReleaseFiler(["ACME BANCORP INC"], ["ABX"], [])));
    }

    [Fact]
    public void TheReferenceSetsLabelsAreItsSamplesReleasesOneEachReadByTheAccuracyReportsOwnReader()
    {
        // The labels committed at 20.3 before the local model read any reference release: one for each release the sample
        // lists, in its order, every word one its question lists, read through the reader the accuracy report reads them
        // with, and the instant the last was written.
        var folder = Path.Combine(Repository.Root, "fixtures", "release-reference-2026");
        var sample = File.ReadAllLines(Path.Combine(folder, "sample.txt")).Where(line => line.Length > 0).ToArray();
        var set = ReleaseLabels.Read(File.ReadAllText(Path.Combine(folder, "labels.json")));

        Assert.Equal(100, sample.Length);
        Assert.Equal(sample, set.Labels.Select(label => label.Accession));
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 0, 10, 42, TimeSpan.Zero), set.LabelledAt);
    }

    [Fact]
    public void TheAnonymisersVersionIsThePinOfItsSourceAndPartOfThePromptsVersion()
    {
        var source = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Core", "Releases", "ReleaseAnonymiser.cs"));

        Assert.Equal(ReleaseAnonymiser.Version, SourcePin.Of([source], ReleaseAnonymiser.VersionDeclaration));
        Assert.NotEqual(ReleaseAnonymiser.Version, SourcePin.Of([source + "\ninternal static class Moved { }"], ReleaseAnonymiser.VersionDeclaration));

        // From the 20.3 correction of 2026-10-22 the fit's rule is the pin of its own file and the prompt's third line, and
        // the version every reading before it was asked under is the instruction's and the anonymiser's alone.
        var fit = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Core", "Releases", "ReleaseFit.cs"));

        Assert.Equal(ReleaseFit.Version, SourcePin.Of([fit], ReleaseFit.VersionDeclaration));
        Assert.NotEqual(ReleaseFit.Version, SourcePin.Of([fit.Replace("public const int Cuts = 3;", "public const int Cuts = 4;", StringComparison.Ordinal)], ReleaseFit.VersionDeclaration));
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ReleaseQuestions.Instruction + "\n" + ReleaseAnonymiser.Version + "\n" + ReleaseFit.Version)))[..12],
            ReleaseQuestions.Version);
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ReleaseQuestions.Instruction + "\n" + ReleaseAnonymiser.Version)))[..12],
            ReleaseQuestions.VersionBeforeTheFit);
    }

    [Fact]
    public async Task ThePrintsReleaseIsTheNewestFromItsWindowTheNightMayReadAndAMissingOrRefusedReadingSaysWhy()
    {
        using var store = ReleaseStore();
        var zone = NewYork;

        // A print reported on Thursday 2026-10-15: a release accepted at 21:39:59 that day is read on its night, one at
        // 21:40:00 is not and is read on the Friday's, and a release of the quarter before, accepted 2026-07-15, answers for
        // no print of October's.
        KeepRelease(store, "0001601046-26-000040", KeysCik, Utc(2026, 7, 15, 20, 0, 0), "an earlier quarter");
        KeepRelease(store, "0001601046-26-000041", KeysCik, Utc(2026, 10, 15, 21, 39, 59), "a second before the late slot");
        store.Execute("INSERT INTO release_reading (accession, exhibit, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at, run_id) VALUES ('0001601046-26-000041', 'ex99.htm', 1, 'read', NULL, 'better', 'kept', 'neutral', 'steady', 3.1, 5000, '2026-10-15T21:41:00Z', 'constructed');");

        using var connection = store.Open();

        var thursday = await ReleaseReadings.ForPrintAsync(connection, KeysCik, new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 15), 1, ReleaseCalendar.Exchange, zone);

        Assert.Equal(("0001601046-26-000041", "better", (string?)null), (thursday.Accession, thursday.Answer?.Results, thursday.Why));

        KeepRelease(store, "0001601046-26-000042", KeysCik, Utc(2026, 10, 16, 21, 40, 0), "at the late slot's start");
        KeepRelease(store, "0001601046-26-000043", KeysCik, Utc(2026, 10, 16, 22, 0, 0), "on a Friday evening");

        var friday = await ReleaseReadings.ForPrintAsync(connection, KeysCik, new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 16), 1, ReleaseCalendar.Exchange, zone);
        var monday = await ReleaseReadings.ForPrintAsync(connection, KeysCik, new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 19), 1, ReleaseCalendar.Exchange, zone);

        // On Friday's night the newest readable is Thursday's, a second before its slot; the one at 21:40:00 and the one
        // after are read from Monday's night, across the weekend, where the newest is not yet read under the version.
        Assert.Equal("0001601046-26-000041", friday.Accession);
        Assert.Equal(("0001601046-26-000043", (ReleaseAnswer?)null, ReleaseReadings.NotRead), (monday.Accession, monday.Answer, monday.Why));

        // A refused reading says so with its cause, and a print whose window holds no release says that.
        store.Execute("INSERT INTO release_reading (accession, exhibit, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at, run_id) VALUES ('0001601046-26-000043', 'ex99.htm', 1, 'refused', 'a word not listed', NULL, NULL, NULL, NULL, NULL, NULL, '2026-10-17T12:06:00Z', 'constructed');");

        var refused = await ReleaseReadings.ForPrintAsync(connection, KeysCik, new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 19), 1, ReleaseCalendar.Exchange, zone);
        var none = await ReleaseReadings.ForPrintAsync(connection, KeysCik, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1), 1, ReleaseCalendar.Exchange, zone);

        Assert.Equal(ReleaseReadings.RefusedWhy + ": a word not listed", refused.Why);
        Assert.Equal((null, ReleaseReadings.NoRelease), (none.Accession, none.Why));

        // The window opens at the start in New York of the session before the report date: Monday 2026-10-19's at Friday's
        // midnight, 04:00 in UTC; and a print the night's calendar cannot place, before the closure table's years, says so.
        var unplaced = await ReleaseReadings.ForPrintAsync(connection, KeysCik, new DateOnly(2024, 8, 20), new DateOnly(2024, 8, 21), 1, ReleaseCalendar.Exchange, zone);

        Assert.Equal(Utc(2026, 10, 16, 4, 0, 0), ReleaseAvailability.WindowFrom(new DateOnly(2026, 10, 19), zone, ReleaseCalendar.Exchange));
        Assert.Equal((null, ReleaseReadings.NotPlaced), (unplaced.Accession, unplaced.Why));
    }

    [Fact]
    public void TheReleaseCalendarReadsTheClosureTableOverItsYearsAndTheLedgersSessionsBeforeThem()
    {
        // The ledger's sessions around Good Friday 2024, held on the Thursday and the Monday and not the Friday, and the
        // last of 2024.
        var calendar = new ReleaseCalendar([new DateOnly(2024, 3, 27), new DateOnly(2024, 3, 28), new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31)]);

        Assert.False(calendar.IsSession(new DateOnly(2024, 3, 29)));
        Assert.Equal(new DateOnly(2024, 3, 28), calendar.SessionBefore(new DateOnly(2024, 4, 1)));
        Assert.Equal(new DateOnly(2024, 4, 1), calendar.SessionAfter(new DateOnly(2024, 3, 28)));

        // Across the table's first day, New Year's Day closed: the ledger's last session of 2024 before the table's first.
        Assert.Equal(new DateOnly(2024, 12, 31), calendar.SessionBefore(new DateOnly(2025, 1, 2)));
        Assert.Equal(new DateOnly(2025, 1, 2), calendar.SessionAfter(new DateOnly(2024, 12, 31)));

        // A day before the first session handed in is placed by neither, the table's calendar alone places no day before
        // its years, and a day past them is refused by the table.
        Assert.Null(calendar.IsSession(new DateOnly(2024, 3, 26)));
        Assert.Null(calendar.SessionBefore(new DateOnly(2024, 3, 27)));
        Assert.Null(ReleaseCalendar.Exchange.SessionBefore(new DateOnly(2025, 1, 2)));
        Assert.Throws<InvalidOperationException>(() => ReleaseCalendar.Exchange.SessionAfter(new DateOnly(2027, 12, 31)));
    }

    [Fact]
    public void TheAgreementTheProbesAndTheToneVariantsSharesAreWorkedAsTheirArithmeticSays()
    {
        // Results against the surprise's sign: better with a surprise above nothing agrees, worse with one below agrees,
        // better below and worse above disagree, and in line, not stated, a surprise of nothing and one not held stand apart.
        var results = ReleaseAgreement.ResultsAgainstSurprise(
        [
            ("better", 4.2), ("worse", -1.0), ("better", -0.5), ("worse", 3.0), ("in line", 1.0), ("not stated", -2.0), ("better", 0.0), ("better", null), (null, 2.0),
        ]);

        Assert.Equal((4, 2, 2, 5, 0.5), (results.Compared, results.Agreed, results.Disagreed, results.Apart, results.Share));

        // An answer against a reference: the same word agrees, a reading not held stands apart.
        var against = ReleaseAgreement.Against([("raised", "raised"), ("kept", "raised"), (null, "kept"), ("none given", "none given")]);

        Assert.Equal((3, 2, 1, 1), (against.Compared, against.Agreed, against.Disagreed, against.Apart));

        // The outlook guided figures show: every pair up raised, every pair down lowered, every pair level kept, mixed none.
        Assert.Equal("raised", ReleaseAgreement.OutlookFromGuidance([110m, 2.1m], [100m, 2.0m]));
        Assert.Equal("lowered or withdrawn", ReleaseAgreement.OutlookFromGuidance([90m], [100m, 5m]));
        Assert.Equal("kept", ReleaseAgreement.OutlookFromGuidance([100m], [100m]));
        Assert.Null(ReleaseAgreement.OutlookFromGuidance([110m, 1m], [100m, 2m]));
        Assert.Null(ReleaseAgreement.OutlookFromGuidance([], [100m]));

        // What the tone variant keeps: results better with any outlook but lowered or withdrawn, none given among them.
        var tone = ReleaseAgreement.Tone(
        [
            new ReleaseAnswer("better", "raised", "confident", "stronger"),
            new ReleaseAnswer("better", "none given", "neutral", "not stated"),
            new ReleaseAnswer("better", "lowered or withdrawn", "cautious", "weaker"),
            new ReleaseAnswer("in line", "raised", "neutral", "steady"),
            null,
        ]);

        Assert.Equal((5, 2, 1, 1, 1, 0.5), (tone.Setups, tone.Passing, tone.FailingResults, tone.FailingOutlook, tone.NotHeld, tone.PassingShare));

        // Whether an answer names the filer: its name whole, its first word where no common word, or its ticker as a word.
        var keysight = new ReleaseFiler(["Keysight Technologies, Inc."], ["KEYS"], []);
        var widgets = new ReleaseFiler(["GENERAL WIDGETS CORP"], ["GWX"], []);

        Assert.True(ReleaseProbes.Names("Keysight", keysight));
        Assert.True(ReleaseProbes.Names("keysight technologies", keysight));
        Assert.True(ReleaseProbes.Names("It reads like KEYS.", keysight));
        Assert.False(ReleaseProbes.Names("Agilent Technologies", keysight));
        Assert.False(ReleaseProbes.Names("unknown", keysight));
        Assert.False(ReleaseProbes.Names("General Motors", widgets));
        Assert.True(ReleaseProbes.Names("General Widgets", widgets));
        Assert.Null(ReleaseProbes.Share(0, 0));
    }

    [Fact]
    public void TheSampleIsSharedByIndexInProportionToMembersHalfEitherSideOfTheCutoffAndDrawnAtItsSeed()
    {
        // A hundred a half among 500, 400 and 600 members: 33.33, 26.67 and 40 whole, the one left to the largest remainder.
        var members = new Dictionary<string, int>(StringComparer.Ordinal) { ["GSPC"] = 500, ["MID"] = 400, ["SML"] = 600 };

        Assert.Equal(new Dictionary<string, int>(StringComparer.Ordinal) { ["GSPC"] = 33, ["MID"] = 27, ["SML"] = 40 }, ReleaseSamples.Shares(100, members));

        // Over constructed releases, ten an index each side of the cutoff, a sample of twelve draws two, two and two a half
        // on each side, the same at the seed every time and another at another seed.
        var cutoff = new DateOnly(2025, 1, 31);
        var candidates = new[] { "GSPC", "MID", "SML" }
            .SelectMany(index => Enumerable.Range(0, 20).Select(at => new SampleCandidate($"{index}-{at:00}", index, at < 10 ? new DateOnly(2024, 6, 1).AddDays(at) : new DateOnly(2025, 6, 1).AddDays(at))))
            .ToArray();
        var even = new Dictionary<string, int>(StringComparer.Ordinal) { ["GSPC"] = 1, ["MID"] = 1, ["SML"] = 1 };
        var drawn = ReleaseSamples.Draw(candidates, 12, cutoff, even);

        Assert.Equal(12, drawn.Count);
        Assert.Equal(drawn, ReleaseSamples.Draw(candidates, 12, cutoff, even));
        Assert.NotEqual(drawn, ReleaseSamples.Draw(candidates, 12, cutoff, even, seed: 7));

        foreach (var index in new[] { "GSPC", "MID", "SML" })
        {
            Assert.Equal(2, drawn.Count(accession => accession.StartsWith(index, StringComparison.Ordinal) && int.Parse(accession[^2..], System.Globalization.CultureInfo.InvariantCulture) < 10));
            Assert.Equal(2, drawn.Count(accession => accession.StartsWith(index, StringComparison.Ordinal) && int.Parse(accession[^2..], System.Globalization.CultureInfo.InvariantCulture) >= 10));
        }
    }

    [Fact]
    public async Task AHistoryReadPausesForASlotStopsAtItsUntilAndKeepsARefusedAnswerRefused()
    {
        using var store = ReleaseStore();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);

        // Started at 12:02:30 on a Sunday, inside the three minutes before the early slot: the run pauses until the slot's
        // allowance has passed, no slot having written its row, and then reads.
        var clock = new CallClock(Utc(2026, 10, 18, 12, 2, 30));

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        KeepRelease(store, "0001601046-26-000050", KeysCik, Utc(2026, 5, 19, 20, 8, 49), "May's release");
        KeepRelease(store, "0001601046-26-000051", KeysCik, Utc(2026, 8, 18, 20, 9, 53), "August's release");

        // A run asked to stop at the instant it starts reads nothing and leaves the pin's history open.
        var unasked = ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("nothing is asked")), settings);
        var first = await new LocalRun(unasked, settings, clock, store.DatabaseFile, store.Root, Moving(clock)).ReadHistoryAsync(clock.UtcNow, TextWriter.Null);

        Assert.Equal((LocalRun.LimitReached, 0), (first.Outcome, first.Read));
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM reading_pin WHERE history_completed_at IS NOT NULL;"));

        var replies = new Queue<ReadingReply>(
        [
            Reply("""{"results": "better", "outlook": "kept", "tone": "neutral", "demand": "steady"}"""),
            Reply("no json here"),
            Reply("no json again"),
        ]);
        var feed = new BodyAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("the feed answers")), settings), _ => replies.Dequeue());
        var outcome = await new LocalRun(feed, settings, clock, store.DatabaseFile, store.Root, Moving(clock)).ReadHistoryAsync(null, TextWriter.Null);

        Assert.True(clock.UtcNow >= Utc(2026, 10, 18, 12, 35, 0), $"The read went on at {clock.UtcNow:O}, before the slot's allowance had passed.");

        // Oldest first: May's read, August's asked once more and refused with its cause, both kept, the history complete.
        Assert.Equal((LocalRun.Finished, 1), (outcome.Outcome, outcome.Read));
        Assert.Equal(
            ["0001601046-26-000050|read|null|better", "0001601046-26-000051|refused|not one object|null"],
            Query(store, "SELECT accession, outcome, cause, results FROM release_reading ORDER BY accession;"));
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM reading_pin WHERE history_completed_at IS NOT NULL;"));

        // A new release and a run asked to stop at the instant it starts: nothing read, stopped as asked, the history not
        // marked again.
        KeepRelease(store, "0001601046-26-000052", KeysCik, Utc(2026, 9, 1, 20, 0, 0), "September's release");

        var stopped = await new LocalRun(feed, settings, clock, store.DatabaseFile, store.Root, Moving(clock)).ReadHistoryAsync(clock.UtcNow, TextWriter.Null);

        Assert.Equal((LocalRun.LimitReached, 0), (stopped.Outcome, stopped.Read));
        Assert.Equal(2L, Scalar(store, "SELECT COUNT(*) FROM release_reading;"));
    }

    [Fact]
    public async Task ASlotReadsTheReleasesAcceptedInTheSevenDaysBeforeItsDayNewestFirst()
    {
        using var store = ReleaseStore();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(Utc(2026, 10, 18, 12, 5, 0), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        // The early slot of 2026-10-18 reads from the start of 2026-10-11 in UTC, seven days before its own: a release
        // accepted a second before that is left to the history read, and one at it and one later are read, newest first.
        KeepRelease(store, "0001601046-26-000080", KeysCik, Utc(2026, 10, 10, 23, 59, 59), "a release a second before the week");
        KeepRelease(store, "0001601046-26-000081", KeysCik, Utc(2026, 10, 11, 0, 0, 0), "a release at the week's start");
        KeepRelease(store, "0001601046-26-000082", KeysCik, Utc(2026, 10, 16, 20, 0, 0), "a release in the week");

        var asked = new List<string>();
        var feed = new BodyAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("the feed answers")), settings), body =>
        {
            asked.Add(body);

            return Reply("""{"results": "in line", "outlook": "kept", "tone": "neutral", "demand": "steady"}""");
        });
        var slot = await new LocalRun(feed, settings, clock, store.DatabaseFile, store.Root, NoWait).SlotAsync(LocalSlots.Early, TextWriter.Null);

        Assert.Equal(["a release in the week", "a release at the week's start"], asked);
        Assert.Equal((LocalRun.Finished, 2), (slot.Outcome, slot.Read));
        Assert.Equal(["0001601046-26-000081", "0001601046-26-000082"], Query(store, "SELECT accession FROM release_reading ORDER BY accession;"));
    }

    [Fact]
    public async Task AHistoryReadUnderAPinWhoseModelFileChangedRefusesAndReadsNothing()
    {
        using var store = ReleaseStore();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(Utc(2026, 10, 18, 9, 0, 0), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        KeepRelease(store, "0001601046-26-000090", KeysCik, Utc(2026, 8, 18, 20, 9, 53), "August's release");

        // The model's file written again under the pin: hashed whole on its changed size and refused, nothing asked or kept.
        models.Change();

        var outcome = await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("nothing is asked")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait)
            .ReadHistoryAsync(null, TextWriter.Null);

        Assert.Equal((LocalRun.PinRefused, 0), (outcome.Outcome, outcome.Read));
        Assert.Contains("model's file", outcome.Why, StringComparison.Ordinal);
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM release_reading;"));
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM reading_pin WHERE history_completed_at IS NOT NULL;"));
    }

    [Fact]
    public async Task AReleaseWhoseStoredTextIsNotItsHashIsStoredRefusedAndAskedNothing()
    {
        using var store = ReleaseStore();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(Utc(2026, 10, 18, 9, 0, 0), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        // May's release with its stored hash changed under it, and August's as stored.
        KeepRelease(store, "0001601046-26-000070", KeysCik, Utc(2026, 5, 19, 20, 8, 49), "May's release");
        KeepRelease(store, "0001601046-26-000071", KeysCik, Utc(2026, 8, 18, 20, 9, 53), "August's release");
        store.Execute($"UPDATE release_document SET text_sha256 = '{new string('0', 64)}' WHERE accession = '0001601046-26-000070';");

        var asked = new List<string>();
        var feed = new BodyAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("the feed answers")), settings), body =>
        {
            asked.Add(body);

            return Reply("""{"results": "better", "outlook": "kept", "tone": "neutral", "demand": "steady"}""");
        });
        var outcome = await new LocalRun(feed, settings, clock, store.DatabaseFile, store.Root, NoWait).ReadHistoryAsync(null, TextWriter.Null);

        Assert.Equal([ReleaseAnonymiser.Anonymise("August's release", new ReleaseFiler([], ["KEYS"], []))], asked);
        Assert.Equal((LocalRun.Finished, 1), (outcome.Outcome, outcome.Read));
        Assert.Equal(
            [$"0001601046-26-000070|refused|{LocalRun.TextChanged}|null", "0001601046-26-000071|read|null|better"],
            Query(store, "SELECT accession, outcome, cause, results FROM release_reading ORDER BY accession;"));
    }

    [Fact]
    public async Task AFilersNamesAreReadOffItsSubmissionsAndAskedOnlyForTheFilersNoReadHasNamed()
    {
        // Apple's submissions: its name now and the three it filed under before, newest first, and its ticker.
        var (cik, names, tickers) = ResultsReleases.FilerOf(Captured("filings-AAPL.json"));

        Assert.Equal("0000320193", cik);
        Assert.Equal(["Apple Inc.", "APPLE INC", "APPLE COMPUTER INC", "APPLE COMPUTER INC/ FA"], names);
        Assert.Equal(["AAPL"], tickers);

        // Over a constructed store holding a release of Keysight's and of Apple's, Keysight's named already: Apple's asked
        // alone, and with again both.
        using var store = ReleaseStore();

        KeepRelease(store, KeysAugust, KeysCik, Utc(2026, 8, 18, 20, 9, 53), "Keysight's");
        KeepRelease(store, "0000320193-26-000010", "0000320193", Utc(2026, 7, 30, 20, 30, 0), "Apple's");
        store.Execute($"INSERT INTO release_filer (cik, names, tickers, read_at, run_id) VALUES ('{KeysCik}', '[\"Keysight Technologies, Inc.\"]', '[\"KEYS\"]', '2026-08-19T12:05:00Z', 'constructed');");

        RecordedReleaseArchiveFeed Feed() => RecordedReleaseArchiveFeed.Of(submissions: new Dictionary<string, string> { [KeysCik] = Captured("filings-KEYS.json"), ["0000320193"] = Captured("filings-AAPL.json") });

        var clock = FixedClock.At(Utc(2026, 10, 18, 9, 0, 0), SessionZones.UnitedStates);
        var first = Feed();
        var named = await new ReleaseFetch(first, clock, store.DatabaseFile, store.Root, NoWait).NamesAsync(again: false, "names-first", TextWriter.Null);

        Assert.Equal(["0000320193"], first.Asked);
        Assert.Equal((1, 1, 1, (string?)null), (named.Filers, named.Named, named.Requests, named.Refusal));
        Assert.Equal(["0000320193|names-first", $"{KeysCik}|constructed"], Query(store, "SELECT cik, run_id FROM release_filer ORDER BY cik;"));
        Assert.Equal(["ok|1|1"], Query(store, $"SELECT outcome, rows_written, network_requests FROM run_log WHERE stage = '{ReleaseFetch.NamesStage}';"));

        var again = Feed();

        await new ReleaseFetch(again, clock, store.DatabaseFile, store.Root, NoWait).NamesAsync(again: true, "names-again", TextWriter.Null);

        Assert.Equal(["0000320193", KeysCik], again.Asked);
    }

    [Fact]
    public async Task TheAccuracyReportMeasuresEachAnswerAgainstItsReferencesAndNamesAReadingWrittenBeforeItsLabel()
    {
        using var store = ReleaseStore();

        // Pin 1 at the cutoff 2025-01-31; two of Keysight's releases read, one before the cutoff and one after, each the
        // release of a print the provider's surprise is stored for; a drift setup of each print's quarter, settled.
        store.Execute("INSERT INTO reading_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version, temperature, seed, answer_tokens, context, cutoff, written_at, history_completed_at, run_id) VALUES ('m', 'f', 'h', 1, '2026-10-18T00:00:00.0000000Z', 'r', 'v', 'p', 0, 1, 1, 1, '2025-01-31', '2026-10-18T00:00:00Z', NULL, 'constructed');");
        KeepRelease(store, "0001601046-24-000010", KeysCik, Utc(2024, 8, 20, 20, 5, 0), "a release before the cutoff");
        KeepRelease(store, "0001601046-25-000020", KeysCik, Utc(2025, 8, 19, 20, 5, 0), "a release after it");
        store.Execute("INSERT INTO release_reading (accession, exhibit, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at, run_id) VALUES " +
            "('0001601046-24-000010', 'ex99.htm', 1, 'read', NULL, 'better', 'raised', 'confident', 'stronger', 3, 5000, '2026-10-19T02:00:00Z', 'constructed'), " +
            "('0001601046-25-000020', 'ex99.htm', 1, 'read', NULL, 'better', 'lowered or withdrawn', 'cautious', 'weaker', 3, 5000, '2026-10-18T01:00:00Z', 'constructed');");
        store.Execute("INSERT INTO earnings_reaction (ticker, report_date, timing, reaction_session, estimate, actual, surprise_pct, move_pct) VALUES " +
            "('KEYS', '2024-08-20', 'after', '2024-08-21', '1.0', '1.2', 20.0, 3.0), ('KEYS', '2025-08-19', 'after', '2025-08-20', '1.0', '0.9', -10.0, -4.0);");

        // A print of 2018, before the first session the ledger read, which the calendar cannot place.
        store.Execute("INSERT INTO pulled_surprise (ticker, event_date, timing, eps_actual, eps_estimate, surprise_percent, pull) VALUES ('KEYS', '2018-08-14', 'after', '1.1', '1.0', 10.0, 'constructed');");

        foreach (var session in new[] { "2024-08-23", "2025-08-22" })
        {
            store.Execute(
                "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, entry, stop, cap, end, settled, source, pin) "
                + $"VALUES ('GSPC', 'drift', 'KEYS', '{session}', 'the live rule', 0, '10', '9', 21, 'target', 1, 'history', 'constructed');");
        }

        // The sessions the ledger read in the week of the earlier print, which place its days before the closure table's.
        foreach (var session in new[] { "2024-08-19", "2024-08-20", "2024-08-21", "2024-08-22", "2024-08-23" })
        {
            store.Execute($"INSERT INTO setup_night (index_code, family, session_date, members, setups, live_passes, source) VALUES ('GSPC', 'drift', '{session}', 500, 1, 0, 'history');");
        }

        // The labels written at 2026-10-18T12:00Z: the reading after the cutoff was written before them, and is named.
        var reference = new ReferenceSet(Utc(2026, 10, 18, 12, 0, 0),
        [
            new ReferenceLabel("0001601046-24-000010", "better", "raised", "neutral", "stronger"),
            new ReferenceLabel("0001601046-25-000020", "worse", "lowered or withdrawn", "cautious", "weaker"),
        ]);

        var report = await new ReleaseAccuracy(FixedClock.At(Utc(2026, 10, 20, 9, 0, 0), SessionZones.UnitedStates), store.DatabaseFile).ReportAsync(reference);

        // Worked by hand: the earlier release's better agrees with a surprise of 20, the later's better disagrees with -10;
        // against the labels, results agree before and not after, outlook before and after, tone not before and after,
        // demand both; the tone variant keeps the earlier setup and fails the later on its outlook.
        Assert.Contains("accuracy under pin 1: 2 release(s) read or refused, 2 read; before the cutoff 2025-01-31 and after it", report, StringComparison.Ordinal);
        Assert.Contains("results against the surprise's sign: before 1 of 1 agree (100.0 per cent), 0 apart; after 0 of 1 agree (0.0 per cent), 0 apart; 1 print(s) of a read filer the calendar cannot place left out", report, StringComparison.Ordinal);
        Assert.Contains("reference set: 2 label(s) written by 2026-10-18T12:00:00Z, 0 given on an exhibit a check has since replaced and left out, 2 read under the pin, 1 read before its label", report, StringComparison.Ordinal);
        Assert.Contains("results against the reference set: before 1 of 1 agree (100.0 per cent), 0 apart; after 0 of 1 agree (0.0 per cent), 0 apart", report, StringComparison.Ordinal);
        Assert.Contains("outlook against the reference set: before 1 of 1 agree (100.0 per cent), 0 apart; after 1 of 1 agree (100.0 per cent), 0 apart", report, StringComparison.Ordinal);
        Assert.Contains("tone against the reference set: before 0 of 1 agree (0.0 per cent), 0 apart; after 1 of 1 agree (100.0 per cent), 0 apart", report, StringComparison.Ordinal);
        Assert.Contains("tone variant on GSPC before the cutoff: 1 settled drift setup(s), 0 with no reading held, 1 passing, 0 failing on results, 0 failing on outlook alone, passing share 100.0 per cent", report, StringComparison.Ordinal);
        Assert.Contains("tone variant on GSPC after the cutoff: 1 settled drift setup(s), 0 with no reading held, 0 passing, 0 failing on results, 1 failing on outlook alone, passing share 0.0 per cent", report, StringComparison.Ordinal);

        // And it writes nothing: the run log holds no row of it.
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM run_log;"));
    }

    [Fact]
    public async Task TheProbesKeepEachAnswerBesideWhatItIsSetAgainstAndTheReferenceSetIsWrittenAsTheModelReadsIt()
    {
        using var store = ReleaseStore();
        using var models = new ModelsFolder();
        using var folder = new TemporaryDirectory();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(Utc(2026, 10, 19, 9, 0, 0), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        // Keysight's and Microsoft's releases either side of the model's cutoff, each filer's setups standing under the
        // S&P 500 in the ledger, and one of Keysight's accepted on the samples' day, which no sample draws.
        var keysight = new ReleaseFiler(["Keysight Technologies, Inc."], ["KEYS"], []);
        var microsoft = new ReleaseFiler(["MICROSOFT CORP"], ["MSFT"], []);
        var releases = new (string Accession, string Cik, DateTimeOffset Accepted, string Text, ReleaseFiler Filer)[]
        {
            ("0001601046-24-000010", KeysCik, Utc(2024, 8, 20, 20, 5, 0), "Keysight Technologies, Inc. reported revenue of $1,345 million for the quarter ended July 31, 2024.", keysight),
            ("0001601046-25-000020", KeysCik, Utc(2025, 8, 19, 20, 5, 0), "Keysight Technologies, Inc. reported revenue of $1,402 million for the quarter ended July 31, 2025.", keysight),
            ("0000789019-24-000030", "0000789019", Utc(2024, 7, 30, 20, 5, 0), "Microsoft Corporation reported revenue of $64,700 million for the quarter ended June 30, 2024.", microsoft),
            ("0000789019-25-000040", "0000789019", Utc(2025, 7, 29, 20, 5, 0), "Microsoft Corporation reported revenue of $76,400 million for the quarter ended June 30, 2025.", microsoft),
        };

        foreach (var (accession, cik, accepted, text, _) in releases)
        {
            KeepRelease(store, accession, cik, accepted, text);
        }

        KeepRelease(store, "0001601046-26-000060", KeysCik, Utc(2026, 10, 18, 13, 0, 0), "Keysight Technologies, Inc. reported revenue of $1,500 million.");
        store.Execute($"INSERT INTO release_filer (cik, names, tickers, read_at, run_id) VALUES ('{KeysCik}', '[\"Keysight Technologies, Inc.\"]', '[\"KEYS\"]', '2026-10-18T12:00:00Z', 'constructed'), ('0000789019', '[\"MICROSOFT CORP\"]', '[\"MSFT\"]', '2026-10-18T12:00:00Z', 'constructed');");

        foreach (var ticker in new[] { "KEYS", "MSFT" })
        {
            store.Execute(
                "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, entry, stop, cap, end, settled, source, pin) "
                + $"VALUES ('GSPC', 'pullback', '{ticker}', '2025-03-03', 'the live rule', 0, '10', '9', 21, 'open', 0, 'history', 'constructed');");
        }

        var anonymised = releases.ToDictionary(release => release.Accession, release => ReleaseAnonymiser.Anonymise(release.Text, release.Filer), StringComparer.Ordinal);
        var raw = releases.ToDictionary(release => release.Accession, release => release.Text, StringComparer.Ordinal);
        var steady = Reply("""{"results": "in line", "outlook": "kept", "tone": "neutral", "demand": "steady"}""");
        var stronger = Reply("""{"results": "better", "outlook": "raised", "tone": "confident", "demand": "stronger"}""");
        var asked = new List<string>();

        async Task<LocalRunOutcome> Probe(string kind, Func<string, ReadingReply> answer)
        {
            var feed = new BodyAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("the feed answers")), settings), body =>
            {
                asked.Add(body);

                return answer(body);
            });

            return await new LocalRun(feed, settings, clock, store.DatabaseFile, store.Root, NoWait).ProbeAsync(kind, TextWriter.Null);
        }

        // Twice: each release's text with its names and dates removed asked twice, Microsoft's of 2025 answered otherwise
        // the second time.
        var answered = new Dictionary<string, int>(StringComparer.Ordinal);
        var twice = await Probe(ReleaseProbes.Twice, body =>
        {
            answered[body] = answered.GetValueOrDefault(body) + 1;

            return body == anonymised["0000789019-25-000040"] && answered[body] == 2 ? stronger : steady;
        });

        Assert.Equal((LocalRun.Finished, 4, 3), (twice.Outcome, twice.Compared, twice.Identical));
        Assert.Equal(releases.SelectMany(release => new[] { anonymised[release.Accession], anonymised[release.Accession] }).Order(StringComparer.Ordinal), asked.Order(StringComparer.Ordinal));

        // Distract: the same text and then the text as filed, Keysight's of 2025 answered otherwise with its names in.
        asked.Clear();

        var distract = await Probe(ReleaseProbes.Distract, body => body == raw["0001601046-25-000020"] ? stronger : steady);

        Assert.Equal((LocalRun.Finished, 4, 3), (distract.Outcome, distract.Compared, distract.Identical));
        Assert.Equal(anonymised.Values.Concat(raw.Values).Order(StringComparer.Ordinal), asked.Order(StringComparer.Ordinal));

        // Re-identify: the model asked which company issued each text, its answer naming the filer or not.
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [anonymised["0001601046-24-000010"]] = "Keysight",
            [anonymised["0001601046-25-000020"]] = "Agilent Technologies",
            [anonymised["0000789019-24-000030"]] = "It reads like MSFT.",
            [anonymised["0000789019-25-000040"]] = "unknown",
        };
        var reidentify = await Probe(ReleaseProbes.Reidentify, body => Reply(names[body]));

        Assert.Equal((LocalRun.Finished, 4, 2), (reidentify.Outcome, reidentify.Compared, reidentify.Identical));
        Assert.Equal(
            [
                "distract|0000789019-24-000030|in line|kept|neutral|steady|1",
                "distract|0000789019-25-000040|in line|kept|neutral|steady|1",
                "distract|0001601046-24-000010|in line|kept|neutral|steady|1",
                "distract|0001601046-25-000020|better|raised|confident|stronger|0",
                "reidentify|0000789019-24-000030|It reads like MSFT.|1",
                "reidentify|0000789019-25-000040|unknown|0",
                "reidentify|0001601046-24-000010|Keysight|1",
                "reidentify|0001601046-25-000020|Agilent Technologies|0",
                "twice|0000789019-24-000030|in line|kept|neutral|steady|1",
                "twice|0000789019-25-000040|better|raised|confident|stronger|0",
                "twice|0001601046-24-000010|in line|kept|neutral|steady|1",
                "twice|0001601046-25-000020|in line|kept|neutral|steady|1",
            ],
            Query(store, "SELECT kind, accession, answer, matched FROM release_probe WHERE outcome = 'read' ORDER BY kind, accession;"));
        Assert.Equal(
            ["reidentify|Keysight Technologies, Inc.", "reidentify|MICROSOFT CORP"],
            Query(store, "SELECT DISTINCT kind, against FROM release_probe WHERE kind = 'reidentify' ORDER BY against;"));

        // A probe run again asks nothing of the releases it has probed.
        asked.Clear();

        var again = await Probe(ReleaseProbes.Twice, _ => steady);

        Assert.Equal((LocalRun.Finished, 0), (again.Outcome, again.Compared));
        Assert.Empty(asked);

        // The reference set written out as the model reads each release, the same four drawn, and no store row written.
        var rows = Scalar(store, "SELECT COUNT(*) FROM run_log;");
        var sample = await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("no model is asked")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait)
            .ReferenceAsync(folder.Path, new DateOnly(2025, 1, 31));

        Assert.Equal(anonymised.Keys.Order(StringComparer.Ordinal), sample.Order(StringComparer.Ordinal));
        Assert.Equal(sample, File.ReadAllLines(Path.Combine(folder.Path, "sample.txt")));

        foreach (var accession in sample)
        {
            Assert.Equal(anonymised[accession], File.ReadAllText(Path.Combine(folder.Path, accession + ".txt")));
        }

        Assert.Equal(rows, Scalar(store, "SELECT COUNT(*) FROM run_log;"));
    }

    // A results release kept as the release fetch keeps one, its text compressed beside its hash.
    static void KeepRelease(TemporaryStore store, string accession, string cik, DateTimeOffset accepted, string text)
    {
        var kept = ResultsReleases.Compressed(text);

        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "INSERT INTO release_document (accession, cik, form, filed, accepted_at, exhibit, url, text_gzip, text_sha256, text_length, run_id) " +
            "VALUES ($accession, $cik, '8-K', $filed, $accepted, 'ex99.htm', 'https://www.sec.gov/Archives/edgar/data/1/ex99.htm', $text, $sha, $length, 'constructed');";
        command.Parameters.AddWithValue("$accession", accession);
        command.Parameters.AddWithValue("$cik", cik);
        command.Parameters.AddWithValue("$filed", accepted.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$accepted", accepted.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$text", kept.Gzip);
        command.Parameters.AddWithValue("$sha", kept.Sha256);
        command.Parameters.AddWithValue("$length", kept.Length);
        command.ExecuteNonQuery();
    }
}
