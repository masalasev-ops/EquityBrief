using System.Reflection;

namespace EquityBrief.Core.Providers;

// What the filings archive requires a caller to declare about itself.
//
// The archive is the first provider here that refuses a request on a transport
// rule rather than on a credential: it answers a request that names no user agent
// with a refusal, and its fair-access policy asks that the agent carry contact
// details. So this is a credential path in shape and not in kind, and it is built
// like one (see: The archive declares a contact in its user agent, and a blank one refuses at startup).
//
// The contact is configuration, in the gitignored secrets file beside the provider
// keys, for two reasons the operator stated. A blank or absent contact refuses at
// startup rather than at the first request, which is the shape the credential path
// has had since 2.1: a run that got as far as the archive and came back refused
// would look like the archive being down. And a value identifying a person sitting
// in that file is one more thing that must never reach a captured fixture or a
// committed manifest, which is why the manifest's credential scan covers it and
// why a dedicated address narrows what a leak would be.
//
// The header is composed here rather than written at a call site, so the tool
// name, the version and the contact have one spelling between them.
public sealed class ArchiveAgent(string contact)
{
    public const string ContactName = "EquityBrief:Providers:SecEdgar:Contact";

    // The tool's own name, which the archive asks callers to identify themselves
    // by. Not abbreviated, for the reason nothing here is.
    public const string Tool = "EquityBrief";

    public string Contact { get; } = string.IsNullOrWhiteSpace(contact)
        ? throw new InvalidOperationException(
            $"No archive contact. Set '{ContactName}' in appsettings.Secrets.json beside " +
            "appsettings.json, or in the environment. The archive refuses a request that names no " +
            "user agent and its fair-access policy asks that the agent carry contact details, so a " +
            "blank one is a request that comes back refused for a reason nothing in the run would " +
            "name.")
        : contact.Trim();

    // The version, read off the assembly rather than stated, because a version
    // written down here would be a second place one fact lives and it would go
    // stale on the first release that did not think to look.
    public static string Version =>
        typeof(ArchiveAgent).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            is { Length: > 0 } informational
            ? informational.Split('+')[0]
            : typeof(ArchiveAgent).Assembly.GetName().Version?.ToString() ?? "0.0";

    // The header the archive reads, in the form its policy asks for: a name, a
    // version, and a contact in parentheses.
    public string Declared => $"{Tool}/{Version} ({Contact})";

    // Never rendered whole, for the reason the credentials are not: a contact that
    // can be printed is one that reaches a log, and the run log is a store this
    // repository copies between machines. The tool and the version are safe to say
    // and the contact is the part withheld, because a message saying only that a
    // header was present is a message nobody can act on.
    public override string ToString() => $"ArchiveAgent({Tool}/{Version}, contact withheld)";

    // Anything about to be written down, with the contact taken out of it.
    //
    // The contact travels in a header rather than in a query string, so it does not
    // reach a URL the way the provider key does. It still reaches a message: a
    // transport failure can quote the request it was making, headers included, and
    // a captured response that echoed the agent back would carry the address into
    // the repository. Both are covered here rather than remembered at each site.
    public string Redact(string text) =>
        string.IsNullOrEmpty(text) ? text : text.Replace(Contact, ContactWithheld, StringComparison.OrdinalIgnoreCase);

    public const string ContactWithheld = "<contact withheld>";
}
