using EquityBrief.Core.Providers;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// The credential's name, which now lives in two places and so needs one of them
// checked against the other.
//
// RUNBOOK told an operator to write appsettings.Secrets.json by hand and never
// said what to put in it, so the first file written by hand used a name of its
// own invention and the code looked for a different one. The runbook says the
// name now, and a name stated in a document and again in code is exactly the
// shape this corpus asserts rather than trusts.
public class ProviderCredentialsTests
{
    [Fact]
    public void TheRunbookNamesTheKeyTheCodeLooksFor()
    {
        var runbook = Corpus.Read("docs/RUNBOOK.md");

        Assert.Contains(ProviderCredentials.ApiKeyName, runbook, StringComparison.Ordinal);

        // And in the file shape as well as in the table, because the operator
        // writing the file by hand needs the nesting and not only the path. The
        // colons are the path separator, so the nested form is the same name
        // spelled the way the file spells it.
        foreach (var segment in ProviderCredentials.ApiKeyName.Split(':'))
        {
            Assert.Contains($"\"{segment}\"", runbook, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ABlankKeyIsRefusedByNameRatherThanSentToTheProvider()
    {
        // A blank key reaches the provider as an anonymous request and comes
        // back as a rejection that names neither the setting nor the file. The
        // refusal has to name both, because the operator reading it is the one
        // who has to write the file.
        foreach (var blank in new[] { "", " ", "\t" })
        {
            var refusal = Assert.Throws<InvalidOperationException>(() => new ProviderCredentials(blank));

            Assert.Contains(ProviderCredentials.ApiKeyName, refusal.Message, StringComparison.Ordinal);
            Assert.Contains("appsettings.Secrets.json", refusal.Message, StringComparison.Ordinal);
        }

        // The counter-test: a key that is present is accepted, so the refusal
        // above is about blankness rather than about everything.
        Assert.Equal("abc", new ProviderCredentials("abc").ApiKey);
    }

    [Fact]
    public void TheKeyIsNeverRendered()
    {
        // A key that can be printed is a key that reaches a log, and the run log
        // is a store this repository copies between machines.
        Assert.DoesNotContain("sk-live-secret", new ProviderCredentials("sk-live-secret").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NoSecretsFileIsTrackedByTheRepository()
    {
        // The gitignore rule, asserted rather than trusted. A secrets file is
        // plaintext beside a committed one, and the operator writes it by hand
        // in the folder the suite reads fixtures from.
        var tracked = Shell.Run("git", ["ls-files"]).StandardOutput
            .Split((char)10, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.Contains("Secrets", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(tracked);
    }
}
