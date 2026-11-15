using System.Globalization;
using System.Security.Cryptography;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Readings;
using EquityBrief.Worker.Quarters;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.10's second part: the rules read each quarter from its filing from the session after the filing
// was made, then from its results release from the first night whose late slot starts after the SEC accepted it, and
// otherwise not at all; the profit check over the four newest quarters so read and the cover over the four newest whose
// filing states interest; the history's first day for each source with its fallback counted; and the measure of what the
// change moves, each worked by hand over constructed quarters and stores.
// see: The rules read each quarter from its filing and then its release from the moment the SEC made it public, and the cover from the four newest quarters stating interest
// see: A release reading is readable from the first night whose late slot starts after the release was accepted
public partial class FixtureExpectations
{
    static DateOnly SourcedDay(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string SourcedStamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The quarters the provider labels: four filed and the fifth, to 2026-09-30, reported in a release before its filing.
    static readonly DateOnly[] SourcedLabels = [.. new[] { "2025-09-30", "2025-12-31", "2026-03-31", "2026-06-30", "2026-09-30" }.Select(SourcedDay)];

    // The four filed quarters, each with the day its filing was made: revenue 100 million, operating income 20 million and
    // net income 10 million a quarter, and the interest given, each as its quarter's figure for the three months to its end.
    static IReadOnlyList<FiledFactRow> SourcedFilings(decimal interest)
    {
        (string End, string Filed)[] quarters = [("2025-09-30", "2025-11-05"), ("2025-12-31", "2026-02-10"), ("2026-03-31", "2026-05-06"), ("2026-06-30", "2026-08-05")];

        return
        [
            .. quarters.SelectMany(quarter =>
            {
                var end = SourcedDay(quarter.End);
                var start = end.AddDays(1).AddMonths(-3);
                var filed = SourcedDay(quarter.Filed);

                return new[]
                {
                    new FiledFactRow("Revenues", start, end, 100_000_000m, filed, "10-Q", "a-" + quarter.End),
                    new FiledFactRow("OperatingIncomeLoss", start, end, 20_000_000m, filed, "10-Q", "a-" + quarter.End),
                    new FiledFactRow("NetIncomeLoss", start, end, 10_000_000m, filed, "10-Q", "a-" + quarter.End),
                    new FiledFactRow("InterestExpense", start, end, interest, filed, "10-Q", "a-" + quarter.End),
                };
            }),
        ];
    }

    // The fifth quarter's results release, its table in millions against the same quarter a year before.
    const string SourcedReleaseText =
        "Condensed consolidated statements of income (in millions) Three months ended September 30, 2026 2025 " +
        "Total revenues 130 100 Operating income 26 20 Net income 13 10";

    [Fact]
    public void AQuarterIsReadFromItsFilingFromTheSessionAfterItWasFiledThenFromItsReleaseFromItsFirstReadableNightAndOtherwiseNotAtAll()
    {
        var filings = SourcedFilings(6_000_000m);

        // The quarter to 2026-06-30, filed on 2026-08-05: not read on that session, read from its filing on the next.
        static string[] Ends(IReadOnlyList<ReadQuarter> read) => [.. read.Select(quarter => SourcedStamp(quarter.PeriodEnd))];

        Assert.Equal(["2026-03-31", "2025-12-31", "2025-09-30"], Ends(QuartersAsRead.Of(SourcedLabels, filings, [], SourcedDay("2026-08-05"))));

        var filedRead = QuartersAsRead.Of(SourcedLabels, filings, [], SourcedDay("2026-08-06"));

        Assert.Equal(["2026-06-30", "2026-03-31", "2025-12-31", "2025-09-30"], Ends(filedRead));
        Assert.Equal((QuarterFrom.Filing, SourcedDay("2026-08-05"), 100_000_000m, 20_000_000m, 10_000_000m, 6_000_000m), (filedRead[0].From, filedRead[0].Known, filedRead[0].Revenue!.Value, filedRead[0].OperatingIncome!.Value, filedRead[0].NetIncome!.Value, filedRead[0].InterestExpense!.Value));

        // Its release accepted at noon on 2026-10-29, before that night's late slot at 21:40: not read on the night before,
        // read on that night's, known from the day before it, its three lines read off its table against the year before as
        // filed and no interest, which no release states.
        ReleaseAccepted beforeTheSlot = new(SourcedReleaseText, new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal("2026-06-30", Ends(QuartersAsRead.Of(SourcedLabels, filings, [beforeTheSlot], SourcedDay("2026-10-28")))[0]);

        var released = QuartersAsRead.Of(SourcedLabels, filings, [beforeTheSlot], SourcedDay("2026-10-29"));

        Assert.Equal(["2026-09-30", "2026-06-30", "2026-03-31", "2025-12-31", "2025-09-30"], Ends(released));
        Assert.Equal((QuarterFrom.Release, SourcedDay("2026-10-28"), 130_000_000m, 26_000_000m, 13_000_000m, (decimal?)null), (released[0].From, released[0].Known, released[0].Revenue!.Value, released[0].OperatingIncome!.Value, released[0].NetIncome!.Value, released[0].InterestExpense));
        Assert.Equal((100_000_000m, 20_000_000m), (released[0].RevenueYearEarlier!.Value, released[0].OperatingIncomeYearEarlier!.Value));

        // Accepted at 22:00, after that night's slot: read from the next session's night and not on its own.
        ReleaseAccepted afterTheSlot = new(SourcedReleaseText, new DateTimeOffset(2026, 10, 29, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal("2026-06-30", Ends(QuartersAsRead.Of(SourcedLabels, filings, [afterTheSlot], SourcedDay("2026-10-29")))[0]);
        Assert.Equal((QuarterFrom.Release, SourcedDay("2026-10-29")), QuartersAsRead.Of(SourcedLabels, filings, [afterTheSlot], SourcedDay("2026-10-30")) is [var late, ..] ? (late.From, late.Known) : default);
        Assert.Equal(SourcedDay("2026-10-29"), QuartersAsRead.KnownFrom(afterTheSlot.Accepted));

        // A quarter neither a filing nor a release reads is read as no quarter at all: the quarter to 2026-12-31, whose only
        // release came before its end.
        Assert.Equal("2026-09-30", Ends(QuartersAsRead.Of([.. SourcedLabels, SourcedDay("2026-12-31")], filings, [beforeTheSlot], SourcedDay("2027-02-01")))[0]);

        // A release whose table does not read the year before as filed reads nothing, and the quarter is not read.
        ReleaseAccepted unread = new(SourcedReleaseText.Replace("130 100", "130 90", StringComparison.Ordinal).Replace("26 20", "26 18", StringComparison.Ordinal).Replace("13 10", "13 9", StringComparison.Ordinal), beforeTheSlot.Accepted);

        Assert.Equal("2026-06-30", Ends(QuartersAsRead.Of(SourcedLabels, filings, [unread], SourcedDay("2026-11-02")))[0]);
    }

    [Fact]
    public void TheProfitCheckReadsTheFourNewestQuartersSoReadAndTheCoverTheFourNewestWhoseFilingStatesInterest()
    {
        var night = SourcedDay("2026-10-29");
        var release = new ReleaseAccepted(SourcedReleaseText, new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));

        FiledIncome[] Read(decimal interest) => [.. QuartersAsRead.Of(SourcedLabels, SourcedFilings(interest), [release], night).Select(quarter => quarter.Income)];

        // The profit over the release's quarter and the three filed before it: 13 + 10 + 10 + 10 million, above nothing.
        var covered = Read(6_000_000m);

        Assert.True(MemberReadings.Profit(covered, night));

        // The cover leaves the release's quarter, its interest not on file, and reads the four filed: 80 million of operating
        // income against twice 24 million of interest, which passes, and they end at 2026-06-30, older than the profit's newest.
        Assert.Equal(
            ["2026-06-30", "2026-03-31", "2025-12-31", "2025-09-30"],
            MemberReadings.CoverQuarters(covered, night).Select(quarter => SourcedStamp(quarter.PeriodEnd)));
        Assert.True(MemberReadings.Coverage(covered, night, "Industrials"));

        // At 11 million a quarter the four filed ask 88 million of the 80 and fail. Read as stating no interest, the release's
        // quarter would have lowered the total: the four newest then 26 + 20 + 20 + 20 = 86 million against twice 33 million,
        // which passes. Both figures come from the same four, so it does not.
        var costly = Read(11_000_000m);

        Assert.False(MemberReadings.Coverage(costly, night, "Industrials"));
        Assert.True(MemberReadings.Coverage([costly[0] with { InterestExpense = 0m }, .. costly[1..]], night, "Industrials"));

        // A financial company passes whatever it files, and a company stating no interest on any quarter passes on four
        // quarters stating their operating income.
        Assert.True(MemberReadings.Coverage(costly, night, MemberReadings.Financials));
        Assert.True(MemberReadings.Coverage([.. costly.Select(quarter => quarter with { InterestExpense = null })], night, "Industrials"));

        // Three quarters stating interest are fewer than four and fail, however small it is.
        Assert.False(MemberReadings.Coverage([.. covered.Select((quarter, at) => at == 1 ? quarter with { InterestExpense = null } : quarter)], night, "Industrials"));
    }

    [Fact]
    public void TheHistoryReadsEachQuarterFromTheDayItsDocumentWasMadePublicAndCountsTheQuartersFallingBackToTheProviders()
    {
        var filings = SourcedFilings(6_000_000m);

        // A quarter with a filing, read from the day after it was filed whatever the provider's date; the release's quarter,
        // from the night after its 22:00 acceptance; and three with neither: the later of the report day and the provider's
        // date where the print came first, and where it came after, and the provider's date where no print falls within 120
        // days of the quarter's end.
        var read = QuartersAsRead.InTheHistory(
            [
                (SourcedDay("2025-09-30"), SourcedDay("2025-11-07")),
                (SourcedDay("2026-09-30"), SourcedDay("2026-11-10")),
                (SourcedDay("2026-12-31"), SourcedDay("2027-02-12")),
                (SourcedDay("2027-03-31"), SourcedDay("2027-05-01")),
                (SourcedDay("2027-06-30"), SourcedDay("2027-08-02")),
            ],
            filings,
            [new DateTimeOffset(2026, 10, 29, 22, 0, 0, TimeSpan.Zero)],
            [SourcedDay("2027-01-28"), SourcedDay("2027-05-03"), SourcedDay("2027-12-01")]);

        (string, string, QuarterFrom?)[] expected =
        [
            ("2025-09-30", "2025-11-05", QuarterFrom.Filing),
            ("2026-09-30", "2026-10-29", QuarterFrom.Release),
            ("2026-12-31", "2027-02-12", null),
            ("2027-03-31", "2027-05-03", null),
            ("2027-06-30", "2027-08-02", null),
        ];

        Assert.Equal(expected, read.Select(quarter => (SourcedStamp(quarter.PeriodEnd), SourcedStamp(quarter.Known), quarter.From)));
    }

    [Fact]
    public async Task TheSweepHistoryReadsEachPulledQuarterFromItsPublicDayAndStatesHowManyFellBackToTheProvidersDate()
    {
        using var store = new TemporaryStore().Migrated();

        // HQ's pulled quarters: the one filed, the one released and one with neither, its print on 2027-01-28 and the
        // provider's date 2027-02-12; HR with no filer known, its one quarter read from the provider's date.
        store.Execute(
            "INSERT INTO pulled_income (ticker, period_end, filing_date, net_income, operating_income, interest_expense, pull) VALUES " +
            "('HQ', '2025-09-30', '2025-11-07', '10', '20', '6', 'p'), ('HQ', '2026-09-30', '2026-11-10', '13', '26', NULL, 'p'), " +
            "('HQ', '2026-12-31', '2027-02-12', '10', '20', '6', 'p'), ('HR', '2026-06-30', '2026-08-03', '5', '5', NULL, 'p');");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('HQ', '2026-08-01T00:00:00Z', 'CIKHQ');");
        store.Execute("INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('HQ', '2027-01-28', 'earnings', 'after', '{}', '2027-01-28T22:00:00Z');");

        foreach (var fact in SourcedFilings(6_000_000m).Where(fact => fact.End == SourcedDay("2025-09-30")))
        {
            store.Execute(FormattableString.Invariant(
                $"INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('CIKHQ', '{fact.Concept}', '{SourcedStamp(fact.Start)}', '{SourcedStamp(fact.End)}', '{fact.Value}', '{SourcedStamp(fact.Filed)}', '10-Q', 'a', 'constructed');"));
        }

        KeepRelease(store, "CIKHQ-26-1", "CIKHQ", new DateTimeOffset(2026, 10, 29, 22, 0, 0, TimeSpan.Zero), SourcedReleaseText);

        var read = await new SweepHistory(store.DatabaseFile).IncomeReadAsync(SourcedDay("2027-03-01"));

        Assert.Equal((4, 1, 1, 2), (read.Quarters, read.FromFilings, read.FromReleases, read.Fallbacks));
        Assert.Equal(
            ["2025-09-30|2025-11-05", "2026-09-30|2026-10-29", "2026-12-31|2027-02-12"],
            read.Income["HQ"].Select(quarter => $"{SourcedStamp(quarter.PeriodEnd)}|{SourcedStamp(quarter.FilingDate)}").Order(StringComparer.Ordinal));
        Assert.Equal(SourcedDay("2026-08-03"), Assert.Single(read.Income["HR"]).FilingDate);

        // The figures stay the pull's, read on the days the documents were made public, and the history's plain read is the
        // same quarters.
        Assert.Equal(13m, read.Income["HQ"].Single(quarter => quarter.PeriodEnd == SourcedDay("2026-09-30")).NetIncome);

        var plain = await new SweepHistory(store.DatabaseFile).IncomeAsync(SourcedDay("2027-03-01"));

        Assert.Equal(read.Income["HQ"], plain["HQ"]);
    }

    [Fact]
    public async Task TheMeasureOfWhatTheChangeMovesRedrawsAnIndexsListFromItsStoredPartsAndWritesNothing()
    {
        using var store = new TemporaryStore().Migrated();
        var night = SourcedDay("2026-11-05");

        // An S&P 400 night: MA listed first under the breakout, its stored profit part passing on the provider's quarters,
        // and MB stopped by its profit part alone. Read from their filings MA's four quarters lose 10 each and MB's earn 10.
        store.Execute($"INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES ('MID', '{SourcedStamp(night)}', 2, 0.6, 1, '{{}}', 0);");

        static string Gates(bool profit) => FamilyRule.GatesJson(
        [
            new EquityBrief.Core.Filter.Gate("market", true, string.Empty, FamilyRule.Values()),
            new EquityBrief.Core.Filter.Gate("setup", true, string.Empty, FamilyRule.Values()),
            new EquityBrief.Core.Filter.Gate("floors", true, string.Empty, FamilyRule.Values()),
            new EquityBrief.Core.Filter.Gate(EquityBrief.Core.Cards.RuleRows.Profit, profit, profit ? string.Empty : "a loss", FamilyRule.Values()),
        ]);

        store.Execute(
            "INSERT INTO index_family_result (index_code, session_date, ticker, family, passed, place, entry, stop, reason, gates) VALUES " +
            $"('MID', '{SourcedStamp(night)}', 'MA', 'breakout', 1, 1, '100', '95', NULL, '{Gates(true)}'), " +
            $"('MID', '{SourcedStamp(night)}', 'MB', 'breakout', 0, NULL, NULL, NULL, 'the profit check', '{Gates(false)}');");
        store.Execute($"INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also) VALUES ('MID', '{SourcedStamp(night)}', 'MA', 'breakout', 'listed', 1, '[]');");

        foreach (var (ticker, net) in new[] { ("MA", -10m), ("MB", 10m) })
        {
            store.Execute($"INSERT INTO company (ticker, fetched_at, cik, sector) VALUES ('{ticker}', '2026-08-01T00:00:00Z', '{CikFor(ticker)}', 'Industrials');");

            foreach (var (end, filed) in new[] { ("2025-12-31", "2026-02-10"), ("2026-03-31", "2026-05-06"), ("2026-06-30", "2026-08-05"), ("2026-09-30", "2026-10-30") })
            {
                store.Execute($"INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date) VALUES ('{ticker}', '2026-11-01T23:50:00Z', '2026-11-01', '{end}', '{filed}');");
                QuarterFacts(store, CikFor(ticker), end, filed, ("NetIncomeLoss", net), ("OperatingIncomeLoss", net));
            }
        }

        var before = StoreHash(store.DatabaseFile);
        var said = new StringWriter();
        var moved = await QuartersMoved.RunAsync(store.DatabaseFile, SourcedDay("2026-11-01"), said);

        // MA's profit now fails and MB's passes: MA leaves the list and MB, the one passer left, takes its place.
        var mid = Assert.Single(moved, one => one.Index == "MID");

        Assert.Equal((night, 2, true), (mid.Night, mid.PartsChanged, mid.MatchesTheNight));
        Assert.Equal(["MA (breakout)"], mid.Removed);
        Assert.Equal(["MB (breakout)"], mid.Added);
        Assert.Contains("quarters-moved: 2026-11-05 MID: 2 member(s) whose parts changed, 2 pick(s) moved, removed MA (breakout), added MB (breakout)", said.ToString(), StringComparison.Ordinal);
        Assert.Contains("quarters-moved: MID: 2 pick(s) moved over 1 night(s) from 2026-11-01, 1 removed and 1 added", said.ToString(), StringComparison.Ordinal);

        // The four named picks of 2026-11-05 are each said, part by part, a pick the store holds no reading of saying so.
        Assert.All(QuartersMoved.Named, ticker => Assert.Contains($"quarters-moved: {ticker} on 2026-11-05 under no index the member readings hold: ", said.ToString(), StringComparison.Ordinal));

        // And it wrote nothing.
        Assert.Equal(before, StoreHash(store.DatabaseFile));
    }
}
