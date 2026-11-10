using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Chart;
using EquityBrief.Core.History;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Charts;
using EquityBrief.Worker.History;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.1: the data check's classes worked by hand on both sides of each threshold over constructed sessions,
// a history's end read from the SEC's captured filings and from constructed ones, the check run over a constructed store moving
// no pulled row, bar or mark, and no source a rule's version pins among those 23.1 writes.
// see: The data check classifies every stock's pulled sessions by stated rules into a table of its own, and moves no price row
// see: A stock's history ends by rule on its company's last regular session read from its Form 25 or the 8-K completing its merger
public partial class FixtureExpectations
{
    internal static string[] DataCheckClaims =>
    [
        CheckReach.Key(Scope.LimitsTable, "The data check's volume"),
        CheckReach.Key(Scope.LimitsTable, "The data check's jumps"),
        CheckReach.Key(Scope.LimitsTable, "The data check's starts to read"),
        CheckReach.Key(Scope.LimitsTable, "A history's end"),
        CheckReach.Key(Scope.FailureTable, "A pulled series the data check cannot read"),
    ];

    // Every row 23.1 adds to the document.
    internal static string[] DataCheckRows => [.. ComponentAccess.DataCheckerRows, .. SchemaColumns.DataCheckStoreRows, .. DataCheckClaims, .. Reading.ReadSurface.DataCheckPageRows];

    static readonly DateOnly HeldFirst = new(2016, 1, 4);

    static HeldSession HeldAt(int at, decimal close, long volume = 200_000, decimal? raw = null) =>
        new(HeldFirst.AddDays(at), close + 0.5m, close + 1, close - 1, close, raw ?? close, volume);

    // 63 sessions at 100 on the given volume, the one in the middle replaced.
    static List<HeldSession> HeldFlat(long volume = 200_000) => [.. Enumerable.Range(0, 63).Select(at => HeldAt(at, 100m + (at % 2 == 0 ? 0.1m : 0m), volume))];

    static PulledCalendar CalendarOf(IEnumerable<DateOnly> days)
    {
        var held = days.Order().ToArray();

        return new PulledCalendar(held.ToDictionary(day => day, _ => 1), [(held[0], held[^1])]);
    }

    static IReadOnlyList<DataFinding> HeldFindings(IReadOnlyList<HeldSession> sessions, IReadOnlyDictionary<DateOnly, int>? jumps = null, IReadOnlyList<CheckedSplit>? splits = null, IReadOnlyList<CheckedDividend>? dividends = null, IReadOnlyList<SessionMark>? marks = null, PulledCalendar? calendar = null, DateOnly? firstRead = null, (string, DateOnly?)? change = null) =>
        DataCheck.Read(
            new HeldStock("HELD", sessions, splits ?? [], dividends ?? [], change, marks),
            calendar ?? CalendarOf(sessions.Select(session => session.Session)),
            jumps ?? new Dictionary<DateOnly, int>(),
            firstRead ?? sessions[0].Session);

    static string KindOn(IReadOnlyList<DataFinding> findings, string kindOf, DateOnly session) =>
        Assert.Single(findings, finding => finding.Class == kindOf && finding.Session == session).Kind;

