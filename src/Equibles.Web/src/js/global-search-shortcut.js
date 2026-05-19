// Global "/" search shortcut — focus the navbar search box from anywhere on the site,
// so the user can start a search without reaching for the mouse on any page.
//
// The /Search page has its own richer keyboard handling (search-keyboard.js, which also
// owns "/" there and adds result navigation). To avoid two "/" handlers fighting, this
// module stays out of the way whenever that page's form is present.

(() => {
    const input = document.getElementById('navbar-search-input');
    if (!input || document.getElementById('global-search-form')) return;

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
})();
