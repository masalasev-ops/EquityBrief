using System.Globalization;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Spending;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Worker.Research;

// The paid lane as configuration states it: which provider and model answer, in which
// mode, with which key, and the caps its spend is held to.
//
// Read in one place, as the local lane's settings are, and read at startup, so a blank
// key or a provider nothing implements refuses before any command runs rather than at
// the first pass.
// see: The research model is one interface with an implementation per wire format, chosen by configuration and never falling back
public static class ResearchLane
{
    public static ResearchModelSettings Settings(IConfiguration configuration) =>
        new(
            configuration[ResearchModelSettings.ProviderKey],
            configuration[ResearchModelSettings.ModelKey],
            configuration[ResearchModelSettings.ThinkingKey],
            Whole(configuration, ResearchModelSettings.TimeoutKey),
            configuration[ResearchModelSettings.ApiKeyName]);

    public static SpendCaps Caps(IConfiguration configuration) =>
        SpendCaps.From(configuration[SpendCaps.DayKey], configuration[SpendCaps.MonthKey]);

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
                "replaced by the default.");
    }
}