    [Fact]
    public void ACandleWithNoVolumeIsThinOnTheStretchsMedianOrItsShareOfEmptySessionsAndOtherwiseCopiedOrLiquid()
    {
        // The middle session of 63 holds no volume; the 62 around it hold 100,000 each, a median at the limit and not under it.
        var liquid = HeldFlat(100_000);
        liquid[31] = HeldAt(31, 107m, 0);
        Assert.Equal(DataCheck.Liquid, KindOn(HeldFindings(liquid), DataCheck.NoVolume, liquid[31].Session));

        // 99,999 is under it.
        var thin = HeldFlat(99_999);
        thin[31] = HeldAt(31, 107m, 0);
        Assert.Equal(DataCheck.Thin, KindOn(HeldFindings(thin), DataCheck.NoVolume, thin[31].Session));

        // Sixteen of the 62 around it empty is a quarter or more, fifteen is not, the median 200,000 either way.
        var sixteen = HeldFlat();
        var fifteen = HeldFlat();
        sixteen[31] = fifteen[31] = HeldAt(31, 107m, 0);

        foreach (var at in new[] { 1, 3, 5, 7, 9, 11, 13, 15, 47, 49, 51, 53, 55, 57, 59, 61 })
        {
            sixteen[at] = HeldAt(at, 100m, 0);
        }

        foreach (var at in new[] { 1, 3, 5, 7, 9, 11, 13, 15, 47, 49, 51, 53, 55, 57, 59 })
        {
            fifteen[at] = HeldAt(at, 100m, 0);
        }

        Assert.Equal(DataCheck.Thin, KindOn(HeldFindings(sixteen), DataCheck.NoVolume, sixteen[31].Session));
        Assert.Equal(DataCheck.Liquid, KindOn(HeldFindings(fifteen), DataCheck.NoVolume, fifteen[31].Session));

        // Every price the close before and no volume, in a liquid stretch, is a copied candle; in a thin one it is thin.
        var copied = HeldFlat();
        copied[31] = new(copied[31].Session, copied[30].Close, copied[30].Close, copied[30].Close, copied[30].Close, copied[30].Close, 0);
        Assert.Equal(DataCheck.Copied, KindOn(HeldFindings(copied), DataCheck.NoVolume, copied[31].Session));

        var copiedThin = HeldFlat(99_999);
        copiedThin[31] = new(copiedThin[31].Session, copiedThin[30].Close, copiedThin[30].Close, copiedThin[30].Close, copiedThin[30].Close, copiedThin[30].Close, 0);
        Assert.Equal(DataCheck.Thin, KindOn(HeldFindings(copiedThin), DataCheck.NoVolume, copiedThin[31].Session));
    }

    [Fact]
    public void AMissingSessionIsOneTheCalendarReadsInsideTheSeriesAndAGapOfTwentyIsAnEmergence()
    {
        var full = HeldFlat();
        var gap = full[31].Session;
        var calendar = CalendarOf(full.Select(session => session.Session));
        var held = full.Where(session => session.Session != gap).ToList();
        var liquid = HeldFindings(held, calendar: calendar);

        Assert.Equal(DataCheck.Liquid, KindOn(liquid, DataCheck.Missing, gap));
        Assert.Contains("median volume is 200,000 shares", Assert.Single(liquid, finding => finding.Class == DataCheck.Missing).Says, StringComparison.Ordinal);

        var thin = HeldFlat(99_999).Where(session => session.Session != gap).ToList();
        Assert.Equal(DataCheck.Thin, KindOn(HeldFindings(thin, calendar: calendar), DataCheck.Missing, gap));

        // A day before the series starts or after it ends is no missing session.
        Assert.DoesNotContain(HeldFindings(full.Skip(1).ToList(), calendar: calendar), finding => finding.Class == DataCheck.Missing);

        // Twenty sessions the calendar reads with no row and then a session again is an emergence, nineteen is not.
        var twenty = full.Where((_, at) => at < 20 || at >= 40).ToList();
        var nineteen = full.Where((_, at) => at < 20 || at >= 39).ToList();

        var emerged = Assert.Single(HeldFindings(twenty, calendar: calendar), finding => finding.Class == DataCheck.Emergence);
        Assert.Equal((full[40].Session, DataCheck.ToRead), (emerged.Session, emerged.Kind));
        Assert.Equal(20, HeldFindings(twenty, calendar: calendar).Count(finding => finding.Class == DataCheck.Missing));
        Assert.DoesNotContain(HeldFindings(nineteen, calendar: calendar), finding => finding.Class == DataCheck.Emergence);
    }

    // 63 sessions, a jump on the 32nd: from 100 to the close given.
    static List<HeldSession> JumpTo(decimal close, long volume = 200_000, long around = 200_000, decimal after = 0)
    {
        var sessions = Enumerable.Range(0, 63).Select(at => HeldAt(at, at < 31 ? 100m : close, around)).ToList();

        sessions[31] = HeldAt(31, close, volume);

        if (after > 0)
        {
            for (var at = 32; at < 63; at++)
            {
                sessions[at] = HeldAt(at, after, around);
            }
        }

        return sessions;
    }

