using EquityBrief.Core.Providers;

namespace EquityBrief.Tests.Providers;

// The archive's transport rule, which is the first one here that refuses a request
// on something other than a credential.
//
// The archive answers a request naming no user agent with a refusal, and its
// fair-access policy asks that the agent carry contact details. So the contact is
// configuration and a blank one refuses at startup rather than at the first
// request, which is the shape the credential path has had since 2.1: a run that
// got as far as the archive and came back refused would read as the archive being
// down.
public class ArchiveAgentTests
{
    const string Contact = "filings@equitybrief.example";

    [Fact]
    public void ABlankContactRefusesAtStartupAndNamesTheSettingToSet()
    {
        // Every blank a configuration file can hold, because a setting present and
        // empty and a setting absent arrive here as the same thing and neither may
        // reach the archive.
        foreach (var blank in new[] { "", " ", "\t", "\n" })
        {
            var refusal = Assert.Throws<InvalidOperationException>(() => new ArchiveAgent(blank));

            Assert.Contains(ArchiveAgent.ContactName, refusal.Message, StringComparison.Ordinal);
            Assert.Contains("appsettings.Secrets.json", refusal.Message, StringComparison.Ordinal);
        }

        // And the refusal says why it is a refusal rather than a default, which is
        // the half a reader acts on: the archive would answer, and the answer would
        // be a rejection naming nothing about this caller.
        var stated = Assert.Throws<InvalidOperationException>(() => new ArchiveAgent(string.Empty));

        Assert.Contains("fair-access", stated.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeaderCarriesTheToolTheVersionAndTheContact()
    {
        var agent = new ArchiveAgent(Contact);

        Assert.Equal($"EquityBrief/{ArchiveAgent.Version} ({Contact})", agent.Declared);

        // The name is not abbreviated, which is this repository's rule about its own
        // name and is what the archive reads to identify a caller.
        Assert.StartsWith("EquityBrief/", agent.Declared, StringComparison.Ordinal);
        Assert.Contains(Contact, agent.Declared, StringComparison.Ordinal);

        // The version is read off the assembly rather than stated, so this asserts
        // that something was read rather than a figure that would need pinning.
        Assert.NotEmpty(ArchiveAgent.Version);
        Assert.DoesNotContain(" ", ArchiveAgent.Version);
    }

    [Fact]
    public void TheContactIsTrimmedBecauseASettingReadFromAFileCarriesWhateverWhitespaceTheFileHad()
    {
        Assert.Equal(Contact, new ArchiveAgent("  " + Contact + "  ").Contact);
        Assert.Equal($"EquityBrief/{ArchiveAgent.Version} ({Contact})", new ArchiveAgent("\t" + Contact).Declared);
    }

    [Fact]
    public void TheContactIsNeverRenderedAndIsScrubbedFromAnythingWrittenDown()
    {
        var agent = new ArchiveAgent(Contact);

        // Rendered, the object says the tool and the version and withholds the
        // contact, because a message saying only that a header was present is one
        // nobody can act on and a message quoting the contact is one that reaches a
        // log.
        Assert.DoesNotContain(Contact, agent.ToString(), StringComparison.Ordinal);
        Assert.Contains("contact withheld", agent.ToString(), StringComparison.Ordinal);
        Assert.Contains(ArchiveAgent.Tool, agent.ToString(), StringComparison.Ordinal);

        // And scrubbed out of text on its way anywhere, which is what covers a
        // transport failure quoting the request it was making, headers included.
        var quoted = $"GET /Archives/edgar failed, agent EquityBrief/1.0 ({Contact})";

        Assert.DoesNotContain(Contact, agent.Redact(quoted), StringComparison.Ordinal);
        Assert.Contains(ArchiveAgent.ContactWithheld, agent.Redact(quoted), StringComparison.Ordinal);

        // Case-insensitively, because an address echoed back by a service is an
        // address that may come back in another case than it went out in.
        Assert.DoesNotContain(
            Contact,
            agent.Redact($"agent ({Contact.ToUpperInvariant()})"),
            StringComparison.OrdinalIgnoreCase);

        // Text holding no contact is handed back as it was, so the scrub is not a
        // second thing that can change a message.
        Assert.Equal("nothing to scrub", agent.Redact("nothing to scrub"));
        Assert.Equal(string.Empty, agent.Redact(string.Empty));
    }
}
