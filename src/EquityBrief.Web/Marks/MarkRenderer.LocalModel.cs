using EquityBrief.Core.Releases;

namespace EquityBrief.Web.Marks;

// One run of the local model as the Run page states it: its kind, its start and end, the documents it fetched and read,
// its refused answers by cause, its median and slowest seconds a call, the pin it read by its model, the start of its
// file's hash, the runtime and the prompt's version, the check of the model's file it made, and what it came to with
// why where it did not finish.
public sealed record LocalRunView(
    string Kind,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int Fetched,
    int Read,
    IReadOnlyList<(string Cause, int Count)> Refused,
    double? MedianSeconds,
    double? SlowestSeconds,
    string? Pin,
    string? FileCheck,
    string Outcome,
    string? Why);

public sealed partial class MarkRenderer
{
    // The local model's region: one row a run, newest first, or one line where the local model has not run.
    // see: The local run is a process of its own at two slots outside the night and refuses any model file, runtime or prompt but its pin
    public string LocalModelRegion(IReadOnlyList<LocalRunView> runs)
    {
        if (runs.Count == 0)
        {
            return FormattableString.Invariant($"<p class=\"local-runs\" data-runs=\"0\">The local model has not run yet. It runs at {LocalSlots.EarlyStart:HH:mm} and {LocalSlots.LateStart:HH:mm} UTC each day, and by hand.</p>");
        }

        var rows = string.Join(string.Empty, runs.Select(run =>
        {
            var refused = run.Refused.Count == 0
                ? "none"
                : string.Join(", ", run.Refused.Select(cause => FormattableString.Invariant($"{cause.Count} {Escaped(cause.Cause)}")));
            var time = run.MedianSeconds is { } median && run.SlowestSeconds is { } slowest
                ? FormattableString.Invariant($"{median:0.0} s, slowest {slowest:0.0} s")
                : "none read";
            var ended = run.EndedAt is { } end ? FormattableString.Invariant($"<span class=\"nw\">{end.UtcDateTime:HH:mm} UTC</span>") : "no end";
            var outcome = Escaped(run.Outcome) + (run.Why is { } why ? ": " + Escaped(why) : string.Empty);

            return FormattableString.Invariant($"<tr data-kind=\"{Escaped(run.Kind)}\" data-outcome=\"{Escaped(run.Outcome)}\">")
                + FormattableString.Invariant($"<td>{Escaped(run.Kind)}</td><td><span class=\"nw\">{run.StartedAt.UtcDateTime:yyyy-MM-dd HH:mm}</span> to {ended}</td>")
                + FormattableString.Invariant($"<td>{run.Fetched}</td><td>{run.Read}</td><td>{refused}</td><td>{time}</td>")
                + $"<td>{Escaped(run.Pin ?? "no pin read")}</td><td>{Escaped(run.FileCheck ?? "not checked")}</td><td>{outcome}</td></tr>";
        }));

        return FormattableString.Invariant($"<div class=\"tbl-wrap\"><table class=\"local-runs\" data-runs=\"{runs.Count}\">")
            + "<tr><th>Run</th><th>Started and ended</th><th>Documents fetched</th><th>Documents read</th><th>Refused answers by cause</th><th>Time a call, median</th><th>Pinned to</th><th>Model's file checked by</th><th>Came to</th></tr>"
            + rows + "</table></div>";
    }

    // From 20.3 the newest results release's four answers under what management said: each question with its word, the
    // day the SEC accepted the release, and the line naming the model and the reading version it was read under from the
    // release with its names and dates removed.
    // see: A release is read with every name and date in it replaced by a neutral word, and product names kept
    public string ReleaseReading(ReleaseReadingView view)
    {
        string Row(string question, string word) =>
            $"<tr data-question=\"{Escaped(question)}\"><td>{Escaped(question)}</td><td data-reading=\"{Reading(question, word)}\">{Escaped(word)}</td></tr>";

        return FormattableString.Invariant($"<div class=\"release-reading\" data-pin=\"{view.Pin}\" data-accepted=\"{view.AcceptedOn:yyyy-MM-dd}\">")
            + FormattableString.Invariant($"<p>The newest results release, accepted {view.AcceptedOn:yyyy-MM-dd}, as the local model read it.</p>")
            + "<div class=\"tbl-wrap\"><table class=\"release-answers\"><thead><tr><th>Question</th><th>The model's answer</th></tr></thead><tbody>"
            + Row(ReleaseQuestions.Results, view.Answer.Results)
            + Row(ReleaseQuestions.Outlook, view.Answer.Outlook)
            + Row(ReleaseQuestions.Tone, view.Answer.Tone)
            + Row(ReleaseQuestions.Demand, view.Answer.Demand)
            + "</tbody></table></div>"
            + FormattableString.Invariant($"<p class=\"release-read-by\">Read by the local model, {Escaped(view.Model)}, reading version {view.Pin}, from the release with its names and dates removed.</p>")
            + "</div>";
    }

    static string Reading(string question, string word) =>
        ReleaseQuestions.For(question).Words.FirstOrDefault(listed => listed.Word == word)?.Reading is { } reading
            ? reading.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "none";
}
