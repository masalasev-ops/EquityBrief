using System.Globalization;
using EquityBrief.Core.Providers;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Worker.Research;

// The local lane as configuration states it: where its model answers, which model,
// and the sections it holds.
//
// Read in one place so the writer is handed values rather than a configuration it
// could read something else out of, which is the seam the on-demand feeds resolve
// through.
// see: The local model answers at an OpenAI-compatible endpoint, and which model answers is configuration
// see: The local lane is a configured list of section names, and the prose writer writes whatever the list holds
public static class LocalLane
{
    public const string SectionsKey = "EquityBrief:Models:LocalLane";

    // The settings, with a key refused by the settings themselves and a number that
    // is not one refused here rather than read as the default, because a timeout
    // typed as "5m" that silently became three hundred seconds is a setting that
    // does something other than what it says.
    public static LocalModelSettings Settings(IConfiguration configuration) =>
        new(
            configuration[LocalModelSettings.BaseAddressKey],
            configuration[LocalModelSettings.ModelKey],
            Whole(configuration, LocalModelSettings.TimeoutKey),
            Whole(configuration, LocalModelSettings.ContextTokensKey),
            configuration[LocalModelSettings.ApiKeyKey]);

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
                "replaced by the default, because a setting that silently became another value does something " +
                "other than what the file says.");
    }
}
