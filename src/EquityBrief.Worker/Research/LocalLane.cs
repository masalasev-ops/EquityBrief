using System.Globalization;
using EquityBrief.Core.Providers;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Worker.Research;

// The local lane as configuration states it: the model profile the settings flag as
// the default, and the sections it holds.
//
// Read in one place so the writer is handed values rather than a configuration it
// could read something else out of, which is the seam the on-demand feeds resolve
// through.
// see: The local lane calls the one model its settings flag as the default, and a profile it cannot read is the local model unavailable
// see: The local lane is a configured list of section names, and the prose writer writes whatever the list holds
public static class LocalLane
{
    public const string SectionsKey = "EquityBrief:Models:LocalLane";

    // The file in a capture's folder that names the models its recordings were made under.
    public const string FixtureModels = "models.json";

    // The keys the lane read before each model was a profile of its own, not read where set, since a model
    // named at one of them would otherwise be silently not the one called.
    static readonly string[] Moved = ["BaseAddress", "Model", "TimeoutSeconds", "ContextTokens"];

    // The profile flagged as the default, read whole and refused where it cannot be: a key anywhere in the
    // lane, a key the lane no longer reads, no profile or not one flagged, a value missing, and a number that
    // is not one rather than read as another, because a timeout typed as "5m" says nothing the lane can do.
    public static LocalModelSettings Settings(IConfiguration configuration)
    {
        RefuseKeys(configuration);

        return Profile(configuration);
    }

    // The profile a run calls, resolved as its feeds are: on a live run the one the settings flag, and on a
    // run over a capture the one the capture's recordings were made under. A key in the settings refuses the
    // run either way, since the refusal is about the configuration whatever it is run against; profiles that
    // cannot be read leave the lane unread with why, so the night goes on without its overnight queue and a
    // pass writes its paid sections.
    public static LocalModelSettings For(IConfiguration configuration, string? source, string? fixtureFolder)
    {
        RefuseKeys(configuration);

        return FeedSource.Resolve(source, fixtureFolder, OfFixture, () => Read(configuration), "the local lane");
    }

    // The flagged profile, or the lane unread with the line saying why none could be read.
    static LocalModelSettings Read(IConfiguration configuration)
    {
        try
        {
            return Profile(configuration);
        }
        catch (InvalidOperationException unread)
        {
            return LocalModelSettings.Unread(unread.Message);
        }
    }

    static LocalModelSettings Profile(IConfiguration configuration)
    {
        foreach (var name in Moved)
        {
            var key = $"{LocalModelSettings.Section}:{name}";

            if (!string.IsNullOrWhiteSpace(configuration[key]))
            {
                throw new InvalidOperationException(
                    $"'{key}' is set, and the lane no longer reads it: each local model is a profile of its own under " +
                    $"'{LocalModelSettings.ProfilesKey}', with {name} in the profile. Move it there.");
            }
        }

        var profiles = configuration.GetSection(LocalModelSettings.ProfilesKey).GetChildren().ToArray();

        if (profiles.Length == 0)
        {
            throw new InvalidOperationException(
                $"The local lane names no model: add a profile under '{LocalModelSettings.ProfilesKey}' with its " +
                $"{LocalModelSettings.BaseAddressName}, {LocalModelSettings.ModelName}, {LocalModelSettings.TimeoutName}, " +
                $"{LocalModelSettings.ContextTokensName} and {LocalModelSettings.LoadName}, and {LocalModelSettings.IsDefaultName} true.");
        }

        var flagged = profiles.Where(profile => Flagged(profile)).Select(profile => profile.Key).ToArray();

        if (flagged.Length != 1)
        {
            throw new InvalidOperationException(
                flagged.Length == 0
                    ? $"No local model profile is flagged {LocalModelSettings.IsDefaultName} true, of {string.Join(", ", profiles.Select(profile => profile.Key))}: flag the one the lane calls."
                    : $"{flagged.Length} local model profiles are flagged {LocalModelSettings.IsDefaultName} true, {string.Join(" and ", flagged)}: the lane calls one, so flag one.");
        }

        var chosen = configuration.GetSection($"{LocalModelSettings.ProfilesKey}:{flagged[0]}");

        return new LocalModelSettings(
            flagged[0],
            chosen[LocalModelSettings.BaseAddressName] ?? string.Empty,
            chosen[LocalModelSettings.ModelName] ?? string.Empty,
            Required(configuration, LocalModelSettings.KeyOf(flagged[0], LocalModelSettings.TimeoutName)),
            Required(configuration, LocalModelSettings.KeyOf(flagged[0], LocalModelSettings.ContextTokensName)),
            Required(configuration, LocalModelSettings.KeyOf(flagged[0], LocalModelSettings.LoadName)));
    }

    // The profile a capture's recordings were made under, from the models file in its folder, so a switch
    // of the shipped default moves no recorded test.
    public static LocalModelSettings OfFixture(string folder) =>
        Settings(new ConfigurationBuilder().AddJsonFile(Path.Combine(Path.GetFullPath(folder), FixtureModels), optional: false).Build());

    // A key for this lane is refused rather than sent, at the lane or in any profile. A local endpoint that
    // authenticates is an endpoint on somebody else's machine, and the queue's zero-cost property rests on
    // this lane being free.
    static void RefuseKeys(IConfiguration configuration)
    {
        var keys = new[] { LocalModelSettings.ApiKeyKey }
            .Concat(configuration.GetSection(LocalModelSettings.ProfilesKey).GetChildren()
                .SelectMany(profile => new[] { LocalModelSettings.KeyOf(profile.Key, "ApiKey"), LocalModelSettings.KeyOf(profile.Key, "Key") }));

        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(configuration[key]))
            {
                throw new InvalidOperationException(
                    $"A key is configured at '{key}', and the local lane takes none. A local model that asks for a key " +
                    "is a model on somebody else's machine, which would put a cost on the one lane the overnight queue " +
                    "is allowed to call. Remove the key, or point this lane at the operator's own runtime.");
            }
        }
    }

    // A profile's flag, true or false as written, and absent as false.
    static bool Flagged(IConfigurationSection profile)
    {
        var value = profile[LocalModelSettings.IsDefaultName];

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return bool.TryParse(value, out var flagged)
            ? flagged
            : throw new InvalidOperationException(
                $"'{LocalModelSettings.KeyOf(profile.Key, LocalModelSettings.IsDefaultName)}' is '{value}', which is neither true nor false.");
    }

    static int Required(IConfiguration configuration, string key) =>
        Whole(configuration, key) ?? throw new InvalidOperationException(
            $"'{key}' is blank, and the local lane reads it from the settings alone. Set it in the profile.");

    // The sections, in the order configuration lists them, or this machine's
    // default where configuration lists none. Checked the way the writer checks a
    // lane, so a misspelt name refuses when the lane is read rather than when a pass
    // reaches it.
    public static IReadOnlyList<string> Sections(IConfiguration configuration)
    {
        var listed = configuration.GetSection(SectionsKey).GetChildren()
            .Select(child => child.Value ?? string.Empty)
            .ToArray();

        return ProseWriter.Checked(listed.Length == 0 ? ProseWriter.DefaultLane : listed);
    }

    static int? Whole(IConfiguration configuration, string key)
    {
        var value = configuration[key];

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : throw new InvalidOperationException(
                $"'{key}' is '{value}', which is not a whole number above zero. It is read as written rather than " +
                "as some other value, because a setting that silently became another does something other than " +
                "what the file says.");
    }
}
