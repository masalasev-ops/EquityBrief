namespace EquityBrief.Web.Marks;

// One job a model ran for a page: what the job is, the model's name, whether the paid lane ran it, what it came to, and
// the profile it ran under, none where no profile the settings name calls its model.
public sealed record ModelJob(string Job, string Model, bool Paid, string Detail, string? Profile = null);

// One model profile the settings name: its name, the model it calls, and whether it runs on the operator's own machine.
public sealed record JobProfile(string Name, string Model, bool Local);

public sealed partial class MarkRenderer
{
    // Each job behind a report with the profile it ran under and whether that profile is paid or local: the news labels of
    // the stories the page draws, by the profile the newest label's own row names; the newest release's reading, which the
    // local run alone writes under its pin; and each written section, by the profile the settings name for its model,
    // the section's own profile first. A model no profile names now is paid where the spend cap priced a call naming it,
    // and local otherwise.
    // see: Every report names the profile each of its jobs ran under and whether it is paid or local, read off the job's own row
    public static IReadOnlyList<ModelJob> ModelJobs(
        IReadOnlyList<WrittenCell> written,
        NewsView? news,
        ReleaseReadingView? release,
        IReadOnlyList<JobProfile> profiles,
        IReadOnlySet<string> pricedModels,
        IReadOnlyDictionary<string, string>? sectionProfiles = null)
    {
        var jobs = new List<ModelJob>();

        ModelJob Job(string job, string model, string detail, JobProfile? profile) =>
            new(job, model, profile is { } named ? !named.Local : pricedModels.Contains(model), detail, profile?.Name);

        JobProfile? ByModel(string model, string? preferred) =>
            profiles.FirstOrDefault(profile => profile.Name == preferred && profile.Model == model)
            ?? profiles.FirstOrDefault(profile => profile.Model == model);

        if (news is { } drawn)
        {
            var labelled = drawn.All - drawn.Unlabelled;

            jobs.Add(drawn.Model is { Length: > 0 } labeller
                ? Job("News labels", labeller, Formatted($"labelled {labelled} of the {drawn.All} stories the page draws"), profiles.FirstOrDefault(profile => profile.Name == drawn.Profile && profile.Model == labeller) ?? (drawn.Local ? profiles.FirstOrDefault(profile => profile.Local && profile.Model == labeller) : ByModel(labeller, drawn.Profile)))
                : new ModelJob("News labels", "none", false, Formatted($"no label stands for the {drawn.All} stories the page draws")));
        }

        if (release is { } read)
        {
            var reader = profiles.FirstOrDefault(profile => profile.Local && profile.Model == read.Model);

            jobs.Add(new ModelJob("The newest results release's reading", read.Model, false, FormattableString.Invariant($"read the release accepted {read.AcceptedOn:yyyy-MM-dd}"), reader?.Name));
        }

        jobs.AddRange(written.Select(section => Job(
            section.Section,
            section.Model,
            FormattableString.Invariant($"written {section.AsOf:yyyy-MM-dd}"),
            ByModel(section.Model, sectionProfiles is not null && sectionProfiles.TryGetValue(section.Section, out var own) ? own : null))));

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

    // The older bands beneath the stored year's, each with its role, its edges and the session the pulled prices turned at,
    // under a line saying they are read from the history before the stored year and that nothing but this page reads them;
    // nothing where none is drawn.
    // see: Older bands are read out as far as a pick's stop and target, marked older and read by nothing else
    public static string OlderBandsTable(IReadOnlyList<EquityBrief.Core.Report.OlderBand> bands, decimal? price)
    {
        if (bands.Count == 0)
        {
            return string.Empty;
        }

        var rows = string.Join(string.Empty, bands.Select(band =>
            FormattableString.Invariant($"<tr class=\"band older\" data-older-role=\"{Escaped(band.Role)}\" data-low-edge=\"{band.Low.ToString(Invariant)}\" data-high-edge=\"{band.High.ToString(Invariant)}\" data-made-on=\"{band.MadeOn:yyyy-MM-dd}\"><td>{Escaped(band.Role)}, older</td><td class=\"num\">{Price(band.Low)} to {Price(band.High)}</td><td>{band.MadeOn:yyyy-MM-dd}</td></tr>")));

        return Formatted($"<div class=\"sub\">Older bands, from the history before the stored year</div><p class=\"older-bands-note\">These are the prices the history pulled before the stored year turned at {(bands.Any(band => band.Role == "resistance") && bands.Any(band => band.Role == "support") ? "on either side of the price" : bands.Any(band => band.Role == "resistance") ? "above the price" : "below the price")}{(price is { } at ? Formatted($" of {Price(at)}") : string.Empty)}, where the stored year holds no band or none as far as a pick's target or stop. They are older and drawn here alone: no rule, plan or listing reads them.</p>")
            + "<div class=\"tbl-wrap\"><table class=\"older-bands\"><thead><tr><th>Band</th><th>Edges</th><th>Turned at on</th></tr></thead><tbody>" + rows + "</tbody></table></div>";
    }

    // Where a pick's or a held trade's target lies above every band the levels draw, stored and older, or its stop below every
    // one, a line saying so with the furthest band on that side: the history read turned at no price as far out.
    // see: Older bands are read out as far as a pick's stop and target, marked older and read by nothing else
    public static string BeyondTheBands(decimal? target, decimal? stop, IReadOnlyList<(decimal Low, decimal High)> bands)
    {
        if (bands.Count == 0)
        {
            return string.Empty;
        }

        var said = new System.Text.StringBuilder();

        if (target is { } aim && bands.All(band => band.High < aim))
        {
            said.Append(Formatted($"<p class=\"degraded\" data-beyond=\"target\">No band drawn here reaches the target of {Price(aim)}: the highest, stored or older, reaches {Price(bands.Max(band => band.High))}, and the history read turned at no price as high.</p>"));
        }

        if (stop is { } floor && bands.All(band => band.Low > floor))
        {
            said.Append(Formatted($"<p class=\"degraded\" data-beyond=\"stop\">No band drawn here reaches the stop of {Price(floor)}: the lowest, stored or older, reaches {Price(bands.Min(band => band.Low))}, and the history read turned at no price as low.</p>"));
        }

        return said.ToString();
    }

    // The line naming the quarter a newer results release reports that the page cannot draw yet: when the quarters fetch
    // next asks the provider for it, when its filing is expected, and the first of the two, on which the page is expected
    // to draw it.
    // see: A page drawing an older quarter than its newest results release reports names the quarter it awaits and the first day it is expected to read it
    public static string QuarterWaitLine(EquityBrief.Core.Report.QuarterWait wait) =>
        Formatted($"<p class=\"degraded quarter-wait\" data-awaiting=\"{DayOf(wait.Quarter)}\" data-released=\"{DayOf(wait.Released)}\" data-next-ask=\"{(wait.NextAsk is { } ask ? DayOf(ask) : "none")}\" data-filing-expected=\"{(wait.FilingExpected is { } filing ? DayOf(filing) : "none")}\" data-filing-due=\"{DayOf(wait.FilingDue)}\" data-clears=\"{DayOf(wait.Clears)}\">")
        + Escaped(FormattableString.Invariant($"The results release of {wait.Released:d MMM yyyy} reports the quarter to {wait.Quarter:d MMM yyyy}, which neither the provider's quarters nor a stored filing carry yet, so the older quarter is drawn beneath: {wait.Ask}; {wait.Filing}. The page is expected to draw it by {wait.Clears:d MMM yyyy}."))
        + "</p>";

    // The section a page's own account of the business is read from.
    public const string WhatItSells = "What the company sells";

    // The line flagging an industry group that names none of the business the company's own segments and revenue lines
    // report, with the share of the quarter's revenue they carry, none where they agree or either is not read.
    // see: A stock's industry group is checked against the company's own segments and revenue lines and not its description
    public static string IndustryMismatch(string? industry, IReadOnlyList<EquityBrief.Core.Report.BusinessLine> lines) =>
        EquityBrief.Core.Report.IndustryCheck.Mismatch(industry, lines) is { } described
            ? Formatted($"<p class=\"notice industry-mismatch\" data-industry=\"{Escaped(industry!)}\" data-business=\"{Escaped(described.Business)}\" data-share=\"{Math.Round(described.Share, 3).ToString(Invariant)}\">The company's own segments and revenue lines put {(described.Share * 100).ToString("0", Invariant)}% of the quarter's revenue in {Escaped(described.Business)}, which the group {Escaped(industry!)} does not name: the group is the provider's classification, and an override set for the stock stands in its place.</p>")
            : string.Empty;

    public static string ModelJobsTable(IReadOnlyList<ModelJob> jobs)
    {
        if (jobs.Count == 0)
        {
            return "<p class=\"model-jobs\" data-jobs=\"0\">No model has run a job for this report yet.</p>";
        }

        var rows = string.Join(string.Empty, jobs.Select(job =>
            Formatted($"<tr data-job=\"{Escaped(job.Job)}\" data-lane=\"{(job.Model == "none" ? "none" : job.Paid ? "paid" : "local")}\" data-profile=\"{Escaped(job.Profile ?? "none")}\"><td>{Escaped(job.Job)}</td>")
            + $"<td>{(job.Model == "none" ? "no model" : (job.Paid ? "the paid model" : "the local model") + (job.Profile is { } profile ? ", profile " + Escaped(profile) : ", no profile names it now"))}</td><td>{Escaped(job.Model)}</td><td>{Escaped(job.Detail)}</td></tr>"));

        return Formatted($"<div class=\"tbl-wrap\"><table class=\"model-jobs\" data-jobs=\"{jobs.Count}\"><caption>Which model ran each job</caption>")
            + "<thead><tr><th>Job</th><th>Ran on</th><th>Model</th><th>What it did</th></tr></thead><tbody>"
            + rows + "</tbody></table></div>";
    }
}
