using System.Globalization;
using EquityBrief.Core.Providers;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Worker.News;

// The news labeller's own limits, as configuration states them: the most it spends in a UTC month, in
// dollars, and how long one run may take. Both are section 17's proposals until its first twenty nights
// settle them.
// see: The news labeller is a process of its own the night starts after the close, and its calls and its spend are its own
public sealed record NewsLimits(decimal MonthLimit, TimeSpan TimeLimit)
{
    public const decimal DefaultMonthLimit = 5m;
    public const int DefaultTimeLimitMinutes = 20;

    public static NewsLimits Default { get; } = new(DefaultMonthLimit, TimeSpan.FromMinutes(DefaultTimeLimitMinutes));
}

// The news job as configuration states it: the profile its `Use` names, resolved as the research job's is,
// and its limits, read in one place and at startup so a blank key or a limit that is not a number refuses
// the run before any call.
// see: A paid job names its model profile in one word the operator switches, and a profile is priced at its configured rates at its call's own timestamp
public static class NewsLane
{
    public static ResearchModelSettings Settings(IConfiguration configuration) =>
        ModelProfiles.Resolve(Value(configuration), Values(configuration), ModelProfiles.NewsJob);

    public static ModelProfile? Profile(IConfiguration configuration) =>
        ModelProfiles.Describe(Value(configuration), Values(configuration), ModelProfiles.NewsJob);

    public static NewsLimits Limits(IConfiguration configuration)
    {
        var month = configuration[ModelProfiles.JobField(ModelProfiles.NewsJob, ModelProfiles.MonthLimitField)];
        var minutes = configuration[ModelProfiles.JobField(ModelProfiles.NewsJob, ModelProfiles.TimeLimitField)];

        return new NewsLimits(
            string.IsNullOrWhiteSpace(month) ? NewsLimits.DefaultMonthLimit : Money(month, ModelProfiles.JobField(ModelProfiles.NewsJob, ModelProfiles.MonthLimitField)),
            TimeSpan.FromMinutes(string.IsNullOrWhiteSpace(minutes) ? NewsLimits.DefaultTimeLimitMinutes : Whole(minutes, ModelProfiles.JobField(ModelProfiles.NewsJob, ModelProfiles.TimeLimitField))));
    }

    static decimal Money(string written, string key) =>
        decimal.TryParse(written.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && amount > 0
            ? amount
            : throw new InvalidOperationException($"'{key}' is '{written}', which is not an amount of money above zero, read as written and never replaced by the default.");

    static int Whole(string written, string key) =>
        int.TryParse(written.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : throw new InvalidOperationException($"'{key}' is '{written}', which is not a whole number of minutes above zero, read as written and never replaced by the default.");

    static Func<string, string?> Value(IConfiguration configuration) => key => configuration[key];

    static Func<string, IEnumerable<string?>> Values(IConfiguration configuration) =>
        key => configuration.GetSection(key).GetChildren().Select(child => child.Value);
}
