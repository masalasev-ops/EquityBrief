using System.Globalization;

namespace EquityBrief.Core.Providers;

// A profile as the read surface states it: which profile a job uses, the model, its prices
// and the earliest date its provider publishes for retiring it with the day that was read.
// Described without the key, which the read surface never reads.
public sealed record ModelProfile(
    string Job,
    string Name,
    string Format,
    string Model,
    ResearchPricing? Pricing,
    DateOnly? Retires,
    DateOnly? RetiresReadOn);

// The paid models configuration names, and the one each paid job uses.
//
// `EquityBrief:Models:Profiles` holds one entry per model: its wire format, where its
// provider answers, the model, the name of the secrets section its key sits under, any
// options, its prices, and the earliest date its provider publishes for retiring it with
// the day that was read. Each paid job names the profile it uses by one word, its `Use`,
// under a section of its own beside its answer budget and how long a call may take.
// Switching a job's model is changing that word, and it takes effect on the job's next run.
// see: A paid job names its model profile in one word the operator switches, and a profile is priced at its configured rates at its call's own timestamp
// see: A paid model is one interface with an implementation per wire format, and a job never falls back from the profile it names
public static class ModelProfiles
{
    public const string Section = "EquityBrief:Models";
    public const string ProfilesSection = Section + ":Profiles";

    // The paid jobs, each under a section of its own.
    public const string ResearchJob = "Research";

    public static readonly string[] Jobs = [ResearchJob];

    // A job's fields.
    public const string UseField = "Use";
    public const string TimeoutField = "TimeoutSeconds";
    public const string AnswerTokensField = "AnswerTokens";

    // A profile's fields.
    public const string FormatField = "Format";
    public const string BaseAddressField = "BaseAddress";
    public const string ModelField = "Model";
    public const string KeyField = "Key";
    public const string OptionsField = "Options";
    public const string PricesField = "Prices";
    public const string RetiresField = "Retires";
    public const string RetiresReadOnField = "RetiresReadOn";
    public const string ThinkingField = "Thinking";

    // The research job's map from a section to the profile that writes it, beside its `Use`, which writes every
    // section the map names none for.
    // see: Research names a profile per section as well as per job, and a Claude profile states its thinking
    public const string SectionsField = "Sections";

    // Where the secrets file holds the key a profile names: that section's own `ApiKey`, and
    // beside it the workspace a key not scoped to one has to name on every request, where the
    // provider asks for one.
    public const string ApiKeyField = "ApiKey";
    public const string WorkspaceIdField = "WorkspaceId";

    // How long before a profile's retirement date the run page names it.
    // see: A profile carries its provider's earliest retirement date, and the run page names it from thirty days before
    public const int RetirementWarningDays = 30;

    public static string Job(string job) => Section + ":" + job;

    public static string JobField(string job, string field) => Job(job) + ":" + field;

    public static string Use(string job) => JobField(job, UseField);

    public static string Profile(string profile) => ProfilesSection + ":" + profile;

    public static string Field(string profile, string field) => Profile(profile) + ":" + field;

    public static string Prices(string profile) => Field(profile, PricesField);

    public static string KeyPath(string keyName) => Section + ":" + keyName + ":" + ApiKeyField;

    public static string WorkspacePath(string keyName) => Section + ":" + keyName + ":" + WorkspaceIdField;

    // The profile a job's `Use` names, refused by name where it names none or one the profiles
    // do not hold, since a job answered by a profile other than the one it names is a job
    // nobody can say whose model wrote for.
    public static string ProfileFor(Func<string, string?> value, string job)
    {
        var use = value(Use(job));

        if (string.IsNullOrWhiteSpace(use))
        {
            throw new InvalidOperationException(
                $"'{Use(job)}' names no profile, and the {job} job needs the one word naming the profile it uses, one of " +
                $"those under '{ProfilesSection}'.");
        }

        var profile = use.Trim();

        if (string.IsNullOrWhiteSpace(value(Field(profile, ModelField))) && string.IsNullOrWhiteSpace(value(Field(profile, FormatField))))
        {
            throw new InvalidOperationException(
                $"'{Use(job)}' names the profile '{profile}', which '{ProfilesSection}' does not hold. The {job} job stops " +
                "here and no other profile answers for it.");
        }

        return profile;
    }

