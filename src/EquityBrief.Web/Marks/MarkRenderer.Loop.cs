using System.Text;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Returns;

namespace EquityBrief.Web.Marks;

// What a Loop page draws of the operator's word: every decision on the index with what the apply step did, the settings
// approvals stored, the live alarm's periods, and the index's newest run, the one whose proposals are put to the operator.
public sealed record LoopDecided(IReadOnlyList<LoopDecisionRow> Decisions, IReadOnlyList<LoopSettingRow> Settings, IReadOnlyList<LoopAlarmRow> Alarms, string? Newest);

// The Loop page's marks: a family's rule today in the words its run stored, and each proposal tested against it, its
// change, its verdict part by part and its test years with the setting each fold's learning years chose. Every figure
// is read off the tester's rows; the marks draw and compute none.
// see: A proposal passes the tester on a block sign-flip test of its total edge after costs against the current rule, corrected within a run and held to a fixed bar across runs
// see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
public sealed partial class MarkRenderer
{
    public const string LoopPoints = "points";

    // A total or a difference in its unit: a book's in points of the buy, a swing family's in risks.
    static string LoopFigure(string unit, double? value) =>
        value is not { } held
            ? "<span class=\"degraded\">none</span>"
            : unit == LoopPoints
                ? Formatted($"{(held * 100).ToString("+0.00;-0.00;0.00", Invariant)} points")
                : Formatted($"{held.ToString("+0.00;-0.00;0.00", Invariant)} risks");

    static string LoopUnits(string unit, int count) =>
        unit == LoopPoints ? Formatted($"{count} month(s)") : Formatted($"{count} trade(s)");

    // The rule a family's proposals were tested against, in the words the run stored.
    public string LoopRule(string family, string? current) =>
        current is null
            ? $"<p class=\"degraded\" data-loop-rule=\"none\" data-family=\"{Escaped(family)}\">No procedure was tested for this family in this run, so no rule is read here.</p>"
            : $"<p class=\"loop-rule\" data-family=\"{Escaped(family)}\" data-rule=\"{Escaped(current)}\"><b>The rule today.</b> {Escaped(Capitalised(current))}</p>";

    // What the autopsy read of a family's finished trades in the run, a line a figure, none where it read none.
    public string LoopFindings(string family, IReadOnlyList<LoopFindingRow> findings) =>
        findings.Count == 0
            ? string.Empty
            : $"<div class=\"loop-findings\" data-family=\"{Escaped(family)}\" data-figures=\"{findings.Count}\"><p><b>What the rule's finished trades did.</b></p><ul>"
                + string.Concat(findings.Select(finding => $"<li data-figure=\"{Escaped(finding.Figure)}\" data-value=\"{(finding.Value is { } value ? value.ToString("R", Invariant) : "none")}\" data-trades=\"{finding.Trades}\">{Escaped(Capitalised(finding.Words))}.</li>"))
                + "</ul></div>";

