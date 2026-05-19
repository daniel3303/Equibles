// Search keyboard navigation — makes the search usable without the mouse:
//   "/"            focus the search box from anywhere on the page
//   ArrowDown/Up   move a highlight through the rendered result links
//   Enter          open the highlighted result (falls back to normal submit)
//   Escape         clear the query
// Works with instant-search.js: results are re-queried live, and the highlight
// resets whenever that module swaps #search-results.

(() => {
    const form = document.getElementById('global-search-form');
    const results = document.getElementById('search-results');
    if (!form || !results) return;

    const input = form.querySelector('input[name="q"]');
    if (!input) return;

    let activeIndex = -1;

    // Visible result-hit anchors only — scoped to the group cards' list items
    // so the filter chips and "See all" links are not part of the rotation.
    // Recomputed each time (instant-search replaces the container).
    const hits = () =>
        [...results.querySelectorAll('.card li a[href]')].filter(a => a.offsetParent !== null);

    // Inline styles (not Tailwind classes) so the highlight needs no CSS-build
    // step and stays theme-agnostic — outline uses currentColor.
    function paint(el, on) {
        el.style.outline = on ? '2px solid' : '';
        el.style.outlineOffset = on ? '-2px' : '';
        el.style.borderRadius = on ? '0.375rem' : '';
    }

    function clear() {
        hits().forEach(a => paint(a, false));
        activeIndex = -1;
        input.removeAttribute('aria-activedescendant');
    }

    function highlight(next) {
        const list = hits();
        if (!list.length) { clear(); return; }
        list.forEach(a => paint(a, false));
        activeIndex = (next + list.length) % list.length;
        const el = list[activeIndex];
        paint(el, true);
        el.scrollIntoView({ block: 'nearest' });
        if (el.id) input.setAttribute('aria-activedescendant', el.id);
    }

    // "/" focuses search — unless the user is already typing in a field.
    document.addEventListener('keydown', (e) => {
        if (e.key !== '/') return;
        const tag = document.activeElement?.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT'
            || document.activeElement?.isContentEditable) return;
        e.preventDefault();
        input.focus();
        input.select();
    });

    // Arrow / Enter / Escape while the search box has focus.
    input.addEventListener('keydown', (e) => {
        if (e.key === 'ArrowDown') {
            e.preventDefault();
            highlight(activeIndex + 1);
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            highlight(activeIndex - 1);
        } else if (e.key === 'Enter') {
            const list = hits();
            if (activeIndex >= 0 && list[activeIndex]) {
                // Open the highlighted hit instead of submitting the form.
                e.preventDefault();
                list[activeIndex].click();
            }
        } else if (e.key === 'Escape') {
            if (input.value) {
                input.value = '';
                input.dispatchEvent(new Event('input', { bubbles: true }));
            }
            clear();
        }
    });

    // Reset the highlight when instant-search replaces the results.
    new MutationObserver(() => { activeIndex = -1; })
        .observe(results, { childList: true });
})();
