using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Core.Components;

namespace EquityBrief.Web.App;

// One name's report as a file to hand to someone: section 15.4's second surface.
//
// It is the name screen's own region, composed by the same code from the same reads, put in a
// document of its own with nothing that needs the application to read it: the styles inline,
// no router, and every disclosure open. It opens in the light palette whatever the machine
// prefers, with one small script whose only work is the press switching it to the dark palette
// and back, so every figure draws with scripts off. The region is handed in already composed
// and nothing here draws a mark or reads a store, so the file carries the pictures and the
// figures the page does and no value the store does not; the masthead's price is the one the
// page drew at the export, stamped with the instant.
// see: A single report is exported as one self-contained file in the page's design, opening light with a dark toggle and stamped with the price it was exported at
// see: Marks are defined once and every screen draws from that list
// see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
public sealed partial class ReportExporter : IComponent
{
    // It reads the read API and touches no store; what it writes is a file the person
    // exporting chooses where to keep, which is its catalogue row and its blank matrix cells.
    public static ComponentAccess Access => ComponentAccess.Nothing;

    // Where the name page's link asks for the file.
    public const string Route = "/exports/name/";

    // The name the file is offered under: the name, and the newest session its figures are
    // from, so two exports of one name on different nights are two files.
    public static string FileName(string ticker, DateOnly? asOf) =>
        "EquityBrief-" + Safe(ticker) + (Day(asOf) is { } session ? "-" + session : string.Empty) + ".html";

    // The link the name page carries, outside the region the file holds, so the file does not
    // carry a link to an address only the application answers.
    public static string Link(string ticker) =>
        $"<p class=\"export\"><a class=\"export-report\" href=\"{Route}{Uri.EscapeDataString(ticker)}\" download>Export this report as a file</a></p>";

    public string Document(string ticker, DateOnly? asOf, string region, DateTimeOffset? exportedAt = null)
    {
        var session = Day(asOf);
        var title = Escaped("EquityBrief: " + ticker + (session is null ? string.Empty : ", " + session));
        var held = session is null ? string.Empty : " on the session of " + session;
        var stamped = exportedAt is { } at
            ? ", exported at " + TimeZoneInfo.ConvertTime(at, NewYork).ToString("HH:mm", CultureInfo.InvariantCulture) + " ET on "
                + TimeZoneInfo.ConvertTime(at, NewYork).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ", the masthead's price as it stood then"
            : string.Empty;
        var instant = exportedAt is { } when ? when.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : string.Empty;

        // The app's own stylesheet, so the file reads as the page it came from, opening in the light palette.
        return $$$"""
            <!doctype html>
            <html lang="en" data-eb-theme="light">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{{title}}}</title>
            <style>
            {{{Stylesheet.Css}}}
            </style>
            </head>
            <body data-exported="report" data-ticker="{{{Escaped(ticker)}}}" data-exported-at="{{{instant}}}">
            <div class="wrap exported">
            <h1>{{{title}}}</h1>
            <p class="exported">One name's report, exported from EquityBrief with every figure as the store held it{{{held}}}{{{stamped}}}. <button type="button" class="theme-toggle" data-theme-toggle aria-pressed="false">Dark palette</button></p>
            {{{Stamped(Unrouted(Opened(region)))}}}
            </div>
            <script>{{{ThemeScript}}}</script>
            </body>
            </html>
            """;
    }

    // The file's one script: the press switches the palette, and nothing else runs.
    public const string ThemeScript =
        "document.querySelector('[data-theme-toggle]').addEventListener('click', function () { var root = document.documentElement; var dark = root.getAttribute('data-eb-theme') !== 'dark'; root.setAttribute('data-eb-theme', dark ? 'dark' : 'light'); this.textContent = dark ? 'Light palette' : 'Dark palette'; this.setAttribute('aria-pressed', dark ? 'true' : 'false'); });";

    // A delayed quote the page drew is marked as the price at the export rather than as live, since a file does not move.
    public static string Stamped(string html) =>
        html.Replace("<span class=\"pill live\">LIVE</span>", "<span class=\"pill live\">AT EXPORT</span>", StringComparison.Ordinal);

    static readonly TimeZoneInfo NewYork = EquityBrief.Core.Time.SessionZones.ResolveSessionZone(EquityBrief.Core.Time.SessionZones.UnitedStates);

    // Every disclosure open, since a file handed to someone has nobody to press one.
    public static string Opened(string html) => Closed().Replace(html, "$0 open");

    // Without the router: a link to one of the application's routes is kept as its words, since
    // a file has no application to route it, and a link to a document the report cites is kept.
    public static string Unrouted(string html) => Routed().Replace(html, "<span class=\"unrouted\">$1</span>");

    static string? Day(DateOnly? day) =>
        day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Safe(string ticker) => Regex.Replace(ticker, "[^A-Za-z0-9.-]", "_");

    [GeneratedRegex(@"<details(?![^>]*\bopen\b)", RegexOptions.IgnoreCase)]
    private static partial Regex Closed();

    [GeneratedRegex("<a (?:[^>]*? )?href=\"#/[^\"]*\"[^>]*>(.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex Routed();

    static string Escaped(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
