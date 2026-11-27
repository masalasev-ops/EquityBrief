namespace EquityBrief.Web.App;

// The page's scripts that draw in the browser alone and ask nothing of the surface, stated once so the app and the
// read-only copy's shell carry the same: Home's and Tonight's waiting cards and setup chips, and Tonight's plans. Each
// reads the screen as `screen`, which both shells name so.
// see: The read-only copy draws each page as the app draws it, and a link it does not hold keeps its element
public sealed partial class SinglePageApp
{
    public const string WaitingScript = """
        // The orders still waiting on Home and Tonight: one row of cards that snaps to a card, moved by swipe, by the arrows,
        // by a chip and by the keyboard; the counter and the chips outlined say which cards are in view.
        // From the operator's correction of 2026-11-24 the cards a setup chip has hidden stand outside the counter and the arrows.
        function woAll(track) { return [...track.querySelectorAll('.wo-card, .home-idea')]; }
        function woSeen(track) {
          const cards = woAll(track).filter((card) => !card.hidden);
          const box = track.getBoundingClientRect();
          let first = -1, last = -1;
          cards.forEach((card, at) => { const r = card.getBoundingClientRect(); if (r.left >= box.left - 4 && r.right <= box.right + 4) { if (first < 0) { first = at; } last = at; } });
          if (first < 0) { first = 0; last = 0; }
          return [first, last, cards];
        }
        function woUpdate(track) {
          const group = track.closest('.wo');
          if (!group) { return; }
          const [first, last, cards] = woSeen(track);
          const count = group.querySelector('[data-wo-count]');
          if (count) { count.textContent = (first + 1) + ' to ' + (last + 1) + ' of ' + cards.length; }
          const all = woAll(track);
          for (const chip of group.querySelectorAll('.wo-chip')) { const at = cards.indexOf(all[parseInt(chip.getAttribute('data-at'), 10)]); chip.classList.toggle('in-view', at >= 0 && at >= first && at <= last); }
          const back = group.querySelector('[data-wo-step="-1"]');
          const on = group.querySelector('[data-wo-step="1"]');
          if (back) { back.disabled = first <= 0; }
          if (on) { on.disabled = last >= cards.length - 1; }
        }
        function woTo(track, card) {
          if (card && !card.hidden) { track.scrollBy({ left: card.getBoundingClientRect().left - track.getBoundingClientRect().left, behavior: 'smooth' }); }
        }
        function woGo(track, at) {
          const cards = woAll(track).filter((card) => !card.hidden);
          woTo(track, cards[Math.max(0, Math.min(at, cards.length - 1))]);
        }
        function carousels() { for (const track of screen.querySelectorAll('[data-wo-track]')) { woUpdate(track); } }
        document.addEventListener('scroll', (event) => { if (event.target.matches && event.target.matches('[data-wo-track]')) { woUpdate(event.target); } }, true);
        addEventListener('resize', () => carousels(), { passive: true });
        // From the operator's correction of 2026-11-24 a setup's chip on Home shows that setup's header and cards alone, the
        // others hidden, and its carousel's count and arrows read again now that it is in view.
        document.addEventListener('change', (event) => {
          const chosen = event.target.matches && event.target.matches('.setup-switch-input') ? event.target : null;
          const box = chosen ? chosen.closest('.setup-switch') : null;
          if (!box) { return; }
          for (const panel of box.querySelectorAll('.setup-switch-panel')) { panel.hidden = panel.getAttribute('data-setup') !== chosen.value; }
          for (const track of box.querySelectorAll('.setup-switch-panel:not([hidden]) [data-wo-track]')) { woUpdate(track); }
        });
        // From the operator's correction of 2026-11-24 a setup's chip over Waiting to buy narrows its ticker chips and its cards to
        // that setup, still nearest to its buy price first, and the heading's count and the counter follow; "All" shows every one.
        document.addEventListener('change', (event) => {
          const chosen = event.target.matches && event.target.matches('.wo-setup-input') ? event.target : null;
          const group = chosen ? chosen.closest('.wo') : null;
          if (!group) { return; }
          const kept = (one) => chosen.value === 'all' || one.getAttribute('data-family') === chosen.value;
          let shown = 0;
          for (const card of group.querySelectorAll('.wo-card')) { card.hidden = !kept(card); shown += card.hidden ? 0 : 1; for (const fold of card.querySelectorAll('details[open]')) { fold.open = false; } }
          for (const chip of group.querySelectorAll('.wo-chip')) { chip.hidden = !kept(chip); }
          const title = group.querySelector('[data-wo-title]');
          if (title) { title.textContent = 'Waiting to buy (' + shown + ')'; }
          const track = group.querySelector('[data-wo-track]');
          if (track) { track.scrollLeft = 0; woUpdate(track); }
        });
        // A waiting card's checks, opened over its lower half, close from the panel's own button.
        document.addEventListener('click', (event) => {
          const close = event.target.closest ? event.target.closest('.wo-close') : null;
          const fold = close ? close.closest('details') : null;
          if (fold) { fold.open = false; }
        });
        // From the operator's order of 2026-11-24 a card's "See it" brings its setup's header into view, the route unchanged.
        document.addEventListener('click', (event) => {
          const jump = event.target.closest ? event.target.closest('[data-jump]') : null;
          const to = jump ? document.getElementById(jump.getAttribute('data-jump')) : null;
          if (to) { to.scrollIntoView({ block: 'start', behavior: 'smooth' }); }
        });
        document.addEventListener('click', (event) => {
          const control = event.target.closest ? event.target.closest('[data-wo-step], .wo-chip') : null;
          const track = control && control.closest('.wo') ? control.closest('.wo').querySelector('[data-wo-track]') : null;
          if (!track) { return; }
          if (control.classList.contains('wo-chip')) { woTo(track, woAll(track)[parseInt(control.getAttribute('data-at'), 10)]); return; }
          const [first, last] = woSeen(track);
          const page = Math.max(1, last - first + 1);
          woGo(track, control.getAttribute('data-wo-step') === '1' ? first + page : first - page);
        });
        document.addEventListener('keydown', (event) => {
          const track = event.target.matches && event.target.matches('[data-wo-track]') ? event.target : null;
          if (!track || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) { return; }
          event.preventDefault();
          const [first, , cards] = woSeen(track);
          woGo(track, event.key === 'Home' ? 0 : event.key === 'End' ? cards.length - 1 : first + (event.key === 'ArrowLeft' ? -1 : 1));
        });
    """;