    [Fact]
    public void AJumpIsAMovePastAQuarterNamedByTheFirstRuleThatHoldsInOrder()
    {
        var on = HeldFirst.AddDays(31);

        // 25% exactly is no jump; 25.01% is, and with nothing to explain it is unexplained.
        Assert.Empty(DataCheck.Jumps(JumpTo(75m)));
        Assert.Equal(DataCheck.Unexplained, KindOn(HeldFindings(JumpTo(74.99m)), DataCheck.Jump, on));

        // Heavy volume is 1.5 times the stretch's median or more: 300,000 against 200,000 is, 299,999 is not.
        Assert.Equal(DataCheck.OnVolume, KindOn(HeldFindings(JumpTo(60m, 300_000)), DataCheck.Jump, on));
        Assert.Equal(DataCheck.Unexplained, KindOn(HeldFindings(JumpTo(60m, 299_999)), DataCheck.Jump, on));

        // Ten stocks jumping on the session is market or sector wide, nine is not; the volume comes before it.
        Assert.Equal(DataCheck.Wide, KindOn(HeldFindings(JumpTo(60m, 299_999), new Dictionary<DateOnly, int> { [on] = 10 }), DataCheck.Jump, on));
        Assert.Equal(DataCheck.Unexplained, KindOn(HeldFindings(JumpTo(60m, 299_999), new Dictionary<DateOnly, int> { [on] = 9 }), DataCheck.Jump, on));
        Assert.Equal(DataCheck.OnVolume, KindOn(HeldFindings(JumpTo(60m, 300_000), new Dictionary<DateOnly, int> { [on] = 10 }), DataCheck.Jump, on));

        // A thin stretch comes before the volume: 300,000 against a median of 99,999 is heavy and still thin.
        Assert.Equal(DataCheck.Thin, KindOn(HeldFindings(JumpTo(60m, 300_000, 99_999)), DataCheck.Jump, on));

        // The operator's word on the session, or on the one before, comes before the count of stocks.
        var marked = HeldFindings(JumpTo(60m), new Dictionary<DateOnly, int> { [on] = 10 }, marks: [new SessionMark(on, SessionMarking.BadPrint, "a bad print")]);
        Assert.Equal(DataCheck.Marked, KindOn(marked, DataCheck.Jump, on));
        Assert.Equal(DataCheck.Marked, KindOn(HeldFindings(JumpTo(60m), marks: [new SessionMark(on.AddDays(-1), SessionMarking.CheckedAndKept, "kept")]), DataCheck.Jump, on));
        Assert.Equal(DataCheck.Unexplained, KindOn(HeldFindings(JumpTo(60m), marks: [new SessionMark(on.AddDays(-2), SessionMarking.CheckedAndKept, "kept")]), DataCheck.Jump, on));

        // A stored action comes first: a split the move matches, or a dividend at least half the fall. 100 to 60 falls 40, so a
        // dividend of 20 explains it and 19.99 does not.
        Assert.Equal(DataCheck.ByAnAction, KindOn(HeldFindings(JumpTo(50m), new Dictionary<DateOnly, int> { [on] = 10 }, splits: [new CheckedSplit(on, 2m, 1m)]), DataCheck.Jump, on));
        Assert.Equal(DataCheck.ByAnAction, KindOn(HeldFindings(JumpTo(60m), dividends: [new CheckedDividend(on, 20m)]), DataCheck.Jump, on));
        Assert.Equal(DataCheck.Unexplained, KindOn(HeldFindings(JumpTo(60m), dividends: [new CheckedDividend(on, 19.99m)]), DataCheck.Jump, on));
    }

    [Fact]
    public void ASpikeBackComesWithinTenPerCentOfTheCloseBeforeInThreeSessionsAndItsMoveBackIsNoJump()
    {
        var on = HeldFirst.AddDays(31);

        // 100 to 70, then 90 on the third session after: 10% from 100, within reach. The move back is part of the spike.
        var back = JumpTo(70m, after: 100m);
        back[32] = HeldAt(32, 72m);
        back[33] = HeldAt(33, 71m);
        back[34] = HeldAt(34, 90m);

        var spike = HeldFindings(back);
        Assert.Equal(DataCheck.SpikeBack, KindOn(spike, DataCheck.Jump, on));
        Assert.Single(spike, finding => finding.Class == DataCheck.Jump);

        // 89.99 is past 10%, so the fall is unexplained and the rise back is a jump of its own.
        var short10 = JumpTo(70m, after: 100m);
        short10[32] = HeldAt(32, 72m);
        short10[33] = HeldAt(33, 71m);
        short10[34] = HeldAt(34, 89.99m);

        var fell = HeldFindings(short10);
        Assert.Equal(DataCheck.Unexplained, KindOn(fell, DataCheck.Jump, on));
        Assert.Equal(DataCheck.Unexplained, KindOn(fell, DataCheck.Jump, HeldFirst.AddDays(34)));

        // Back on the fourth session after is too late.
        var late = JumpTo(70m, after: 100m);
        late[32] = HeldAt(32, 72m);
        late[33] = HeldAt(33, 71m);
        late[34] = HeldAt(34, 72m);
        Assert.Equal(DataCheck.Unexplained, KindOn(HeldFindings(late), DataCheck.Jump, on));
    }

