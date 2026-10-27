using System.Globalization;
using System.Net;
using System.Text;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Worker.Sweep;

// The Fundamentals family's history test on each index, the S&P 500 among them: every night replayed on membership as it
// stood over the 144 settings of its grid from the first scored session, each trade bought at the next open and judged
// after its round trip against an equal-weighted hold of the members answered on the night it was chosen; each setting's
// figures with each year's edge and the edge without its five largest results, what luck alone passes over the grid, the
// proposal among the settings meeting the quality family's floors in months, the tester's year-by-year verdict of the
// grid as a procedure, and the first year the release readings reach. It writes its report and its figures and nothing
// else: nothing is registered and the store is only read.
// see: The Fundamentals family's history test walks its trades over the pulled history on 144 settings an index and registers nothing
public sealed partial class IndexSweepRunner
{
    public const string Improving = ImprovingRule.Name;

    // The years each Fundamentals answer names on their own, the quality family's: 2020's crash and recovery, 2022's fall,
    // and the newest two.
    public static IReadOnlyList<int> ImprovingNamedYears => QualityNamedYears;

    async Task<int> ImprovingAsync(string indexCode, CancellationToken cancellation)
    {
        if (!DipIndices.TryGetValue(indexCode, out var named))
        {
            output.WriteLine($"{Verb}: name an index with '--index', one of {string.Join(", ", DipIndices.Keys)}");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, FamilySweepRunner.Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

            return 2;
        }

        var started = clock.UtcNow;
        var history = new SweepHistory(databaseFile);
        var through = await history.NewestSessionAsync(cancellation);
        var inputs = indexCode == IndexFamilies.LargeIndex
            ? await history.ReadAsync(through, output.WriteLine, cancellation)
            : await history.ReadAsync(through, output.WriteLine, cancellation, index: indexCode, asItStood: true);

        if (inputs.Names.Count == 0)
        {
            output.WriteLine($"{Verb}: the store holds no member of the {named}; pull them first with 'history-pull --members --index {indexCode}', then their history");

            return 2;
        }

        var folder = Claim(SweepFolder.Resolve(configuredFolder, dataRoot), started);

        output.WriteLine("run " + Path.GetFileName(folder));

        var releases = await WalkForwardTester.ReleasesAsync(databaseFile, clock, cancellation);
        var read = await WalkForwardTester.ReadAsync(indexCode, history, through, inputs, cancellation);
        var lay = await ImprovingLay.ReadAsync(read, history, releases.ByFiler, through, output.WriteLine, cancellation);
        var grid = ImprovingGrid.Settings;
        var books = new ImprovingBook[grid.Count];

        output.WriteLine(FormattableString.Invariant($"walking {grid.Count} setting(s) over {read.Calendar.Length - read.FirstScored} night(s)"));

        Parallel.For(0, grid.Count, at => books[at] = lay.Book(grid[at]));

        var figures = books.Select(book => (book.Setting, Figures: HeavyweightSweep.Figures(book.Setting.Key, book.Costed), Months: ImprovingLay.MonthsOf(book, read.Calendar, lay.Months))).ToArray();
        var proposal = ImprovingLay.Propose(figures);
        var tested = read.Folds.Count > 0 ? WalkForwardTester.Judge(read, [ImprovingLay.Procedure(read, books, lay.Months)]).Single() : null;
        var coverage = lay.Coverage(releases.ByFiler);
        var reach = coverage.FirstOrDefault(year => year.Releases > 0)?.Year;
        var page = ImprovingPage(named, inputs, read, figures, proposal, tested, coverage, releases.Version, releases.Cutoff, reach, through, started);
        var report = WriteRun(
            folder,
            page,
            new
            {
                index = indexCode,
                family = Improving,
                through,
                names = inputs.Names.Count,
                luck = HeavyweightSweep.Luck(grid.Count),
                proposal = proposal?.Key,
                readingVersion = releases.Version,
                cutoff = releases.Cutoff,
                releasesReach = reach,
                coverage,
                settings = figures.Select(one => new { one.Setting.Key, one.Setting.Words, one.Figures, one.Months, meetsFloors = QualityRule.MeetsFloors(one.Months) }),
                tested = tested is null ? null : new
                {
                    tested.Proposal.Words,
                    folds = tested.Proposal.Folds.Select(fold => new { fold.Fold.Year, fold.Fold.Complete, chosen = fold.Chosen }),
                    tested.Proposal.StableFolds,
                    years = tested.Proposal.Evidence.Years,
                    verdict = tested.Verdict,
                    passed = tested.Passed,
                },
                started,
                finished = clock.UtcNow,
            },
            new SweepAnswer(indexCode, Improving, null, proposal is not null));
        var (_, starting, startingMonths) = figures.Single(one => one.Setting == ImprovingGrid.Starting);

        output.WriteLine(reach is { } first
            ? FormattableString.Invariant($"the release readings reach {first}: a setting asking a flag first holds nothing before it")
            : "no release reading is held for a member: a setting asking a flag first holds nothing");
        output.WriteLine(FormattableString.Invariant($"the starting setting: {starting.Trades} trade(s) ended and {starting.Open} held, edge {FamilySweepReport.Number(Points(starting.Edge))} points after its round trip, {startingMonths.Held} month(s) held, above nothing in {startingMonths.YearsAbove} year(s) by its months"));
        output.WriteLine(proposal is { } proposed
            ? "proposed " + proposed.Key + ", edge " + FamilySweepReport.Number(Points(figures.Single(one => one.Setting == proposed).Figures.Edge)) + " points"
            : "none passed: no setting meets the floors");

        if (tested is not null)
        {
            output.WriteLine(WalkForwardTester.Line(tested));
        }

        output.WriteLine("report " + report);

        return 0;
    }

