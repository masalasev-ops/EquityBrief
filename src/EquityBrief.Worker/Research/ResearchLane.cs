using System.Globalization;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Spending;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Worker.Research;

// The paid lane as configuration states it: the wire format, where the provider
// answers, which model, the options it is asked with, the key, the prices, and the caps
// its spend is held to.
//
// Read in one place, as the local lane's settings are, and read at startup, so a blank
// key, a model with no price or a format nothing implements refuses before any command
// runs rather than at the first pass. Nothing here names a provider: the shipped
// configuration does, and switching model is a change to that file and the secrets file.
// see: The research model is named only in configuration, and a call is priced at the configured rates its own timestamp falls in
public static class ResearchLane
{
    public static ResearchModelSettings Settings(IConfiguration configuration) =>
        new(
            configuration[ResearchModelSettings.FormatKey],
            configuration[ResearchModelSettings.BaseAddressKey],
            configuration[ResearchModelSettings.ModelKey],
            configuration[ResearchModelSettings.ApiKeyKey],
            Pricing(configuration),
            configuration[ResearchModelSettings.OptionsKey],
            Whole(configuration, ResearchModelSettings.TimeoutKey),
            Whole(configuration, ResearchModelSettings.AnswerTokensKey));

    public static SpendCaps Caps(IConfiguration configuration) =>
        SpendCaps.From(configuration[SpendCaps.DayKey], configuration[SpendCaps.MonthKey]);

    // The prices, read by the one reader the read surface's queue page reads them with.
    static ResearchPricing? Pricing(IConfiguration configuration) =>
        ResearchPricing.From(
            key => configuration[key],
            key => configuration.GetSection(key).GetChildren().Select(child => child.Value));

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