    // Each reading's spread over a family's finished listings in the run, in a section folded shut, a row a reading;
    // none where the run stored none for the family.
    // see: Winners against losers proposes a condition only where it beats a within-night shuffle of its own search
    public string LoopReadings(string family, IReadOnlyList<LoopReadingRow> spreads)
    {
        if (spreads.Count == 0)
        {
            return string.Empty;
        }

        static string Figure(double? value, string format) => value is { } held ? held.ToString(format, Invariant) : "none";

        var html = new StringBuilder();

        html.Append(Invariant, $"<details class=\"loop-readings\" data-family=\"{Escaped(family)}\" data-readings=\"{spreads.Count}\"><summary>Each reading, winners against losers</summary>");
        html.Append("<div class=\"tbl-wrap\"><table class=\"loop-reading-table\"><thead><tr>");
        html.Append(TippedHeading("Reading", "the reading of the ledger's catalogue, read on each listing's session as the night reads it"));
        html.Append(TippedHeading("Listings", "the finished listings holding the reading, those that beat the same plan on every member that night and those that did not", "r"));
        html.Append(TippedHeading("Winners' median", "the median of the reading among the listings that beat the same plan on every member", "r"));
        html.Append(TippedHeading("Losers' median", "the median of the reading among the others", "r"));
        html.Append(TippedHeading("Edge by tenth", "the mean edge after costs, in risks, of each tenth of the listings in the reading's order, lowest first"));
        html.Append("</tr></thead><tbody>");

        foreach (var spread in spreads)
        {
            var deciles = string.Join(" ", spread.Deciles.Select(decile => Figure(decile, "+0.00;-0.00;0.00")));

            html.Append(Invariant, $"<tr data-reading=\"{Escaped(spread.Reading)}\" data-units=\"{spread.Units}\" data-winners=\"{spread.Winners}\" data-losers=\"{spread.Losers}\">");
            html.Append(Invariant, $"<td>{Escaped(spread.Reading)}</td>");
            html.Append(Invariant, $"<td class=\"r num\">{spread.Units}: {spread.Winners} won, {spread.Losers} did not</td>");
            html.Append(Invariant, $"<td class=\"r num\">{Figure(spread.WinnersMedian, "0.####")}</td><td class=\"r num\">{Figure(spread.LosersMedian, "0.####")}</td>");
            html.Append(Invariant, $"<td class=\"num\" data-deciles=\"{Escaped(deciles)}\">{Escaped(deciles)}</td></tr>");
        }

        html.Append("</tbody></table></div></details>");

        return html.ToString();
    }

    // A family's learned score in the run, drawn only where the run brought the score's proposals for it: its weight in
    // words with its hash, the words saying it has not passed the tester on this index until one of its proposals did,
    // and from then the rank each of tonight's cards carries under it, read off the cards and never worked out here.
    // see: A pick's card draws the setups like it under its rule beneath the rule's record, and the score's rank only once the score passed on its index
    public string LoopScore(string family, IReadOnlyList<LoopProposalRow> proposals, LoopModelRow? model, IReadOnlyList<LoopRankRow> ranks)
    {
        var scored = proposals.Where(proposal => proposal.Proposal.StartsWith(RidgeScore.Proposal, StringComparison.Ordinal)).ToArray();

        if (scored.Length == 0)
        {
            return string.Empty;
        }

        var validated = scored.Any(proposal => proposal.Passed);
        var html = new StringBuilder();

        html.Append(Invariant, $"<div class=\"loop-score\" data-family=\"{Escaped(family)}\" data-validated=\"{(validated ? 1 : 0)}\" data-hash=\"{Escaped(model?.Hash ?? "none")}\">");
        html.Append(model is { } held
            ? Formatted($"<p class=\"loop-model\" data-setups=\"{held.Setups}\"><b>The learned score.</b> {Escaped(Capitalised(held.Words))}; {Escaped(held.Hash)}, fitted on the setups that ended before {held.LearnedBefore.ToString("yyyy-MM-dd", Invariant)}.</p>")
            : "<p class=\"loop-model degraded\" data-setups=\"none\"><b>The learned score.</b> No score was fitted on all finished data in this run: too few of the ledger's finished setups held every reading it weighs.</p>");

        if (!validated)
        {
            html.Append(Invariant, $"<p class=\"loop-rank degraded\" data-rank=\"none\">{Escaped(EquityBrief.Core.Cards.CardSimilar.NotValidated)}.</p></div>");

            return html.ToString();
        }

        var ranked = ranks.Where(rank => rank.Rank is not null).ToArray();

        html.Append(ranked.Length == 0
            ? "<p class=\"loop-rank\" data-ranks=\"0\">Passed the tester on this index in this run; no card tonight carries a rank under it.</p>"
            : Formatted($"<p class=\"loop-rank\" data-ranks=\"{ranked.Length}\">Passed the tester on this index in this run; tonight's cards rank at ")
                + string.Join(", ", ranked.Select(rank => Formatted($"<span data-ticker=\"{Escaped(rank.Ticker)}\" data-rank=\"{rank.Rank}\">{Escaped(rank.Ticker)}, hundredth {rank.Rank}</span>")))
                + ".</p>");
        html.Append("</div>");

        return html.ToString();
    }

    // The Loop page's two presses, which the read surface maps.
    public const string LoopDecideRoute = "/loop/decide/";

    public const string LoopRestoreRoute = "/loop/restore/";

    static string DayOf(string instant) => instant.Length >= 10 ? instant[..10] : instant;

    // The operator's word on one proposal: what was decided and what the apply step did with it; for a proposal of the
    // index's newest run that passed and states a change, the Approve and Decline presses, or why none is offered; and
    // a proposal declined before over fewer blocks put again with the line that says so.
    // see: An approved change is applied before the next night from the night's own build, on the index it was approved on alone
    // see: A declined proposal is put again once a new complete block has been added since the decline and it passes with that block
    public string LoopDecision(string index, LoopRunRow run, LoopProposalRow proposal, LoopDecided decided)
    {
        var key = proposal.Family + "|" + proposal.Proposal;
        var mine = decided.Decisions.FirstOrDefault(one => one.Run == run.RunId && one.Family == proposal.Family && one.Proposal == proposal.Proposal);

        if (mine is null && (!proposal.Passed || proposal.Change is null))
        {
            return string.Empty;
        }

        var html = new StringBuilder();

        html.Append(Invariant, $"<div class=\"loop-decision\" data-loop-key=\"{Escaped(key)}\">");

        if (mine is not null)
        {
            html.Append(mine.Decision == LoopDecisions.Declined
                ? $"<p class=\"loop-decided\" data-decision=\"declined\"><b>Declined</b> on {Escaped(DayOf(mine.DecidedAt))}: {Escaped(mine.Reason ?? string.Empty)}. It changes nothing.</p>"
                : $"<p class=\"loop-decided\" data-decision=\"approved\" data-outcome=\"{Escaped(mine.Outcome ?? "waiting")}\"><b>Approved</b> on {Escaped(DayOf(mine.DecidedAt))}. "
                    + (mine.Outcome switch
                    {
                        LoopDecisions.Applied => $"Applied on {Escaped(DayOf(mine.AppliedAt ?? string.Empty))}: {Escaped(mine.Words ?? string.Empty)}.",
                        LoopDecisions.Refused => $"Not applied: {Escaped(mine.Words ?? string.Empty)}.",
                        _ => "It is applied before the next night.",
                    })
                    + "</p>");
        }
        else if (run.RunId != decided.Newest)
        {
            html.Append("<p class=\"degraded\" data-decision=\"older-run\">A newer run stands for this index, and its proposals are the ones put to you.</p>");
        }
        else if (LoopDecisions.Refusal(index, proposal.Family) is { } refused)
        {
            html.Append($"<p class=\"degraded\" data-decision=\"not-offered\">Approve is not offered here: {Escaped(refused)}.</p>");
        }
        else
        {
            var before = decided.Decisions
                .Where(one => one.Run != run.RunId && one.Family == proposal.Family && one.Proposal == proposal.Proposal && one.Decision == LoopDecisions.Declined && one.Change == proposal.Change)
                .OrderByDescending(one => one.Blocks ?? 0)
                .FirstOrDefault();
            var approvedInRun = decided.Decisions.Any(one => one.Run == run.RunId && one.Family == proposal.Family && one.Decision == LoopDecisions.Approved);

            if (before is not null && !LoopDecisions.PutAgain(before.Blocks ?? 0, proposal.Blocks, passedNow: true))
            {
                html.Append(Formatted($"<p class=\"loop-decided\" data-decision=\"declined-before\"><b>Declined</b> on {Escaped(DayOf(before.DecidedAt))} over {before.Blocks ?? 0} blocks: {Escaped(before.Reason ?? string.Empty)}. It is put to you again once a later run reads a new complete block and it passes with that block.</p>"));
            }
            else if (approvedInRun)
            {
                html.Append("<p class=\"degraded\" data-decision=\"one-a-run\">Another change to this family was approved in this run, and one change a family a run is applied.</p>");
            }
            else
            {
                if (before is not null)
                {
                    html.Append(Formatted($"<p class=\"loop-decided\" data-decision=\"put-again\"><b>Put to you again:</b> declined on {Escaped(DayOf(before.DecidedAt))} over {before.Blocks ?? 0} blocks, and this run reads {proposal.Blocks}.</p>"));
                }

                var action = $"{LoopDecideRoute}{Uri.EscapeDataString(index)}/{Uri.EscapeDataString(run.RunId)}/{Uri.EscapeDataString(proposal.Family)}";
                var hidden = $"<input type=\"hidden\" name=\"proposal\" value=\"{Escaped(proposal.Proposal)}\">";

                html.Append($"<p class=\"loop-ask\">Approve to apply this change before the next night, on this index alone; decline to leave the rule as it stands.</p>");
                html.Append($"<form class=\"loop-press\" method=\"post\" action=\"{Escaped(action)}\" data-loop-key=\"{Escaped(key)}\">{hidden}<input type=\"hidden\" name=\"decision\" value=\"approve\"><button type=\"submit\">Approve</button></form>");
                html.Append($"<form class=\"loop-press\" method=\"post\" action=\"{Escaped(action)}\" data-loop-key=\"{Escaped(key)}\">{hidden}<input type=\"hidden\" name=\"decision\" value=\"decline\"><label>Reason <input name=\"reason\" required></label> <button type=\"submit\">Decline</button></form>");
            }
        }

        html.Append("<p class=\"loop-said\" aria-live=\"polite\"></p></div>");

        return html.ToString();
    }

    // Every decision on the index, newest first, with what the apply step did, and every setting approvals stored, each
    // family's newest first; none where neither holds a row.
    public string LoopHistory(LoopDecided decided)
    {
        if (decided.Decisions.Count == 0 && decided.Settings.Count == 0)
        {
            return "<p class=\"degraded\" data-loop-history=\"none\">No proposal on this index holds your word yet, and no approval has changed a rule here.</p>";
        }

        var html = new StringBuilder();

        if (decided.Settings.Count > 0)
        {
            html.Append(Invariant, $"<div class=\"loop-settings\" data-settings=\"{decided.Settings.Count}\"><p><b>The settings approvals stored.</b> Each family stands at its newest.</p><ul>");

            foreach (var setting in decided.Settings)
            {
                html.Append(Invariant, $"<li data-setting=\"{setting.Id}\" data-family=\"{Escaped(setting.Family)}\">The {Escaped(setting.Family)}, change {setting.Id} on {Escaped(DayOf(setting.SetAt))}: {Escaped(setting.Words)}, from {Escaped(setting.Proposal)} of run {Escaped(setting.Run)}.</li>");
            }

            html.Append("</ul></div>");
        }

        if (decided.Decisions.Count > 0)
        {
            html.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"loop-decisions\" data-decisions=\"{decided.Decisions.Count}\"><thead><tr>");
            html.Append(TippedHeading("Decided", "the day the operator's word was given"));
            html.Append(TippedHeading("Family", "the family the proposal changes on this index"));
            html.Append(TippedHeading("Proposal", "the proposal and the run that tested it, or the restore the alarm put"));
            html.Append(TippedHeading("Word", "approved or declined, with the reason a decline gives"));
            html.Append(TippedHeading("Applied", "what the apply step before the next night did with an approval"));
            html.Append("</tr></thead><tbody>");

            foreach (var one in decided.Decisions)
            {
                var word = one.Decision == LoopDecisions.Declined ? "declined: " + (one.Reason ?? string.Empty) : "approved";
                var applied = one.Decision == LoopDecisions.Declined ? "nothing, as declined"
                    : one.Outcome switch
                    {
                        LoopDecisions.Applied => $"applied on {DayOf(one.AppliedAt ?? string.Empty)}: {one.Words}",
                        LoopDecisions.Refused => $"not applied: {one.Words}",
                        _ => "waiting for the next night",
                    };

                html.Append($"<tr data-run=\"{Escaped(one.Run)}\" data-family=\"{Escaped(one.Family)}\" data-decision=\"{Escaped(one.Decision)}\" data-outcome=\"{Escaped(one.Outcome ?? "waiting")}\">");
                html.Append($"<td class=\"num\">{Escaped(DayOf(one.DecidedAt))}</td><td>{Escaped(one.Family)}</td><td>{Escaped(Capitalised(one.Proposal))}, {Escaped(one.Run)}</td><td>{Escaped(word)}</td><td>{Escaped(applied)}</td></tr>");
            }

            html.Append("</tbody></table></div>");
        }

        return html.ToString();
    }