    // A job's settings, the key read from where its profile names it, through the two ways a
    // caller holds its configuration: the value at a key, and the values listed under one.
    public static ResearchModelSettings Resolve(Func<string, string?> value, Func<string, IEnumerable<string?>> values, string job) =>
        ResolveProfile(value, values, job, ProfileFor(value, job));

    // One named profile's settings for a job, refused by name where the profiles do not hold it.
    public static ResearchModelSettings ResolveProfile(Func<string, string?> value, Func<string, IEnumerable<string?>> values, string job, string profile)
    {
        if (string.IsNullOrWhiteSpace(value(Field(profile, ModelField))) && string.IsNullOrWhiteSpace(value(Field(profile, FormatField))))
        {
            throw new InvalidOperationException(
                $"The {job} job names the profile '{profile}', which '{ProfilesSection}' does not hold. The job stops here and " +
                "no other profile answers for it.");
        }

        var keyName = value(Field(profile, KeyField));

        return new ResearchModelSettings(
            job,
            profile,
            value(Field(profile, FormatField)),
            value(Field(profile, BaseAddressField)),
            value(Field(profile, ModelField)),
            keyName,
            string.IsNullOrWhiteSpace(keyName) ? null : value(KeyPath(keyName.Trim())),
            ResearchPricing.From(Prices(profile), value, values),
            value(Field(profile, OptionsField)),
            Whole(value, JobField(job, TimeoutField)),
            Whole(value, JobField(job, AnswerTokensField)),
            Date(value, Field(profile, RetiresField)),
            Date(value, Field(profile, RetiresReadOnField)),
            string.IsNullOrWhiteSpace(keyName) ? null : value(WorkspacePath(keyName.Trim())),
            value(Field(profile, ThinkingField)));
    }

    // A job's profile without its key, for the read surface, which states prices, peak windows
    // and a retirement date and makes no call. None where the job names no profile.
    public static ModelProfile? Describe(Func<string, string?> value, Func<string, IEnumerable<string?>> values, string job)
    {
        if (string.IsNullOrWhiteSpace(value(Use(job))))
        {
            return null;
        }

        var profile = ProfileFor(value, job);

        return new ModelProfile(
            job,
            profile,
            value(Field(profile, FormatField))?.Trim() is { Length: > 0 } format ? format : ResearchModelSettings.OpenAiFormat,
            value(Field(profile, ModelField))?.Trim() ?? string.Empty,
            ResearchPricing.From(Prices(profile), value, values),
            Date(value, Field(profile, RetiresField)),
            Date(value, Field(profile, RetiresReadOnField)));
    }

    // The prices of every profile a job's pass may call, without their keys: the one its `Use` names, then each other
    // profile its map of sections names, once each, so a pass waits for the peak windows of all of them. A profile
    // with no prices adds nothing here, and its job refuses it at startup.
    // see: Research names a profile per section as well as per job, and a Claude profile states its thinking
    public static IReadOnlyList<ResearchPricing> PricesFor(Func<string, string?> value, Func<string, IEnumerable<string?>> values, string job)
    {
        var profiles = new List<string> { ProfileFor(value, job) };

        foreach (var named in values(JobField(job, SectionsField)))
        {
            if (named?.Trim() is { Length: > 0 } profile && !profiles.Contains(profile, StringComparer.Ordinal))
            {
                profiles.Add(profile);
            }
        }

        return [.. profiles.Select(profile => ResearchPricing.From(Prices(profile), value, values)).OfType<ResearchPricing>()];
    }

    // Whether a profile's retirement date falls within the warning's reach of a day: on or
    // after the day it is thirty days before the date. A profile with no date never does.
    public static bool RetiresSoon(ModelProfile profile, DateOnly day) =>
        profile.Retires is { } retires && day >= retires.AddDays(-RetirementWarningDays);

    static int? Whole(Func<string, string?> value, string key)
    {
        var written = value(key);

        if (string.IsNullOrWhiteSpace(written))
        {
            return null;
        }

        return int.TryParse(written, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : throw new InvalidOperationException(
                $"'{key}' is '{written}', which is not a whole number above zero. It is read as written rather than " +
                "replaced by the default.");
    }

    static DateOnly? Date(Func<string, string?> value, string key)
    {
        var written = value(key);

        if (string.IsNullOrWhiteSpace(written))
        {
            return null;
        }

        return DateOnly.TryParseExact(written.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new InvalidOperationException(
                $"'{key}' is '{written}', which is not a date written as yyyy-MM-dd. A date read against the machine's " +
                "locale would be a different date on another machine.");
    }
}
