// Global Cmd/Ctrl+K search shortcut — focuses the navbar search box from anywhere
// on the site, matching the convention used by GitHub / Linear / Vercel and the
// commercial portal.
//
// The /Search page has its own richer keyboard handling (search-keyboard.js, which
// also owns the shortcut there and adds result navigation). To avoid two handlers
// fighting, this module stays out of the way whenever that page's form is present.

(() => {
    const input = document.getElementById('navbar-search-input');
    if (!input || document.getElementById('global-search-form')) return;

    // Cmd+K (macOS) / Ctrl+K (other) focuses search from anywhere — including
    // from inside another input. The modifier keeps the binding unambiguous,
    // so we don't have to suppress it while the user is typing elsewhere.
    document.addEventListener('keydown', (e) => {
        if (e.key.toLowerCase() !== 'k') return;
        if (!(e.metaKey || e.ctrlKey)) return;
        if (e.shiftKey || e.altKey) return;
        e.preventDefault();
        input.focus();
        input.select();
    });
})();