    // The live alarm on the index: each family's periods against the low its reference gives at their own count, the
    // newest first in the line above them, a family flagged on its newest period with the restore waiting for approval
    // where an approval changed it and saying nothing is to restore where none did. A swing family's mean edge is in
    // risks and a book's in points. The page draws the alarm's rows and computes none of them.
    // see: The live alarm flags a rule whose edge stood under its reference's fifth percentile two periods running
    public string LoopAlarm(string index, LoopDecided decided)
    {
        if (decided.Alarms.Count == 0)
        {
            return "<p class=\"degraded\" data-loop-alarm=\"none\">The live alarm has read no period on this index yet: a month, or a book's quarter, is read once it has closed with every trade that ended in it holding its edge, against the newest tester run's reference.</p>";
        }

        var html = new StringBuilder();

        foreach (var family in decided.Alarms.GroupBy(one => one.Family))
        {
            var periods = family.OrderByDescending(one => one.Period).ToArray();
            var newest = periods[0];
            var book = family.Key == EquityBrief.Core.Families.HeavyweightRule.Name;

            string Figure(double? value) => value is not { } held ? "none" : book
                ? (held * 100).ToString("+0.00;-0.00;0.00", Invariant) + " points"
                : held.ToString("+0.00;-0.00;0.00", Invariant) + " risks";

            html.Append(Invariant, $"<div class=\"loop-alarm\" data-family=\"{Escaped(family.Key)}\" data-flagged=\"{(newest.Flagged ? 1 : 0)}\" data-periods=\"{periods.Length}\">");
            html.Append(newest.Flagged
                ? Formatted($"<p class=\"loop-flag\"><b>The {Escaped(family.Key)} rule is flagged.</b> Its edge after costs stood under its reference's low in {newest.Streak} counted {(book ? "quarters" : "months")} running, the newest {newest.Period.ToString("yyyy-MM", Invariant)}. The flag changes nothing by itself.</p>")
                : $"<p class=\"loop-flag-none\"><b>The {Escaped(family.Key)} rule is not flagged.</b></p>");

            if (newest.Flagged)
            {
                html.Append(Restore(index, family.Key, decided));
            }

            html.Append("<div class=\"tbl-wrap\"><table class=\"loop-periods\"><thead><tr>");
            html.Append(TippedHeading(book ? "Quarter" : "Month", "the period the units ended in"));
            html.Append(TippedHeading("Units", book ? "the holdings sold in the quarter" : "the trades that ended in the month", "r"));
            html.Append(TippedHeading("Mean edge", "their mean edge after costs against the same plan on every member, or a holding's against its size cut", "r"));
            html.Append(TippedHeading("Low", "the fifth percentile of the means of as many of the reference's units, drawn ten thousand times in weekly blocks", "r"));
            html.Append(TippedHeading("Read", "whether the period counted, under five units it does not, and whether it stood under the low"));
            html.Append("</tr></thead><tbody>");

            foreach (var period in periods)
            {
                var read = !period.Counted ? "not counted, too few units" : period.Under ? $"under the low, {period.Streak} running" : "at or above the low";

                html.Append(Invariant, $"<tr data-period=\"{period.Period.ToString("yyyy-MM-dd", Invariant)}\" data-units=\"{period.Units}\" data-counted=\"{(period.Counted ? 1 : 0)}\" data-under=\"{(period.Under ? 1 : 0)}\" data-streak=\"{period.Streak}\" data-reference=\"{Escaped(period.Reference)}\">");
                html.Append(Invariant, $"<td class=\"num\">{period.Period.ToString("yyyy-MM", Invariant)}</td><td class=\"r num\">{period.Units}</td><td class=\"r num\">{Figure(period.Edge)}</td><td class=\"r num\">{Figure(period.Low)}</td><td>{Escaped(read)}</td></tr>");
            }

            html.Append("</tbody></table></div></div>");
        }

        return html.ToString();
    }

