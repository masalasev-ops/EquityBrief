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

// From 22.10 Quality at a fair price's history test on each index, the S&P 500 among them: the book walked on membership
// as it stood over the 72 settings of its grid from the first scored session, each holding bought at the next open and
// judged after its round trip against an equal-weighted hold of the members answered on the night it was chosen; each
// setting's figures with each year's edge and the edge without its five largest results, what luck alone passes over the
// grid, the proposal among the settings meeting the family floors, and the tester's year-by-year verdict of the grid as a
// procedure. It writes its report and its figures and nothing else: nothing is registered and the store is only read.
// see: Quality at a fair price's history test walks the book over the pulled history on 72 settings an index and registers nothing
public sealed partial class IndexSweepRunner
{
    public const string Quality = QualityRule.Name;

    // The years each index's answer names on their own, 2020's crash and recovery, 2022's fall, and the newest two.
    public static IReadOnlyList<int> QualityNamedYears { get; } = [2020, 2022, 2025, 2026];

    async Task<int> QualityAsync(string indexCode, CancellationToken cancellation)
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

        var read = await WalkForwardTester.ReadAsync(indexCode, history, through, inputs, cancellation);
        var lay = await QualityLay.ReadAsync(read, history, through, output.WriteLine, cancellation);
        var grid = QualityRule.Grid;
        var books = new QualityBook[grid.Count];

        output.WriteLine(FormattableString.Invariant($"walking {grid.Count} setting(s) over {lay.Reshuffles.Count} reshuffle(s)"));

        Parallel.For(0, grid.Count, at => books[at] = lay.Book(grid[at]));

        var figures = books.Select(book => (book.Setting, Figures: HeavyweightSweep.Figures(book.Setting.Key, book.Costed), Months: QualityLay.MonthsOf(book, read.Calendar, lay.Reshuffles))).ToArray();
        var proposal = QualityLay.Propose(figures);
        var tested = read.Folds.Count > 0 ? WalkForwardTester.Judge(read, [QualityLay.Procedure(read, books, lay.Reshuffles)]).Single() : null;
        var coverage = lay.Coverage();
        var page = QualityPage(named, inputs, read, figures, proposal, tested, coverage, through, started);
        var report = WriteRun(
            folder,
            page,
            new
            {
                index = indexCode,
                family = Quality,
                through,
                reshuffles = lay.Reshuffles.Count,
                names = inputs.Names.Count,
                luck = HeavyweightSweep.Luck(grid.Count),
                proposal = proposal?.Key,
                coverage = coverage.Select(one => new { session = one.Session, answered = one.Answered, scored = one.Scored, factsReach = one.FactsReach, valued = one.Valued, qualifying = one.Qualifying }),
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
            new SweepAnswer(indexCode, Quality, null, proposal is not null));
        var (_, starting, startingMonths) = figures.Single(one => one.Setting == QualityRule.Starting);

        output.WriteLine(FormattableString.Invariant($"the starting setting: {starting.Trades} holding(s) ended and {starting.Open} held, edge {FamilySweepReport.Number(Points(starting.Edge))} points after its round trip, {startingMonths.Held} month(s) held, above nothing in {startingMonths.YearsAbove} year(s) by its months"));
        output.WriteLine(proposal is { } proposed
            ? "proposed " + proposed.Key + ", edge " + FamilySweepReport.Number(Points(figures.Single(one => one.Setting == proposed).Figures.Edge)) + " points"
            : "none passed: no setting meets the family floors");

        if (tested is not null)
        {
            output.WriteLine(WalkForwardTester.Line(tested));
        }

        output.WriteLine("report " + report);

        return 0;
    }

    static double? Points(double? edge) => edge * 100;

