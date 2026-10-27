namespace EquityBrief.Web.Marks;

// One job a model ran for a page: what the job is, the model's name, whether the paid lane ran it, and what it came to.
public sealed record ModelJob(string Job, string Model, bool Paid, string Detail);

public sealed partial class MarkRenderer
{
    // Each job behind a report with the model that ran it and whether that model is paid or local: the news labels of the
    // stories the page draws, the newest release's reading, and each written section. A model is paid where the run log
    // names it on a priced call or a labeller's run, and local otherwise.
    // see: Every report names the model each of its jobs ran on and whether it is paid or local, the news region the model of its newest label
    public static IReadOnlyList<ModelJob> ModelJobs(
        IReadOnlyList<WrittenCell> written,
        NewsView? news,
        ReleaseReadingView? release,
        IReadOnlySet<string> paidModels)
    {
        var jobs = new List<ModelJob>();

        if (news is { } drawn)
        {
            var labelled = drawn.All - drawn.Unlabelled;

            jobs.Add(drawn.Model is { Length: > 0 } labeller
                ? new ModelJob("News labels", labeller, paidModels.Contains(labeller), Formatted($"labelled {labelled} of the {drawn.All} stories the page draws"))
                : new ModelJob("News labels", "none", false, Formatted($"no label stands for the {drawn.All} stories the page draws")));
        }

        if (release is { } read)
        {
            jobs.Add(new ModelJob("The newest results release's reading", read.Model, paidModels.Contains(read.Model), FormattableString.Invariant($"read the release accepted {read.AcceptedOn:yyyy-MM-dd}")));
        }

        jobs.AddRange(written.Select(section => new ModelJob(section.Section, section.Model, paidModels.Contains(section.Model), FormattableString.Invariant($"written {section.AsOf:yyyy-MM-dd}"))));

        return jobs;
    }

    // The line saying which concepts a page drew with two values and the values, none where every concept holds one.
    // see: Each concept a page draws holds one value worked at the stored close beside the provider's own marked stale where struck far from it, and a check flags any concept drawn twice
    public static string ConceptClashes(IReadOnlyList<(string Concept, IReadOnlyList<string> Values)> clashes) =>
        clashes.Count == 0
            ? string.Empty
            : Formatted($"<p class=\"notice concept-clash\" role=\"status\" data-clashes=\"{clashes.Count}\">This page draws {(clashes.Count == 1 ? "one concept" : Formatted($"{clashes.Count} concepts"))} with more than one value, which a fix owes: ")
                + string.Join("; ", clashes.Select(clash => Escaped(clash.Concept.Replace('-', ' ')) + " drawn as " + string.Join(" and ", clash.Values.Select(Escaped))))
                + ".</p>";

    // The section a page's own account of the business is read from.
    public const string WhatItSells = "What the company sells";

    // The line flagging an industry group that names none of the business the page's own text describes, none where they
    // agree or either is not read.
    // see: A stock's industry group is the provider's until an override is set and is checked against the page's own text, and the industry cycle reads only sources naming the company or its industry
    public static string IndustryMismatch(string? industry, string? sells) =>
        EquityBrief.Core.Report.IndustryCheck.Mismatch(industry, sells) is { } business
            ? Formatted($"<p class=\"notice industry-mismatch\" data-industry=\"{Escaped(industry!)}\" data-business=\"{Escaped(business)}\">The page's own account of what the company sells describes {Escaped(business)}, which the group {Escaped(industry!)} does not name: the group is the provider's classification, and an override set for the stock stands in its place.</p>")
            : string.Empty;

    public static string ModelJobsTable(IReadOnlyList<ModelJob> jobs)
    {
        if (jobs.Count == 0)
        {
            return "<p class=\"model-jobs\" data-jobs=\"0\">No model has run a job for this report yet.</p>";
        }

        var rows = string.Join(string.Empty, jobs.Select(job =>
            Formatted($"<tr data-job=\"{Escaped(job.Job)}\" data-lane=\"{(job.Model == "none" ? "none" : job.Paid ? "paid" : "local")}\"><td>{Escaped(job.Job)}</td>")
            + $"<td>{(job.Model == "none" ? "no model" : job.Paid ? "the paid model" : "the local model")}</td><td>{Escaped(job.Model)}</td><td>{Escaped(job.Detail)}</td></tr>"));

        return Formatted($"<div class=\"tbl-wrap\"><table class=\"model-jobs\" data-jobs=\"{jobs.Count}\"><caption>Which model ran each job</caption>")
            + "<thead><tr><th>Job</th><th>Ran on</th><th>Model</th><th>What it did</th></tr></thead><tbody>"
            + rows + "</tbody></table></div>";
    }
}