    // The restore a flagged family's alarm puts: the setting before the newest change an approval stored, its press or its
    // state, or why there is nothing to restore.
    static string Restore(string index, string family, LoopDecided decided)
    {
        if (LoopDecisions.Refusal(index, family) is { } refused)
        {
            return $"<p class=\"degraded\" data-restore=\"not-offered\">No restore is offered here: {Escaped(refused)}.</p>";
        }

        if (decided.Settings.Where(one => one.Family == family).OrderByDescending(one => one.Id).FirstOrDefault() is not { } newest)
        {
            return "<p class=\"degraded\" data-restore=\"none\">Nothing to restore: no approval has changed this rule, so it stands at its own setting.</p>";
        }

        var proposal = LoopDecisions.RestoreProposal(newest.Id);

        if (decided.Decisions.FirstOrDefault(one => one.Run == LoopDecisions.RestoreRun && one.Family == family && one.Proposal == proposal) is { } restored)
        {
            return $"<p class=\"loop-decided\" data-restore=\"{Escaped(restored.Outcome ?? "waiting")}\"><b>Restore approved</b> on {Escaped(DayOf(restored.DecidedAt))}: {Escaped(restored.Outcome is null ? "it is applied before the next night" : restored.Words ?? string.Empty)}.</p>";
        }

        var key = family + "|" + proposal;
        var action = $"{LoopRestoreRoute}{Uri.EscapeDataString(index)}/{Uri.EscapeDataString(family)}/{newest.Id.ToString(Invariant)}";

        return $"<div class=\"loop-decision\" data-loop-key=\"{Escaped(key)}\" data-restore=\"offered\"><p class=\"loop-ask\">A restore waits for your approval: the setting before change {newest.Id.ToString(Invariant)}, {Escaped(newest.Words)}.</p>"
            + $"<form class=\"loop-press\" method=\"post\" action=\"{Escaped(action)}\" data-loop-key=\"{Escaped(key)}\"><button type=\"submit\">Approve the restore</button></form><p class=\"loop-said\" aria-live=\"polite\"></p></div>";
    }