    // The report: the run, the starting setting's figures with each year's edge, the proposal, luck over the grid, the
    // tester's verdict year by year, and every setting's figures, the strongest first.
    static string QualityPage(
        string named,
        SweepHistoryInputs inputs,
        LoopRead read,
        IReadOnlyList<(QualitySettings Setting, HeavyweightFigures Figures, QualityMonths Months)> figures,
        QualitySettings? proposal,
        WalkForwardTester.Tested? tested,
        IReadOnlyList<(DateOnly Session, int Answered, int Scored, int FactsReach, int Valued, int Qualifying)> coverage,
        DateOnly through,
        DateTimeOffset started)
    {
        static string Edge(double? edge) => FamilySweepReport.Number(Points(edge));

        string Year(HeavyweightFigures one, int year) =>
            year - SweepColumns.FirstScored.Year is var at && at >= 0 && at < one.YearEdge.Length
                ? Edge(one.YearEdge[at]) + FormattableString.Invariant($" ({one.YearTrades[at]})")
                : "none";

        var page = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>" + WebUtility.HtmlEncode(named) + " quality at a fair price</title><style>" + SweepReport.Style + "</style></head><body><main>");
        var (_, starting, startingMonths) = figures.Single(one => one.Setting == QualityRule.Starting);
        var meeting = figures.Count(one => QualityRule.MeetsFloors(one.Months));
        var floors = FormattableString.Invariant($"the family's floors of {QualityRule.MonthsHeldFloor} months held and {QualityRule.YearsAboveFloor} of {SweepFigures.Years} years whose months' mean edge stands above nothing");

        page.Append(FormattableString.Invariant($"<h1>Quality at a fair price on the {WebUtility.HtmlEncode(named)}: its history over {figures.Count} settings</h1>"));
        page.Append(FormattableString.Invariant($"<p class=\"survivors\">{WebUtility.HtmlEncode(Membership(named, inputs))} Each holding is bought at the open after the reshuffle that chose it and judged after its round trip against an equal-weighted hold of every member answered that night, each from its own open after it and to its last close as a member; each member's scorecard and valuation read from its filer's facts as first filed before the session. Nothing is written and nothing is registered.</p>"));
        page.Append(FormattableString.Invariant($"<p>Scored from {read.Calendar[read.FirstScored]:yyyy-MM-dd} through {through:yyyy-MM-dd} over {inputs.Names.Count:N0} names, run {started.UtcDateTime:yyyy-MM-dd HH:mm} UTC. Edges are in percentage points after the round trip.</p>"));

        page.Append("<h2>The starting setting</h2>");
        page.Append(FormattableString.Invariant($"<p>{WebUtility.HtmlEncode(QualityRule.Starting.Words)}: {starting.Trades:N0} holding(s) ended and {starting.Open} still held; edge {Edge(starting.Edge)} points, its own return {Edge(starting.Result)} against the members' {Edge(starting.CutReturn)}; above nothing in {starting.YearsBeating} of {starting.YearEdge.Length} years; without its five largest results {Edge(starting.EdgeWithoutLargest)}; a holding held a median {FamilySweepReport.Number(starting.HeldMedian)} sessions.</p>"));
        page.Append("<p>Its years, each year's edge with the holdings ended in it: ");
        page.Append(string.Join("; ", Enumerable.Range(SweepColumns.FirstScored.Year, starting.YearEdge.Length).Select(year => FormattableString.Invariant($"{year} {Year(starting, year)}"))));
        page.Append(FormattableString.Invariant($". The years each answer names: {string.Join(", ", QualityNamedYears)}.</p>"));
        page.Append(FormattableString.Invariant($"<p>By its months, the unit its floors read: {startingMonths.Held} month(s) held stocks; each year's mean edge over the months held in it: "));
        page.Append(string.Join("; ", startingMonths.YearEdge.Select((edge, at) => FormattableString.Invariant($"{SweepColumns.FirstScored.Year + at} {(edge is null ? "none held" : Edge(edge))}"))));
        page.Append(FormattableString.Invariant($"; above nothing in {startingMonths.YearsAbove} of {SweepFigures.Years} years.</p>"));

        page.Append("<h2>What the history could read</h2><p>At each year's first reshuffle: the members answered with a close, those whose filings gave a score of five checks or more, those whose filings as first filed reach every quarter the valuation reads, those whose price over earnings could be read against their own last twelve quarters, and those the starting setting qualifies. A member the history holds too few filings or prices for is answered but neither scored nor valued.</p>");
        page.Append("<div class=\"table\"><table><thead><tr><th>Reshuffle</th><th>Answered</th><th>Scored</th><th>Filings reach twelve quarters</th><th>Valued</th><th>Qualifying</th></tr></thead><tbody>");

        foreach (var one in coverage.GroupBy(one => one.Session.Year).Select(year => year.First()))
        {
            page.Append(FormattableString.Invariant($"<tr><td>{one.Session:yyyy-MM-dd}</td><td class=\"num\">{one.Answered}</td><td class=\"num\">{one.Scored}</td><td class=\"num\">{one.FactsReach}</td><td class=\"num\">{one.Valued}</td><td class=\"num\">{one.Qualifying}</td></tr>"));
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

            page.Append(FormattableString.Invariant($"<p>{WebUtility.HtmlEncode(WalkForwardTester.Line(tested))}.</p><p>Inside each fold the grid's 72 books are read on the holdings and the months that ended before the fold's year, the family's floors in months in proportion to the years it learned on, and the fold's choice is tested on its months in its year against the starting setting's.</p>"));
            page.Append("<div class=\"table\"><table><thead><tr><th>Test year</th><th>The fold chose</th><th>Its months</th><th>Its total</th><th>The starting setting's months</th><th>Its total</th></tr></thead><tbody>");

            foreach (var (fold, chosen) in procedure.Folds)
            {
                var year = procedure.Evidence.Years.FirstOrDefault(row => row.Year == fold.Year);

                page.Append(FormattableString.Invariant($"<tr><td>{fold.Year}{(fold.Complete ? string.Empty : ", to date")}</td><td>{WebUtility.HtmlEncode(chosen ?? "none, the starting setting's own")}</td><td class=\"num\">{year?.ProposedUnits ?? 0:N0}</td><td class=\"num\">{FamilySweepReport.Number(year?.ProposedTotal)}</td><td class=\"num\">{year?.CurrentUnits ?? 0:N0}</td><td class=\"num\">{FamilySweepReport.Number(year?.CurrentTotal)}</td></tr>"));
            }

            page.Append(FormattableString.Invariant($"</tbody></table></div><p>{verdict.Blocks} block(s); adjusted p {(verdict.Adjusted is { } p ? p.ToString("0.####", CultureInfo.InvariantCulture) : "not read, under the block floor")} against {LoopGate.Bar:0.####}; better in {verdict.Better} of {verdict.Counted} counted year(s); with the five largest left out of each side {FamilySweepReport.Number(verdict.Trimmed)}; {verdict.Units:N0} month(s){(verdict.Counts ? string.Empty : ", short of the count")}.</p>"));
        }

        page.Append("<h2>Every setting, the strongest first</h2><div class=\"table\"><table><thead><tr><th>Setting</th><th>Holdings</th><th>Edge</th><th>Years above nothing by its holdings</th>");
        page.Append(string.Concat(QualityNamedYears.Select(year => FormattableString.Invariant($"<th>{year}</th>"))));
        page.Append("<th>Without the five largest</th><th>Months held</th><th>Years above nothing by its months</th><th>Meets the floors</th></tr></thead><tbody>");

        foreach (var (setting, one, months) in figures.OrderByDescending(one => one.Figures.Edge ?? double.MinValue).ThenBy(one => one.Setting.Key, StringComparer.Ordinal))
        {
            page.Append(FormattableString.Invariant($"<tr><td>{WebUtility.HtmlEncode(setting.Key)}</td><td class=\"num\">{one.Trades:N0}</td><td class=\"num\">{Edge(one.Edge)}</td><td class=\"num\">{one.YearsBeating}</td>"));
            page.Append(string.Concat(QualityNamedYears.Select(year => "<td class=\"num\">" + Year(one, year) + "</td>")));
            page.Append(FormattableString.Invariant($"<td class=\"num\">{Edge(one.EdgeWithoutLargest)}</td><td class=\"num\">{months.Held}</td><td class=\"num\">{months.YearsAbove}</td><td>{(QualityRule.MeetsFloors(months) ? "yes" : "no")}</td></tr>"));
        }

        page.Append("</tbody></table></div></main></body></html>");

        return page.ToString();
    }
}