    [Fact]
    public void ASplitOrDividendTheClosesDoNotCarryIsFlaggedAndOneTheyCarryIsNot()
    {
        var on = HeldFirst.AddDays(31);

        // A two for one split leaves the adjusted close half the raw one before it; the same ratio either side is not carried.
        IReadOnlyList<HeldSession> carried = [.. Enumerable.Range(0, 63).Select(at => HeldAt(at, 50m, raw: at < 31 ? 100m : 50m))];
        IReadOnlyList<HeldSession> notCarried = [.. Enumerable.Range(0, 63).Select(at => HeldAt(at, at < 31 ? 100m : 50m, raw: at < 31 ? 100m : 50m))];
        CheckedSplit[] split = [new(on, 2m, 1m)];

        Assert.DoesNotContain(HeldFindings(carried, splits: split), finding => finding.Class == DataCheck.NotCarried);
        Assert.Equal(DataCheck.Split, KindOn(HeldFindings(notCarried, splits: split), DataCheck.NotCarried, on));

        // A dividend of 1 on a raw close of 100 should leave a step of 1%; none is a gap of 1%, past the half per cent.
        IReadOnlyList<HeldSession> paid = [.. Enumerable.Range(0, 63).Select(at => HeldAt(at, at < 31 ? 99m : 99m, raw: at < 31 ? 100m : 99m))];
        IReadOnlyList<HeldSession> unpaid = [.. Enumerable.Range(0, 63).Select(at => HeldAt(at, at < 31 ? 100m : 99m, raw: at < 31 ? 100m : 99m))];
        CheckedDividend[] dividend = [new(on, 1m)];

        Assert.DoesNotContain(HeldFindings(paid, dividends: dividend), finding => finding.Class == DataCheck.NotCarried);
        Assert.Equal(DataCheck.Dividend, KindOn(HeldFindings(unpaid, dividends: dividend), DataCheck.NotCarried, on));
    }

    [Fact]
    public void ATickerChangeAndAWhenIssuedStartAreFlaggedForAPersonToReadAgainstTheFilings()
    {
        // A change on a day the series holds no session stands on the first session after it.
        var full = HeldFlat(100_000);
        var sessions = full.Where((_, at) => at != 10).ToList();
        var change = Assert.Single(HeldFindings(sessions, change: ("OLD", full[10].Session), calendar: CalendarOf(sessions.Select(session => session.Session))), finding => finding.Class == DataCheck.TickerChange);

        Assert.Equal((full[11].Session, DataCheck.ToRead), (change.Session, change.Kind));
        Assert.Equal(sessions[0].Session, Assert.Single(HeldFindings(sessions, change: ("OLD", null)), finding => finding.Class == DataCheck.TickerChange).Session);

        // A series starting after the first session read whose first session trades under a quarter of the median after it:
        // 24,999 against 100,000 is, 25,000 is not, and a series starting on the first session read is not read for it.
        var starts = HeldFlat(100_000);
        starts[0] = HeldAt(0, 100m, 24_999);
        Assert.Single(HeldFindings(starts, firstRead: HeldFirst.AddDays(-1)), finding => finding.Class == DataCheck.WhenIssued);
        Assert.DoesNotContain(HeldFindings(starts, firstRead: HeldFirst), finding => finding.Class == DataCheck.WhenIssued);

        starts[0] = HeldAt(0, 100m, 25_000);
        Assert.DoesNotContain(HeldFindings(starts, firstRead: HeldFirst.AddDays(-1)), finding => finding.Class == DataCheck.WhenIssued);
    }

    [Fact]
    public void EachFindingFallsInOneEraByItsSessionsYear()
    {
        Assert.Equal(["2005 to 2009", "2010 to 2014", "2015 to 2019", "2020 on"], DataCheck.Eras);
        Assert.Equal("2005 to 2009", DataCheck.Era(new DateOnly(2009, 12, 31)));
        Assert.Equal("2010 to 2014", DataCheck.Era(new DateOnly(2010, 1, 4)));
        Assert.Equal("2010 to 2014", DataCheck.Era(new DateOnly(2014, 12, 31)));
        Assert.Equal("2015 to 2019", DataCheck.Era(new DateOnly(2015, 1, 2)));
        Assert.Equal("2015 to 2019", DataCheck.Era(new DateOnly(2019, 12, 31)));
        Assert.Equal("2020 on", DataCheck.Era(new DateOnly(2020, 1, 2)));
        Assert.Equal(DataCheck.BeforeTheEras, DataCheck.Era(new DateOnly(2004, 12, 31)));
    }