    // One proposal: what it changes, its verdict part by part, its test years, and what the gate could detect.
    public string LoopProposal(LoopProposalRow proposal, IReadOnlyList<LoopTestRow> years)
    {
        var book = proposal.Unit == LoopPoints;
        var fewest = LoopScreens.Fewest(book);
        var html = new StringBuilder();

        html.Append(Invariant, $"<div class=\"loop-proposal\" data-family=\"{Escaped(proposal.Family)}\" data-proposal=\"{Escaped(proposal.Proposal)}\" data-passed=\"{(proposal.Passed ? 1 : 0)}\" data-years=\"{years.Count}\">");
        html.Append(Invariant, $"<h4 class=\"loop-name\">{Escaped(Capitalised(proposal.Proposal))}: {(proposal.Passed ? "passed the tester" : "did not pass the tester")}</h4>");
        html.Append(proposal.Words is { } words
            ? $"<p class=\"loop-change\" data-words=\"{Escaped(words)}\"><b>Proposed, run on all finished data.</b> {Escaped(Capitalised(words))}</p>"
            : "<p class=\"loop-change degraded\" data-words=\"none\"><b>No change.</b> The procedure run on all finished data chose no setting that met the floors, so it proposes nothing; its test years below are what it chose year by year.</p>");

        if (proposal.Finding is { } finding)
        {
            html.Append($"<p class=\"loop-finding\" data-finding=\"{Escaped(finding)}\"><b>Why.</b> {Escaped(Capitalised(finding))}.</p>");
        }

        var gate = proposal.Adjusted is { } p
            ? Formatted($"<li data-part=\"gate\" data-adjusted=\"{p.ToString("R", Invariant)}\" data-held=\"{(proposal.Gate ? 1 : 0)}\">The gate: an adjusted p-value of {p.ToString("0.0000", Invariant)} over {proposal.Blocks} blocks of 63 sessions, against the bar of {LoopGate.Bar.ToString("0.0000", Invariant)}; {(proposal.Gate ? "at or under it" : "over it")}.</li>")
            : Formatted($"<li data-part=\"gate\" data-adjusted=\"none\" data-held=\"0\">The gate is not read: {proposal.Blocks} block(s) of 63 sessions, under the floor of {Blocks.Floor}.</li>");
        var stable = Formatted($"<li data-part=\"stable\" data-counted=\"{proposal.Counted}\" data-better=\"{proposal.Better}\" data-held=\"{(proposal.Stable ? 1 : 0)}\">Stability: better in {proposal.Better} of the {proposal.Counted} complete year(s) holding {fewest} {(book ? "months" : "trades")} a side, against three in five with the latest complete year counted and better; {(proposal.Stable ? "held" : "not held")}.</li>");
        var trimmed = Formatted($"<li data-part=\"trimmed\" data-trimmed=\"{(proposal.Trimmed is { } t ? t.ToString("R", Invariant) : "none")}\" data-held=\"{(proposal.Trimmed > 0 ? 1 : 0)}\">With the {LoopScreens.LeftOut} largest results by size left out of each side, the proposal's total less the rule's: {LoopFigure(proposal.Unit, proposal.Trimmed)}; {(proposal.Trimmed > 0 ? "above nothing" : "not above nothing")}.</li>");
        var counts = Formatted($"<li data-part=\"counts\" data-units=\"{proposal.Units}\" data-held=\"{(proposal.Counts ? 1 : 0)}\">The count: {LoopUnits(proposal.Unit, proposal.Units)} over the test years, against the {(book ? LoopScreens.BookMonths : LoopScreens.SwingTrades)} {(book ? "months a book is" : "trades a swing family is")} judged on; {(proposal.Counts ? "enough" : "too few")}.</li>");

        html.Append(Invariant, $"<ul class=\"loop-verdict\">{gate}{stable}{trimmed}{counts}</ul>");
        html.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"loop-years\" data-family=\"{Escaped(proposal.Family)}\" data-proposal=\"{Escaped(proposal.Proposal)}\"><thead><tr>");
        html.Append(TippedHeading("Year", "the test year, the newest the months since the latest complete year"));
        html.Append(TippedHeading("Chosen on the years before", "the setting the procedure chose on the trades that ended before the year began"));
        html.Append(TippedHeading("The rule today", "its total edge after costs over the units it entered in the year", "r"));
        html.Append(TippedHeading("Proposed", "the chosen setting's total edge after costs over the units it entered in the year", "r"));
        html.Append(TippedHeading("Better", "whether the proposal's total stood above the rule's, in a year the stability screen counts", "r"));
        html.Append("</tr></thead><tbody>");

