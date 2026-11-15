using EquityBrief.Api.Reading;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EquityBrief.Tests.Harness;

// The read surface hosted for the suite, its client naming a person's browser as a press on the page does, since from the
// second 23.10 correction of 2026-11-13 the read surface refuses a write from anything else.
// see: A write reaches the store only from a person's press on a page, and the read surface refuses one from a script or a headless browser
public abstract class PageFactory : WebApplicationFactory<ReadApi>
{
    public const string PageAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", PageAgent);
    }
}
