// Visible clear (✕) button for the /Search query box. Escape already clears the
// field (search-keyboard.js), but that is keyboard-only and undiscoverable — this
// gives a mouse affordance and keeps it in sync with what the user has typed.
//
// Clearing dispatches a bubbling `input` event so instant-search.js re-queries and
// swaps back to the empty state; no separate reset path to keep in sync.

(() => {
    const form = document.getElementById('global-search-form');
    const button = document.getElementById('search-clear');
    if (!form || !button) return;

    const input = form.querySelector('input[name="q"]');
    if (!input) return;

    const sync = () => button.classList.toggle('hidden', input.value.length === 0);

    input.addEventListener('input', sync);

    button.addEventListener('click', () => {
        input.value = '';
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.focus();
    });

    // Reflect the field's initial state (e.g. on a server-rendered query).
    sync();
})();
