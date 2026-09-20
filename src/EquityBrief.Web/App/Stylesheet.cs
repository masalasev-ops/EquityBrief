namespace EquityBrief.Web.App;

// The one stylesheet, which the app's shell and the exported report both carry, so a
// report handed to someone reads as the page it came from.
// see: A single report can still be exported as a self-contained file
//
// Every colour is a token, and the comment at its head is the rule each token is held to.
// The marks draw with the tokens' names rather than with values, so both palettes restyle
// every picture without a mark knowing which one is showing.
// see: Support and resistance own two hues and nothing else uses them
// see: Not yet measured is drawn as a dashed outline, never as a pale value
public static class Stylesheet
{
    public const string Css = """
/* COLOUR RULES. Every token is one of these kinds; none of them carries a meaning not written here.
   --sup, --sup-fill, --sup-ink   A level BELOW the price. Nothing else may use this hue.
   --res, --res-fill, --res-ink   A level ABOVE the price. Nothing else may use this hue.
   --dash-ink    The stroke of a dashed outline, which always means not yet measured or not stored.
                 A neutral grey: it is never a fill, and it is never used without the dash.
   --accent      Neutral slate. The top rule of a section computed tonight, the computed badge, and controls.
   --research    Neutral plum-grey. The top rule and dateline of a section written by research or taken
                 from a filing, which may be weeks old.
   --s1 to --s4  Four steps of one neutral hue, for more or less.
   The rest are paper and ink: grounds, text, hairlines, shadow. */
:root{
 --page:#f1efe9; --surface:#fbfaf6; --plot:#f6f4ee; --ink:#1d1f22; --ink-2:#43464b; --soft:#5f6369; --hair:#dcd8ce; --hair-2:#c9c4b7;
 --accent:#4d5869; --accent-ink:#f7f6f2; --research:#6e5d72; --research-ink:#5a4a5e;
 --s1:#e4e1d9; --s2:#bab5a9; --s3:#7f7a6f; --s4:#2f2f2d;
 --sup:#2e7a53; --sup-fill:rgba(46,122,83,.12); --sup-ink:#1f5b3c;
 --res:#bd631d; --res-fill:rgba(189,99,29,.12); --res-ink:#8a4511;
 --dash-ink:#6a6d71; --shadow:0 1px 2px rgba(40,34,20,.06),0 2px 8px rgba(40,34,20,.04);
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
 --dash-ink:#a3a8ad; --shadow:0 1px 2px rgba(0,0,0,.35); color-scheme:dark;
}}
:root[data-theme='dark']:not([data-eb-theme='light']),:root[data-eb-theme='dark']{
 --page:#121416; --surface:#1a1d20; --plot:#16191b; --ink:#e8e5dd; --ink-2:#c3c0b8; --soft:#9ea3a9; --hair:#2d3237; --hair-2:#3d434a;
 --accent:#8c98aa; --accent-ink:#121416; --research:#a795ab; --research-ink:#c4b3c8;
 --s1:#262a2e; --s2:#4b5157; --s3:#868c92; --s4:#dcd8cf;
 --sup:#6fc08f; --sup-fill:rgba(111,192,143,.13); --sup-ink:#93d4ab;
 --res:#eba062; --res-fill:rgba(235,160,98,.13); --res-ink:#f2bb8c;
 --dash-ink:#a3a8ad; --shadow:0 1px 2px rgba(0,0,0,.35); color-scheme:dark;
}
/* The names the marks draw with, each the token of the same kind, so every mark follows the palette. */
:root{ --muted:var(--soft); --rule:var(--hair); --panel:var(--plot); --support:var(--sup); --resistance:var(--res); }
*{box-sizing:border-box}
html,body{margin:0}
body{background:var(--page);color:var(--ink);font:15px/1.6 var(--sans);-webkit-font-smoothing:antialiased;overflow-wrap:break-word}
a{color:var(--ink);text-decoration:underline;text-decoration-thickness:1px;text-underline-offset:3px}
a:hover{text-decoration-thickness:2px}
:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
.num,td.num,.tnum{font-variant-numeric:tabular-nums}
h1,h2,h3{font-family:var(--serif);font-weight:600;text-wrap:balance;margin:0}
.wrap{width:100%;max-width:var(--column);margin:0 auto;padding-inline:var(--gutter)}
code{font-size:.92em}

/* masthead */
.mast{position:sticky;top:env(safe-area-inset-top,0px);z-index:20;background:var(--page);border-bottom:1px solid var(--hair)}
.mast .wrap{display:grid;grid-template-columns:minmax(0,1fr) auto;align-items:center;gap:8px 20px;padding-block:10px}
.m-id{display:flex;align-items:baseline;gap:12px;flex-wrap:wrap;min-width:0}
.m-brand{font:600 12px var(--sans);letter-spacing:.14em;text-transform:uppercase;text-decoration:none;color:var(--soft)}
.m-tk{font:700 20px var(--sans);letter-spacing:.02em}
.m-co{font:600 18px var(--serif)}
.m-screen{font:600 18px var(--serif)}
.m-px{font:600 17px var(--sans);font-variant-numeric:tabular-nums}
.m-chg{font:500 14px var(--sans);font-variant-numeric:tabular-nums;color:var(--ink-2)}
.m-asof{font-size:12.5px;color:var(--soft);flex-basis:100%;margin-top:-4px}
.m-asof a{color:var(--soft)}
.m-right{display:flex;align-items:center;gap:16px}
.m-nav{display:flex;gap:14px;font-size:13.5px}
.m-nav a{text-decoration:none;color:var(--soft);padding-block:10px}
.m-nav a[aria-current='page']{color:var(--ink);text-decoration:underline;text-underline-offset:6px}
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
.list-table a.select{font:700 13px var(--sans);letter-spacing:.03em}
.list-table td.day-change{white-space:nowrap}
.list-table{table-layout:auto;min-width:820px}
.list-table th.rz,.list-table td.rz{width:64px;padding:0 3px;border-left:1px solid var(--hair);text-align:center}
.list-table th.rz{text-transform:none;letter-spacing:0;font-size:11px;color:var(--ink)}
.list-table th.rz abbr{text-decoration:none;cursor:help;border-bottom:1px dotted var(--soft)}
.list-table td.c-nm .co{display:block;font-size:12.5px;line-height:1.25;max-width:150px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.list-table a.open{font-size:11px;color:var(--soft);margin-left:4px}
.list-table td.trend-state{font-size:12.5px;color:var(--ink-2)}
.list-table .reason{position:relative;display:flex;align-items:center;justify-content:center;height:28px;padding:0 3px;background:var(--s4);color:var(--surface);font:600 10.5px/1 var(--sans);cursor:help}
.list-table .reason .record{display:none;position:absolute;z-index:10;top:32px;left:-4px;width:280px;background:var(--ink);color:var(--surface);padding:9px 11px;border-radius:6px;font:400 12px/1.45 var(--sans);text-align:left;white-space:normal}
.list-table td.rz:nth-last-child(-n+3) .reason .record{left:auto;right:-4px}
.list-table .reason:hover .record,.list-table .reason:focus .record{display:block}
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
.more,.oneline{margin:14px 0 0;font-size:13.5px;color:var(--ink-2);max-width:70ch}
.watch-list .watched{display:inline-block;margin:4px 8px 0 0}
.selwrap{display:grid;grid-template-columns:minmax(0,390px) minmax(0,1fr);gap:24px;align-items:start}
.sel-links{display:flex;flex-wrap:wrap;gap:10px;margin-top:14px}
#selected{scroll-margin-top:84px}

/* universe */
.sector-strip{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:0;margin:0}
.sector-strip .sector{padding:8px 10px;border-left:1px solid var(--hair);font-size:12.5px;color:var(--ink-2);font-variant-numeric:tabular-nums}
.sector-strip .sector a{display:block;text-decoration:none;color:inherit}
.sector-strip .sector b{display:block;font:600 12px var(--sans);color:var(--ink)}
.sector-strip .sector[aria-current='true']{background:var(--ink);color:var(--surface)} .sector-strip .sector[aria-current='true'] b{color:var(--surface)}
.universe-filters{display:flex;flex-wrap:wrap;gap:8px;align-items:center;margin-top:4px}
.chips-label{font:600 11px var(--sans);letter-spacing:.12em;text-transform:uppercase;color:var(--soft);margin-right:4px;min-width:60px}
.chip{font:600 12.5px var(--sans);text-decoration:none;color:var(--ink-2);border:1px solid var(--hair-2);border-radius:999px;padding:5px 12px;min-height:32px;display:inline-flex;align-items:center}
.chip[aria-pressed='true']{background:var(--ink);color:var(--surface);border-color:var(--ink)}
.chip-break{flex-basis:100%;height:0}
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
.card.spined[data-section='The short version'] .prose{font:19px/1.55 var(--serif);max-width:60ch}
.written-by{margin-top:10px;font-size:12.5px;color:var(--soft)}
.key-elsewhere{font-size:13.5px;color:var(--soft)}
.section-sources,.sources{margin:10px 0 0;padding-left:20px;font-size:13px;color:var(--ink-2)}
.section-sources li,.sources li{margin:3px 0}
.research p{margin:0 0 8px}
.research .research-state{font-weight:600}
p[data-most],.cost{font-size:13px;color:var(--soft);margin-top:8px}
.absent{border:1.3px dashed var(--dash-ink);border-radius:8px;padding:18px 20px;margin-top:20px;background:var(--surface)}
.absent h2{font-size:20px}
.cases{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:24px}
.plan-grid{display:grid;grid-template-columns:auto minmax(0,1fr);gap:24px;align-items:start}
.break-even,.sizing{margin:12px 0 0;max-width:70ch}
.proposal-note{font-size:13px;color:var(--soft);margin:0 0 8px}
.how-it-got-here figure{margin:0}
.how-it-got-here figcaption{font-size:12px;color:var(--soft);margin-top:4px}
.moves-table td[data-cause='none'],td.cause[data-cause='none']{color:var(--soft);font-style:italic}
/* the contents, at the head of a name's page: two columns of links, one on a narrow screen */
nav.contents{margin:0 0 24px;padding:16px 20px;border:1px solid var(--hair);border-radius:8px;background:var(--surface)}
nav.contents ol{list-style:none;margin:0;padding:0;columns:2;column-gap:28px;font-size:14px}
nav.contents li{margin:0 0 7px;break-inside:avoid}
nav.contents a{text-decoration:none;color:var(--soft);display:flex;gap:10px;align-items:baseline;min-height:22px}
nav.contents a:hover{color:var(--ink)}
nav.contents .c-n{color:var(--soft);font-variant-numeric:tabular-nums;min-width:1.4em;text-align:right;flex:0 0 auto}
@media (max-width:700px){nav.contents ol{columns:1}}
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
.overnight-queue p,.shadow-candidates p,.stale-and-failed p,.fell-back p,.refused-documents p{margin:0 0 8px}
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
.m-mom{fill:none;stroke:var(--ink);stroke-width:1.3} .m-mom-2{fill:none;stroke:var(--s3);stroke-width:1.1} .m-hist{fill:var(--s3)}
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
.m-barlab-in{font-size:11px;font-weight:600;fill:var(--surface);font-variant-numeric:tabular-nums} .m-barlab-out{font-size:11px;font-weight:600;fill:var(--ink);font-variant-numeric:tabular-nums}
.sw{display:inline-block;width:12px;height:10px;margin-right:6px;vertical-align:-1px}
.sw-sup{background:var(--sup-fill);border-top:1.5px solid var(--sup);border-bottom:1.5px solid var(--sup)}
.sw-res{background:var(--res-fill);border-top:1.5px solid var(--res);border-bottom:1.5px solid var(--res)}
tr.band[data-role='support'] td:first-child::before{content:"";display:inline-block;width:12px;height:10px;margin-right:6px;vertical-align:-1px;background:var(--sup-fill);border-top:1.5px solid var(--sup);border-bottom:1.5px solid var(--sup)}
tr.band[data-role='resistance'] td:first-child::before{content:"";display:inline-block;width:12px;height:10px;margin-right:6px;vertical-align:-1px;background:var(--res-fill);border-top:1.5px solid var(--res);border-bottom:1.5px solid var(--res)}

@media (max-width:640px){
 :root{--gutter:14px}
 .card{padding:16px 16px 20px}
 .card.spined{grid-template-columns:1fr} .spine{border-right:0;border-bottom:1px solid var(--hair);padding:0 0 10px;margin-bottom:12px}
 .night,.selwrap,.plan-grid,.cases,.ops{grid-template-columns:1fr}
 .facts{grid-template-columns:repeat(2,minmax(0,1fr))}
 .verdicts{grid-template-columns:repeat(2,minmax(0,1fr))}
 .headline .big{font-size:56px}
 .mast .wrap{grid-template-columns:1fr}
 .m-right{flex-wrap:wrap;gap:4px 14px}
 .m-search{flex-basis:100%}
 .m-search input{width:100%}
}
@media print{ .mast,.export,form[method='post']{display:none} .card{box-shadow:none;break-inside:avoid} }
""";
}
