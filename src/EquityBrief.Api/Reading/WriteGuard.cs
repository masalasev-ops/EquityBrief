namespace EquityBrief.Api.Reading;

// From the second 23.10 correction of 2026-11-13 the read surface writes the store only on a person's press on its page: a
// request that writes is refused unless it names a browser a person uses, so a read-back, a sweep or a test run against it
// that sends one, from a script, a headless browser or with no name at all, writes nothing.
// see: A write reaches the store only from a person's press on a page, and the read surface refuses one from a script or a headless browser
public static class WriteGuard
{
    public const string Refusal =
        "<p class=\"card-refused\" data-refused=\"script\">Nothing was written: only a person's press on this tool's page writes, and this request came from a script, a headless browser or nothing that names a browser.</p>";

    // The names a script, a command line, a library or a browser driven without a window gives a request, read in any case.
    static readonly string[] Scripted =
    [
        "headless", "python", "curl", "wget", "powershell", "httpie", "node", "undici", "axios", "go-http-client", "okhttp",
        "java/", "libwww", "phantomjs", "playwright", "puppeteer", "selenium", "aiohttp", "httpx", "postman", "insomnia",
    ];

    // Whether a request only reads: a page, its parts and the browser's asking what a route allows.
    public static bool Reads(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    // Whether a request's name for what sent it is a browser a person uses: it opens as every browser's does and names none of
    // the scripts, libraries or windowless browsers above.
    public static bool FromAPersonsBrowser(string? agent) =>
        agent is { Length: > 0 } named
        && named.StartsWith("Mozilla/", StringComparison.Ordinal)
        && !Scripted.Any(name => named.Contains(name, StringComparison.OrdinalIgnoreCase));
}
