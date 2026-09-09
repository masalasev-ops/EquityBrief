using System.Text.RegularExpressions;

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
    public override string ToString() => Withheld;

    public const string Withheld = "ProviderCredentials(key withheld)";

    // Anything about to be written down, with the key and any request address
    // taken out of it.
    //
    // This provider takes its key as a query parameter, so the key is one
    // substring away from every message a failed request produces and from every
    // URL that could reach a log line, a run log row or a stack trace. Scrubbing
    // at the point the text leaves the feed is the only place that covers all
    // three, because the alternative is remembering at each of them.
    //
    // The address goes as well as the key, and not only because the rule says no
    // request URL reaches a log. An address is a key that has not been
    // recognised yet: a message quoting a URL this process did not build, or a
    // provider that renamed the parameter, would both carry a live key past a
    // scrub that only looked for the value this object happens to hold.
    public string Redact(string text) =>
        string.IsNullOrEmpty(text)
            ? text
            : Addresses.Replace(text.Replace(ApiKey, KeyWithheld, StringComparison.Ordinal), AddressWithheld);

    public const string KeyWithheld = "<key withheld>";

    public const string AddressWithheld = "<address withheld>";

    static readonly Regex Addresses = new(@"[a-zA-Z][a-zA-Z0-9+.\-]*://\S*", RegexOptions.Compiled);
}