    public const string PlansScript = """
        // Tonight's plans: a row shows its plan beside the list and takes its own address without drawing the page again, full
        // screen on a phone with All plans to go back; the filter and a tile at a glance show one group; a plan's tabs show one
        // panel; and a setup's row opens its card beneath it, closing the one open.
        function showPlan(id) {
          const plans = screen.querySelector('.tn-plans');
          if (!plans) { return; }
          for (const article of plans.querySelectorAll('.pl-plan')) { const on = article.id === id; article.hidden = !on; article.classList.toggle('on', on); }
          for (const row of plans.querySelectorAll('.pl-row')) { row.classList.toggle('on', row.getAttribute('data-plan-id') === id); }
          plans.classList.add('sheet');
        }
        function filterPlans(group) {
          const plans = screen.querySelector('.tn-plans');
          if (!plans) { return; }
          for (const button of plans.querySelectorAll('.pl-filter button')) { button.classList.toggle('on', button.getAttribute('data-plan-filter') === group); }
          for (const part of plans.querySelectorAll('.pl-list [data-g]')) { part.style.display = group === 'all' || part.getAttribute('data-g') === group ? '' : 'none'; }
          plans.querySelector('.pl-list').classList.toggle('all', group === 'waiting');
        }
        screen.addEventListener('click', (event) => {
          const row = event.target.closest('.pl-row[data-plan-id]');
          if (row) {
            event.preventDefault();
            showPlan(row.getAttribute('data-plan-id'));
            history.pushState(null, '', row.getAttribute('href'));
            return;
          }
          const back = event.target.closest('.pl-back');
          if (back) { const plans = back.closest('.tn-plans'); if (plans) { plans.classList.remove('sheet'); plans.scrollIntoView({ block: 'start' }); } return; }
          const filter = event.target.closest('[data-plan-filter]');
          if (filter) {
            filterPlans(filter.getAttribute('data-plan-filter'));
            if (filter.classList.contains('g-tile')) { const plans = document.getElementById('plans'); if (plans) { plans.scrollIntoView({ block: 'start' }); } }
            return;
          }
          const goTo = event.target.closest('[data-scroll]');
          if (goTo) { const target = document.getElementById(goTo.getAttribute('data-scroll')); if (target) { target.scrollIntoView({ block: 'start' }); } return; }
          const more = event.target.closest('.pl-more');
          if (more) { more.closest('.pl-list').classList.add('all'); more.hidden = true; return; }
          const tab = event.target.closest('.pp-tabs button[data-p]');
          if (tab) {
            const article = tab.closest('.pl-plan');
            for (const button of article.querySelectorAll('.pp-tabs button')) { button.classList.toggle('on', button === tab); }
            for (const panel of article.querySelectorAll('.pp-panel')) { panel.classList.toggle('on', panel.getAttribute('data-p') === tab.getAttribute('data-p')); }
            return;
          }
          const toggle = event.target.closest('[data-setup-toggle]');
          if (toggle) {
            const family = toggle.getAttribute('data-setup-toggle');
            const table = toggle.closest('.st-tbl');
            const opening = toggle.getAttribute('aria-expanded') !== 'true';
            for (const button of table.querySelectorAll('[data-setup-toggle]')) { const on = opening && button === toggle; button.setAttribute('aria-expanded', on ? 'true' : 'false'); button.textContent = on ? 'Close' : 'Open'; button.closest('tr').classList.toggle('on', on); }
            for (const panel of table.querySelectorAll('tr.st-open')) { panel.hidden = !(opening && panel.getAttribute('data-for') === family); }
          }
        });
    """;
}
