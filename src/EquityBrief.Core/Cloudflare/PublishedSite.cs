namespace EquityBrief.Core.Cloudflare;

// What the read-only copy of the site on Cloudflare Pages is called and where it lives, stated once for the read surface
// that writes it and the worker that checks and uploads it: the verb both are started with, the folder under the data root
// the copy is written into, the report on it written beside that folder and never inside it, the Cloudflare Pages project it
// is uploaded to, and the two settings in the secrets file the upload is gated on. Everything the copy is made by sits under
// a folder of this name in each project, apart from the read surface the operator runs.
// see: A read-only copy of the site is published to Cloudflare Pages from the read surface's own screens with no control, a link it does not hold drawn as plain text
public static class PublishedSite
{
    public const string Verb = "publish-site";

    public const string Folder = "site";

    public const string ReportFile = "site-report.txt";

    // The file in the copy the host reads its headers from, which is sent beside the deployment rather than as a page.
    public const string HeadersFile = "_headers";

    // The project the copy is uploaded to, and the operator's other site, which no upload ever names.
    public const string Project = "equitybrief";

    public const string NeverProject = "premarketdesk";

    // The branch a deployment is made on, which is the project's production branch.
    public const string Branch = "main";

    public const string TokenKey = "EquityBrief:Providers:CloudFlare:API_TOKEN";

    public const string AccountKey = "EquityBrief:Providers:CloudFlare:ACCOUNT_ID";

    // The flag that uploads the copy once its check finds nothing to refuse; without it the verb writes the copy and its
    // report and sends nothing.
    public const string Upload = "--upload";

    // What every page of the copy names itself, so it is never taken for the read surface the operator runs.
    public const string Name = "EquityBrief, read-only copy";
}
