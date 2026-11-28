using System.Globalization;

namespace EquityBrief.Web.App;

// From the operator's order of 2026-11-28 on Tonight's and Paper trading's alignment: every block at the top of a page sits on
// the page column, the content box of the screen the title, the notice box and the tiles sit on, and every block inside a card
// or a plan sits on that card's inner edges, each within a pixel. A running paragraph may be held to a reading width, its left
// edge still on the edge. A block laid out by a row or a grid beside others is placed by that row, so its own edges are not
// read, and a card inside one is read against its own inner edges; a drawing is read as one block, a control as none, and a
// block in a box scrolling sideways against that box scrolled to its start. A row of items with no left border and no
// background of its own adds no inset, so its first item starts on the edge, as the folder tabs' first tab does; a
// disclosure's summary row keeps its padding, which holds its marker as a list's holds its bullets.
// see: Every block sits on its page column or its card's inner edges as a browser lays the pages out at 1440 and 390
public static class PageColumn
{
    // How far a block's edge may stand from the edge it sits on, in pixels.
    public const double Pixels = 1;

    // One block as the browser placed it: its name, what it sits in, its left and right edges, the edges it sits on, whether it is
    // a running paragraph held to a reading width, and whether it sits in a box that scrolls sideways, its edges read as that box
    // scrolled to its start, where it may run wider than the box and never narrower.
    public sealed record Placed(string Block, string Within, double Left, double Right, double InnerLeft, double InnerRight, bool Reading, bool Scrolls = false);

    // A page as the script reads it: the window's width, the page's own width, and every block it read.
    public sealed record Laid(double Width, double ScrollWidth, IReadOnlyList<Placed> Blocks);

    // Each block of a page off the edges it sits on, naming the block, what it sits in, where it runs and where it should; and a
    // page wider than its window.
    public static IReadOnlyList<string> Faults(string page, Laid laid)
    {
        var faults = new List<string>();

        if (laid.ScrollWidth > laid.Width + Pixels)
        {
            faults.Add(Invariant($"{page}: the page runs {laid.ScrollWidth:0} px wide in a window of {laid.Width:0} px"));
        }

        if (laid.Blocks.Count == 0)
        {
            faults.Add($"{page}: the script read no block");
        }

        foreach (var block in laid.Blocks)
        {
            var left = Math.Abs(block.Left - block.InnerLeft) > Pixels;
            var right = block.Reading ? block.Right > block.InnerRight + Pixels
                : block.Scrolls ? block.Right < block.InnerRight - Pixels
                : Math.Abs(block.Right - block.InnerRight) > Pixels;

            if (left || right)
            {
                faults.Add(Invariant($"{page}: {block.Block} in {block.Within} runs {block.Left:0.#} to {block.Right:0.#}, where its edges are {block.InnerLeft:0.#} to {block.InnerRight:0.#}"));
            }
        }

        return faults;
    }

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // The cards whose blocks sit on their own inner edges.
    public const string Cards = ".card, .pl-plan";

    // What a browser reads off a page once its screen is drawn, as a JSON object of Laid's fields: the screen's children against
    // its content box, and each shown card's against its own, each through the wrappers that add no edge of their own.
    public const string Script = """
        (async () => {
          const until = Date.now() + 20000;
          const screen = document.getElementById('screen');
          while (Date.now() < until && !(screen && screen.children.length > 0)) { await new Promise((done) => setTimeout(done, 100)); }
          if (location.hash.includes('/plan/')) { while (Date.now() < until && !document.querySelector('.pl-plan.on')) { await new Promise((done) => setTimeout(done, 100)); } }
          if (document.fonts && document.fonts.ready) { await document.fonts.ready; }
          await new Promise((done) => requestAnimationFrame(() => requestAnimationFrame(done)));
          await new Promise((done) => setTimeout(done, 300));
          const cards = '.card, .pl-plan';
          const flows = ['block', 'flex', 'grid', 'table', 'list-item', 'flow-root'];
          const px = (value) => parseFloat(value) || 0;
          const name = (one) => one.tagName.toLowerCase() + (one.id ? '#' + one.id : '') + [...one.classList].sort().map((each) => '.' + each).join('') + (one.dataset && one.dataset.card ? '[' + one.dataset.card + ']' : '');
          const inner = (one) => { const box = one.getBoundingClientRect(); const style = getComputedStyle(one); return [box.left + px(style.borderLeftWidth) + px(style.paddingLeft), box.right - px(style.borderRightWidth) - px(style.paddingRight)]; };
          const shown = (one) => { const style = getComputedStyle(one); const box = one.getBoundingClientRect(); return style.display !== 'none' && style.visibility !== 'hidden' && style.position !== 'absolute' && style.position !== 'fixed' && box.width > 0 && box.height > 0; };
          const blocks = [];
          const walk = (container, within, scrolls) => {
            const style = getComputedStyle(container);
            if (container instanceof SVGElement || (container !== screen && !container.matches(cards) && style.display !== 'block')) { return; }
            const scroller = ['auto', 'scroll'].includes(style.overflowX);
            const offset = scroller ? container.scrollLeft : 0;
            const [left, right] = inner(container);
            for (const one of container.children) {
              if ((one instanceof SVGElement && one.tagName.toLowerCase() !== 'svg') || one.matches('a, button, input, select, textarea, label') || !shown(one) || !flows.includes(getComputedStyle(one).display)) { continue; }
              const box = one.getBoundingClientRect();
              const own = getComputedStyle(one);
              const bare = !one.matches(cards) && one.tagName !== 'SUMMARY' && own.display === 'flex' && px(own.borderLeftWidth) === 0 && (own.backgroundColor === 'rgba(0, 0, 0, 0)' || own.backgroundColor === 'transparent') && own.backgroundImage === 'none';
              blocks.push({ Block: name(one), Within: within, Left: (bare ? box.left + px(own.paddingLeft) : box.left) + offset, Right: box.right + offset, InnerLeft: left, InnerRight: right, Reading: one.tagName === 'P' && getComputedStyle(one).maxWidth !== 'none', Scrolls: scroller || scrolls });
              if (!one.matches(cards)) { walk(one, within, scroller || scrolls); }
            }
          };
          walk(screen, 'the page column', false);
          for (const card of screen.querySelectorAll(cards)) { const style = getComputedStyle(card); if (style.display !== 'none' && style.visibility !== 'hidden' && card.getBoundingClientRect().width > 0) { walk(card, name(card), false); } }
          return JSON.stringify({ Width: innerWidth, ScrollWidth: document.documentElement.scrollWidth, Blocks: blocks });
        })()
        """;
}