    // The report: the run, how far the release readings reach, the starting setting's figures with each year's edge, what
    // each year could read, the proposal, luck over the grid, the tester's verdict year by year, and every setting's
    // figures, the strongest first.
    static string ImprovingPage(
        string named,
        SweepHistoryInputs inputs,
        LoopRead read,
        IReadOnlyList<(ImprovingSweepSetting Setting, HeavyweightFigures Figures, QualityMonths Months)> figures,
        ImprovingSweepSetting? proposal,
        WalkForwardTester.Tested? tested,
        IReadOnlyList<ImprovingYear> coverage,
        long? version,
        DateOnly? cutoff,
        int? reach,
        DateOnly through,
        DateTimeOffset started)
    {
        static string Edge(double? edge) => FamilySweepReport.Number(Points(edge));

        string Year(HeavyweightFigures one, int year) =>
            year - SweepColumns.FirstScored.Year is var at && at >= 0 && at < one.YearEdge.Length
                ? Edge(one.YearEdge[at]) + FormattableString.Invariant($" ({one.YearTrades[at]})")
                : "none";

        var page = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>" + WebUtility.HtmlEncode(named) + " fundamentals</title><style>" + SweepReport.Style + "</style></head><body><main>");
        var (_, starting, startingMonths) = figures.Single(one => one.Setting == ImprovingGrid.Starting);
        var meeting = figures.Count(one => QualityRule.MeetsFloors(one.Months));
        var floors = FormattableString.Invariant($"the floors of {QualityRule.MonthsHeldFloor} months held and {QualityRule.YearsAboveFloor} of {SweepFigures.Years} years whose months' mean edge stands above nothing");
        var flagFirst = figures.Count(one => one.Setting.FlagFirst);

        page.Append(FormattableString.Invariant($"<h1>Fundamentals on the {WebUtility.HtmlEncode(named)}: its history over {figures.Count} settings</h1>"));
        page.Append(FormattableString.Invariant($"<p class=\"survivors\">{WebUtility.HtmlEncode(Membership(named, inputs))} Every night is replayed: each member's newly read filing scored from its filer's facts as first filed before the night, the score the filing before it held, the change in its sales growth, and whether a flag stood the night before, read from its results releases by the family's own rule. Each trade is bought at the open after the night that chose it and judged after its round trip against an equal-weighted hold of every member answered that night, each from its own open after it and to its last close as a member. Nothing is written and nothing is registered.</p>"));
        page.Append(FormattableString.Invariant($"<p>Scored from {read.Calendar[read.FirstScored]:yyyy-MM-dd} through {through:yyyy-MM-dd} over {inputs.Names.Count:N0} names, run {started.UtcDateTime:yyyy-MM-dd HH:mm} UTC. Edges are in percentage points after the round trip.</p>"));

        page.Append("<h2>The release readings</h2>");
        page.Append(version is { } pin
            ? FormattableString.Invariant($"<p>The releases are read under reading version {pin}, the newest whose history read is complete, its model's cutoff {(cutoff is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "not stated")}. ")
              + (reach is { } first
                  ? FormattableString.Invariant($"They reach {first}: a year before it holds no flag, so the {flagFirst} settings asking a flag first hold nothing in it. A release accepted before the model's cutoff was read by a model whose training may hold what followed it.</p>")
                  : FormattableString.Invariant($"No member's release is read under it, so the {flagFirst} settings asking a flag first hold nothing.</p>"))
            : FormattableString.Invariant($"<p>No reading version's history read is complete, so no flag stands and the {flagFirst} settings asking a flag first hold nothing.</p>"));

        page.Append("<h2>The starting setting</h2>");
        page.Append(FormattableString.Invariant($"<p>{WebUtility.HtmlEncode(ImprovingGrid.Starting.Words)}: {starting.Trades:N0} trade(s) ended and {starting.Open} still held; edge {Edge(starting.Edge)} points, its own return {Edge(starting.Result)} against the members' {Edge(starting.CutReturn)}; above nothing in {starting.YearsBeating} of {starting.YearEdge.Length} years; without its five largest results {Edge(starting.EdgeWithoutLargest)}; a trade held a median {FamilySweepReport.Number(starting.HeldMedian)} sessions.</p>"));
        page.Append("<p>Its years, each year's edge with the trades ended in it: ");
        page.Append(string.Join("; ", Enumerable.Range(SweepColumns.FirstScored.Year, starting.YearEdge.Length).Select(year => FormattableString.Invariant($"{year} {Year(starting, year)}"))));
        page.Append(FormattableString.Invariant($". The years each answer names: {string.Join(", ", ImprovingNamedYears)}.</p>"));
        page.Append(FormattableString.Invariant($"<p>By its months, the unit its floors read: {startingMonths.Held} month(s) held stocks; each year's mean edge over the months held in it: "));
        page.Append(string.Join("; ", startingMonths.YearEdge.Select((edge, at) => FormattableString.Invariant($"{SweepColumns.FirstScored.Year + at} {(edge is null ? "none held" : Edge(edge))}"))));
        page.Append(FormattableString.Invariant($"; above nothing in {startingMonths.YearsAbove} of {SweepFigures.Years} years.</p>"));

        page.Append(FormattableString.Invariant($"<h2>What the history could read</h2><p>Each year: the members' filings first read on its nights, those whose scorecard read five checks or more, those raising the score above the filing before's, those clearing the starting bar of {ImprovingGrid.Starting.BuyInNine} in 9, those whose sales growth sped up, those a flag stood for the night before, and the results releases read under the version accepted in it.</p>"));
        page.Append("<div class=\"table\"><table><thead><tr><th>Year</th><th>Filings read</th><th>Scored</th><th>Rising</th><th>At the bar</th><th>Growth faster</th><th>Flagged the night before</th><th>Releases read</th></tr></thead><tbody>");

        foreach (var year in coverage)
        {
            page.Append(FormattableString.Invariant($"<tr><td>{year.Year}</td><td class=\"num\">{year.Filings:N0}</td><td class=\"num\">{year.Scored:N0}</td><td class=\"num\">{year.Rising:N0}</td><td class=\"num\">{year.Clearing:N0}</td><td class=\"num\">{year.Faster:N0}</td><td class=\"num\">{year.Flagged:N0}</td><td class=\"num\">{year.Releases:N0}</td></tr>"));
        }

        page.Append("</tbody></table></div>");

        page.Append("<h2>The proposal</h2>");
        page.Append(proposal is { } proposed
            ? FormattableString.Invariant($"<p>{WebUtility.HtmlEncode(proposed.Words)} ({WebUtility.HtmlEncode(proposed.Key)}): edge {Edge(figures.Single(one => one.Setting == proposed).Figures.Edge)} points, the best among the {meeting} setting(s) meeting {floors}.</p>")
            : FormattableString.Invariant($"<p>None: no setting meets {floors}.</p>"));
        page.Append(FormattableString.Invariant($"<p>Luck alone: of {figures.Count} settings with no edge, about {HeavyweightSweep.Luck(figures.Count):0.0} would stand above nothing in {FamilySweep.YearsBeating} or more of the {SweepFigures.Years} years.</p>"));

        page.Append("<h2>The tester, year by year</h2>");

        if (tested is null)
        {
            page.Append(FormattableString.Invariant($"<p>Not read: the history ends before the first test year, {LoopFolds.FirstTestYear}.</p>"));
        }
        else
        {
            var (procedure, verdict) = (tested.Proposal, tested.Verdict);

            page.Append(FormattableString.Invariant($"<p>{WebUtility.HtmlEncode(WalkForwardTester.Line(tested))}.</p><p>Inside each fold the grid's {figures.Count} books are read on the trades and the months that ended before the fold's year, the floors in months in proportion to the years it learned on, and the fold's choice is tested on its months in its year against the starting setting's.</p>"));
            page.Append("<div class=\"table\"><table><thead><tr><th>Test year</th><th>The fold chose</th><th>Its months</th><th>Its total</th><th>The starting setting's months</th><th>Its total</th></tr></thead><tbody>");

            foreach (var (fold, chosen) in procedure.Folds)
            {
                var year = procedure.Evidence.Years.FirstOrDefault(row => row.Year == fold.Year);

                page.Append(FormattableString.Invariant($"<tr><td>{fold.Year}{(fold.Complete ? string.Empty : ", to date")}</td><td>{WebUtility.HtmlEncode(chosen ?? "none, the starting setting's own")}</td><td class=\"num\">{year?.ProposedUnits ?? 0:N0}</td><td class=\"num\">{FamilySweepReport.Number(year?.ProposedTotal)}</td><td class=\"num\">{year?.CurrentUnits ?? 0:N0}</td><td class=\"num\">{FamilySweepReport.Number(year?.CurrentTotal)}</td></tr>"));
            }

            page.Append(FormattableString.Invariant($"</tbody></table></div><p>{verdict.Blocks} block(s); adjusted p {(verdict.Adjusted is { } p ? p.ToString("0.####", CultureInfo.InvariantCulture) : "not read, under the block floor")} against {LoopGate.Bar:0.####}; better in {verdict.Better} of {verdict.Counted} counted year(s); with the five largest left out of each side {FamilySweepReport.Number(verdict.Trimmed)}; {verdict.Units:N0} month(s){(verdict.Counts ? string.Empty : ", short of the count")}.</p>"));
        }

        page.Append("<h2>Every setting, the strongest first</h2><div class=\"table\"><table><thead><tr><th>Setting</th><th>Trades</th><th>Edge</th><th>Years above nothing by its trades</th>");
        page.Append(string.Concat(ImprovingNamedYears.Select(year => FormattableString.Invariant($"<th>{year}</th>"))));
        page.Append("<th>Without the five largest</th><th>Months held</th><th>Years above nothing by its months</th><th>Meets the floors</th></tr></thead><tbody>");

        foreach (var (setting, one, months) in figures.OrderByDescending(one => one.Figures.Edge ?? double.MinValue).ThenBy(one => one.Setting.Key, StringComparer.Ordinal))
        {
            page.Append(FormattableString.Invariant($"<tr><td>{WebUtility.HtmlEncode(setting.Key)}</td><td class=\"num\">{one.Trades:N0}</td><td class=\"num\">{Edge(one.Edge)}</td><td class=\"num\">{one.YearsBeating}</td>"));
            page.Append(string.Concat(ImprovingNamedYears.Select(year => "<td class=\"num\">" + Year(one, year) + "</td>")));
            page.Append(FormattableString.Invariant($"<td class=\"num\">{Edge(one.EdgeWithoutLargest)}</td><td class=\"num\">{months.Held}</td><td class=\"num\">{months.YearsAbove}</td><td>{(QualityRule.MeetsFloors(months) ? "yes" : "no")}</td></tr>"));
        }

        page.Append("</tbody></table></div></main></body></html>");

        return page.ToString();
    }
}
