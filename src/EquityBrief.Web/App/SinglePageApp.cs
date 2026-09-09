using EquityBrief.Core.Components;

namespace EquityBrief.Web.App;

// The shell the browser loads once, and the routes it answers.
//
// Section 15.4 puts the shell and the marks on the server and the routing in
// the page. So this writes the shell and nothing else: the marks arrive already
// drawn, and the only thing the browser does is ask for the one belonging to
// the route it is on. A page that assembled a mark from values would be the
// second renderer the marks decision exists to prevent.
// see: Marks are defined once and every screen draws from that list
// see: A screen reads and renders, and computes nothing
//
// At 1.3 it answers one route, a name's chart. The other four screens arrive
// with the data behind them, and this file is where they are added.
public sealed class SinglePageApp : IComponent
{
    // It reads the read API and touches no store, which is its catalogue row
    // and its blank matrix cells.
    public static ComponentAccess Access => ComponentAccess.Nothing;

    public const string NameRoute = "#/name/";

    // The hash route, so one document serves every screen and the browser never
    // asks the server for a page it already has.
    //
    // The script is four lines and renders nothing. It reads the hash and puts
    // the server's own markup where it goes, which is what keeps the rule that
    // a mark needs no script to draw.
    public string Shell(string title) =>
        $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{Escaped(title)}}</title>
        <style>
          :root { --ink: #1c1c1c; --muted: #6a6a6a; --rule: #d8d8d8; --panel: #f6f6f4; }
          body { margin: 0; padding: 24px; font: 14px/1.5 "Segoe UI", system-ui, sans-serif; color: var(--ink); }
          h1 { font-size: 19px; margin: 0 0 4px; }
          p.route { color: var(--muted); margin: 0 0 18px; }
          .degraded { color: var(--muted); }
        </style>
        </head>
        <body>
        <h1>{{Escaped(title)}}</h1>
        <p class="route">Open a name at <code>#/name/AAPL</code>.</p>
        <main id="screen"></main>
        <script>
        async function show() {
          const hash = location.hash;
          const screen = document.getElementById('screen');
          if (!hash.startsWith('{{NameRoute}}')) { screen.innerHTML = ''; return; }
          const ticker = encodeURIComponent(hash.slice('{{NameRoute}}'.length));
          const response = await fetch('/marks/level-chart/' + ticker);
          screen.innerHTML = await response.text();
        }
        addEventListener('hashchange', show);
        show();
        </script>
        </body>
        </html>
        """;

    static string Escaped(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