    static string HistoryEndsFixture => Path.Combine(Repository.Root, "fixtures", "history-ends-2026-11-06");

    [Fact]
    public void MerrillsHistoryEndsAtItsMergersCompletionAndPepsiCosHasNoEndAsTheFilingsWereReadByHand()
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(HistoryEndsFixture, "expectations", "history-ends.json")));

        Assert.Equal("derived", expected.RootElement.GetProperty("derivation").GetString());

        var cases = expected.RootElement.GetProperty("cases");

        foreach (var name in new[] { "mer", "pep" })
        {
            var read = HistoryEnd.Of(FilerFilings.Read(Path.Combine(HistoryEndsFixture, name)));
            var want = cases.GetProperty(name);

            Assert.Equal(want.GetProperty("endsBefore").GetString(), read.EndsBefore?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Assert.Equal(want.GetProperty("readFrom").GetString(), read.ReadFrom?.Accession);
        }

        var merrill = FilerFilings.Read(Path.Combine(HistoryEndsFixture, "mer"));

        // On the days a pull reads as sessions, the last before 2009-01-02, a Friday after the New Year holiday, is 2008-12-31.
        var traded = CalendarOf([new DateOnly(2008, 12, 29), new DateOnly(2008, 12, 30), new DateOnly(2008, 12, 31), new DateOnly(2009, 1, 2), new DateOnly(2009, 1, 5)]);
        Assert.Equal(new DateOnly(2008, 12, 31), HistoryEnd.LastSession(HistoryEnd.Of(merrill), traded));
        Assert.Equal(cases.GetProperty("mer").GetProperty("lastSession").GetString(), HistoryEnd.LastSession(HistoryEnd.Of(merrill), traded)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.Null(HistoryEnd.LastSession(HistoryEnd.Of(FilerFilings.Read(Path.Combine(HistoryEndsFixture, "pep"))), traded));
        Assert.Equal("Its history ends on the last session before 2009-01-02, the day its 8-K reporting its merger complete was filed (0000950123-09-000005).", HistoryEnd.Of(merrill).Says);

        // Each removal read for what it is: the New York Stock Exchange's of the common stock, takes effect on 2009-01-15; the
        // depositary shares are another class; Chicago's is a regional exchange's; the exchangeable securities name another
        // company's common stock and are another class.
        FilerFiling Filed(string accession) => merrill.Single(filing => filing.Accession == accession);

        Assert.True(HistoryEnd.IsTheCommonStock(HistoryEnd.ClassOf(Filed("0000876661-09-000017").Document)));
        Assert.True(HistoryEnd.IsAnOwnExchange(HistoryEnd.ExchangeOf(Filed("0000876661-09-000017").Document)));
        Assert.False(HistoryEnd.IsTheCommonStock(HistoryEnd.ClassOf(Filed("0000876661-09-000019").Document)));
        Assert.True(HistoryEnd.IsTheCommonStock(HistoryEnd.ClassOf(Filed("0000876882-09-000004").Document)));
        Assert.False(HistoryEnd.IsAnOwnExchange(HistoryEnd.ExchangeOf(Filed("0000876882-09-000004").Document)));
        Assert.False(HistoryEnd.IsTheCommonStock(HistoryEnd.ClassOf(Filed("0001143313-08-000178").Document)));
        Assert.False(HistoryEnd.IsTheCommonStock(HistoryEnd.ClassOf(Filed("0001193125-08-231874").Document)));

        // Without the 8-K, the New York Stock Exchange's removal is the end, ten days after it was filed.
        var removal = HistoryEnd.Of([.. merrill.Where(filing => !HistoryEnd.CompletesAMerger(filing))]);
        Assert.Equal((new DateOnly(2009, 1, 15), "0000876661-09-000017"), (removal.EndsBefore!.Value, removal.ReadFrom!.Accession));

        // PepsiCo's move to Nasdaq and its delisting from Chicago, each read off its own document.
        var pepsi = FilerFilings.Read(Path.Combine(HistoryEndsFixture, "pep"));
        var moved = pepsi.Single(filing => filing.Accession == "0000950103-17-012553");
        var chicago = pepsi.Single(filing => filing.Accession == "0001104659-18-036411");

        Assert.Equal("common stock, par value 1-2/3 cents per share", HistoryEnd.ClassOf(moved.Document));
        Assert.True(HistoryEnd.IsAnOwnExchange(HistoryEnd.ExchangeOf(moved.Document)));
        Assert.Equal("chicago stock exchange", HistoryEnd.ExchangeOf(chicago.Document));
        Assert.True(HistoryEnd.IsTheCommonStock(HistoryEnd.ClassOf(pepsi.Single(filing => filing.Accession == "0000950103-17-012545").Document)));

        // Without its registration on Nasdaq, the removal from the New York Stock Exchange would be its end.
        var unmoved = HistoryEnd.Of([.. pepsi.Where(filing => filing.Accession != "0000950103-17-012545")]);
        Assert.Equal("0000950103-17-012553", unmoved.ReadFrom?.Accession);
    }

    // The archive's answers as the fixture kept them, a document the fixture did not keep answered empty.
    sealed class KeptFilings(string folder) : EquityBrief.Core.Providers.IFilerFilingsFeed
    {
        public int Requests { get; private set; }

        public List<string> Asked { get; } = [];

        Task<string> Read(string file)
        {
            Requests++;
            Asked.Add(file);

            return Task.FromResult(File.Exists(Path.Combine(folder, file)) ? File.ReadAllText(Path.Combine(folder, file)) : string.Empty);
        }

        public Task<string> IndexAsync(string cik, CancellationToken cancellation = default) => Read("submissions.json");

        public Task<string> PageAsync(string page, CancellationToken cancellation = default) => Read(page);

        public Task<string> DocumentAsync(string cik, string accession, string document, CancellationToken cancellation = default) =>
            Read(Path.Combine(FilerFilings.DocumentsFolder, accession + "-" + document));
    }

    [Fact]
    public async Task TheReadOfAHistorysEndKeepsEveryFileAsSentBeforeTheRuleReadsIt()
    {
        using var kept = new TemporaryDirectory();
        var feed = new KeptFilings(Path.Combine(HistoryEndsFixture, "pep"));
        var read = await new HistoryEndFilings(feed).CaptureAsync("77476", kept.Path);

        // The index, its two older pages, and the document of each of the 21 removals and registrations the index lists.
        Assert.Equal(3 + 21, feed.Requests);
        Assert.Equal(["submissions.json", "CIK0000077476-submissions-001.json", "CIK0000077476-submissions-002.json"], feed.Asked.Take(3));
        Assert.Equal(File.ReadAllText(Path.Combine(HistoryEndsFixture, "pep", "submissions.json")), File.ReadAllText(Path.Combine(kept.Path, "submissions.json")));
        Assert.Equal(21, Directory.GetFiles(Path.Combine(kept.Path, FilerFilings.DocumentsFolder)).Length);

        // A structured form is asked for as filed, without the stylesheet's folder its listing names.
        Assert.Contains(Path.Combine(FilerFilings.DocumentsFolder, "0001354457-21-000517-primary_doc.xml"), feed.Asked);
        Assert.Null(read.EndsBefore);
    }

    static FilerFiling Filing(string accession, string form, DateOnly filed, string? document = null, params string[] items) =>
        new(accession, form, filed, null, items, "doc.htm", document);

    const string CommonOnTheExchange = "<exchange><cik>1</cik><entityName>NEW YORK STOCK EXCHANGE LLC</entityName></exchange><descriptionClassSecurity>Common Stock, par value $0.01 per share</descriptionClassSecurity>";

    [Fact]
    public void AnEndIsTheEarlierOfTheRemovalAndTheMergerAndNoneGoesToTheOperatorsList()
    {
        var day = new DateOnly(2012, 6, 1);

        // A removal ten days after its filing against an 8-K filed nine days after it: the 8-K is earlier. Filed eleven, the removal is.
        Assert.Equal("merger", HistoryEnd.Of([Filing("removal", "25-NSE", day, CommonOnTheExchange), Filing("merger", "8-K", day.AddDays(9), null, "2.01", "5.01")]).ReadFrom!.Accession);
        Assert.Equal("removal", HistoryEnd.Of([Filing("removal", "25-NSE", day, CommonOnTheExchange), Filing("merger", "8-K", day.AddDays(11), null, "3.01", "5.01")]).ReadFrom!.Accession);

        // An 8-K completing an acquisition without a change in control of the company is the company buying another, no end.
        Assert.Null(HistoryEnd.Of([Filing("bought", "8-K", day, null, "2.01", "9.01")]).EndsBefore);

        // An amended removal is read as the removal is.
        Assert.Equal(day.AddDays(10), HistoryEnd.Of([Filing("amended", "25-NSE/A", day, CommonOnTheExchange)]).EndsBefore);

        // A registration of the common stock 30 days from the removal moves the listing, 31 does not.
        var registered = "Title of each class to be so registered Name of each exchange on which each class is to be registered Common Stock, par value $0.01 per share The Nasdaq Stock Market LLC";
        Assert.Null(HistoryEnd.Of([Filing("removal", "25-NSE", day, CommonOnTheExchange), Filing("moved", "8-A12B", day.AddDays(30), registered)]).EndsBefore);
        Assert.Equal(day.AddDays(10), HistoryEnd.Of([Filing("removal", "25-NSE", day, CommonOnTheExchange), Filing("moved", "8-A12B", day.AddDays(31), registered)]).EndsBefore);

        // Common stock with its preferred stock purchase rights is the common stock; preferred stock is not.
        Assert.True(HistoryEnd.IsTheCommonStock("common stock, $1 par value, and preferred stock purchase rights"));
        Assert.False(HistoryEnd.IsTheCommonStock("6.25% non-cumulative preferred stock"));

        // No such filing goes to the operator as one list, saying so, and a removal whose document was not kept says that too.
        var none = HistoryEnd.Of([Filing("annual", "10-K", day)]);
        Assert.Equal((null, null), (none.EndsBefore, none.ReadFrom));
        Assert.Contains("the operator rules on its end", none.Says, StringComparison.Ordinal);
        Assert.Contains("1 Form 25 document(s) were not kept", HistoryEnd.Of([Filing("unkept", "25-NSE", day)]).Says, StringComparison.Ordinal);
    }

    static void HeldRows(TemporaryStore store, string ticker, string pull, params (string Session, decimal Close, long Volume)[] rows)
    {
        using var connection = store.Open();

        foreach (var (session, close, volume) in rows)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull)
                VALUES ($ticker, $session, $close, $close, $close, $close, $close, $volume, $pull);";
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", session);
            command.Parameters.AddWithValue("$close", close.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$volume", volume);
            command.Parameters.AddWithValue("$pull", pull);
            command.ExecuteNonQuery();
        }
    }

    static string Dumped(TemporaryStore store, string table) => string.Join("\n", Query(store, $"SELECT * FROM {table} ORDER BY 1, 2, 3;"));

    [Fact]
    public async Task TheCheckWritesItsFindingsWholeForTheRunReadsEachSessionsNewestPullAndMovesNoPulledRowBarOrMark()
    {
        using var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 6, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        string Day(int at) => HeldFirst.AddDays(at).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Two stocks over eight days; ZERO holds no volume on its fifth and misses its sixth, which ONE holds. FALL falls from 100
        // to 60 on its fourth in the older pull, which the newer pull holds at 100.
        HeldRows(store, "ONE", "pull-1", [.. Enumerable.Range(0, 8).Select(at => (Day(at), 100m, 200_000L))]);
        HeldRows(store, "ZERO", "pull-1", [.. Enumerable.Range(0, 8).Where(at => at != 5).Select(at => (Day(at), at == 4 ? 101m : 100m, at == 4 ? 0L : 200_000L))]);
        HeldRows(store, "FALL", "pull-1", [.. Enumerable.Range(0, 8).Select(at => (Day(at), at >= 3 ? 60m : 100m, 200_000L))]);
        HeldRows(store, "FALL", "pull-2", [.. Enumerable.Range(3, 5).Select(at => (Day(at), 100m, 200_000L))]);
        await new SessionMarks(clock, store.DatabaseFile).MarkAsync("ONE", HeldFirst.AddDays(2), SessionMarking.CheckedAndKept, "kept", "mark-1");

        var before = new[] { "pulled_bar", "bar", "session_mark" }.Select(table => Dumped(store, table)).ToArray();

        // A print writes nothing.
        var printed = await new DataChecker(clock, store.DatabaseFile).RunAsync("check-0", write: false);
        Assert.Equal(2, printed.Findings.Count);
        Assert.Empty(Query(store, "SELECT ticker FROM data_check;"));

        var outcome = await new DataChecker(clock, store.DatabaseFile).RunAsync("check-1");

        Assert.Equal((3, 23), (outcome.Stocks, outcome.Sessions));
        Assert.Equal(
            [$"ZERO|{Day(4)}|no volume|liquid|2015 to 2019|check-1", $"ZERO|{Day(5)}|missing session|liquid|2015 to 2019|check-1"],
            Query(store, "SELECT ticker || '|' || session_date || '|' || class || '|' || kind || '|' || era || '|' || run_id FROM data_check ORDER BY session_date;"));
        Assert.Equal(["data-check|2"], Query(store, "SELECT stage || '|' || rows_written FROM run_log WHERE run_id = 'check-1';"));

        // A second run replaces the first's rows whole.
        await new DataChecker(clock, store.DatabaseFile).RunAsync("check-2");
        Assert.Equal(["check-2"], Query(store, "SELECT DISTINCT run_id FROM data_check;"));

        Assert.Equal(before, new[] { "pulled_bar", "bar", "session_mark" }.Select(table => Dumped(store, table)).ToArray());
    }

    [Fact]
    public async Task AStockWhoseSeriesTheCheckCannotReadIsNamedAndTheOthersReadOn()
    {
        using var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 6, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        string Day(int at) => HeldFirst.AddDays(at).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        HeldRows(store, "ZERO", "pull-1", [.. Enumerable.Range(0, 8).Select(at => (Day(at), at == 4 ? 101m : 100m, at == 4 ? 0L : 200_000L))]);
        HeldRows(store, "BAD", "pull-1", [.. Enumerable.Range(0, 8).Select(at => (Day(at), 100m, 0L))]);

        using (var connection = store.Open())
        {
            using var spoil = connection.CreateCommand();
            spoil.CommandText = $"UPDATE pulled_bar SET close = 'a price no one sent' WHERE ticker = 'BAD' AND session_date = '{Day(3)}';";
            spoil.ExecuteNonQuery();
        }

        var outcome = await new DataChecker(clock, store.DatabaseFile).RunAsync("check-1");

        Assert.Equal([$"BAD {Day(3)}: a price or the date the check cannot read"], outcome.Unread);
        Assert.Equal((1, 8), (outcome.Stocks, outcome.Sessions));
        Assert.Equal(["ZERO"], Query(store, "SELECT DISTINCT ticker FROM data_check;"));
        Assert.Equal([$"1 stock(s) and 8 session(s) read from {Day(0)} to {Day(7)}, 1 finding(s); 1 stock(s) not read: BAD {Day(3)}: a price or the date the check cannot read"], Query(store, "SELECT detail FROM run_log WHERE run_id = 'check-1';"));
    }

    [Fact]
    public async Task TheOperatorsWordMarksTheSessionAHistoryEndsOn()
    {
        using var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 6, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        Assert.Contains(SessionMarking.HistoryEnds, SessionMarking.Kinds);
        Assert.Equal(1, await new SessionMarks(clock, store.DatabaseFile).MarkAsync("MER", new DateOnly(2008, 12, 31), SessionMarking.HistoryEnds, "Its history ends here.", "mark-1"));
        Assert.Equal(["history ends"], Query(store, "SELECT kind FROM session_mark WHERE ticker = 'MER';"));
    }

    // Every source 23.1 writes or changes. From 23.10 the marks' kinds and their reads are pinned by every family rule, whose
    // reads of the pull read them, so that file has left this list (see: From phase 23's restart the sweep's history and every rule's read of the pull read the marks, each stock from its start and past each bad print).
    internal static readonly string[] DataCheckSources =
    [
        "src/EquityBrief.Core/History/DataCheck.cs",
        "src/EquityBrief.Core/History/FilerFilings.cs",
        "src/EquityBrief.Core/History/HistoryEnd.cs",
        "src/EquityBrief.Worker/History/DataChecker.cs",
        "src/EquityBrief.Worker/History/HistoryEndFilings.cs",
        "src/EquityBrief.Worker/Program.cs",
        "src/EquityBrief.Data/Migrations/SchemaMigrations.cs",
    ];

    [Fact]
    public void NoSourceARulesVersionPinsIsOne23Point1Writes()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(DataCheckSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(DataCheckSources.Intersect(pinned, StringComparer.Ordinal));

        string[] readings = [.. LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))];

        Assert.Empty(DataCheckSources.Intersect(readings, StringComparer.Ordinal));
    }
}
