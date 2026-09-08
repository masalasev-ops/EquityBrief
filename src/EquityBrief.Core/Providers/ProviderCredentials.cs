namespace EquityBrief.Core.Providers;

// Where the provider key comes from, stated once.
//
// The key lives in appsettings.Secrets.json beside appsettings.json, gitignored
// and never committed, registered before environment variables so an environment
// variable still wins. That ordering is what lets a runner override a local file
// rather than the reverse, and RUNBOOK names writing this file by hand as the
// one part of moving an installation that cannot be scripted.
//
// The key is read through here rather than at each call site so there is one
// name to change and one place that refuses a blank.
public sealed class ProviderCredentials(string apiKey)
{
    public const string ApiKeyName = "EquityBrief:Providers:Eodhd:ApiKey";

    public string ApiKey { get; } = string.IsNullOrWhiteSpace(apiKey)
        ? throw new InvalidOperationException(
            $"No provider key. Set '{ApiKeyName}' in appsettings.Secrets.json beside " +
            "appsettings.json, or in the environment. A blank key reaches the provider as an " +
            "anonymous request and comes back as a rejection that names nothing.")
        : apiKey;

    // Never rendered, because a key that can be printed is a key that reaches a
    // log, and the run log is a store this repository copies between machines.
    public override string ToString() => "ProviderCredentials(key withheld)";
}
