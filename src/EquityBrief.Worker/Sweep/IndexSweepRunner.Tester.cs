using System.Globalization;
using System.Net;
using System.Text;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Readings;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Worker.Sweep;

public sealed partial class IndexSweepRunner
{
    public const string TesterOwn = "tester-own";

    public const string TesterPullbackOwn = "tester-pullback-own";

    // The pullback's search on the index's own history judged as the tester judges a search, apart from the other families
    // since its search runs over every setting of its grid inside each fold.
    async Task<int> TesterPullbackOwnAsync(string indexCode, string named, string words, SweepHistory history, DateOnly through, SweepHistoryInputs inputs, HeavyweightHistory companies, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income, string folder, DateTimeOffset started, CancellationToken cancellation)
    {
        var read = OwnSwingRead.Laid(indexCode, inputs, companies, income, through);

        if (read.Folds.Count == 0)
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: the history ends {through:yyyy-MM-dd}, before the first test year, {LoopFolds.FirstTestYear}"));

            return 2;
        }

        var large = await OwnSwingRead.LargeAsync(history, through, output.WriteLine, cancellation);
        var own = await OwnPullbackRead.ReadAsync(read, large, history, through, output.WriteLine, cancellation);

        own.Charge(1);

        var tested = WalkForwardTester.Judge(read, [OwnProcedures.Pullback(read, own, output.WriteLine)]);

