namespace EquityBrief.Web.App;

// The one stylesheet, which the app's shell and the exported report both carry, so a
// report handed to someone reads as the page it came from.
// see: A single report is exported as one self-contained file in the page's design, opening light with a dark toggle and stamped with the price it was exported at
//
// Every colour is a token, and the comment at its head is the rule each token is held to.
// The marks draw with the tokens' names rather than with values, so both palettes restyle
// every picture without a mark knowing which one is showing.
// see: Green means support or a gain and red a stop or a loss, with orange for resistance
// see: Each setup family has a hue of its own drawn as a mark and a tint beside its word, the Fundamentals family's among them
// see: Not yet measured is drawn as a dashed outline, never as a pale value
public static class Stylesheet
{
    public const string Css = """
/* COLOUR RULES. Every token is one of these kinds; none of them carries a meaning not written here.
   --sup, --sup-fill, --sup-ink   The GREEN: a level BELOW the price, the part of a page that buys, and every figure
                 that rose. As text it is --sup-ink, at 4.5 to 1 or more on every ground.
   --res, --res-fill, --res-ink   The ORANGE: a level ABOVE the price and the part of a page that sells. As text it
                 is --res-ink.
   --up, --up-fill      A figure that ROSE, in the green, its sign always written beside it, on every page.
   --down, --down-fill  A figure that FELL and a stop, in the red, its sign or its word always written beside it.
   --warn, --warn-fill  The AMBER: caution, a pick's card's warning and waiting, the word always written beside it.
   --dash-ink    The stroke of a dashed outline, which always means not yet measured or not stored.
                 A neutral grey: it is never a fill, and it is never used without the dash.
   --accent      Neutral slate. The top rule of a section computed tonight, the computed badge, controls, and the
                 pill of a live rule, ink on solid slate.
   --research    Neutral plum-grey. The top rule and dateline of a section written by research or taken
                 from a filing, which may be weeks old.
   --s1 to --s4  Four steps of one neutral hue, for more or less.
   --stat, --stat-2, --stat-fill   Status: a night that finished or is running, and the Run page's pictures
                 of counts. Two steps of one blue. Never a level and never a pill.
   --wait, --wait-fill   Waiting: a night not run yet and a provisional rule's pill, in the amber.
   --fail, --fail-fill   The RED: a night that stopped, was left unfinished or never ran, a loss and a stop. The night's
                 state is always in words beside it. The pill of a report gone stale or a monthly run that stopped is
                 the accent's ink on solid red, its word inside it.
   --fam-<family>, --fam-<family>-tint   A setup family's own hue, drawn as a MARK alone, a card's or a tile's edge, a
                 tag's swatch or an account's line, at 3 to 1 or more on every ground and 15 or more in OKLab times a
                 hundred from the green, the orange, the red and each other; and its tint behind ink text on its tags,
                 tiles and card headers. Never the colour of text, and always beside the family's word.
   The rest are paper and ink: grounds, text, hairlines, shadow. */
:root{
 --page:#f1efe9; --surface:#fbfaf6; --plot:#f6f4ee; --ink:#1d1f22; --ink-2:#43464b; --soft:#5f6369; --hair:#dcd8ce; --hair-2:#c9c4b7;
 --accent:#4d5869; --accent-ink:#f7f6f2; --research:#6e5d72; --research-ink:#5a4a5e;
 --s1:#e4e1d9; --s2:#bab5a9; --s3:#7f7a6f; --s4:#2f2f2d;
 --sup:#2e7a53; --sup-fill:rgba(46,122,83,.12); --sup-ink:#1f5b3c;
 --res:#bd631d; --res-fill:rgba(189,99,29,.12); --res-ink:#8a4511;
 --up:#1f5b3c; --up-fill:rgba(46,122,83,.12); --down:#9e2f2a; --down-fill:rgba(178,58,52,.1); --warn:#6e5a00; --warn-fill:rgba(110,90,0,.12);
 --dash-ink:#6a6d71; --shadow:0 1px 2px rgba(40,34,20,.06),0 2px 8px rgba(40,34,20,.04);
 --stat:#2f6db0; --stat-2:#8db4de; --stat-fill:rgba(47,109,176,.12);
 --wait:#6e5a00; --wait-fill:rgba(110,90,0,.12); --fail:#b23a34; --fail-fill:rgba(178,58,52,.1);
 --fam-pullback:#084e6c; --fam-pullback-tint:rgba(8,78,108,.16); --fam-breakout:#70639e; --fam-breakout-tint:rgba(112,99,158,.16);
 --fam-drift:#663d05; --fam-drift-tint:rgba(102,61,5,.16); --fam-fundamentals:#274fe7; --fam-fundamentals-tint:rgba(39,79,231,.16);
 --fam-heavyweight:#851473; --fam-heavyweight-tint:rgba(133,20,115,.16); --fam-dip:#e000e8; --fam-dip-tint:rgba(224,0,232,.16);
 --fam-quality:#0c8ee8; --fam-quality-tint:rgba(12,142,232,.16); --fam-improving:#fc3484; --fam-improving-tint:rgba(252,52,132,.16);
 --serif:Charter,"Iowan Old Style",Georgia,"Times New Roman",serif;
 --sans:system-ui,-apple-system,"Segoe UI",Roboto,"Helvetica Neue",sans-serif;
 /* The column, at its widest: the widest picture a screen draws, with the card's side padding
    and the page's gutter around it. Wider than that and a line of prose runs past what an eye
    follows; narrower and the chart and the profile beside it are read through a scroll box. */
 --column:1700px; --gutter:20px;
 color-scheme:light;
}
@media (prefers-color-scheme: dark){ :root:not([data-theme='light']):not([data-eb-theme='light']){
 --page:#121416; --surface:#1a1d20; --plot:#16191b; --ink:#e8e5dd; --ink-2:#c3c0b8; --soft:#9ea3a9; --hair:#2d3237; --hair-2:#3d434a;
 --accent:#8c98aa; --accent-ink:#121416; --research:#a795ab; --research-ink:#c4b3c8;
 --s1:#262a2e; --s2:#4b5157; --s3:#868c92; --s4:#dcd8cf;
 --sup:#6fc08f; --sup-fill:rgba(111,192,143,.13); --sup-ink:#93d4ab;
 --res:#eba062; --res-fill:rgba(235,160,98,.13); --res-ink:#f2bb8c;
 --up:#93d4ab; --up-fill:rgba(111,192,143,.13); --down:#f49089; --down-fill:rgba(229,83,75,.14); --warn:#efe08a; --warn-fill:rgba(239,224,138,.13);
 --dash-ink:#a3a8ad; --shadow:0 1px 2px rgba(0,0,0,.35); color-scheme:dark;
 --stat:#6aa9e8; --stat-2:#3f7fc0; --stat-fill:rgba(106,169,232,.14);
 --wait:#efe08a; --wait-fill:rgba(239,224,138,.13); --fail:#e5534b; --fail-fill:rgba(229,83,75,.14);
 --fam-pullback:#4097bd; --fam-pullback-tint:rgba(64,151,189,.18); --fam-breakout:#9d58e9; --fam-breakout-tint:rgba(157,88,233,.18);
 --fam-drift:#90693e; --fam-drift-tint:rgba(144,105,62,.18); --fam-fundamentals:#8ec4fc; --fam-fundamentals-tint:rgba(142,196,252,.18);
 --fam-heavyweight:#f478dc; --fam-heavyweight-tint:rgba(244,120,220,.18); --fam-dip:#d8009c; --fam-dip-tint:rgba(216,0,156,.18);
 --fam-quality:#0058f8; --fam-quality-tint:rgba(0,88,248,.18); --fam-improving:#10fc78; --fam-improving-tint:rgba(16,252,120,.18);
}}
:root[data-theme='dark']:not([data-eb-theme='light']),:root[data-eb-theme='dark']{
 --page:#121416; --surface:#1a1d20; --plot:#16191b; --ink:#e8e5dd; --ink-2:#c3c0b8; --soft:#9ea3a9; --hair:#2d3237; --hair-2:#3d434a;
 --accent:#8c98aa; --accent-ink:#121416; --research:#a795ab; --research-ink:#c4b3c8;
 --s1:#262a2e; --s2:#4b5157; --s3:#868c92; --s4:#dcd8cf;
 --sup:#6fc08f; --sup-fill:rgba(111,192,143,.13); --sup-ink:#93d4ab;
 --res:#eba062; --res-fill:rgba(235,160,98,.13); --res-ink:#f2bb8c;
 --up:#93d4ab; --up-fill:rgba(111,192,143,.13); --down:#f49089; --down-fill:rgba(229,83,75,.14); --warn:#efe08a; --warn-fill:rgba(239,224,138,.13);
 --dash-ink:#a3a8ad; --shadow:0 1px 2px rgba(0,0,0,.35); color-scheme:dark;
 --stat:#6aa9e8; --stat-2:#3f7fc0; --stat-fill:rgba(106,169,232,.14);
 --wait:#efe08a; --wait-fill:rgba(239,224,138,.13); --fail:#e5534b; --fail-fill:rgba(229,83,75,.14);
 --fam-pullback:#4097bd; --fam-pullback-tint:rgba(64,151,189,.18); --fam-breakout:#9d58e9; --fam-breakout-tint:rgba(157,88,233,.18);
 --fam-drift:#90693e; --fam-drift-tint:rgba(144,105,62,.18); --fam-fundamentals:#8ec4fc; --fam-fundamentals-tint:rgba(142,196,252,.18);
 --fam-heavyweight:#f478dc; --fam-heavyweight-tint:rgba(244,120,220,.18); --fam-dip:#d8009c; --fam-dip-tint:rgba(216,0,156,.18);
 --fam-quality:#0058f8; --fam-quality-tint:rgba(0,88,248,.18); --fam-improving:#10fc78; --fam-improving-tint:rgba(16,252,120,.18);
}
/* The names the marks draw with, each the token of the same kind, so every mark follows the palette. */
:root{ --muted:var(--soft); --rule:var(--hair); --panel:var(--plot); --support:var(--sup); --resistance:var(--res); }
*{box-sizing:border-box}
html,body{margin:0}
body{background:var(--page);color:var(--ink);font:15px/1.6 var(--sans);-webkit-font-smoothing:antialiased;overflow-wrap:break-word}
a{color:var(--ink);text-decoration:underline;text-decoration-thickness:1px;text-underline-offset:3px}
a:hover{text-decoration-thickness:2px}
:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
/* A select is as wide as its longest choice unless held to what holds it, and a rule's words run past a phone's screen. */
select{max-width:100%}
.num,td.num,.tnum{font-variant-numeric:tabular-nums}
h1,h2,h3{font-family:var(--serif);font-weight:600;text-wrap:balance;margin:0}
.wrap{width:100%;max-width:var(--column);margin:0 auto;padding-inline:var(--gutter)}
code{font-size:.92em}

/* masthead */
.mast{position:sticky;top:env(safe-area-inset-top,0px);z-index:20;background:var(--page);border-bottom:1px solid var(--hair)}
/* one bar at every width: the brand, the four screens read most and the two menus, the search, the index menu and the
   palette's toggle, the screen drawn marked as a pill; on a narrower screen the links, the search and the toggle drop
   into a panel beneath a Menu button while the index menu stays in the bar. The screen's title and date line stand
   beneath the bar. */
.mast .wrap{position:relative;display:flex;flex-wrap:nowrap;align-items:center;gap:8px 12px;padding-block:8px}
.m-brand{order:0;flex:none}
.m-panel{display:contents}
.m-nav{order:1;display:flex;align-items:center;gap:2px}
.m-nav>a,.m-group-btn{display:inline-flex;align-items:center;min-height:34px;padding:0 11px;border:0;border-radius:8px;background:none;font:500 13.5px var(--sans);color:var(--ink-2);text-decoration:none;white-space:nowrap;cursor:pointer}
.m-nav>a:hover,.m-group-btn:hover{background:var(--hair);color:var(--ink)}
.m-nav>a[aria-current='page'],.m-group.current .m-group-btn{background:var(--hair);color:var(--ink);font-weight:600}
.m-group{position:relative}
.m-group-btn::after{content:"";width:6px;height:6px;margin:-3px 0 0 8px;border-right:1.5px solid currentColor;border-bottom:1.5px solid currentColor;transform:rotate(45deg)}
.m-drop{display:none;position:absolute;top:calc(100% + 6px);left:0;z-index:30;min-width:210px;flex-direction:column;padding:6px;background:var(--surface);border:1px solid var(--hair-2);border-radius:8px;box-shadow:var(--shadow)}
.m-group.open .m-drop{display:flex}
.m-drop a{padding:8px 10px;border-radius:6px;font:500 13.5px var(--sans);color:var(--ink-2);text-decoration:none;white-space:nowrap}
.m-drop a:hover{background:var(--hair);color:var(--ink)}
.m-drop a[aria-current='page']{color:var(--ink);font-weight:600}
.m-drop .lane{flex-wrap:wrap;margin-top:6px;padding:10px 6px 4px;border-top:1px solid var(--hair)}
.m-search{order:2;margin-left:auto}
.m-index{order:3;flex:none;min-width:0}
.mast .theme{order:4;flex:none}
.m-menu{order:5;display:none;flex:none;align-items:center;font:600 12.5px var(--sans);color:var(--ink);background:var(--surface);border:1px solid var(--hair-2);border-radius:6px;padding:0 12px;min-height:36px;cursor:pointer}
.m-index .universe-pick{margin:0}
.m-index .universe-lbl{position:absolute;width:1px;height:1px;overflow:hidden;clip-path:inset(50%);white-space:nowrap}
.m-index .universe-pick select{min-height:36px;max-width:100%;padding:0 8px;border-radius:6px;font:500 13px var(--sans)}
.m-badge{display:inline-block;margin-left:4px;min-width:16px;padding:0 5px;border-radius:999px;background:var(--accent);color:var(--accent-ink);font:600 10.5px/16px var(--sans);text-align:center}
.m-badge[hidden]{display:none}
.page-head{display:flex;align-items:baseline;gap:6px 12px;flex-wrap:wrap;padding-top:14px}
.night-picker .np-short{display:none}
/* on a phone the title holds one line and the date line is the night as a short date between its arrows; a date line
   drawing no arrows, a screen's sentence ending on its date and a stock's masthead line with its quote and its links
   among them, wraps whole */
@media (max-width:560px){
 .page-head .m-screen{max-width:100%;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
 .page-head .asof-words{display:none}
 .page-head .night-picker{margin-left:0}
 .night-picker .np-full{display:none}
 .night-picker .np-short{display:inline}
}
@media (max-width:1279px){
 .m-menu{display:inline-flex}
 .m-index{margin-left:auto}
 .m-panel{display:none;position:absolute;top:100%;right:var(--gutter);z-index:30;flex-direction:column;align-items:stretch;gap:12px;padding:14px;background:var(--surface);border:1px solid var(--hair-2);border-radius:8px;box-shadow:var(--shadow);width:min(340px,calc(100vw - 2*var(--gutter)))}
 .m-panel.open{display:flex}
 .m-panel .m-nav{flex-direction:column;align-items:stretch;gap:0}
 .m-panel .m-group-btn{min-height:0;padding:12px 11px 4px;font:600 11.5px var(--sans);letter-spacing:.08em;text-transform:uppercase;color:var(--soft);background:none;cursor:default}
 .m-panel .m-group-btn::after{display:none}
 .m-panel .m-drop{display:flex;position:static;min-width:0;padding:0 0 0 8px;border:0;box-shadow:none;background:none}
 .m-panel .m-search{margin-left:0}
 .m-panel .m-search input{width:100%}
}
/* on a phone the bar holds two rows, the brand with the Menu button and then the index menu */
@media (max-width:480px){
 .mast .wrap{flex-wrap:wrap}
 .m-menu{margin-left:auto}
 .m-index{order:6;flex-basis:100%;margin-left:0}
 .m-index:empty{display:none}
}
.m-brand{font:600 12px var(--sans);letter-spacing:.14em;text-transform:uppercase;text-decoration:none;color:var(--soft)}
.m-tk{font:700 20px var(--sans);letter-spacing:.02em}
.m-co{font:600 18px var(--serif)}
.m-screen{font:600 18px var(--serif)}
.m-px{font:600 17px var(--sans);font-variant-numeric:tabular-nums}
.m-chg{font:500 14px var(--sans);font-variant-numeric:tabular-nums;color:var(--ink-2)}
.m-asof{font-size:12.5px;color:var(--soft);flex-basis:100%;margin-top:-4px}
.m-asof a{color:var(--soft)}
.night-picker{display:inline-flex;align-items:center;gap:4px;margin-left:10px;vertical-align:middle}
.night-picker .np-move{display:inline-block;min-width:22px;text-align:center;line-height:20px;border:1px solid var(--hair);border-radius:4px;color:var(--ink);text-decoration:none}
.night-picker a.np-move:hover{border-color:var(--ink)}
.night-picker span.np-move{opacity:.35}
.night-picker .np-cal{position:relative;display:inline-block}
.night-picker .np-date{list-style:none;cursor:pointer;font-size:12.5px;line-height:18px;padding:0 6px;border:1px solid var(--hair);border-radius:4px;background:var(--surface);color:var(--ink);font-variant-numeric:tabular-nums}
.night-picker .np-date::-webkit-details-marker{display:none}
.night-picker .np-months{position:absolute;z-index:30;top:24px;left:0;display:flex;gap:14px;padding:10px;background:var(--surface);border:1px solid var(--hair);border-radius:6px;box-shadow:var(--shadow);max-width:80vw;overflow-x:auto}
.np-month{font-size:12px;font-variant-numeric:tabular-nums}
.np-caption{font-weight:600;padding:0 0 4px;color:var(--ink);white-space:nowrap}
.np-grid{display:grid;grid-template-columns:repeat(7,24px);gap:3px 2px;text-align:center;line-height:18px}
.np-wd{font-weight:500;color:var(--soft)}
.np-grid .np-day{color:var(--ink);font-weight:600;text-decoration:underline;text-underline-offset:3px}
.np-grid .np-day[aria-current]{background:var(--ink);color:var(--surface);border-radius:3px;text-decoration:none}
.np-grid .np-off{color:var(--soft);opacity:.45}
/* The name page's watch press outranks the rules every posted form and its button take, and states every property they state, so it sits on the masthead's line at the small controls' size. */
form.name-watch[method='post']{display:inline-flex;align-items:center;gap:8px;margin:0;align-self:center}
form.name-watch[method='post'] button{font:600 12px var(--sans);color:var(--ink-2);background:transparent;border:1px solid var(--hair-2);border-radius:999px;padding:2px 10px;min-height:0;cursor:pointer}
form.name-watch[method='post'] button:hover{color:var(--ink);border-color:var(--ink)}
.name-watch .watching{font:600 12.5px var(--sans);color:var(--ink)}
.watch-add{display:flex;gap:8px;align-items:center;margin:0 0 14px}
.watch-add input{font:inherit;font-size:14px;padding:6px 10px;min-width:240px;border:1px solid var(--hair);border-radius:6px;background:var(--surface);color:var(--ink)}
.watch-said:empty{display:none}
.watch-said{margin:0 0 10px}
.watch-table .co{display:block;color:var(--soft);font-size:12px}
.watch-table td.listed{color:var(--ink);font-weight:700}
.watch-table td.stopped{color:var(--soft)}
.watch-table .btn-2{padding:0 8px;line-height:20px}
/* a date in these tables' cells is never broken at its hyphens, and on a phone the two tables scroll sideways in their
   box rather than squeeze their words into columns a few letters wide */
.picks-table td.num:first-child,.watch-table td.num,.researched-table td.num{white-space:nowrap}
.watch-table{min-width:1040px}
.watch-table .chip{white-space:nowrap}
.researched-table{min-width:900px}
.level-summary td.strength{white-space:nowrap}
.level-summary .str-bar{display:inline-block;height:8px;margin-right:6px;vertical-align:middle;border-radius:2px;background:var(--ink-2);opacity:.45}
.level-summary tr.close-row td{border-top:2px solid var(--ink);border-bottom:2px solid var(--ink);font-weight:600;color:var(--ink)}
.level-key{font-size:12.5px;color:var(--soft);margin:6px 0 0;max-width:72ch}
.night-picker .np-newest{margin-left:4px;color:var(--ink)}
.lane{display:flex;align-items:center;gap:8px;font:600 12px var(--sans);color:var(--soft)}
.lane-opts{display:inline-flex;border:1px solid var(--hair-2);border-radius:6px;overflow:hidden}
.lane-opt{padding:0 10px;min-height:36px;display:inline-flex;align-items:center;color:var(--soft);background:var(--surface)}
.lane-opt[data-offered='false']{opacity:.5;cursor:not-allowed;text-decoration:line-through}
.lane-opt.on{color:var(--ink);background:var(--hair);font-weight:700}
.lane-waits{font:12.5px var(--sans);color:var(--soft);margin:0 0 14px}
.queue-part{margin-block:22px}
.queue-table td,.queue-table th{padding:6px 10px;text-align:left;white-space:nowrap}
.queue-table th.num,.queue-table td.num{text-align:right}
/* An instant on the queue page holds its time in UTC beneath the pointer, which the dotted line says is there. */
.queue time[title]{text-decoration:underline dotted;text-decoration-thickness:1px;text-underline-offset:3px;cursor:help}
.queue-older{font:13px var(--sans);margin:-8px 0 22px}
form.ask[method='post']{display:inline-block;margin:0 0 0 8px}
/* The two small controls outrank the rule every posted form's button takes, and state every property that rule states, so neither is drawn at that button's size. */
form.ask[method='post'] button,form.withdraw-control[method='post'] button{font:600 11.5px var(--sans);color:var(--soft);background:var(--surface);border:1px solid var(--hair-2);border-radius:5px;padding:2px 8px;min-height:0;cursor:pointer}
.theme{font:600 12.5px var(--sans);color:var(--ink);background:var(--surface);border:1px solid var(--hair-2);border-radius:6px;padding:0 12px;min-height:36px;cursor:pointer}
.m-search{margin:0}
.m-search input{font:14px var(--sans);color:var(--ink);background:var(--surface);border:1px solid var(--hair-2);border-radius:6px;padding:0 10px;min-height:36px;width:220px}
.m-search input::placeholder{color:var(--soft)}
.c-nm .researched-on{display:block;font-size:12px;color:var(--soft)}
.notice{margin:18px 0 0;padding:10px 14px;border:1px solid var(--hair-2);border-radius:8px;background:var(--surface);font-size:14px}
main .screen-mast{display:none}
.exported .screen-mast{display:flex;align-items:baseline;gap:12px;flex-wrap:wrap;padding:14px 0 10px;border-bottom:1px solid var(--hair)}

/* cards */
main{padding-block:8px 64px}
.card{background:var(--surface);border:1px solid var(--hair);border-top:4px solid var(--accent);border-radius:8px;box-shadow:var(--shadow);padding:20px 24px 24px;margin-top:20px}
.card.spined{display:grid;grid-template-columns:132px minmax(0,1fr);gap:0 24px;border-top-color:var(--research)}
.card.spined.fund{border-top-color:var(--s3)}
.spine{border-right:1px solid var(--hair);padding-right:16px}
.spine .lbl{color:var(--research-ink)}
.card.spined.fund .spine .lbl{color:var(--soft)}
.dl{display:flex;flex-direction:column;gap:1px;margin-top:10px;font:italic 13px/1.45 var(--serif);color:var(--soft)}
.dl-k{font:600 10px var(--sans);font-style:normal;letter-spacing:.12em;text-transform:uppercase;color:var(--soft)}
.dl b{font:600 15px var(--sans);font-style:normal;font-variant-numeric:tabular-nums;color:var(--ink)}
.main{min-width:0}
.main .card-b{margin-top:12px}
.main>h2{margin-top:0}
.card-h{display:flex;justify-content:space-between;align-items:flex-start;gap:16px;flex-wrap:wrap}
.lbl{font:600 11px var(--sans);letter-spacing:.14em;text-transform:uppercase;color:var(--soft)}
.card h2{font-size:23px;line-height:1.25;margin-top:3px}
.lede{color:var(--soft);margin:4px 0 0;max-width:62ch;font-size:14.5px}
.stamp{display:inline-flex;align-items:center;gap:6px;font:600 11.5px var(--sans);border-radius:999px;padding:3px 10px;white-space:nowrap;font-variant-numeric:tabular-nums}
.stamp.computed{background:var(--accent);color:var(--accent-ink)}
.stamp.fund{border:1px solid var(--s3);color:var(--ink-2)}
.stamp.research{border:3px double var(--accent);color:var(--ink);padding:2px 10px}
.stamp .age{font-weight:400;color:var(--soft)}
.card-b{margin-top:16px}
.card-b>:first-child{margin-top:0}
.pill{display:inline-block;font:600 11.5px var(--sans);border:1px solid var(--hair-2);border-radius:999px;padding:1px 9px;color:var(--ink-2);white-space:nowrap}
/* a rule's state: a live rule ink on solid slate, a provisional one in the amber with its word, and a variant a dashed slate outline */
.status-pill{display:inline-block;font:600 11px var(--sans);letter-spacing:.04em;border:1px solid transparent;border-radius:999px;padding:1px 9px;white-space:nowrap}
.status-pill.live{background:var(--accent);color:var(--accent-ink)}
.status-pill.provisional{background:var(--wait-fill);border-color:var(--wait);color:var(--wait)}
.status-pill.variant{border:1.3px dashed var(--accent);color:var(--ink-2)}
.status-pill.failed{background:var(--fail);color:var(--accent-ink)}
/* a setup family: its word in ink on its tint, beside a swatch of its hue; and its hue as the edge of its tile or card */
.fam-tag{display:inline-flex;align-items:center;gap:6px;font:600 12px var(--sans);color:var(--ink);border-radius:5px;padding:1px 8px 1px 6px;white-space:nowrap}
.fam-swatch{display:inline-block;width:10px;height:10px;border-radius:2px;flex:none}
.fam-tag[data-family='pullback']{background:var(--fam-pullback-tint)} .fam-swatch[data-family='pullback']{background:var(--fam-pullback)} .fam-edge[data-family='pullback']{border-left-color:var(--fam-pullback)}
.fam-tag[data-family='breakout']{background:var(--fam-breakout-tint)} .fam-swatch[data-family='breakout']{background:var(--fam-breakout)} .fam-edge[data-family='breakout']{border-left-color:var(--fam-breakout)}
.fam-tag[data-family='drift']{background:var(--fam-drift-tint)} .fam-swatch[data-family='drift']{background:var(--fam-drift)} .fam-edge[data-family='drift']{border-left-color:var(--fam-drift)}
.fam-tag[data-family='fundamentals']{background:var(--fam-fundamentals-tint)} .fam-swatch[data-family='fundamentals']{background:var(--fam-fundamentals)} .fam-edge[data-family='fundamentals']{border-left-color:var(--fam-fundamentals)}
.fam-tag[data-family='heavyweight']{background:var(--fam-heavyweight-tint)} .fam-swatch[data-family='heavyweight']{background:var(--fam-heavyweight)} .fam-edge[data-family='heavyweight']{border-left-color:var(--fam-heavyweight)}
.fam-tag[data-family='dip']{background:var(--fam-dip-tint)} .fam-swatch[data-family='dip']{background:var(--fam-dip)} .fam-edge[data-family='dip']{border-left-color:var(--fam-dip)}
.fam-tag[data-family='quality']{background:var(--fam-quality-tint)} .fam-swatch[data-family='quality']{background:var(--fam-quality)} .fam-edge[data-family='quality']{border-left-color:var(--fam-quality)}
.fam-tag[data-family='improving']{background:var(--fam-improving-tint)} .fam-swatch[data-family='improving']{background:var(--fam-improving)} .fam-edge[data-family='improving']{border-left-color:var(--fam-improving)}
/* The Fundamentals card, as the operator's mockup draws it: its sentence set large, the bought count and word in the
   buying green and the flagged in the caution amber, the word always beside each, two radio inputs choosing which of its two lists shows with their labels drawn as a
   tab bar, the chosen tab on the family's tint with its hue for an edge, and each list a table, stacked a row at a time on
   a phone. */
.improving-card>.card-summary{font-size:18px;font-weight:600;line-height:1.4;color:var(--ink)}
.improving-tiles b[data-state=bought]{color:var(--sup-ink)} .improving-tiles b[data-state=flagged]{color:var(--warn)}
.improving-card>.improving-tab{position:absolute;opacity:0;width:0;height:0;pointer-events:none}
.improving-tabs{display:flex;flex-wrap:wrap;gap:4px;margin:18px 0 0;border-bottom:1px solid var(--hair)}
.improving-tabname{cursor:pointer;padding:9px 16px;margin-bottom:-1px;border:1px solid transparent;border-bottom:0;border-radius:8px 8px 0 0;font:600 15px var(--sans);color:var(--ink-2)}
.improving-tab[value="fastest"]:checked~.improving-tabs .improving-tabname[for="improving-tab-fastest"],.improving-tab[value="strongest"]:checked~.improving-tabs .improving-tabname[for="improving-tab-strongest"]{background:var(--fam-improving-tint);border-color:var(--fam-improving);color:var(--ink);font-weight:700}
.improving-tab[value="fastest"]:checked~.improving-panel[data-tab=strongest],.improving-tab[value="strongest"]:checked~.improving-panel[data-tab=fastest]{display:none}
.improving-lead{color:var(--ink-2);margin:12px 0}
.list-table.improving-table{min-width:0}
.improving-table td{vertical-align:top;padding-top:10px;padding-bottom:10px;white-space:normal}
.improving-table td.c-nm{width:14%} .improving-table td.improving-score{width:14%;font-variant-numeric:tabular-nums} .improving-table td.improving-status{width:30%}
.improving-table td.improving-score b,.improving-table td.improving-status b{display:block;font-weight:600;color:var(--ink)}
.improving-from{display:block;font-size:12.5px;color:var(--up)}
.improving-status span,.improving-quality{display:block;font-size:12.5px;color:var(--ink-2)}
.improving-table td.improving-status[data-state=bought] b{color:var(--sup-ink)} .improving-table td.improving-status[data-state=flagged] b{color:var(--warn)}
@media (max-width:560px){
 .improving-tabname{padding:8px 10px;font-size:14px}
 .list-table.improving-table{display:block} .list-table.improving-table thead{display:none} .list-table.improving-table tbody{display:block}
 .list-table.improving-table tr{display:grid;grid-template-areas:'nm score' 'changed changed' 'tonight tonight';grid-template-columns:minmax(0,1fr) auto;column-gap:12px;padding:8px 0;border-bottom:1px solid var(--hair)}
 .list-table.improving-table td{display:block;width:auto;height:auto;border:0;padding:2px 0;white-space:normal}
 .list-table.improving-table td.c-nm{grid-area:nm} .list-table.improving-table td.improving-score{grid-area:score;text-align:right}
 .list-table.improving-table td.improving-changed{grid-area:changed;color:var(--ink-2)} .list-table.improving-table td.improving-status{grid-area:tonight}
}
@media (max-width:560px){
 .list-table.quality-table{min-width:0;display:block} .list-table.quality-table thead{display:none} .list-table.quality-table tbody{display:block}
 .list-table.quality-table tr{display:grid;grid-template-areas:'nm score since' 'place place place' 'tonight tonight tonight';grid-template-columns:minmax(0,1fr) auto auto;column-gap:12px;padding:8px 0;border-bottom:1px solid var(--hair)}
 .list-table.quality-table td{display:block;height:auto;border:0;padding:1px 0;white-space:normal}
 .list-table.quality-table td.c-nm{grid-area:nm} .list-table.quality-table td.quality-score{grid-area:score} .list-table.quality-table td.quality-since{grid-area:since}
 .list-table.quality-table td.quality-place{grid-area:place;color:var(--ink-2)} .list-table.quality-table td.quality-tonight{grid-area:tonight}
 .list-table.quality-table td.quality-score::before{content:'score ';color:var(--soft);font-size:12px}
 .list-table.quality-table td.quality-since::before{content:'since bought ';color:var(--soft);font-size:12px}
}
.fam-edge{border-left:4px solid var(--s3)}
/* a signed figure: a rise in the green and a fall in the red, on every page, its sign always written beside it */
.gain{color:var(--up)} .loss{color:var(--down)}
svg .gain{fill:var(--up)} svg .loss{fill:var(--down)}
.key{margin-top:12px;background:var(--page);border:1px solid var(--hair);border-radius:6px;padding:10px 14px;font-size:13px;line-height:1.55;color:var(--soft)}
.key b{color:var(--ink);font-weight:600}
.key p{margin:0}
.key .take{margin-top:6px;padding-top:6px;border-top:1px solid var(--hair)}
.key .k2{margin-top:4px}
.dg{display:grid;grid-template-columns:max-content 1fr;gap:4px 16px;margin:8px 0 0}
.dg dt{font-weight:600;color:var(--ink)} .dg dd{margin:0}
.fig{overflow-x:auto}
/* A picture keeps its own ratio as the column narrows, and the row anchors its pictures at the
   top, which is what keeps a price at one height in the chart and in the profile beside it. */
.fig svg{display:block;max-width:100%;height:auto}
.row-fig{display:flex;align-items:flex-start;gap:0}
.sub{font:600 11px var(--sans);letter-spacing:.12em;text-transform:uppercase;color:var(--ink);margin:22px 0 8px}
.soft{color:var(--soft)}
.nw{white-space:nowrap}
.dash{display:inline-block;border:1.3px dashed var(--dash-ink);border-radius:4px;padding:1px 8px;font-size:12.5px;color:var(--ink-2)}
.btn,form[method='post'] button{font:600 14px var(--sans);color:var(--accent-ink);background:var(--accent);border:0;border-radius:6px;padding:0 16px;min-height:44px;cursor:pointer}
.btn-2,.export-report{font:600 13px var(--sans);color:var(--ink);background:transparent;border:1px solid var(--hair-2);border-radius:6px;padding:0 12px;min-height:36px;cursor:pointer;text-decoration:none;display:inline-flex;align-items:center}
form[method='post']{display:inline-block;margin:8px 10px 0 0}
form[method='post'] button:disabled{opacity:.6;cursor:progress}
.export{margin:18px 0 0;display:flex;justify-content:flex-end}

/* tables: every table a region draws reads the same way */
.tbl-wrap{overflow-x:auto}
main table,.exported table{border-collapse:collapse;width:100%;font-size:13.5px}
main th,.exported th{font:600 10.5px/1.3 var(--sans);letter-spacing:.07em;text-transform:uppercase;color:var(--soft);text-align:left;padding:0 10px 7px 0;border-bottom:1px solid var(--ink);vertical-align:bottom}
main td,.exported td{padding:7px 10px 7px 0;border-bottom:1px solid var(--hair);vertical-align:middle;font-variant-numeric:tabular-nums}
main tbody tr:nth-child(5n) td{border-bottom-color:var(--hair-2)}
main caption{caption-side:top;text-align:left;font:600 11px var(--sans);letter-spacing:.12em;text-transform:uppercase;color:var(--ink);padding:0 0 8px}
td ul{margin:0;padding-left:16px}
.tk{font:700 13px var(--sans);letter-spacing:.03em}
.co{color:var(--ink-2);font-size:13px}

/* the words a region states in place of a figure it does not have */
.degraded{color:var(--soft);font-style:italic}
p.degraded,nav.walk .degraded{font-size:14px}
p.degraded[data-sessions],p.degraded[data-bands='0'],p.degraded[data-readings='0'],p.degraded[data-rows='0'],p.degraded[data-moves='none']{border:1.3px dashed var(--dash-ink);border-radius:8px;padding:18px 20px;font-style:normal;color:var(--ink-2);background:var(--surface)}
p[data-last-asked-at],.no-year,p[data-sessions]:not(.degraded){margin:20px 0 0;border:1px solid var(--hair-2);border-radius:8px;background:var(--surface);padding:12px 16px;font-size:14px}
p[data-last-asked-at]::before{content:"Prices may be out of date. ";font-weight:700}
span[data-last-asked-at]{display:block;margin:2px 0 0;border:0;padding:0;background:none;font-size:11.5px;line-height:1.35;font-style:italic;color:var(--ink-2)}
span[data-last-asked-at]::before{content:none}

/* tonight */
.night{display:grid;grid-template-columns:auto 1fr;gap:28px;align-items:start}
.headline{display:flex;align-items:flex-end;gap:14px;margin:0}
.headline .big{font:600 76px/.85 var(--serif);letter-spacing:-.02em;font-variant-numeric:tabular-nums}
.headline .cap{font:18px/1.3 var(--serif)} .headline .cap span{display:block;font:14px var(--sans);color:var(--soft)}
.ops{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px 22px;margin:0}
.ops>p{margin:0;border-left:1px solid var(--hair-2);padding-left:12px;font-size:13.5px;color:var(--ink-2)}
.ops>p.fired{font-weight:600;color:var(--ink)}
.list-table td{height:44px;padding-block:3px}
.list-table tr[data-ticker]{cursor:pointer}
.list-table tr[data-ticker]:hover td{background:var(--page)}
.list-table tr.sel td{background:var(--page);box-shadow:inset 0 2px 0 var(--ink),inset 0 -2px 0 var(--ink)}
.list-table a.name-link{font:700 13px var(--sans);letter-spacing:.03em}
.list-table td.day-change{white-space:nowrap}
.list-table{table-layout:auto;min-width:820px}
.list-table th.rz,.list-table td.rz{width:64px;padding:0 3px;border-left:1px solid var(--hair);text-align:center}
.list-table th.rz{text-transform:none;letter-spacing:0;font-size:11px;color:var(--ink)}
.list-table td.c-nm .co{display:block;font-size:12.5px;line-height:1.25;max-width:150px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.list-table a.open{font-size:11px;color:var(--soft);margin-left:4px}
.list-table td.trend-state{font-size:12.5px;color:var(--ink-2)}
/* the gate a stock close to a buy point missed, with the trade its plan states on a line of its own beneath */
.list-table td.missed{font-size:12.5px;line-height:1.35;max-width:320px}
.list-table td.missed .missed-trade{display:block;color:var(--soft);font-size:12px;margin-top:2px}
/* Tonight's card of the stocks one gate short: its one line the press that opens it, what each had in words on the row */
details.close-to-buy>summary{cursor:pointer;font-weight:600}
.close-to-buy-table td.close-had{font-size:12.5px;line-height:1.35;min-width:180px;max-width:340px}
.close-to-buy .close-universe{margin:8px 0 0}
/* the state a member's reported quarters give it, beside the trend word, with what the numbers say while the word is under the pointer or has focus.
   The sentences are fixed to the window rather than to the table, whose box scrolls sideways and would grow a scrollbar
   around them and cut them off, and the shell's script places them beside the word. */
.business{position:relative;display:inline-block;margin-left:6px;font-size:11.5px;border:1px solid var(--hair-2);border-radius:999px;padding:0 7px;color:var(--ink-2);cursor:help}
.news-why .says,.business .says{display:none;position:fixed;z-index:40;width:340px;max-width:80vw;padding:8px 10px;background:var(--panel);border:1px solid var(--hair-2);border-radius:6px;font-size:12.5px;line-height:1.45;color:var(--ink);white-space:normal}
.business:hover .says,.business:focus .says,.business:focus-within .says{display:block}
.news-why:hover .says,.news-why:focus .says,.news-why:focus-within .says{display:block}
.numbers-say{margin:0 0 12px}
.numbers-say .says-heading{margin:0 0 4px;font:600 15px var(--serif)}
.numbers-say .read-from{margin:0 0 6px;font-size:12.5px;color:var(--soft)}
.numbers-say ul.says{margin:0 0 10px;padding-left:18px;font-size:14px;line-height:1.5}
.numbers-behind summary{cursor:pointer;font-size:13px;color:var(--ink-2)}
.list-table th.place,.list-table td.place{width:1%;padding-right:12px;text-align:right;color:var(--soft);white-space:nowrap}
.list-table td.place{font-size:12.5px}
.list-count{margin:0 0 10px;font-size:13.5px;color:var(--ink-2)}
/* a reason on tonight's list: the word, and a panel saying what the night measured it over.
   It is shown while the reason is under the pointer and goes as the pointer leaves, and it is
   shown to a keyboard on focus, which is `focus-visible` rather than `focus` so that a click
   does not pin it open. The values are drawn in the panel rather than put in a title
   attribute, which is a native tooltip a touch screen cannot reach at all. The panel is fixed
   to the window rather than to the table, whose box scrolls sideways and would grow a scrollbar
   around it and cut it off, and the shell's script places it beside the reason. */
.list-table .reason{position:relative}
.list-table .reason>.r-head{display:flex;align-items:center;justify-content:center;height:28px;padding:0 3px;background:var(--s4);color:var(--surface);font:600 10.5px/1 var(--sans);cursor:help}
.list-table .reason .why{display:none;position:fixed;z-index:40;width:280px;background:var(--ink);color:var(--surface);padding:9px 11px;border-radius:6px;font:400 12px/1.45 var(--sans);text-align:left;white-space:normal}
.list-table .reason:hover .why,.list-table .reason:focus-visible .why{display:block}
.list-table .why-fired{margin:0 0 6px;font-weight:600}
.list-table .why-fired::first-letter{text-transform:uppercase}
.list-table .reason-values{display:grid;grid-template-columns:auto minmax(0,1fr);gap:2px 10px;margin:0}
.list-table .reason .why .reason-values dt{color:var(--s2)}
.list-table .reason-values dd{margin:0;font-variant-numeric:tabular-nums;text-align:right}
.list-table .reason .why .no-values{margin:0;color:var(--s2)}
.list-table .reason .record{display:block;margin-top:8px;padding-top:8px;border-top:1px solid var(--s3)}
.list-table tfoot td{border-bottom:0;border-top:1px solid var(--ink);vertical-align:top;padding-top:6px;height:auto}
.list-table tfoot .reason{background:none;color:var(--ink-2);height:auto;display:block;cursor:default}
.record-foot{display:flex;flex-direction:column;font:10.5px/1.3 var(--sans);color:var(--ink-2);text-align:left}
.record-foot b{font-size:12px;color:var(--ink)}
.record-foot.not-measured{border:1.3px dashed var(--dash-ink);padding:3px 4px}
.rec-lab{font-size:12px;line-height:1.45;color:var(--soft);padding-right:16px}
.why-it-is-here .reason{display:block;background:none;color:var(--ink);font:15px/1.55 var(--sans);max-width:none;padding:0;margin:0 0 12px}
.why-it-is-here .reason::first-letter{text-transform:uppercase}
.why-it-is-here .values{display:block;font-size:12.5px;color:var(--soft);margin-top:2px}
.why-it-is-here .reason .reason-name{font-weight:700}
.oneline{margin:14px 0 0;font-size:13.5px;color:var(--ink-2);max-width:70ch}
.watch-list .watched{display:inline-block;margin:4px 8px 0 0}
/* tonight's top: a tile a setup in its tint and edge with tonight's count and its state, then one table of tonight's buy
   points, each row tagged with its setup and the row selected drawing its plan and its card in a row beneath it */
.setup-tiles{display:grid;grid-template-columns:repeat(auto-fill,minmax(170px,1fr));gap:10px;margin:16px 0 0}
.setup-tile{display:flex;flex-direction:column;align-items:flex-start;gap:4px;text-align:left;font:inherit;color:var(--ink);background:var(--surface);border:1px solid var(--hair);border-left-width:4px;border-radius:8px;padding:10px 12px;cursor:pointer}
.setup-tile .tile-count{font:600 26px/1.1 var(--sans);font-variant-numeric:tabular-nums}
.setup-tile .tile-counted{font-size:12.5px;color:var(--ink-2)}
.setup-tile .tile-state{font-size:12.5px;color:var(--ink-2)}
.buy-points td.setup{white-space:nowrap}
.buy-points tr.fold-row>td{white-space:normal;background:var(--plot);padding:12px}
.buy-points tr[data-fold]{scroll-margin-top:84px}
.selwrap{display:grid;grid-template-columns:minmax(0,390px) minmax(0,1fr);gap:24px;align-items:start}
.sel-links{display:flex;flex-wrap:wrap;gap:10px;margin-top:14px}
.sel-unwritten{margin:14px 0 0;font-size:13.5px;color:var(--ink-2)}
#selected{scroll-margin-top:84px}

/* paper trading: each account's line in its family's mark, a variant's dashed and the operator's in ink */
.paper-families{display:flex;flex-wrap:wrap;gap:8px;margin:4px 0 12px;font-size:13px}
.paper-families a{padding:3px 10px;border:1px solid var(--hair);border-radius:999px;text-decoration:none;color:var(--ink);display:inline-flex;align-items:center;gap:6px}
.paper-families a[aria-current='true']{border-color:var(--ink);font-weight:600}
.paper-chart{margin:0}
.paper-chart svg,.paper-running svg{width:100%;height:auto;display:block}
.paper-zero{stroke:var(--hair-2);stroke-width:1}
.paper-axis{font-size:11px;fill:var(--soft);font-variant-numeric:tabular-nums}
.paper-line{fill:none;stroke:var(--ink);stroke-width:1.8;stroke-linejoin:round}
.paper-line.dashed{stroke-dasharray:5 4;stroke-width:1.4}
.paper-line[data-family='pullback']{stroke:var(--fam-pullback)} .paper-line[data-family='breakout']{stroke:var(--fam-breakout)} .paper-line[data-family='drift']{stroke:var(--fam-drift)}
.paper-line[data-family='fundamentals']{stroke:var(--fam-fundamentals)} .paper-line[data-family='heavyweight']{stroke:var(--fam-heavyweight)} .paper-line[data-family='dip']{stroke:var(--fam-dip)}
.paper-keys{display:flex;flex-wrap:wrap;gap:6px;margin-top:8px}
.paper-key{display:inline-flex;align-items:center;gap:6px;font:12px var(--sans);color:var(--ink);background:var(--surface);border:1px solid var(--hair);border-radius:999px;padding:2px 10px;cursor:pointer;max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.paper-key[aria-pressed='false']{opacity:.45;text-decoration:line-through}
.paper-key-mark{display:inline-block;width:16px;height:0;border-top:2.5px solid var(--ink);flex:none}
.paper-key-mark[data-standing='variant']{border-top-style:dashed}
.paper-key-mark[data-family='pullback']{border-top-color:var(--fam-pullback)} .paper-key-mark[data-family='breakout']{border-top-color:var(--fam-breakout)} .paper-key-mark[data-family='drift']{border-top-color:var(--fam-drift)}
.paper-key-mark[data-family='fundamentals']{border-top-color:var(--fam-fundamentals)} .paper-key-mark[data-family='heavyweight']{border-top-color:var(--fam-heavyweight)} .paper-key-mark[data-family='dip']{border-top-color:var(--fam-dip)}
/* paper trading's parts: what the page is and the account's line with the form behind its press; a tile a family's live
   or provisional account beside the fund's, stacking on a phone; the open and closed tabs and the family chips, wrapping;
   the trades, the variants and the scorecard's variants each in a table its box scrolls; a rule's scorecard in two halves,
   side by side and one beneath the other on a phone, its test years as boxes, its scale of luck in three zones and its
   progress as a bar */
.paper-what{font-size:14.5px;color:var(--ink-2);margin:0 0 10px;max-width:78ch}
.paper-line{display:flex;flex-wrap:wrap;gap:4px 18px;align-items:baseline;margin:0 0 8px;font-size:14px;color:var(--ink-2)}
.paper-line b{color:var(--ink);font-variant-numeric:tabular-nums}
.paper-change>summary{display:inline-block;cursor:pointer;font:600 13.5px var(--sans);color:var(--ink);text-decoration:underline;text-underline-offset:3px;padding:6px 0;min-height:32px}
.paper-change[open]>summary{margin-bottom:8px}
.paper-tiles{display:grid;grid-template-columns:repeat(auto-fill,minmax(190px,1fr));gap:10px;margin:0 0 14px}
.paper-tile{display:flex;flex-direction:column;gap:4px;padding:10px 12px;border:1px solid var(--hair-2);border-radius:8px;background:var(--surface);min-width:0}
.paper-tile.fund{background:var(--plot)}
.paper-tile.combined{border-color:var(--ink)}
.paper-line[data-standing='combined']{stroke:var(--ink);stroke-width:2.8}
.paper-key-mark[data-standing='combined']{border-top-width:4px}
.paper-combined-skips{font-size:14px;color:var(--ink-2);margin:0 0 10px;max-width:78ch}
.paper-combined-table{min-width:640px}
.strip-line{font-size:12.5px;color:var(--ink-2);margin:0 0 8px}
.strip-tiles{display:flex;flex-wrap:wrap;gap:8px}
.strip-tile{display:flex;flex-direction:column;gap:2px;flex:1 1 128px;min-width:0;padding:8px 10px;border:1px solid var(--hair-2);border-radius:8px;background:var(--surface)}
.st-label{font:600 12px var(--sans);color:var(--ink-2)}
.st-level{font:600 17px var(--sans);font-variant-numeric:tabular-nums;color:var(--ink)}
.st-change{font-size:12.5px;font-variant-numeric:tabular-nums}
.st-words,.st-beneath{font-size:11.5px;color:var(--ink-2);overflow-wrap:anywhere}
.copy-strip{margin-top:10px}
.paper-tile .pt-name{display:flex;flex-wrap:wrap;align-items:center;gap:6px;font:600 13px var(--sans);color:var(--ink)}
.paper-tile .pt-figure{font:600 22px var(--sans);font-variant-numeric:tabular-nums}
.paper-tile .pt-sub{font-size:12.5px;color:var(--ink-2);font-variant-numeric:tabular-nums}
.paper .day,.paper-variants .too-few{white-space:nowrap}
.paper-tabs,.paper-chips{display:flex;flex-wrap:wrap;align-items:center;gap:6px;margin:0 0 10px}
.paper-bought,.paper-variants,.paper-only,.score-table{min-width:720px}
.paper-bought td,.paper-variants td,.paper-only td,.score-table td{vertical-align:top}
.pb-stock a{font-weight:600}
.pb-co,.pb-day{display:block;font-size:12px;color:var(--soft);font-weight:400}
.pv-note{display:block;font-size:12px;color:var(--warn);font-weight:400;max-width:42ch}
.pb-also{display:block;font-size:12px;color:var(--ink-2)}
.pb-day{white-space:nowrap}
.pb-standing{font-weight:600}
.pb-standing.near-target,.pb-standing.leading{color:var(--up)}
.pb-standing.near-stop,.pb-standing.opened-under,.pb-standing.below-average{color:var(--down)}
.pb-standing.opened-over,.pb-standing.lost-lead{color:var(--warn)}
.paper-unbought,.paper-adopted,.paper-entry{font-size:13px;color:var(--ink-2);margin:10px 0 0}
.paper-unbought .pu-reason{margin-right:6px}
.paper-pick{display:flex;flex-wrap:wrap;align-items:center;gap:6px;margin:0 0 12px;max-width:min(100%,560px)}
.paper-pick select{min-height:36px;padding:0 8px;border-radius:6px;font:500 13.5px var(--sans)}
.pv-rule .paper-log{font-weight:600}
.pv-verdict{display:block;font-size:12px;color:var(--soft)}
.pv-only-h{font-size:15px;margin:16px 0 6px}
.too-few{display:block;font-size:12px;color:var(--soft)}
.score{border:1px solid var(--hair-2);border-radius:8px;background:var(--surface)}
.score-h{display:flex;flex-wrap:wrap;align-items:center;gap:6px 10px;padding:12px 14px;border-bottom:1px solid var(--hair)}
.score-h h3{font-size:17px}
.score-rule{font-size:13px;color:var(--ink-2);flex:1 1 240px;min-width:0}
.score-halves{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,380px),1fr));gap:1px;background:var(--hair)}
.score-half{padding:12px 14px;min-width:0;background:var(--surface)}
.sh-h{display:flex;flex-wrap:wrap;align-items:center;gap:6px 10px;margin-bottom:4px}
.sh-lbl{font:600 11.5px var(--sans);letter-spacing:.1em;text-transform:uppercase;color:var(--soft)}
.score-half h4{font:600 14.5px var(--sans);margin:12px 0 4px}
.sh-line{font-size:13.5px;color:var(--ink-2);margin:6px 0}
.sh-count{font-size:14px;color:var(--ink);font-weight:600;margin:6px 0}
.sh-years{display:flex;flex-wrap:wrap;gap:6px;margin:6px 0}
.sy{display:flex;flex-direction:column;align-items:center;gap:1px;min-width:68px;padding:5px 8px;border:1px solid var(--hair-2);border-radius:8px;font-variant-numeric:tabular-nums}
.sy.better{background:var(--up-fill);border-color:var(--up)}
.sy.worse{background:var(--down-fill);border-color:var(--down)}
.sy .sy-y{font-size:11.5px;color:var(--soft)}
.sy b{font-size:13px}
.sy .sy-e{font-size:12px}
.luck{margin:6px 0 0;max-width:420px}
.luck-scale{width:100%;height:auto;display:block}
.luck-scale .ls-easily{fill:var(--down-fill);stroke:var(--down);stroke-width:1}
.luck-scale .ls-maybe{fill:var(--warn-fill);stroke:var(--warn);stroke-width:1}
.luck-scale .ls-real{fill:var(--up-fill);stroke:var(--up);stroke-width:1}
.luck-scale .ls-mark{fill:var(--ink);stroke:var(--surface);stroke-width:2.5}
.luck-scale .ls-word{font:11px var(--sans);fill:var(--soft)}
.sh-warn{font-size:13px;color:var(--warn);background:var(--warn-fill);border-radius:6px;padding:6px 9px;margin:8px 0 0}
.sh-progress{width:100%;max-width:420px;height:10px;display:block;margin:6px 0 4px}
.sh-progress .sp-track{fill:var(--s1)}
.sh-progress .sp-fill{fill:var(--ink-2)}
.sh-ends{display:flex;flex-wrap:wrap;justify-content:space-between;gap:4px 10px;max-width:420px;font-size:12px;color:var(--soft)}
.score-variants{padding:6px 14px 12px;border-top:1px solid var(--hair)}
.score-variants>summary{cursor:pointer;font:600 13.5px var(--sans);padding:8px 0;min-height:32px}
.score-none{padding:8px 14px;margin:0}
.paper-yours p{margin:4px 0;font-size:14px}
.paper-yours .paper-count{font-variant-numeric:tabular-nums;font-weight:600}
@media (max-width:560px){
 .paper-tiles{grid-template-columns:1fr}
}
/* from 19.7 the remaining screens: the Run page's two headings, its trades line and the monthly run; the Loop page's
   summary, its p meter and its proposal rows with their paired year bars; the Ledger's edge bars, decile strip and tabs;
   the Researched page's two orders */
.run-heading{font:600 13px var(--sans);letter-spacing:.08em;text-transform:uppercase;color:var(--ink-2);margin:18px 0 6px}
.run-trades{font-size:14px;color:var(--ink-2);margin:10px 0}
.monthly-run .mr-state,.monthly-run .mr-shape,.monthly-run .mr-links{font-size:14px;margin:6px 0}
.monthly-run .mr-links a{margin-right:14px}
.monthly-table td{vertical-align:top}
.family-records .family-head th{text-align:left;font-weight:600;padding-top:10px}
.p-meter{display:inline-block;vertical-align:middle}
.p-meter .pm-track{fill:var(--s1)}
.p-meter .pm-bar{stroke:var(--ink);stroke-width:1.5}
.loop-summary td{vertical-align:top}
.loop-summary .loop-better{display:block;font-size:12px;color:var(--ink-2);font-variant-numeric:tabular-nums}
.loop-must{font-size:13px;color:var(--ink-2);margin:10px 0 0}
.loop-row{border-top:1px solid var(--hair);padding:6px 0}
.loop-row summary{display:flex;flex-wrap:wrap;align-items:center;gap:6px 12px;cursor:pointer;font-size:14px}
.loop-row .lr-name{font-weight:600}
.loop-row .lr-change{flex:1 1 220px;min-width:0;overflow-wrap:anywhere;color:var(--ink-2)}
.loop-row .lr-years,.loop-row .lr-trimmed,.loop-row .lr-count{font-size:12px;color:var(--ink-2);font-variant-numeric:tabular-nums}
.loop-yearbars,.ledger-edge-bars,.ledger-decile-strip{display:block;margin:6px 0 2px;max-width:100%}
.loop-yearbars .yb-axis,.ledger-edge-bars .yb-axis{stroke:var(--hair-2);stroke-width:1}
.loop-yearbars .yb-this{fill:var(--ink-2)}
.loop-yearbars .yb-that{fill:var(--soft);opacity:.55}
.loop-yearbars .yb-year,.ledger-edge-bars .yb-year{font:10px var(--sans);fill:var(--soft)}
.ledger-decile-strip .ds-line{stroke:var(--hair-2);stroke-width:2}
.ledger-decile-strip .ds-zero{stroke:var(--ink-2);stroke-width:1;stroke-dasharray:2 2}
.ledger-tabs{display:flex;flex-wrap:wrap;gap:6px;margin:4px 0 10px}
.ledger-tabs a[aria-current='true']{outline:2px solid var(--ink-2);outline-offset:1px}
.ledger-unwritten,.ledger-deciles-said,.watch-add-line{font-size:13px;color:var(--ink-2);margin:6px 0}
.researched-sort{font-size:13px;margin:4px 0 8px}
.researched-sort a[aria-current='true']{font-weight:600}
.paper-heads ul{margin:6px 0 0;padding-left:18px;font-size:13px}
.paper-heads li{margin:3px 0}
.account-pick{display:flex;flex-wrap:wrap;align-items:center;gap:6px;margin:8px 0;max-width:min(100%,560px)}
/* on a phone the account form stands each setting's words whole above its box, one setting beneath another */
@media (max-width:560px){
 form.account-control{display:block;margin-right:0}
 .account-control label{display:block;margin:0 0 10px}
 .account-control input{display:block;width:100%;margin-top:4px}
}
.paper-log-since .too-few,.paper-heads .too-few{display:inline}
.paper-log-table td:nth-child(-n+5){white-space:nowrap}
.paper-running{margin:0 0 12px;max-width:420px}
.paper-running figcaption{font-size:12.5px;color:var(--ink-2)}

/* universe */
.sector-tiles{display:grid;grid-template-columns:repeat(auto-fill,minmax(168px,1fr));gap:8px;margin:0}
.sector-tile{display:flex;flex-direction:column;gap:3px;padding:9px 11px;border:1px solid var(--hair-2);border-radius:8px;text-decoration:none;color:var(--ink-2);font-size:12.5px;font-variant-numeric:tabular-nums;background:var(--surface);min-width:0}
.sector-tile .tile-name{font:600 13px var(--sans);color:var(--ink)}
.sector-tile[aria-pressed='true']{border-color:var(--ink);box-shadow:inset 0 0 0 1px var(--ink)}
.sector-tile .cell-bar{margin-left:0;margin-top:3px;display:block;width:100%}
.cell-bar{position:relative;display:inline-block;vertical-align:middle;width:44px;height:6px;margin-left:6px;border-radius:3px;background:var(--hair)}
.cell-fill{position:absolute;left:0;top:0;bottom:0;border-radius:3px;background:var(--ink-2)}
.cell-tick{position:absolute;top:-2px;bottom:-2px;width:2px;margin-left:-1px;background:var(--ink)}
.short-tag{margin-top:3px;font-size:11.5px}
td.c-nm .short-tag{display:inline-flex}
.universe-search{display:flex;flex-wrap:wrap;gap:8px;align-items:center;margin:4px 0 10px}
.universe-search label{display:flex;align-items:center;gap:8px;flex:1 1 220px;max-width:420px}
.universe-search input{flex:1;min-width:0;font:inherit;min-height:40px;padding:6px 10px;border-radius:8px;border:1px solid var(--rule);background:var(--surface);color:var(--ink)}
a.sort{margin-left:4px;text-decoration:none;color:var(--soft);font-size:11px}
a.sort[aria-pressed='true']{color:var(--ink)}
.universe-filters{display:flex;flex-wrap:wrap;gap:8px;align-items:center;margin-top:4px}
.chips-label{font:600 11px var(--sans);letter-spacing:.12em;text-transform:uppercase;color:var(--soft);margin-right:4px;min-width:60px}
.chip{font:600 12.5px var(--sans);text-decoration:none;color:var(--ink-2);border:1px solid var(--hair-2);border-radius:999px;padding:5px 12px;min-height:32px;display:inline-flex;align-items:center}
.chip[aria-pressed='true']{background:var(--ink);color:var(--surface);border-color:var(--ink)}
.chip-break{flex-basis:100%;height:0}
.chip .n{font-weight:400;color:var(--soft);margin-left:6px;font-variant-numeric:tabular-nums}
.chip[aria-pressed='true'] .n{color:var(--surface);opacity:.75}
.picks-filters{margin-bottom:12px}
.counts{display:grid;grid-template-columns:repeat(6,minmax(0,1fr));gap:0;margin:4px 0 0;border:1px solid var(--hair);border-radius:8px;overflow:hidden}
.counts div{padding:10px 14px;border-right:1px solid var(--hair);background:var(--surface)}
.counts div:last-child{border-right:0}
.counts dt{font:600 10.5px/1.3 var(--sans);letter-spacing:.07em;text-transform:uppercase;color:var(--soft)}
.counts dd{margin:4px 0 0;font:600 22px/1.1 var(--serif);font-variant-numeric:tabular-nums}
.counts .grp{color:var(--soft);font-weight:400;font-size:12px;display:block;margin-top:2px;font-family:var(--sans)}
@media (max-width:760px){.counts{grid-template-columns:repeat(3,minmax(0,1fr))}.counts div:nth-child(3){border-right:0}.counts div:nth-child(-n+3){border-bottom:1px solid var(--hair)}}
.not-yet-rate{margin:14px 0 0;padding:12px 14px;border:1.3px dashed var(--dash-ink);border-radius:8px;font-size:14px;color:var(--ink-2);max-width:78ch}
.not-yet-rate b{color:var(--ink);font-variant-numeric:tabular-nums}
.rate{margin-top:14px}
.rate-bar{width:100%;max-width:620px;height:auto;display:block}
.rb-t{fill:var(--s4)} .rb-s{fill:var(--s2)} .rb-o{fill:var(--s3)}
.rb-even{stroke:var(--ink);stroke-width:1.5}
.rb-lab{font:11px var(--sans);fill:var(--ink-2)}
.rb-legend{display:flex;flex-wrap:wrap;gap:6px 18px;font-size:12.5px;color:var(--ink-2);margin:6px 0 0}
.rb-legend i{display:inline-block;width:12px;height:10px;margin-right:6px;vertical-align:-1px;border-radius:2px}
.rb-key-t{background:var(--s4)} .rb-key-s{background:var(--s2)} .rb-key-o{background:var(--s3)}
.three{display:flex;flex-wrap:wrap;gap:10px 28px;margin:12px 0 0}
.three div{min-width:150px}
.three dt{font:600 10.5px/1.3 var(--sans);letter-spacing:.07em;text-transform:uppercase;color:var(--soft)}
.three dd{margin:3px 0 0;font:600 20px/1.1 var(--serif);font-variant-numeric:tabular-nums}
.three small{display:block;font:400 12px var(--sans);color:var(--soft);margin-top:3px}
.picks-table td.r,.picks-table th.r{text-align:right}
.picks-table td.status{white-space:nowrap}
.picks-table td.res{font-weight:600;white-space:nowrap}
.picks-table td.res.open{font-weight:400;color:var(--soft)}
.picks-table a.nm{color:var(--ink);text-decoration:none}
.picks-table a.nm:hover .tk{text-decoration:underline}
.picks-table .tk{display:block;font-weight:600}
.picks-table .co{display:block;color:var(--soft);font-size:12px}
.listed-again{display:block;font:400 12px var(--sans);color:var(--soft);white-space:normal}
.family-records .record-from,.family-records .record-restarted{display:block;font:400 12px var(--sans);color:var(--soft);white-space:normal}
.still-open-table td.stands{font-size:13px}
.still-open-table .co{display:block;color:var(--soft);font-size:12px}
.picked{margin:0 0 10px;font-size:14.5px}
.news-region{position:relative}
.news-bar{display:block;width:100%;max-width:520px;height:14px;margin:4px 0 8px}
.nb-track{fill:var(--s2)}
.name .nb-positive{fill:var(--up)}
.name .nb-negative{fill:var(--down)}
.news-counts-line{margin:0 0 8px;font-size:13.5px}
.news-unlabelled,.news-none{margin:0 0 8px;font-size:13.5px;color:var(--soft)}
.news-region>.news-tab{position:absolute;opacity:0;width:0;height:0;pointer-events:none}
.news-tabs{display:flex;flex-wrap:wrap;gap:6px;margin-bottom:10px}
.news-tabs .chip{cursor:pointer}
.news-tab[value="all"]:checked~.news-tabs .chip[for="news-tab-all"],.news-tab[value="positive"]:checked~.news-tabs .chip[for="news-tab-positive"],.news-tab[value="negative"]:checked~.news-tabs .chip[for="news-tab-negative"],.news-tab[value="neutral"]:checked~.news-tabs .chip[for="news-tab-neutral"],.news-tab[value="opinion"]:checked~.news-tabs .chip[for="news-tab-opinion"],.news-tab[value="unlabelled"]:checked~.news-tabs .chip[for="news-tab-unlabelled"]{background:var(--ink);color:var(--surface);border-color:var(--ink)}
.news-tab[value="all"]:checked~.news-tabs .chip[for="news-tab-all"] .n,.news-tab[value="positive"]:checked~.news-tabs .chip[for="news-tab-positive"] .n,.news-tab[value="negative"]:checked~.news-tabs .chip[for="news-tab-negative"] .n,.news-tab[value="neutral"]:checked~.news-tabs .chip[for="news-tab-neutral"] .n,.news-tab[value="opinion"]:checked~.news-tabs .chip[for="news-tab-opinion"] .n,.news-tab[value="unlabelled"]:checked~.news-tabs .chip[for="news-tab-unlabelled"] .n{color:var(--surface);opacity:.75}
.news-rows{list-style:none;margin:0;padding:0}
.news-row{padding:6px 0;border-top:1px solid var(--hair-2);font-size:13.5px}
.news-row .news-title{font-weight:600;color:var(--ink);text-decoration:none}
.news-row .news-title:hover,.news-row .news-title:focus{text-decoration:underline}
.news-row .tag{display:inline-block;font:600 11px var(--sans);border:1px solid var(--hair-2);border-radius:999px;padding:1px 8px;margin-left:6px;color:var(--ink-2);vertical-align:middle}
.news-row .news-meta{display:block;color:var(--soft);font-size:12px}
.news-row .news-why{display:inline-block;margin-left:6px;font:600 11px var(--sans);color:var(--soft);border-bottom:1px dotted var(--soft);cursor:help}
.news-tab[value="all"]:checked~.news-rows .news-row:not([data-tabs~="all"]),.news-tab[value="positive"]:checked~.news-rows .news-row:not([data-tabs~="positive"]),.news-tab[value="negative"]:checked~.news-rows .news-row:not([data-tabs~="negative"]),.news-tab[value="neutral"]:checked~.news-rows .news-row:not([data-tabs~="neutral"]),.news-tab[value="opinion"]:checked~.news-rows .news-row:not([data-tabs~="opinion"]),.news-tab[value="unlabelled"]:checked~.news-rows .news-row:not([data-tabs~="unlabelled"]){display:none}
.news-model{margin:8px 0 0;font-size:11.5px;color:var(--soft)}
.list-table td.news-counts{white-space:nowrap;font-variant-numeric:tabular-nums}
.list-table .np,.list-table .nn{font-weight:600}
.family-state{margin:0 0 10px;font-size:13.5px;color:var(--ink-2)}
.family-state .provisional{font-weight:600;color:var(--ink)}
.family-sweep{margin:-6px 0 10px;font-size:13.5px;color:var(--ink)}
.family-frozen{margin:0 0 10px;font-size:13px;color:var(--ink-2)}
.family-empty{margin:0 0 8px}
.fam-head{display:flex;flex-wrap:wrap;align-items:center;gap:8px;margin:0 0 10px}
.fam-head .fam-tag{font-size:14px;padding:3px 10px 3px 8px}
.fam-head .fam-count{font-size:13px;color:var(--ink-2)}
.fam-head .guide{margin-left:auto}
.fam-head .guide[open]{flex-basis:100%;margin-left:0}
.fam-head .guide>summary{list-style:none;cursor:pointer;width:26px;height:26px;border:1px solid var(--hair-2);border-radius:999px;display:inline-flex;align-items:center;justify-content:center;font:600 13px var(--sans);color:var(--ink-2)}
.fam-head .guide>summary::-webkit-details-marker{display:none}
.fam-head .guide-b{margin:8px 0 0;padding:10px 12px;border:1px solid var(--hair);border-radius:6px;background:var(--surface);font-size:13px;line-height:1.5;color:var(--ink-2)}
p.no-variant{margin:0 0 8px;font-size:12.5px;color:var(--soft)}
.rule-chips{display:flex;flex-wrap:wrap;gap:6px;list-style:none;margin:0 0 6px;padding:0}
.rule-chip{font:500 12px var(--sans);color:var(--ink-2);border:1px solid var(--hair);border-radius:999px;padding:2px 9px;background:var(--plot)}
.rule-chip.differs{border-color:var(--warn);color:var(--ink);background:var(--warn-fill);font-weight:600}
.rule-sentence{margin:0 0 10px}
.rule-sentence>summary{cursor:pointer;font-size:12.5px;color:var(--ink-2)}
.rule-share,.family-empty-line{margin:6px 0;font-size:13.5px;color:var(--ink-2)}
.family-empty-line.flagged{color:var(--ink);font-weight:600}
/* a card listing nothing folds to one line, its rule, funnel and rows behind it; a card of one line holds its guide */
.card.folded{padding:12px 18px;margin-top:12px}
.card.folded .card-b{margin-top:0}
.fam-fold>summary{position:relative;list-style:none;cursor:pointer;display:flex;flex-wrap:wrap;align-items:center;gap:6px 8px;padding-right:22px}
.fam-fold>summary::-webkit-details-marker{display:none}
.fam-fold>summary::after{content:"";position:absolute;right:4px;top:9px;width:7px;height:7px;border-right:1.5px solid var(--ink-2);border-bottom:1.5px solid var(--ink-2);transform:rotate(45deg)}
.fam-fold[open]>summary::after{transform:rotate(225deg)}
.fam-fold>summary .family-empty-line{margin:0}
.fam-fold-b{margin-top:12px}
.fam-fold-b>.guide{margin:0 0 10px}
.fam-fold-b>.guide>summary,.card-one-line .guide>summary{list-style:none;cursor:pointer;width:26px;height:26px;border:1px solid var(--hair-2);border-radius:999px;display:inline-flex;align-items:center;justify-content:center;font:600 13px var(--sans);color:var(--ink-2)}
.fam-fold-b>.guide>summary::-webkit-details-marker,.card-one-line .guide>summary::-webkit-details-marker{display:none}
.fam-fold-b .guide-b,.card-one-line .guide-b{margin:8px 0 0;padding:10px 12px;border:1px solid var(--hair);border-radius:6px;font-size:13px;line-height:1.5;color:var(--ink-2)}
.card-one-line{display:flex;flex-wrap:wrap;align-items:center;gap:8px 10px}
.card-one-line>p{flex:1 1 auto;margin:0;font-size:13.5px;color:var(--ink-2)}
.card-one-line .guide{margin-left:auto}
.card-one-line .guide[open]{flex-basis:100%;margin-left:0}
.market-links{margin:4px 0 0;font-size:13px;color:var(--ink-2)}
.hw-empties{margin:12px 0 0}
.hw-empties>summary{cursor:pointer;font:600 13px var(--sans);color:var(--ink-2)}
.hw-empty-list{list-style:none;margin:8px 0 0;padding:0;display:flex;flex-direction:column;gap:6px;font-size:13px;color:var(--ink-2)}
.hw-empty-list .hw-why{color:var(--soft)}
.funnel-bar{margin:10px 0}
.funnel-bar .rule-funnel{list-style:none;margin:0;padding:0;display:grid;gap:3px}
.funnel-bar li{display:grid;grid-template-columns:minmax(90px,150px) 1fr 76px;align-items:center;gap:8px;font-size:12px;color:var(--ink-2)}
.funnel-bar .fb-bar{height:8px;background:var(--hair);border-radius:2px;overflow:hidden}
.funnel-bar .fb-fill{display:block;height:100%;background:var(--s3)}
.funnel-bar li.largest-drop .fb-name{font-weight:600;color:var(--ink)}
.funnel-bar li.largest-drop .fb-fill{background:var(--warn)}
.funnel-bar .fb-count{text-align:right;font-variant-numeric:tabular-nums;color:var(--ink)}
.funnel-drop{margin:4px 0 0;font-size:12.5px;color:var(--ink-2)}
details.expander{margin:10px 0 0;border:1px solid var(--hair);border-radius:6px;background:var(--surface)}
details.expander>summary{cursor:pointer;padding:8px 12px;font:600 13px var(--sans);color:var(--ink)}
details.expander[open]>summary{border-bottom:1px solid var(--hair)}
details.expander>*:not(summary){margin:10px 12px}
.hw-tiles{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px;margin:8px 0 12px}
.hw-tile{background:var(--surface);border:1px solid var(--hair);border-radius:8px;padding:10px 12px;display:flex;flex-direction:column;gap:3px;min-width:0}
.hw-tile b{font:600 20px/1.2 var(--sans);font-variant-numeric:tabular-nums;color:var(--ink)}
.hw-tile span{font-size:12px;color:var(--ink-2)}
.heavyweight-table tr.hw-sector th{text-align:left;font:600 12.5px var(--sans);letter-spacing:0;text-transform:none;background:var(--plot);color:var(--ink);padding:6px 8px}
.hw-fund{font-weight:400;color:var(--ink-2);margin-left:8px}
.hw-bar{display:inline-block;width:60px;height:7px;background:var(--hair);border-radius:2px;vertical-align:middle;overflow:hidden}
.hw-fill{display:block;height:100%}
.hw-fill.gain{background:var(--up)} .hw-fill.loss{background:var(--down)}
.hw-pill{display:inline-block;font:600 11px var(--sans);border:1px solid var(--hair-2);border-radius:999px;padding:1px 8px;color:var(--ink-2);white-space:nowrap}
@media (max-width:700px){ .hw-tiles{grid-template-columns:repeat(2,minmax(0,1fr))} .funnel-bar li{grid-template-columns:minmax(70px,100px) 1fr 66px} }
.family-note{margin:8px 0 0;padding-top:8px;border-top:1px solid var(--hair-2);font-size:13px;color:var(--soft)}
.family-table td.why-tonight{font-size:13px;max-width:340px;white-space:normal}
.family-table td.plan-stop{color:var(--down)}
.family-table td.plan-target{color:var(--res-ink)}
.also-family{display:inline-block;margin-left:6px;font:400 11px var(--sans);border:1px solid var(--hair-2);border-radius:999px;padding:0 7px;color:var(--soft);vertical-align:middle}
.card-toggle{display:inline-block;margin-left:6px;font:600 11.5px var(--sans);color:var(--ink-2);background:var(--surface);border:1px solid var(--hair-2);border-radius:5px;padding:1px 8px;min-height:0;cursor:pointer;vertical-align:middle}
.card-toggle[aria-expanded='true']{background:var(--plot);color:var(--ink)}
.list-table tr.card-row>td{white-space:normal;background:var(--plot);padding:12px}
.decision-card{position:sticky;left:0;max-width:min(860px,calc(100vw - 2*var(--gutter) - 24px));font-size:13.5px;color:var(--ink)}
/* a row a table opens beneath one of its rows, a pick's plan and card, held inside the box the table scrolls in however
   wide the table is, so a phone reads it without scrolling sideways */
.tbl-wrap.opens-rows{container-type:inline-size}
.opens-rows .row-open{position:sticky;left:12px;max-width:calc(100cqw - 24px)}
.list-table .report-state{display:block;font-size:12px;color:var(--soft)}
.card-head{margin:0 0 8px;color:var(--ink-2)}
.card-lines{list-style:none;margin:0 0 12px;padding:0;display:grid;gap:6px}
.card-line{display:grid;grid-template-columns:24px 150px minmax(0,1fr);gap:8px;align-items:start}
.card-mark{display:inline-flex;align-items:center;justify-content:center;box-sizing:border-box;width:24px;height:24px;border-radius:6px;font:600 11px var(--sans)}
.card-mark svg{width:16px;height:16px;fill:none;stroke:currentColor;stroke-width:1.9;stroke-linecap:round;stroke-linejoin:round}
.card-mark svg .dot{fill:currentColor;stroke:none}
.card-mark[data-verdict='tick']{color:var(--up);background:var(--up-fill);border:1px solid var(--up)}
.card-mark[data-verdict='note']{color:var(--ink-2);background:var(--plot);border:1px solid var(--hair-2)}
.card-mark[data-verdict='warning']{color:var(--warn);background:var(--warn-fill);border:1px solid var(--warn)}
.card-record{border:1px solid var(--hair-2);border-radius:6px;padding:10px 12px}
.card-record.outline{border-style:dashed}
.card-record h5{margin:0 0 6px;font:600 13px var(--sans)}
.card-figures{display:grid;grid-template-columns:130px minmax(0,1fr);gap:4px 10px;margin:8px 0}
.card-figures dt{color:var(--soft)}
.card-figures dd{margin:0;font-variant-numeric:tabular-nums}
.card-caution{margin:6px 0 0;font-size:12.5px;color:var(--ink-2)}
.card-rule{margin:6px 0 0;font-size:12px;color:var(--soft)}
.behind-it{margin:12px 0 0;border:1px solid var(--hair-2);border-radius:6px;padding:10px 12px;max-width:560px}
.behind-it h5{margin:0 0 6px;font:600 13px var(--sans)}
.behind-lede{margin:0 0 8px;font-size:12.5px;color:var(--ink-2)}
.behind-readings,.behind-push{list-style:none;margin:0 0 10px;padding:0;display:grid;gap:4px}
.behind-readings li,.behind-push li{display:grid;grid-template-columns:minmax(0,170px) 100px 48px;gap:10px;align-items:center;font-size:13px}
.bh-bar{display:block}
.bh-track{fill:var(--s1)}
.bh-fill{fill:var(--s3)}
.bh-gain{fill:var(--up)} .bh-loss{fill:var(--down)}
.bh-zero{stroke:var(--ink-2);stroke-width:1}
.bh-figure{text-align:right;font-variant-numeric:tabular-nums}
.behind-total{margin:0;font-size:13px}
/* Tonight in the reader's words, from 22.6: the one-minute card, the six setups, each setup's card and the opened pick */
.tonight-links{margin:12px 0 0;font-size:13.5px;color:var(--ink-2)}
.card-summary{margin:0 0 10px;font-size:15px;color:var(--ink)}
.one-minute{display:grid;gap:14px}
.minute-line p{margin:0}
.minute-line .minute-said{font-size:13.5px;color:var(--ink-2)}
.market-bar{margin:10px 0 0;max-width:560px}
.market-bar svg{display:block;width:100%;height:10px}
.market-bar .bar-track{fill:var(--s1)}
.market-bar .bar-fill.open{fill:var(--up)}
.market-bar .bar-fill.closed{fill:var(--down)}
.market-bar .bar-floor{fill:var(--ink)}
.bar-labels{display:flex;justify-content:space-between;gap:8px;margin:4px 0 0;font-size:12px;color:var(--soft)}
.setup-tile .tile-top{display:flex;flex-wrap:wrap;align-items:center;gap:6px}
.setup-tile .tile-plain{font-size:13px;color:var(--ink-2)}
.setup-tile .tile-tonight{font-size:13px;font-weight:600;color:var(--ink)}
.setup-about{flex-basis:100%;margin:2px 0 0;font-size:13.5px;color:var(--ink-2)}
.rule-view{display:flex;flex-wrap:wrap;align-items:center;gap:8px;margin:0 0 10px;font-size:13px;color:var(--ink-2)}
.rule-view>*{min-width:0}
.card-h>div{min-width:0}
.rule-select{max-width:100%;min-height:34px;padding:0 8px;border-radius:6px;font:500 13px var(--sans)}
details.fold{margin:10px 0 0;border-top:1px solid var(--hair)}
details.fold>summary{cursor:pointer;padding:8px 0;font:600 13.5px var(--sans);color:var(--ink)}
details.fold>.fold-b{padding:0 0 8px}
.gloss.tonight-words{display:grid;grid-template-columns:minmax(0,180px) minmax(0,1fr);gap:6px 16px;margin:0;padding:12px 14px;font-size:13.5px}
.gloss.tonight-words .gloss-row{display:contents}
.gloss.tonight-words dt{font-weight:600}
.gloss.tonight-words dd{margin:0;color:var(--ink-2)}
.buys-read,.close-said,.versions-said,.record-said{font-size:13.5px;color:var(--ink-2)}
.decision-card h5{margin:14px 0 6px;font:600 11.5px var(--sans);letter-spacing:.1em;text-transform:uppercase;color:var(--soft)}
.decision-card .card-summary{font-size:14.5px}
.card-ticks{list-style:none;margin:0;padding:0;display:grid;gap:6px}
.card-tick{display:grid;grid-template-columns:24px minmax(0,1fr);gap:8px;align-items:start}
.plan-tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:8px;margin:0 0 8px}
.plan-tile{background:var(--surface);border:1px solid var(--hair);border-radius:8px;padding:8px 10px;display:flex;flex-direction:column;gap:2px;min-width:0}
.plan-tile .tile-label,.plan-tile .tile-under{font-size:12px;color:var(--ink-2)}
.plan-tile b{font:600 17px/1.25 var(--sans);font-variant-numeric:tabular-nums}
.plan-tile b.loss{color:var(--down)} .plan-tile b.gain{color:var(--up)}
.card-reward{margin:0 0 6px;font-weight:600}
.card-management{margin:6px 0;padding-left:18px;font-size:13px;color:var(--ink-2)}
.card-note{margin:6px 0 0;font-size:12.5px;color:var(--soft)}
.card-hits ul{list-style:none;margin:0;padding:0;display:grid;gap:6px}
.card-hits li{display:flex;align-items:flex-start;gap:10px;padding:0 0 6px;border-bottom:1px solid var(--hair)}
.card-hits .card-mark{flex:none}
.hit-when{flex:0 0 92px;font-weight:600;font-variant-numeric:tabular-nums}
.hit-what{flex:1 1 auto;min-width:0}
.hit-why{display:block;font-size:12.5px;color:var(--ink-2)}
.card-score p,.card-record p{margin:0 0 6px}
.card-compares .behind-readings li{grid-template-columns:minmax(0,150px) 100px minmax(0,1fr)}
.card-compares .bh-words{font-size:12.5px;color:var(--ink-2)}
.card-full{margin:12px 0 0;font-size:13.5px}
@media (max-width:560px){
 .card-compares .behind-readings li{grid-template-columns:minmax(0,1fr) 100px} .card-compares .bh-words{grid-column:1/-1}
 .gloss.tonight-words{grid-template-columns:1fr} .hit-when{flex-basis:70px}
}
.stt{position:relative;display:inline-block;width:120px;height:6px;border-radius:3px;background:linear-gradient(90deg,var(--fail) 0,var(--fail) 3px,var(--s2) 3px,var(--s2) calc(100% - 3px),var(--res) calc(100% - 3px))}
.stt .stt-mark{position:absolute;top:-4px;width:3px;height:14px;margin-left:-1px;border-radius:2px;background:var(--ink)}
.stt.trailing{width:auto;height:auto;background:none;font-size:12px;color:var(--soft);white-space:nowrap}
.stt.trailing .stt-stop{display:inline-block;width:3px;height:10px;margin-right:6px;border-radius:2px;background:var(--fail);vertical-align:middle}
.rp-labeller{margin:10px 0 0;font-size:13.5px}
.trade-line{display:block;overflow:visible}
.tl-track{stroke:var(--s2);stroke-width:1.5}
.tl-stop{stroke:var(--fail);stroke-width:2.5}
.tl-target{stroke:var(--res);stroke-width:2.5}
.tl-buy{stroke:var(--ink-2);stroke-width:1.5}
.tl-open{fill:var(--surface);stroke:var(--ink);stroke-width:1.6}
.tl-done{fill:var(--ink);stroke:var(--ink);stroke-width:1.6}
.tl-say{font:10.5px var(--sans);fill:var(--soft)}
.universe-table td{height:40px;padding-block:3px}
td.c-nm .co{display:block;font-size:12.5px;line-height:1.25;max-width:190px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.universe-table td[data-last-listed],.universe-table td.to-earnings{white-space:nowrap}
td details summary{cursor:pointer;color:var(--ink-2)}
.sizing{margin-bottom:12px}
.universe-paging{display:flex;justify-content:space-between;align-items:center;gap:12px;margin-top:14px;font-size:13.5px}

/* name */
.intro{margin-top:22px;padding-bottom:4px}
.intro-p{font:18px/1.55 var(--serif);max-width:64ch;margin:0}
.refuse{list-style:none;margin:12px 0 0;padding:0;display:flex;flex-direction:column;gap:2px;font-size:14px;color:var(--ink-2)}
.refuse li{padding-left:14px;position:relative}
.refuse li::before{content:"";position:absolute;left:0;top:.72em;width:7px;height:1.5px;background:var(--ink-2)}
.gloss{margin-top:14px;border:1px solid var(--hair);border-radius:6px;background:var(--surface)}
.gloss summary{cursor:pointer;padding:9px 14px;font:600 13px var(--sans);min-height:40px;display:flex;align-items:center}
.gloss[open] summary{border-bottom:1px solid var(--hair)}
.gloss .dg{margin:0;padding:12px 14px;font-size:13.5px;line-height:1.5;gap:6px 18px}
p.trend-state{display:inline-block;margin:0 0 4px;font:600 11.5px var(--sans);border:1px solid var(--hair-2);border-radius:999px;padding:1px 9px;color:var(--ink-2)}
.facts{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:0;border-top:1px solid var(--hair);margin:0}
.facts div{padding:9px 12px 9px 0;border-bottom:1px solid var(--hair)}
.facts dt{font-size:11.5px;color:var(--soft)}
.facts dd{margin:1px 0 0;font:600 15px var(--sans);font-variant-numeric:tabular-nums}
.facts dd small{font-weight:400;color:var(--soft);font-size:12px}
.fact-strip{margin:14px 0 0;font-size:13px;color:var(--soft)}
.written-section{margin:0}
.written-section>h3{display:none}
.written-section .prose{max-width:66ch;margin:0 0 10px}
.written-by{margin-top:10px;font-size:12.5px;color:var(--soft)}
.section-sources,.sources{margin:10px 0 0;padding-left:20px;font-size:13px;color:var(--ink-2)}
.section-sources li,.sources li{margin:3px 0}
.research p{margin:0 0 8px}
.research .research-state{font-weight:600}
p[data-most],.cost{font-size:13px;color:var(--soft);margin-top:8px}
.absent{border:1.3px dashed var(--dash-ink);border-radius:8px;padding:18px 20px;margin-top:20px;background:var(--surface)}
.absent h2{font-size:20px}
.cases{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:24px}
.plan-grid{display:grid;grid-template-columns:auto minmax(0,1fr);gap:24px;align-items:start}
/* the numbers' snapshot: a figure to a row in two columns, what it is on the left and the figure on
   the right with what it is measured against beneath, the rows ruled as a table's are */
dl.snapshot{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));column-gap:36px;margin:0 0 4px;border-top:1px solid var(--ink)}
dl.snapshot>div{display:flex;justify-content:space-between;align-items:baseline;gap:16px;padding:9px 0;border-bottom:1px solid var(--hair)}
dl.snapshot dt{color:var(--ink-2);font-size:13.5px}
dl.snapshot dd{margin:0;text-align:right;font-variant-numeric:tabular-nums}
dl.snapshot dd b{font-weight:600;font-size:15px}
dl.snapshot dd small{display:block;font-size:12.5px;color:var(--soft)}
/* the short card of a trade a family holds the stock on: a line to a row, what it is on the left and its figure on the
   right, ruled as the snapshot's rows are */
.held-card{max-width:620px;margin:18px 0 0;padding:12px 16px;border:1px solid var(--hair-2);border-radius:8px;background:var(--surface)}
.held-card h4{margin:0 0 8px;font:600 11.5px var(--sans);letter-spacing:.1em;text-transform:uppercase;color:var(--soft)}
.held-lines{margin:0;border-top:1px solid var(--ink)}
.held-lines>div{display:flex;justify-content:space-between;align-items:baseline;gap:16px;padding:7px 0;border-bottom:1px solid var(--hair)}
.held-lines dt{color:var(--ink-2);font-size:13.5px}
.held-lines dd{margin:0;text-align:right;font-variant-numeric:tabular-nums}
/* what management said and every other filed figure, closed until opened */
details.guidance-fold,details.numbers-detail{margin:14px 0 0;border:1px solid var(--hair);border-radius:6px;background:var(--surface)}
details.guidance-fold>summary,details.numbers-detail>summary{cursor:pointer;padding:9px 14px;font:600 13px var(--sans);min-height:40px;display:flex;align-items:center}
details.guidance-fold[open]>summary,details.numbers-detail[open]>summary{border-bottom:1px solid var(--hair)}
details.guidance-fold>blockquote,details.numbers-detail>*:not(summary){margin:12px 14px}
details.guidance-fold>summary::after,details.numbers-detail>summary::after{content:"Show";margin-left:auto;padding-left:16px;font-weight:400;color:var(--soft)}
details.guidance-fold[open]>summary::after,details.numbers-detail[open]>summary::after{content:"Hide"}
.fold-hide{display:block;margin:6px 14px 12px auto;font:600 13px var(--sans);color:var(--ink);background:transparent;border:1px solid var(--hair-2);border-radius:6px;padding:0 14px;min-height:34px;cursor:pointer}
details.guidance-fold>.fold-hide,details.numbers-detail>.fold-hide{margin:6px 14px 12px auto}
ul.guidance-passage{margin:0 0 10px;padding-left:20px}
ul.guidance-passage>li{margin:0 0 6px}
ul.guidance-passage>li.beneath{margin-left:18px;list-style-type:circle}
/* the segment table, each group's label once as a heading over its lines, the lines set in beneath
   it and their figures to the right */
.numbers-segments tr.segment-group th{padding:16px 10px 6px 0;font:600 13.5px var(--sans);letter-spacing:0;text-transform:none;color:var(--ink);border-bottom:1px solid var(--hair-2)}
.numbers-segments tr:not(.segment-group) td:first-child{padding-left:14px}
.numbers-segments tr.segment-heading td{color:var(--soft);font-style:italic}
.numbers-segments td.num,.numbers-segments tr:first-child th:last-child{text-align:right}
/* the entries and the exits, each under its own heading, the zone column one width in both so
   the two tables read as one */
.plan-sub:first-child{margin-top:0}
.tranche-table th:first-child,.exit-table th:first-child{width:11em}
.tranche-table td.num,.exit-table td.num{white-space:nowrap}
.break-even,.sizing{margin:12px 0 0;max-width:70ch}
.proposal-note{font-size:13px;color:var(--soft);margin:0 0 8px}
.moves-table td[data-cause='none'],td.cause[data-cause='none']{color:var(--soft);font-style:italic}
.moves-table td:nth-child(-n+3){white-space:nowrap}
/* the contents, at the head of a name's page: two columns of links, one on a narrow screen */
nav.contents{margin:0 0 24px;padding:16px 20px;border:1px solid var(--hair);border-radius:8px;background:var(--surface)}
nav.contents ol{list-style:none;margin:0;padding:0;columns:2;column-gap:28px;font-size:14px}
nav.contents li{margin:0 0 7px;break-inside:avoid}
nav.contents a{text-decoration:none;color:var(--soft);display:flex;gap:10px;align-items:baseline;min-height:22px}
nav.contents a:hover{color:var(--ink)}
nav.contents .c-n{color:var(--soft);font-variant-numeric:tabular-nums;min-width:1.4em;text-align:right;flex:0 0 auto}
@media (max-width:700px){nav.contents ol{columns:1}}
/* the two cases and the risks, a row to a claim or to a risk, the rows ruled apart as a table's
   are: the cases under each case's label, and what would confirm a risk set in beneath it.
   Neutral ink: the two hues are a level's and nothing else may use them. */
.case{margin:0 0 22px;max-width:78ch}
.case h4{margin:0;padding:0 0 7px;border-bottom:1px solid var(--hair-2);font:600 13px var(--sans);letter-spacing:.06em;color:var(--soft);text-transform:uppercase}
ul.claim-rows,ul.risks{list-style:none;margin:0 0 10px;padding:0;max-width:78ch}
.written-section>ul.claim-rows,.written-section>ul.risks{border-top:1px solid var(--hair-2)}
ul.claim-rows>li,ul.risks>li{padding:10px 0;border-bottom:1px solid var(--hair)}
ul.claim-rows>li:last-child,ul.risks>li:last-child{border-bottom:0}
.written-section ul.claim-rows .prose,.written-section ul.risks .prose{margin:0;max-width:none}
.written-section ul.risks .confirms{margin:6px 0 0;padding-left:12px;border-left:2px solid var(--hair);color:var(--soft);font-size:14px}
/* the risks as a table where every risk states what would confirm it: the risk and its confirmation
   side by side, a risk to a row */
table.risks-table{max-width:120ch}
table.risks-table th:first-child{width:50%}
/* the name page and its file: a reading column a thousand pixels wide, the lead under the masthead, and each region
   ruled down its left by its role, a buy in the support hue, a sale in the resistance hue, caution in amber with the
   word written, and everything else in slate, a written region in plum-grey. A figure that rose or fell is drawn in
   its own hue here and nowhere else, its sign always beside it. */
main .name,.exported .name{max-width:1000px;margin-inline:auto}
.exported>h1,.exported>p.exported{max-width:1000px;margin-inline:auto}
.m-ix{font:500 13px var(--sans);color:var(--ink-2)}
.m-price{display:inline-flex;align-items:baseline;gap:8px}
.pill.live{border-color:var(--ink);color:var(--ink);font-weight:700;letter-spacing:.06em}
.pill.closed{color:var(--ink-2);font-weight:700;letter-spacing:.06em}
.pill.new{margin-left:8px;border-color:var(--accent);color:var(--ink);font-size:10.5px;padding:0 6px}
.lead{margin:22px 0 4px}
.lead-line{font:600 24px/1.3 var(--serif);margin:0 0 14px;max-width:62ch}
.lead-tiles{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:12px;margin:0 0 6px}
.lead-tile{background:var(--surface);border:1px solid var(--hair);border-radius:8px;padding:12px 14px;display:flex;flex-direction:column;gap:3px;min-width:0}
.lead-tile .k{font:600 10.5px var(--sans);letter-spacing:.08em;text-transform:uppercase;color:var(--soft)}
.lead-tile .v{font:600 26px/1.15 var(--sans);font-variant-numeric:tabular-nums;color:var(--ink)}
.lead-tile .v.none{font-size:16px;font-weight:500;color:var(--soft)}
.lead-tile .s{font-size:12.5px;line-height:1.4;color:var(--ink-2)}
.range-bar{display:block;margin:4px 0 2px}
.range-bar .rb-track{stroke:var(--s2);stroke-width:4;stroke-linecap:round}
.range-bar .rb-dot{fill:var(--ink);stroke:var(--surface);stroke-width:2}
.lead-tile .v.gain{color:var(--up)} .lead-tile .v.loss{color:var(--down)}
.name .card{border-top-width:1px;border-top-color:var(--hair);border-left:4px solid var(--accent)}
.name .card.spined{border-left-color:var(--research)}
.name .card.spined.fund{border-left-color:var(--s3)}
.name .card[data-role='buy']{border-left-color:var(--sup)}
.name .card[data-role='caution']{border-left-color:var(--warn)}
.name .role-word{display:block;margin:0 0 4px;font:700 10.5px var(--sans);letter-spacing:.12em;text-transform:uppercase;color:var(--warn)}
.name .exit-table td:first-child{box-shadow:inset 3px 0 0 var(--res);padding-left:10px}
.name .tranche-table td:first-child{box-shadow:inset 3px 0 0 var(--sup);padding-left:10px}
.read-fold>summary,.rule-part>summary{cursor:pointer;font:600 14px var(--sans);color:var(--ink);padding:6px 0}
.rule-part{border-top:1px solid var(--hair);padding:4px 0}
.rule-part[open]>summary{margin-bottom:8px}
.technicals{margin-top:6px}
/* tables on the name page: numbers right-aligned in tabular figures, the key row marked, group rows, and bands as pills */
.name td.num,.name th.num{text-align:right;font-variant-numeric:tabular-nums}
.name tr.key-row td{background:var(--plot);font-weight:600}
.name tr.group-row td{font:600 11px var(--sans);letter-spacing:.08em;text-transform:uppercase;color:var(--soft);background:var(--page)}
.band-pill{border-width:1.5px}
.band-pill.sup{background:var(--sup-fill);border-color:var(--sup);color:var(--sup-ink)}
.band-pill.res{background:var(--res-fill);border-color:var(--res);color:var(--res-ink)}
/* two columns read side by side: what is working beside what is not, and what would turn it up beside what would make it worse */
.two-col{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px}
.two-col>div{border:1px solid var(--hair);border-radius:8px;padding:12px 14px;background:var(--surface)}
.name .two-col>.col-up{border-top:3px solid var(--up)}
.name .two-col>.col-down{border-top:3px solid var(--down)}
.two-col h4{margin:0 0 6px;font:600 12px var(--sans);letter-spacing:.08em;text-transform:uppercase;color:var(--soft)}
/* the quarter's, the margins', the analysts', the dividend's and the valuation's pictures: one axis each, a row above
   naming each series, a change by its sign in the rise or the fall hue, and every other series a step of one neutral */
.m-gridline{stroke:var(--hair);stroke-width:.6}
.mark-short{font-size:13px;color:var(--ink-2);margin:6px 0}
.written-part{margin-top:14px;padding-top:6px;border-top:1px solid var(--hair)}
.segments-raw>summary,.margins-table>summary{cursor:pointer;font:600 13.5px var(--sans);color:var(--ink);padding:6px 0}
.analyst-target,.valuation-line{font-size:14px;color:var(--ink-2);margin:10px 0}
.name .gb-rev-up{fill:var(--up)} .name .gb-rev-down{fill:var(--down)}
.name .gb-eps-up{fill:var(--up-fill);stroke:var(--up);stroke-width:1.5} .name .gb-eps-down{fill:var(--down-fill);stroke:var(--down);stroke-width:1.5}
.gb-rev.lg-swatch{fill:var(--ink-2)} .gb-eps.lg-swatch{fill:none;stroke:var(--ink-2);stroke-width:1.5}
.ml-gross,.ml-operating,.ml-net,.et-0,.et-1,.yl-yield,.yl-ten,.mb-line{fill:none;stroke-linejoin:round;stroke-width:2}
.ml-gross{stroke:var(--s3)} .ml-operating{stroke:var(--ink-2)} .ml-net{stroke:var(--ink)}
.ml-gross-dot,.lg-swatch.ml-gross{fill:var(--s3);stroke:none} .ml-operating-dot,.lg-swatch.ml-operating{fill:var(--ink-2);stroke:none} .ml-net-dot,.lg-swatch.ml-net{fill:var(--ink);stroke:none}
.sb-bar{fill:var(--s3)} .sb-change{font-size:11px;font-weight:600}
.et-0{stroke:var(--ink)} .et-1{stroke:var(--s3)} .et-0-dot,.lg-swatch.et-0{fill:var(--ink);stroke:none} .et-1-dot,.lg-swatch.et-1{fill:var(--s3);stroke:none}
.name .rt-strong-buy{fill:var(--up)} .name .rt-buy{fill:var(--up);fill-opacity:.5} .rt-hold{fill:var(--s2)} .name .rt-sell{fill:var(--down);fill-opacity:.5} .name .rt-strong-sell{fill:var(--down)}
.m-dash{fill:none;stroke:var(--dash-ink);stroke-width:1.3;stroke-dasharray:4 3}
.name .db-raised{fill:var(--up)} .name .db-cut{fill:var(--down)} .db-level{fill:var(--s3)} .db-partial{fill-opacity:.55}
.yl-yield{stroke:var(--ink)} .yl-ten{stroke:var(--s3)} .yl-yield-dot,.lg-swatch.yl-yield{fill:var(--ink);stroke:none} .yl-ten-dot,.lg-swatch.yl-ten{fill:var(--s3);stroke:none}
.mb-band,.lg-swatch.mb-band{fill:var(--s1)} .mb-line{stroke:var(--ink-2);stroke-width:1.6} .mb-dot,.lg-swatch.mb-line{fill:var(--ink-2);stroke:none} .mb-night,.lg-swatch.mb-night{fill:var(--ink)}
.pd-dot{fill:var(--s3)} .pd-own{fill:var(--ink);stroke:var(--surface);stroke-width:2} .pd-median{stroke:var(--soft);stroke-width:1.5}
/* the reactions against the typical earnings move, the long view, the ladder figure, the tranches' reward to risk and the
   chart's report and ex-dividend tags: a move by its sign in the rise or the fall hue, the plan's buys and sells in the
   level hues, and every other series a step of one neutral */
.name .rr-up{fill:var(--up)} .name .rr-down{fill:var(--down)} .rr-move.lg-swatch{fill:var(--ink-2)}
.rr-range,.lg-swatch.rr-range{fill:var(--s1)} .rr-past{stroke:var(--ink);stroke-width:1.2}
.lv-close{fill:none;stroke:var(--ink);stroke-width:1.6;stroke-linejoin:round} .lg-swatch.lv-close,.lv-dot{fill:var(--ink);stroke:none}
.lv-sup{fill:var(--sup-fill);stroke:var(--sup);stroke-width:1} .lv-res{fill:var(--res-fill);stroke:var(--res);stroke-width:1} .lv-rule{fill:var(--ink-2)}
.lf-head{font-size:11.5px;font-weight:700;letter-spacing:.08em;text-transform:uppercase} .lf-sell{fill:var(--res-ink)} .lf-buy{fill:var(--sup-ink)}
.lf-pill.lf-sup{fill:var(--sup-fill);stroke:var(--sup)} .lf-pill.lf-res{fill:var(--res-fill);stroke:var(--res)}
.lf-pill-t{font-size:11px;font-weight:600} .lf-sup .lf-pill-t{fill:var(--sup-ink)} .lf-res .lf-pill-t{fill:var(--res-ink)} .lf-use{font-weight:600}
.rk-bar{fill:var(--s3)} .rk-bar.rk-under{fill:var(--s2)} .rk-one{stroke:var(--soft);stroke-width:1.2;stroke-dasharray:4 3}
.ev-tag{fill:var(--surface);stroke:var(--ink-2);stroke-width:1} .ev-tag-t{font-size:9px;font-weight:700;fill:var(--ink)}
.typical-move,.long-view-basis,.long-view-levels,.plan-trail,.labels-cover,.shares-sum{font-size:13.5px;color:var(--ink-2);margin:8px 0}
/* the calendar's timeline, the risks' two columns and the sources: results in ink, an ex-dividend date a neutral step,
   drawn hollow where estimated, and the Fed's and the CPI's dates in the caution hue */
.ct-dot{stroke:var(--surface);stroke-width:2} .ct-results{fill:var(--ink)} .ct-ex-dividend{fill:var(--s3)} .name .ct-fomc,.name .ct-cpi{fill:var(--warn)}
.ct-ex-dividend.ct-estimated{fill:var(--surface);stroke:var(--s3);stroke-width:2}
.calendar-past,.computed-line{font-size:13.5px;color:var(--ink-2);margin:8px 0}
.risk-cols .computed-line{font-weight:600;color:var(--ink)}
.moves-table td.cause-source{font-size:12.5px;color:var(--ink-2)}
@media (max-width:700px){ .lead-tiles{grid-template-columns:repeat(2,minmax(0,1fr))} .two-col{grid-template-columns:1fr} .lead-line{font-size:20px} }
table.risks-table td{vertical-align:top;padding-top:10px;padding-bottom:10px}
.written-section table.risks-table .prose{margin:0;max-width:none;font-size:14.5px}
nav.walk{display:grid;grid-template-columns:1fr auto 1fr;gap:12px;align-items:center;margin-top:24px;padding-top:16px;border-top:1px solid var(--ink)}
nav.walk a{text-decoration:none;display:flex;flex-direction:column;min-height:44px;justify-content:center;font:600 16px var(--serif)}
nav.walk a::before{font:12px var(--sans);color:var(--soft);letter-spacing:.08em;text-transform:uppercase}
nav.walk a[rel='prev']::before{content:"Previous on the list"}
nav.walk a[rel='next']{text-align:right;align-items:flex-end}
nav.walk a[rel='next']::before{content:"Next on the list"}
nav.walk .mid{font-size:12.5px;color:var(--soft);text-align:center}

/* run */
.stage-table td.detail{font-size:12px;color:var(--ink-2);max-width:340px;overflow-wrap:anywhere}
.stage-table tr[data-outcome='failed'] td{font-weight:700}
.stage-table details summary{cursor:pointer;color:var(--soft)}
.total,.priced-calls{margin:12px 0 0;font-size:13.5px;color:var(--ink-2)}
.base-rate,.nights{margin:0 0 8px;font-size:13.5px}
.base-rate[data-pinned='true']{padding:8px 12px;background:var(--page);border-left:3px solid var(--ink)}
td.not-measured{font-size:12.5px;color:var(--ink-2)}
td.not-measured::before{content:"";display:inline-block;width:22px;height:10px;margin-right:6px;vertical-align:-1px;border:1.3px dashed var(--dash-ink);border-radius:2px}
.harness{display:block}
.harness p:first-child{font:600 20px var(--serif)}
.shadow-candidates p,.stale-and-failed p,.fell-back p,.refused-documents p{margin:0 0 8px}
.verdicts{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:0;border-top:1px solid var(--ink);margin:0}
.verdicts div{padding:12px 16px 12px 0;border-bottom:1px solid var(--hair)}
.verdicts dt{font:600 15px var(--serif)} .verdicts dd{margin:2px 0 0}
.verdicts .v{font:600 30px var(--serif);font-variant-numeric:tabular-nums;display:block}
.verdicts .x{font-size:12.5px;color:var(--soft);display:block;margin-top:2px}

/* marks */
svg text{font-family:var(--sans)}
.m-plot{fill:var(--plot)}
.m-band-sup{fill:var(--sup-fill)} .m-band-res{fill:var(--res-fill)}
.m-edge-sup{stroke:var(--sup);stroke-width:1} .m-edge-res{stroke:var(--res);stroke-width:1}
.m-wick{stroke:var(--ink);stroke-width:1}
.m-c-up{fill:var(--plot);stroke:var(--ink);stroke-width:1} .m-c-dn{fill:var(--ink);stroke:var(--ink);stroke-width:1}
.m-v-up{fill:var(--plot);stroke:var(--s3);stroke-width:.8} .m-v-dn{fill:var(--s3)}
.m-ma{fill:none;stroke-linejoin:round}
.m-ma-0{stroke:var(--s3);stroke-width:1} .m-ma-1{stroke:var(--ink-2);stroke-width:1.2} .m-ma-2{stroke:var(--s2);stroke-width:2.4}
.m-legend-t{font-size:10.5px;fill:var(--ink-2)}
.m-legend-sup{fill:var(--sup-ink)} .m-legend-res{fill:var(--res-ink)}
.m-tick-sup{fill:var(--sup-ink)} .m-tick-res{fill:var(--res-ink)}
.m-now{stroke:var(--ink);stroke-width:.8;opacity:.55}
.m-pick{stroke-width:1.2;fill:none} .m-pick-stop{stroke:var(--down)} .m-pick-target{stroke:var(--up)}
.m-tick-pick-stop{fill:var(--down)} .m-tick-pick-target{fill:var(--up)} .m-pick-beyond{font:500 11px var(--sans);fill:var(--ink-2)}
.m-tick{font-size:10.5px;fill:var(--soft);font-variant-numeric:tabular-nums}
.m-cap{font-size:11px;fill:var(--ink-2)}
.m-head{font-size:10.5px;font-weight:600;letter-spacing:.1em;fill:var(--ink)}
.m-axisline{stroke:var(--soft);stroke-width:1}
.m-nowtag{fill:var(--ink)} .m-nowtag-t{font-size:10.5px;font-weight:600;fill:var(--surface)}
.m-nowline{stroke:var(--ink);stroke-width:1.5}
.m-prof{fill:var(--s2)} .m-prof-shelf{fill:var(--s4)}
.m-evenrule{stroke:var(--soft);stroke-width:.8} .m-evenrule2{stroke:var(--ink);stroke-width:.8}
.m-sale{stroke:var(--res);stroke-width:3} .m-buy{fill:var(--sup)}
.m-zone-sup{fill:var(--sup-fill);stroke:var(--sup);stroke-width:1} .m-zone-res{fill:var(--res-fill);stroke:var(--res);stroke-width:1}
.m-row{font-size:12px;font-weight:600;fill:var(--ink)} .m-rownote{font-size:11px;fill:var(--ink-2)}
.m-stop{stroke:var(--ink-2);stroke-width:1} .m-inval{stroke:var(--ink);stroke-width:2.5}
.m-leader{stroke:var(--hair-2);stroke-width:1}
.m-neutral{fill:var(--s1)} .m-zero{stroke:var(--soft);stroke-width:1}
.m-mom{fill:none;stroke:var(--ink);stroke-width:1.4} .m-mom-2{fill:none;stroke:var(--ink-2);stroke-width:1.2;stroke-dasharray:4 3} .m-hist{fill:var(--s2)}
.m-last{fill:var(--ink)} .m-edge-rule{stroke:var(--s2);stroke-width:1}
.m-pane-h{font-size:13px;font-weight:600;fill:var(--ink)} .m-pane-c{font-size:12px;fill:var(--ink-2)}
.m-rule-t{font-size:10.5px;fill:var(--soft);font-variant-numeric:tabular-nums;paint-order:stroke;stroke:var(--plot);stroke-width:3px;stroke-linejoin:round}
/* A name's year, drawn beside its ticker in the peers table and the universe table while the pointer is over
   the cell or the ticker has focus. It is fixed to the window rather than to the table, whose box scrolls
   sideways and would cut it off, and the shell's script places it beside the cell it belongs to. */
.peers-table .peer-link{font-weight:700;letter-spacing:.02em}
.peers-table .same-industry{margin-left:6px}
.peer-pop{display:none;position:fixed;z-index:40;background:var(--surface);border:1px solid var(--hair-2);border-radius:8px;box-shadow:var(--shadow);padding:8px}
.peer:hover .peer-pop,.peer:focus-within .peer-pop,.universe-table td.c-nm:hover .peer-pop,.universe-table td.c-nm:focus-within .peer-pop{display:block}
.peer-pop svg{display:block;max-width:none}
/* A column heading's account of what its column holds, shown while the pointer is over the heading or it has
   the focus, fixed to the window and placed by the shell's script as a name's year is. It sets its own type,
   since a heading's capitals and tracking would otherwise carry into the sentence. */
th.tipped{cursor:help}
th.tipped .th-t{border-bottom:1px dotted var(--soft)}
.head-tip{display:none;position:fixed;z-index:40;width:max-content;max-width:320px;background:var(--surface);border:1px solid var(--hair-2);border-radius:8px;box-shadow:var(--shadow);padding:8px 10px;font:400 13px/1.45 var(--sans);letter-spacing:normal;text-transform:none;color:var(--ink);white-space:normal;text-align:left}
th.tipped:hover .head-tip,th.tipped:focus-within .head-tip{display:block}
.m-track{stroke:var(--s2);stroke-width:1}
.m-dist-sup{fill:var(--sup)} .m-dist-res{fill:var(--res)}
.m-link-sup{stroke:var(--sup);stroke-width:1.5;fill:none} .m-link-res{stroke:var(--res);stroke-width:1.5;fill:none}
.m-close{fill:var(--ink)}
.m-dnum{font-size:10.5px;fill:var(--ink-2);font-variant-numeric:tabular-nums}
.m-won{fill:var(--s4)} .m-lost{fill:var(--s2)} .m-resolved{fill:var(--s3)} .m-trk{fill:var(--s1)}
.m-list{fill:var(--s4)}
.m-absent{fill:none;stroke:var(--dash-ink);stroke-width:1.3;stroke-dasharray:5 4}
.m-absent-t{font-size:13px;font-weight:600;fill:var(--ink)} .m-absent-s{font-size:11px;fill:var(--ink-2)}
.m-mark{fill:var(--ink)} .m-mark-t{font-size:9.5px;font-weight:700;fill:var(--surface)}
.level-chart .m-legend-t,.level-chart .m-tick,.level-chart .m-nowtag-t,.level-chart .m-cap,.volume-profile .m-tick{font-size:15px}
.level-chart .m-mark-t{font-size:12px}
/* The chart page, from 19.11: the chart at the column's width above everything else, its interval links and its levels'
   toggle, the volume in the gain's and the loss's hue, a listing rule's lines, and the grid of figures in groups. */
.candle-chart .m-legend-t,.candle-chart .m-tick,.candle-chart .m-nowtag-t,.candle-chart .m-cap{font-size:15px}
.candle-chart .level-bands{display:none} .chart-fig.bands-on .candle-chart .level-bands{display:inline}
.candle-chart .vol-up{fill:var(--up)} .candle-chart .vol-down{fill:var(--down)} .candle-chart .vol-flat{fill:var(--muted)}
.candle-chart .plan-line{stroke-width:1.6} .candle-chart .plan-buy,.candle-chart .plan-target{stroke:var(--up)} .candle-chart .plan-stop{stroke:var(--down)}
.candle-chart .plan-tick-buy,.candle-chart .plan-tick-target{fill:var(--up)} .candle-chart .plan-tick-stop{fill:var(--down)}
.candle-chart .candle-hit{cursor:crosshair}
.chart-controls{display:flex;flex-wrap:wrap;align-items:center;gap:10px;margin:0 0 8px}
.chart-controls .intervals{display:flex;gap:4px}
.chart-controls .interval{padding:3px 10px;border:1px solid var(--hair);border-radius:4px;text-decoration:none}
.chart-controls .interval.on{background:var(--ink);color:var(--surface);border-color:var(--ink)}
.chart-reading{font-variant-numeric:tabular-nums;margin:4px 0 8px;min-height:1.4em}
.report-line{display:flex;flex-wrap:wrap;align-items:center;gap:10px;margin:8px 0 14px}
.report-line .open-report{display:inline-flex;align-items:center;text-decoration:none}
.report-standing{margin:0 0 14px}
.report-standing .rs-line{display:flex;flex-wrap:wrap;align-items:center;gap:8px;margin:0}
.report-standing .rs-why{color:var(--ink-2)}
.report-standing .rs-line+p{margin:6px 0 0;color:var(--soft);font-size:13px}
/* From 22.4 the chart page's brief read: its three panels side by side, one beneath another at a phone's width. */
.brief-summary{margin:0 0 12px;font-size:15px}
.brief-panels{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:14px}
.brief-panel h3{font-size:13px;margin:0 0 6px;text-transform:uppercase;letter-spacing:.04em;color:var(--ink-2)}
.brief-panel ul{margin:0;padding-left:18px}
.brief-panel li{margin:0 0 4px;overflow-wrap:anywhere}
.brief-chips{display:flex;flex-wrap:wrap;gap:6px;margin:12px 0 0}
.brief-watch{margin:10px 0 0}
.brief-foot{display:flex;flex-wrap:wrap;gap:8px;margin:10px 0 0;color:var(--soft);font-size:13px}
@media (max-width:760px){.brief-panels{grid-template-columns:1fr}}
/* From 22.8 the fundamentals card: each check a ticked, crossed or unscored sentence, and the lines not scored beneath. */
.fundamentals-summary{margin:0 0 10px;font-size:15px;font-weight:600}
.fundamentals-checks{list-style:none;margin:0;padding:0}
.fundamentals-checks li{display:flex;gap:8px;margin:0 0 5px;overflow-wrap:anywhere}
.fundamentals-checks li>span:last-child{min-width:0}
.fundamentals-checks .check-mark{flex:0 0 1.1em;font-weight:700;color:var(--soft)}
.fundamentals-checks li[data-answer='passed'] .check-mark{color:var(--up)}
.fundamentals-checks li[data-answer='failed'] .check-mark{color:var(--down)}
.fundamentals-awaited{margin:8px 0 0;color:var(--warn)}
.fundamentals-context-head{font-size:13px;margin:12px 0 6px;text-transform:uppercase;letter-spacing:.04em;color:var(--ink-2)}
.fundamentals-context{margin:0;padding-left:18px}
.fundamentals-context li{margin:0 0 4px;overflow-wrap:anywhere}
.fundamentals-foot{margin:10px 0 0;color:var(--soft);font-size:13px}
.chart-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:14px}
.grid-group h3{font-size:13px;margin:0 0 6px;text-transform:uppercase;letter-spacing:.04em;color:var(--ink-2)}
.grid-group dl{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1.25fr);gap:3px 10px;margin:0}
.grid-group dt{color:var(--ink-2)} .grid-group dd{margin:0;text-align:right;font-variant-numeric:tabular-nums}
/* From 20.12 the chart page's graphs beneath its chart: a print's estimate a hollow dot and its reported figure filled in
   the rise or the fall hue by its beat or its miss, the quarter to report next dashed, revenue a neutral bar beside net
   income in the rise or the fall hue by its sign with the net margin a line in ink, the price an ink dot against the
   analysts' mean target, and the analysts' ratings the report's own ramp. */
.chart-graphs{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,560px),1fr));gap:18px}
.chart-graph h3{font-size:13px;margin:0 0 6px;text-transform:uppercase;letter-spacing:.04em;color:var(--ink-2)}
.chart-graph .fig+.fig{margin-top:10px}
.mark-note{font-size:13px;color:var(--soft);margin:6px 0 0}
.ed-estimate{fill:var(--surface);stroke:var(--ink-2);stroke-width:1.5}
.ed-actual,.lg-swatch.ed-actual{fill:var(--ink-2)} .chart-graphs .ed-actual-up{fill:var(--up)} .chart-graphs .ed-actual-down{fill:var(--down)}
.ed-next{fill:none;stroke:var(--dash-ink);stroke-width:1.5;stroke-dasharray:3 2}
.ed-result{font-size:11px;font-weight:600} .ed-result.flat{fill:var(--ink-2)}
.ib-revenue{fill:var(--s3)} .lg-swatch.ib-income{fill:var(--ink-2)} .chart-graphs .ib-income-up{fill:var(--up)} .chart-graphs .ib-income-down{fill:var(--down)}
.ib-margin{fill:none;stroke:var(--ink);stroke-width:2;stroke-linejoin:round} .ib-margin-dot,.lg-swatch.ib-margin{fill:var(--ink);stroke:none}
.tm-price{fill:var(--ink)} .tm-target{fill:var(--surface);stroke:var(--ink-2);stroke-width:2}
.tm-against{font-size:13px;font-weight:700}
.tr-range{fill:var(--s3);stroke:var(--ink-2);stroke-width:1} .tr-mean{fill:var(--ink)} .tr-median{fill:var(--surface);stroke:var(--ink);stroke-width:1.5}
.firm-actions .muted,.tipranks-ratings .muted{font-size:13px;margin:4px 0 6px}
.more-actions summary{cursor:pointer;font-size:13px;margin:6px 0}
.ratings-press{display:flex;flex-wrap:wrap;align-items:center;gap:8px;margin:0 0 8px}
.ratings-calls,.ratings-said,.ratings-wait{font-size:13px;color:var(--soft)}
.tr-article{display:block;font-size:12px}
.chart-graphs .rt-strong-buy{fill:var(--up)} .chart-graphs .rt-buy{fill:var(--up);fill-opacity:.5} .chart-graphs .rt-sell{fill:var(--down);fill-opacity:.5} .chart-graphs .rt-strong-sell{fill:var(--down)}
.route-times code{font-size:12.5px}
.m-barlab-in{font-size:11px;font-weight:600;fill:var(--surface);font-variant-numeric:tabular-nums} .m-barlab-out{font-size:11px;font-weight:600;fill:var(--ink);font-variant-numeric:tabular-nums}
.sw{display:inline-block;width:12px;height:10px;margin-right:6px;vertical-align:-1px}
.sw-sup{background:var(--sup-fill);border-top:1.5px solid var(--sup);border-bottom:1.5px solid var(--sup)}
.sw-res{background:var(--res-fill);border-top:1.5px solid var(--res);border-bottom:1.5px solid var(--res)}
tr.band[data-role='support'] td:first-child::before{content:"";display:inline-block;width:12px;height:10px;margin-right:6px;vertical-align:-1px;background:var(--sup-fill);border-top:1.5px solid var(--sup);border-bottom:1.5px solid var(--sup)}
tr.band[data-role='resistance'] td:first-child::before{content:"";display:inline-block;width:12px;height:10px;margin-right:6px;vertical-align:-1px;background:var(--res-fill);border-top:1.5px solid var(--res);border-bottom:1.5px solid var(--res)}
/* The Run page: the pictures on top, status in the blues, violet and red and never in a level's hue,
   and the tables folded beneath them. */
.run-pair{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:18px;align-items:start}
.run-pair>.card{margin:0}
.night-status .ns-head{display:flex;gap:18px;align-items:center}
.status-mark .sm-ring{stroke-width:2.5}
.status-mark .sm-glyph{fill:none;stroke-width:4;stroke-linecap:round;stroke-linejoin:round}
.night-status[data-tone='ok'] .sm-ring{fill:var(--stat-fill);stroke:var(--stat)} .night-status[data-tone='ok'] .sm-glyph{stroke:var(--stat)}
.night-status[data-tone='wait'] .sm-ring{fill:var(--wait-fill);stroke:var(--wait)} .night-status[data-tone='wait'] .sm-glyph{stroke:var(--wait)}
.night-status[data-tone='fail'] .sm-ring{fill:var(--fail-fill);stroke:var(--fail)} .night-status[data-tone='fail'] .sm-glyph{stroke:var(--fail)}
.night-status[data-tone='quiet'] .sm-ring{fill:var(--panel);stroke:var(--soft)} .night-status[data-tone='quiet'] .sm-glyph{stroke:var(--soft)}
.ns-headline{font:600 28px/1.2 var(--serif);margin:2px 0}
.night-status[data-tone='fail'] .ns-headline{color:var(--fail)}
.ns-said{color:var(--ink-2);margin:0;max-width:78ch}
.night-tries{margin:8px 0 0;padding-left:20px;color:var(--ink-2);max-width:78ch}
.night-tries li{margin:2px 0}
.night-notice-box{margin:0 0 14px}
.night-notice{border-radius:8px;padding:10px 14px;margin:0;border:1px solid var(--hair);background:var(--panel)}
.night-notice[data-tone='ok']{border-color:var(--stat);background:var(--stat-fill)}
.night-notice[data-tone='wait']{border-color:var(--wait);background:var(--wait-fill)}
.night-notice[data-tone='fail']{border-color:var(--fail);background:var(--fail-fill)}
.night-notice p{margin:0}
.night-notice .nn-said{color:var(--ink-2);margin-top:4px}
.night-control{margin:10px 0 0}
.night-said{color:var(--ink-2);margin:6px 0 0}
.ns-figures{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px;margin:16px 0}
.ns-figures .tile{background:var(--panel);border-radius:8px;padding:10px 14px}
.ns-figures .tile b{display:block;font-size:24px;font-variant-numeric:tabular-nums}
.ns-figures .tile span{color:var(--soft);font-size:13px}
.step-time .caption,.breadth-line-wrap .caption{font-size:13px;color:var(--soft);margin-bottom:4px}
.step-bar{width:100%;height:24px;display:block}
.step-bar .sb-ran{fill:var(--stat)} .step-bar .sb-ran-2{fill:var(--stat-2)}
.step-bar .sb-unreached{fill:none;stroke:var(--dash-ink);stroke-dasharray:4 3}
.step-bar .sb-stopped{fill:var(--fail-fill);stroke:var(--fail);stroke-width:2;stroke-dasharray:5 4}
.step-groups{list-style:none;margin:8px 0 0;padding:0;display:flex;flex-wrap:wrap;gap:4px 18px;font-size:13px;color:var(--ink-2)}
.market-picture .mp-label{font:600 22px/1.2 var(--serif);margin:0 0 6px}
.market-picture .mp-body{display:flex;flex-wrap:wrap;gap:18px;align-items:center}
.market-picture .mp-figures p{margin:0 0 8px;color:var(--ink-2)} .market-picture .mp-figures b{font-size:20px;color:var(--ink);margin-right:4px}
.market-picture .mp-rule{font-size:13px;color:var(--soft)}
.gauge{max-width:100%;height:auto}
.gauge .g-track{fill:none;stroke:var(--hair);stroke-width:18;stroke-linecap:round}
.gauge .g-value{fill:none;stroke:var(--stat);stroke-width:18;stroke-linecap:round}
.gauge .g-floor{stroke:var(--ink);stroke-width:2.5}
.gauge .g-figure{font-size:34px;font-weight:600;fill:var(--ink)} .gauge .g-none{font-size:18px;fill:var(--soft)}
.gauge .g-floor-text,.gauge .g-caption{font-size:12px;fill:var(--soft)}
.breadth-line{width:100%;height:70px;display:block}
.breadth-line .bl-floor{stroke:var(--soft);stroke-width:1;stroke-dasharray:4 4;vector-effect:non-scaling-stroke}
.breadth-line .bl-line{fill:none;stroke:var(--stat);stroke-width:2.5;stroke-linejoin:round;vector-effect:non-scaling-stroke}
.breadth-line .bl-last{fill:var(--stat)}
.funnel-bars{width:100%;height:auto;display:block}
.funnel-bars .fb-bar{fill:var(--stat)}
.funnel-bars .fb-words{font-size:13px;font-weight:600;fill:var(--ink)} .funnel-bars .fb-count{font-size:13px;fill:var(--ink-2);font-variant-numeric:tabular-nums}
.funnel-picture .fp-listed{margin:10px 0 0;display:flex;align-items:baseline;gap:8px;flex-wrap:wrap}
.funnel-picture .fp-listed b{font-size:30px}
.funnel-picture .fp-open{margin-left:auto;font-weight:600}
.run-detail{margin-top:26px}
.run-detail>.lbl{margin-bottom:8px}
.run-detail details.fold{border-top:1px solid var(--hair);padding:8px 0}
.run-detail details.fold>summary{cursor:pointer;font-weight:600;min-height:32px;padding:4px 0}
.run-detail details.fold[open]>summary{margin-bottom:10px}
.run-pair+.run-pair{margin-top:18px}
.trades-picture .tp-body{display:flex;gap:20px;align-items:center;flex-wrap:wrap}
.trades-ring .tr-track{fill:none;stroke:var(--hair);stroke-width:10}
.trades-ring .tr-track.tr-unmet{stroke:var(--dash-ink);stroke-dasharray:6 6}
.trades-ring .tr-done{fill:none;stroke:var(--stat);stroke-width:10}
.trades-ring .tr-figure{font-size:26px;font-weight:600;fill:var(--ink)} .trades-ring .tr-caption{font-size:12px;fill:var(--soft)}
.tp-tiles,.rp-tiles,.wr-tiles{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;flex:1}
.wr-tiles{grid-template-columns:repeat(4,minmax(0,1fr))}
.trades-picture .tile,.research-picture .tile,.worry-picture .tile{background:var(--panel);border-radius:8px;padding:8px 12px}
.trades-picture .tile b,.research-picture .tile b,.worry-picture .tile b{display:block;font-size:22px;font-variant-numeric:tabular-nums}
.trades-picture .tile span,.research-picture .tile span,.worry-picture .tile span{color:var(--soft);font-size:13px}
.tp-rate,.fr-said{color:var(--ink-2)} .tp-open{margin:0;text-align:right;font-weight:600}
.fresh-bars,.pass-bars,.spend-bar{width:100%;height:auto;display:block}
.fresh-bars .fr-base{stroke:var(--hair-2);stroke-width:1}
.fresh-bars .fr-repeated{fill:var(--stat-2)} .fresh-bars .fr-new{fill:var(--stat)}
.fr-key{font-size:13px;color:var(--ink-2);display:flex;gap:6px 14px;flex-wrap:wrap;align-items:center}
.fr-key .sw-new{background:var(--stat)} .fr-key .sw-repeated{background:var(--stat-2)}
.research-picture .rp-spend{margin:0 0 6px}
.spend-bar .sp-track{fill:var(--hair)} .spend-bar .sp-spent{fill:var(--stat)}
.pass-bars .pb-bar{fill:var(--stat)} .pass-bars .pb-day{font-size:11px;fill:var(--soft)}
.worries{list-style:none;margin:0 0 12px;padding:0;display:grid;gap:8px}
.worries li{display:flex;gap:10px;align-items:flex-start}
.worry-mark{flex:none} .worry-mark path{fill:none;stroke-width:2.2;stroke-linecap:round;stroke-linejoin:round}
.worries li[data-state='held'] .worry-mark circle{fill:var(--stat-fill)} .worries li[data-state='held'] .worry-mark path{stroke:var(--stat)}
.worries li[data-state='failed'] .worry-mark circle{fill:var(--fail-fill)} .worries li[data-state='failed'] .worry-mark path{stroke:var(--fail)}
.worries li[data-state='failed'] .worry-why{color:var(--fail)}
.worries li[data-state='not read'] .worry-mark circle{fill:none;stroke:var(--dash-ink);stroke-dasharray:3 3} .worries li[data-state='not read'] .worry-mark path{stroke:var(--dash-ink)}
.run-pair+.card,.card+.run-pair{margin-top:18px}
.learning-picture .lp-clocks{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:28px}
.learning-picture .lp-head{font-weight:600;font-size:17px;margin:0 0 8px}
.lp-said,.lp-note{color:var(--ink-2);font-size:14px} .lp-note{color:var(--soft);font-size:13px}
.progress-bar{width:100%;max-width:380px;height:auto;display:block}
.progress-bar .pg-track{fill:var(--hair)} .progress-bar .pg-done{fill:var(--stat)}
.band-row{display:grid;grid-template-columns:170px minmax(0,1fr);gap:10px;align-items:center;font-size:14px}
.band-dot{width:100%;max-width:300px;height:auto}
.band-dot .bd-axis{stroke:var(--hair);stroke-width:2}
.band-dot .bd-band{fill:var(--stat-fill);stroke:var(--stat);stroke-width:1}
.band-dot .bd-band.bd-unmeasured{fill:none;stroke:var(--dash-ink);stroke-dasharray:4 3}
.band-dot .bd-dot{fill:var(--ink)}
.edge-line{width:100%;max-width:520px;height:auto;display:block}
.edge-line .el-track{stroke:var(--hair);stroke-width:6;stroke-linecap:round}
.edge-line .el-done{stroke:var(--stat);stroke-width:6;stroke-linecap:round}
.edge-line .el-today{fill:var(--stat)} .edge-line .el-check{fill:var(--surface);stroke:var(--wait);stroke-width:3}
.edge-line .el-text{font-size:12px;fill:var(--ink)} .edge-line .el-note{font-size:12px;fill:var(--soft)}
.versions-table td{vertical-align:middle}
.share-bar{vertical-align:middle} .share-bar .sh-track{fill:var(--hair)} .share-bar .sh-done{fill:var(--stat-2)}
.tag-wait{font-size:12px;color:var(--wait);border:1px dashed var(--wait);border-radius:12px;padding:2px 10px;white-space:nowrap}
.lp-open{text-align:right;font-weight:600;margin:10px 0 0}
.compare-picture .cp-choose{display:flex;flex-direction:column;gap:4px;max-width:360px;margin-bottom:10px}
.compare-picture select{font:inherit;min-height:44px;padding:6px 10px;border-radius:8px;border:1px solid var(--stat);background:var(--surface);color:var(--ink)}
.universe-pick{display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin:14px 0 6px}
.universe-pick .universe-lbl{font-size:12px;font-weight:600;letter-spacing:.06em;text-transform:uppercase;color:var(--muted)}
.universe-pick select{font:inherit;min-height:44px;padding:6px 10px;border-radius:8px;border:1px solid var(--rule);background:var(--surface);color:var(--ink)}
.compare-picture .cp-changes{background:var(--panel);border-radius:8px;padding:10px 14px}
.compare-picture .cp-body{display:grid;grid-template-columns:300px minmax(0,1fr);gap:24px;align-items:center}
.overlap-rings{max-width:100%;height:auto}
.overlap-rings .or-live{fill:var(--stat-fill);stroke:var(--stat);stroke-width:2}
.overlap-rings .or-version{fill:var(--wait-fill);stroke:var(--wait);stroke-width:2}
.overlap-rings .or-count{font-size:26px;font-weight:600;fill:var(--ink)} .overlap-rings .or-name{font-size:12px;fill:var(--soft)}
.cp-groups{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}
.cp-group{background:var(--panel);border-radius:10px;padding:10px 14px}
.cp-group .cp-heading{font-size:12px;letter-spacing:.08em;text-transform:uppercase;font-weight:600;margin:0 0 6px;color:var(--soft)}
.cp-group ul{list-style:none;margin:0;padding:0;display:grid;gap:6px}
.cp-group li a{font-weight:600} .cp-group .cp-why{display:block;font-size:13px;color:var(--ink-2)} .cp-none{color:var(--soft);font-size:13px;margin:0}
.cp-tiles{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px;margin-top:12px}
.compare-picture .tile,.checkpoint-picture .tile{background:var(--panel);border-radius:8px;padding:8px 12px}
.compare-picture .tile b{display:block;font-size:22px} .compare-picture .tile span{color:var(--soft);font-size:13px}
.checkpoint-picture .ck-key{font-size:13px;color:var(--ink-2);display:flex;flex-wrap:wrap;gap:6px 14px;align-items:center}
.ck-k{display:inline-block;width:12px;height:12px;border-radius:50%;vertical-align:-1px}
.ck-k-share{background:var(--stat)} .ck-k-even{width:3px;border-radius:0;background:var(--ink)} .ck-k-null{border:2px solid var(--soft)} .ck-k-luck{width:24px;border-radius:6px;background:var(--stat-fill)}
.ck-row{display:grid;grid-template-columns:220px minmax(0,1fr) minmax(0,1fr) auto;gap:14px;align-items:center;padding:10px 0;border-top:1px solid var(--hair)}
.ck-name{font-weight:600} .ck-words{color:var(--ink-2);font-size:14px} .ck-verdict{font-size:13px;font-weight:600}
.ck-scale{width:100%;height:auto;display:block}
.ck-scale .ck-axis{stroke:var(--hair);stroke-width:2} .ck-scale .ck-luck{fill:var(--stat-fill)} .ck-scale .ck-null{fill:none;stroke:var(--soft);stroke-width:2}
.ck-scale .ck-even{stroke:var(--ink);stroke-width:2.5} .ck-scale .ck-share{fill:var(--stat)}
.ck-locked{border:1.5px dashed var(--dash-ink);border-radius:8px;padding:4px 10px;color:var(--soft);font-size:13px;text-align:center}
.report-table td.rc{white-space:nowrap;font-weight:600}
.report-table td.rc small{display:block;font-weight:400;font-size:12px;color:var(--soft)}
.report-table td.rc-first{color:var(--stat)} .report-table td.rc-retry{color:var(--stat);font-style:italic} .report-table td.rc-out{color:var(--fail)} .report-table td.rc-earlier{color:var(--soft);font-weight:400}
.report-table small.both{color:var(--wait)!important}
.report-key,.both-sides{font-size:13px;color:var(--ink-2);margin:8px 0}
.report-why{margin:8px 0;padding-left:0;list-style:none;font-size:13px;color:var(--ink-2);display:grid;gap:4px}
.trial-list{list-style:none;margin:0;padding:0}
.trial{border-top:1px solid var(--hair);padding:10px 0}
.trial-head{margin:0 0 6px;font-weight:600}
.trial-sides,.trial-pair{display:grid;grid-auto-flow:column;grid-auto-columns:minmax(0,1fr);gap:16px}
.trial-side{margin:0;font-size:14px}
.trial-column h4{margin:6px 0;font-size:13px}
.trial-round{font-size:12px;color:var(--soft);margin:8px 0 2px}
.trial-draft p{margin:0 0 6px;font-size:14px}
@media (max-width:640px){ .trial-sides,.trial-pair{grid-auto-flow:row;grid-auto-columns:auto} }
@media (max-width:900px){ .run-pair{grid-template-columns:1fr} .ns-figures{grid-template-columns:repeat(2,minmax(0,1fr))} .wr-tiles{grid-template-columns:repeat(2,minmax(0,1fr))} .learning-picture .lp-clocks,.compare-picture .cp-body,.cp-groups,.cp-tiles{grid-template-columns:1fr} .ck-row{grid-template-columns:1fr} }

@media (max-width:640px){
 :root{--gutter:14px}
 .card{padding:16px 16px 20px}
 .card.spined{grid-template-columns:1fr} .spine{border-right:0;border-bottom:1px solid var(--hair);padding:0 0 10px;margin-bottom:12px}
 .night,.selwrap,.plan-grid,.cases,.ops,dl.snapshot{grid-template-columns:1fr}
 .facts{grid-template-columns:repeat(2,minmax(0,1fr))}
 .verdicts{grid-template-columns:repeat(2,minmax(0,1fr))}
 .headline .big{font-size:56px}
 .card-line,.card-figures{grid-template-columns:1fr}
 .card-mark{justify-self:start}
 /* a figure keeps a width its words can be read at and scrolls sideways in its box, where shrunk to the screen its
    labels stood four pixels high; the chart and its profile are sized together by their own rule and the plan column
    is drawn narrower than this already */
 .fig>svg:not(.plan-column){min-width:560px}
}
/* the universe on a phone: each row a card of the name, the close, the trend and the distance picture, the other
   columns and the headings left to a wider screen */
@media (max-width:560px){
 .universe-table,.universe-table tbody{display:block}
 .universe-table tr:first-child{display:none}
 .universe-table tr{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:4px 12px;padding:10px 0;border-bottom:1px solid var(--hair)}
 .universe-table td{display:none;height:auto;padding:0;border:0}
 .universe-table td.c-nm{display:block}
 .universe-table td:nth-child(3){display:block;text-align:right}
 .universe-table td:nth-child(4){display:block;grid-column:1/-1;color:var(--ink-2);font-size:12.5px}
 .universe-table td:nth-child(5){display:block;grid-column:1/-1}
 .sector-tiles{grid-template-columns:repeat(2,minmax(0,1fr))}
}
@media print{ .mast,.export,form[method='post']{display:none} .card{box-shadow:none;break-inside:avoid} }
""";
}
