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

    // The prices, or none where configuration states none, which the settings refuse by
    // name. Peak hours are written as "01-04", a UTC start hour and end hour; days by
    // their English names.
    static ResearchPricing? Pricing(IConfiguration configuration)
    {
        var hit = configuration[ResearchModelSettings.CacheHitKey];
        var miss = configuration[ResearchModelSettings.CacheMissKey];
        var output = configuration[ResearchModelSettings.OutputKey];

        if (string.IsNullOrWhiteSpace(miss) && string.IsNullOrWhiteSpace(output) && string.IsNullOrWhiteSpace(hit))
        {
            return null;
        }

        var hours = configuration.GetSection(ResearchModelSettings.PeakHoursKey).GetChildren()
            .Select(child => Window(child.Value ?? string.Empty))
            .ToArray();

        var days = configuration.GetSection(ResearchModelSettings.PeakDaysKey).GetChildren()
            .Select(child => Enum.TryParse<DayOfWeek>(child.Value, ignoreCase: true, out var day)
                ? day
                : throw new InvalidOperationException($"'{ResearchModelSettings.PeakDaysKey}' names '{child.Value}', which is not a day of the week."))
            .ToArray();

        var multiple = configuration[ResearchModelSettings.PeakMultipleKey];

        return new ResearchPricing(
            Money(hit, ResearchModelSettings.CacheHitKey) ?? 0m,
            Money(miss, ResearchModelSettings.CacheMissKey) ?? 0m,
            Money(output, ResearchModelSettings.OutputKey) ?? 0m,
            hours,
            days,
            Money(multiple, ResearchModelSettings.PeakMultipleKey) ?? 1m);
    }

    static (int From, int To) Window(string value)
    {
        var parts = value.Split('-');

        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var from)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var to)
                ? (from, to)
                : throw new InvalidOperationException(
                    $"'{ResearchModelSettings.PeakHoursKey}' holds '{value}', and a peak window is written as a UTC start hour and end hour, as 01-04.");
    }

    static decimal? Money(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw new InvalidOperationException(
                $"'{key}' is '{value}', which is not an amount written with a decimal point. It is read as written rather than " +
                "replaced by a default.");
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
                "replaced by the default.");
    }
}