        foreach (var year in years)
        {
            var counted = year.Complete && year.CurrentUnits >= fewest && year.ProposedUnits >= fewest;
            var better = counted ? (year.ProposedTotal > year.CurrentTotal ? "yes" : "no") : year.Complete ? "not counted, too few" : "not counted, to date";

            html.Append(Invariant, $"<tr data-year=\"{year.Year}\" data-complete=\"{(year.Complete ? 1 : 0)}\" data-current-units=\"{year.CurrentUnits}\" data-proposed-units=\"{year.ProposedUnits}\" data-current-total=\"{year.CurrentTotal.ToString("R", Invariant)}\" data-proposed-total=\"{year.ProposedTotal.ToString("R", Invariant)}\">");
            html.Append(Invariant, $"<td class=\"num\">{year.Year}{(year.Complete ? string.Empty : " to date")}</td>");
            html.Append(year.Chosen is { } chosen
                ? $"<td data-chosen=\"{Escaped(chosen)}\">{Escaped(Capitalised(chosen))}</td>"
                : "<td data-chosen=\"none\" class=\"degraded\">no setting met the floors on the years before, so the year reads the rule today</td>");
            html.Append(Invariant, $"<td class=\"r num\">{LoopFigure(proposal.Unit, year.CurrentTotal)} over {LoopUnits(proposal.Unit, year.CurrentUnits)}</td>");
            html.Append(Invariant, $"<td class=\"r num\">{LoopFigure(proposal.Unit, year.ProposedTotal)} over {LoopUnits(proposal.Unit, year.ProposedUnits)}</td>");
            html.Append(Invariant, $"<td class=\"r\" data-better=\"{better}\">{better}</td></tr>");
        }