        return Tested(indexCode, named, words, inputs, read, through, started, folder, tested, "Inside each fold the pullback's search reads every setting of its grid, the market check's own breadth one of its dials, on the trades that ended before the fold's year, its record's trades, years and blocks held to its floors in proportion to the years it learned on and its recent years its own last three, its proposal refined and extended as the search's own; the fold's second stage reads its own ten strongest settings, a level kept where it is higher in six of every eight of the fold's years, rounded up, with two of the last three, on six of the ten, the survivors crossed with and without the profit and interest-cover check, and the fold chooses the stronger of the proposal and the crossings meeting the family floors in proportion.");
    }

    // Each family's search on the index's own history judged as the tester judges a search: run inside every fold on the
    // years before its test year, scored on that year against the rule the index's night reads today, and the proposals
    // of one family read together by the step-down, each with its adjusted p and its three screens. It writes its report
    // and nothing else, and no proposal of it is put to anyone or applied.
    // see: A family's search on the S&P 400 or 600 is judged by the tester year by year with its second stage run inside each fold
    // see: A proposal passes the tester on a block sign-flip test of its total edge after costs against the current rule, corrected within a run and held to a fixed bar across runs
    async Task<int> TesterOwnAsync(string indexCode, string named, string words, SweepHistory history, DateOnly through, SweepHistoryInputs inputs, HeavyweightHistory companies, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income, string folder, DateTimeOffset started, CancellationToken cancellation)
    {
        var read = OwnSwingRead.Laid(indexCode, inputs, companies, income, through);

        if (read.Folds.Count == 0)
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: the history ends {through:yyyy-MM-dd}, before the first test year, {LoopFolds.FirstTestYear}"));

            return 2;
        }

        var large = await OwnSwingRead.LargeAsync(history, through, output.WriteLine, cancellation);
        var proposals = new List<LoopProposalRead>();

        foreach (var (family, floor) in new[] { (BreakoutRule.Name, 0.0), (DriftRule.Name, LoopProcedures.DriftStopFloor) })
        {
            var own = await OwnSwingRead.ReadAsync(read, large, family, history, through, floor, cancellation);

            proposals.Add(OwnProcedures.Swing(read, own, output.WriteLine));
        }

        var lay = await HeavyweightLay.ReadAsync(read, history, through, output.WriteLine, cancellation, large);

        proposals.AddRange(OwnProcedures.Heavyweights(read, lay, output.WriteLine));

        var tested = WalkForwardTester.Judge(read, proposals);

        return Tested(indexCode, named, words, inputs, read, through, started, folder, tested, FormattableString.Invariant($"The breakout's and the drift's grids carry the market check's seven levels as a dial, the drift's search read at a stop floor of {LoopProcedures.DriftStopFloor:0} typical move as the runbook runs it and its rule today with none, as the night reads it; each fold reads its own second stage over its own ten strongest settings, a level kept where it is higher in six of every eight of the fold's years, rounded up, with two of the last three, on six of the ten, and its survivors crossed, the fold choosing the stronger of its grid's choice and its crossings meeting the floors in proportion. The sector heavyweights' two designs are each searched over their full grids at three qualities and two floors."));
    }

    // The tested proposals as the report and the console state them.
    int Tested(string indexCode, string named, string words, SweepHistoryInputs inputs, LoopRead read, DateOnly through, DateTimeOffset started, string folder, IReadOnlyList<WalkForwardTester.Tested> tested, string procedures)
    {
        var note = FormattableString.Invariant($"{Membership(named, inputs)} Each search runs inside every fold on the years before its test year, the family floors in proportion to those years, and is scored on that year against the rule the index's night reads today, every trade bought at the next open after its round trip; the proposals of one family are read together by the step-down, and each passes only at an adjusted p at or under {LoopGate.Bar:0.####} with its three screens. {procedures} Nothing is written and nothing is put to anyone.");
        var page = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>" + WebUtility.HtmlEncode(named) + " searches year by year</title><style>" + SweepReport.Style + "</style></head><body><main>");

        page.Append(FormattableString.Invariant($"<h1>The {WebUtility.HtmlEncode(named)} {WebUtility.HtmlEncode(words)} judged by the tester</h1><p class=\"survivors\">{WebUtility.HtmlEncode(note)}</p><p>Test years {string.Join(", ", read.Folds.Select(fold => fold.Year.ToString(CultureInfo.InvariantCulture)))}, the history through {through:yyyy-MM-dd}, run {started.UtcDateTime:yyyy-MM-dd HH:mm} UTC.</p>"));

        foreach (var one in tested)
        {
            var (proposal, verdict) = (one.Proposal, one.Verdict);

            page.Append(FormattableString.Invariant($"<h2>The {WebUtility.HtmlEncode(proposal.Family)}, {WebUtility.HtmlEncode(proposal.Proposal)}</h2><p>{WebUtility.HtmlEncode(WalkForwardTester.Line(one))}.</p><p>The rule today: {WebUtility.HtmlEncode(proposal.Current)}. The proposal on all finished data: {WebUtility.HtmlEncode(proposal.Words ?? "no setting chosen")}.</p>"));
            page.Append("<div class=\"table\"><table><thead><tr><th>Test year</th><th>The fold chose</th><th>Its units</th><th>Its total</th><th>The rule today's units</th><th>Its total</th></tr></thead><tbody>");

            foreach (var (fold, chosen) in proposal.Folds)
            {
                var year = proposal.Evidence.Years.FirstOrDefault(row => row.Year == fold.Year);

                page.Append(FormattableString.Invariant($"<tr><td>{fold.Year}{(fold.Complete ? string.Empty : ", to date")}</td><td>{WebUtility.HtmlEncode(chosen ?? "none, the rule today's own")}</td><td class=\"num\">{year?.ProposedUnits ?? 0:N0}</td><td class=\"num\">{FamilySweepReport.Number(year?.ProposedTotal)}</td><td class=\"num\">{year?.CurrentUnits ?? 0:N0}</td><td class=\"num\">{FamilySweepReport.Number(year?.CurrentTotal)}</td></tr>"));
            }

            page.Append(FormattableString.Invariant($"</tbody></table></div><p>{verdict.Blocks} block(s); adjusted p {(verdict.Adjusted is { } p ? p.ToString("0.####", CultureInfo.InvariantCulture) : "not read, under the block floor")} against {LoopGate.Bar:0.####}; better in {verdict.Better} of {verdict.Counted} counted year(s); with the five largest left out of each side {FamilySweepReport.Number(verdict.Trimmed)}; {verdict.Units:N0} {WebUtility.HtmlEncode(proposal.Unit)} unit(s){(verdict.Counts ? string.Empty : ", short of the count")}.</p>"));
        }

        page.Append("</main></body></html>");

        var report = WriteRun(
            folder,
            page.ToString(),
            new
            {
                index = indexCode,
                family = TesterOwn,
                note,
                through,
                bar = LoopGate.Bar,
                tested = tested.Select(one => new
                {
                    one.Proposal.Family,
                    one.Proposal.Proposal,
                    one.Proposal.Words,
                    one.Proposal.Current,
                    one.Proposal.Unit,
                    folds = one.Proposal.Folds.Select(fold => new { fold.Fold.Year, fold.Fold.Complete, chosen = fold.Chosen }),
                    one.Proposal.StableFolds,
                    years = one.Proposal.Evidence.Years,
                    verdict = one.Verdict,
                    passed = one.Passed,
                }),
            },
            null);

        foreach (var one in tested)
        {
            output.WriteLine(WalkForwardTester.Line(one));
        }

        output.WriteLine("report " + report);

        return 0;
    }
}
