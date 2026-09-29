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

    static Func<string, string?> Value(IConfiguration configuration) => key => configuration[key];

    static Func<string, IEnumerable<string?>> Values(IConfiguration configuration) =>
        key => configuration.GetSection(key).GetChildren().Select(child => child.Value);
}