        html.Append("</tbody></table></div>");
        html.Append(proposal.Detectable is { } detectable
            ? Formatted($"<p class=\"loop-power\" data-detectable=\"{detectable.ToString("R", Invariant)}\">At this bar the gate detects a difference of about {LoopFigure(proposal.Unit, detectable)} {(book ? "a month" : "a trade")} four times in five, read from the blocks' own scatter; a smaller true difference passes less often than that.</p>")
            : "<p class=\"loop-power degraded\" data-detectable=\"none\">The blocks are too few for the gate's power to be read.</p>");
        html.Append(proposal.Proposal.StartsWith(RidgeScore.Proposal, StringComparison.Ordinal)
            ? Formatted($"<p class=\"loop-folds\" data-stable-folds=\"{proposal.StableFolds}\" data-folds=\"{years.Count}\">{proposal.StableFolds} of the {years.Count} folds held enough finished setups to fit the score on their own years before.</p>")
            : Formatted($"<p class=\"loop-folds\" data-stable-folds=\"{proposal.StableFolds}\" data-folds=\"{years.Count}\">{proposal.StableFolds} of the {years.Count} folds chose {(book ? "the same setting as" : "within a grid step of")} the proposal on their own years before.</p>"));
        html.Append("</div>");

        return html.ToString();
    }
}
