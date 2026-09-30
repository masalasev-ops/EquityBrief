using EquityBrief.Core.Providers;
using EquityBrief.Core.Spending;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Worker.Research;

// The paid lane as configuration states it: the profile the research job's `Use` names,
// with its wire format, where the provider answers, which model, the options it is asked
// with, the key, the prices, and the caps its spend is held to.
//
// Read in one place, as the local lane's settings are, and read at startup, so a blank
// key, a model with no price or a format nothing implements refuses before any command
// runs rather than at the first pass. Nothing here names a provider: the shipped
// configuration does, and switching model is one word in that file.
// see: A paid job names its model profile in one word the operator switches, and a profile is priced at its configured rates at its call's own timestamp
public static class ResearchLane
{
    public static ResearchModelSettings Settings(IConfiguration configuration) =>
        ModelProfiles.Resolve(Value(configuration), Values(configuration), ModelProfiles.ResearchJob);

    // The research job's profile without its key, which is what a drain waiting out a peak
    // window reads its windows from: a key the secrets file does not hold stops the pass the
    // drain runs, on the pass's own row, and not the drain before it can say so.
    public static ModelProfile? Profile(IConfiguration configuration) =>
        ModelProfiles.Describe(Value(configuration), Values(configuration), ModelProfiles.ResearchJob);

    public static SpendCaps Caps(IConfiguration configuration) =>
        SpendCaps.From(configuration[SpendCaps.DayKey], configuration[SpendCaps.MonthKey]);

    // Each section the research job's map names a profile for, resolved, the job's own settings for a section that
    // names the job's profile. A name the map holds that is not one of figure 12.2's sections, and a profile the
    // profiles do not hold, refuse the job by name at startup, as a key the secrets file does not hold does.
    // see: Research names a profile per section as well as per job, and a Claude profile states its thinking
    public static IReadOnlyDictionary<string, ResearchModelSettings> Sections(IConfiguration configuration, ResearchModelSettings job)
    {
        var key = ModelProfiles.JobField(ModelProfiles.ResearchJob, ModelProfiles.SectionsField);
        var profiles = new Dictionary<string, ResearchModelSettings>(StringComparer.Ordinal) { [job.Profile] = job };
        var sections = new Dictionary<string, ResearchModelSettings>(StringComparer.Ordinal);

        foreach (var entry in configuration.GetSection(key).GetChildren())
        {
            if (!EquityBrief.Core.Research.ClaimRules.Sections.Contains(entry.Key, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{key}' names the section '{entry.Key}', which is not one of the sections figure 12.2 names. The research job " +
                    "stops here rather than write a section under a profile nobody meant for it.");
            }

            var profile = entry.Value?.Trim();

            if (string.IsNullOrEmpty(profile))
            {
                throw new InvalidOperationException($"'{key}:{entry.Key}' names no profile, and a section the map holds is written by the profile it names.");
            }

            if (!profiles.TryGetValue(profile, out var settings))
            {
                profiles[profile] = settings = ModelProfiles.ResolveProfile(Value(configuration), Values(configuration), ModelProfiles.ResearchJob, profile);
            }

            sections[entry.Key] = settings;
        }

        return sections;
    }

    // The trial the research job's configuration names, or none where it names no profile: a second profile asked
    // for named sections after a pass, over a stated number of reports from a stated day.
    // see: A trial asks a second profile for named sections after a report and records its drafts beside the report, never in it
    public const string TrialField = "Trial";

    public static ResearchTrial? Trial(IConfiguration configuration)
    {
        var key = ModelProfiles.JobField(ModelProfiles.ResearchJob, TrialField);
        var use = configuration[key + ":" + ModelProfiles.UseField];

        if (string.IsNullOrWhiteSpace(use))
        {
            return null;
        }

        string[] sections = [.. configuration.GetSection(key + ":Sections").GetChildren().Select(child => child.Value ?? string.Empty)];

        if (sections.Length == 0 || sections.Any(section => !EquityBrief.Core.Research.ClaimRules.Sections.Contains(section, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException($"'{key}:Sections' names no section, or one that is not a section figure 12.2 names.");
        }

        var reports = int.TryParse(configuration[key + ":Reports"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) && count > 0
            ? count
            : throw new InvalidOperationException($"'{key}:Reports' is not a whole number of reports above zero.");

        var from = DateOnly.TryParseExact(configuration[key + ":From"], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day)
            ? day
            : throw new InvalidOperationException($"'{key}:From' is not a date written as yyyy-MM-dd.");

        return new ResearchTrial(
            ModelProfiles.ResolveProfile(Value(configuration), Values(configuration), ModelProfiles.ResearchJob, use.Trim()),
            sections,
            reports,
            from);
    }

    // The prices of every profile a pass may call, which the drain waits out the peak windows of.
    public static IReadOnlyList<ResearchPricing> Prices(IConfiguration configuration) =>
        ModelProfiles.PricesFor(Value(configuration), Values(configuration), ModelProfiles.ResearchJob);

    static Func<string, string?> Value(IConfiguration configuration) => key => configuration[key];

    static Func<string, IEnumerable<string?>> Values(IConfiguration configuration) =>
        key => configuration.GetSection(key).GetChildren().Select(child => child.Value);
}
