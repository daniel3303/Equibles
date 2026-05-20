// Instant search — debounced as-you-type results on /Search, no full page reload.
// Progressive enhancement: if this doesn't run, the form still submits a normal GET.

(() => {
    const form = document.getElementById('global-search-form');
    const results = document.getElementById('search-results');
    if (!form || !results || !window.fetch || !window.AbortController) return;

    const input = form.querySelector('input[name="q"]');
    // Category is a list of same-name radios in the sidebar; the checked one is "active".
    const categoryRadios = form.querySelectorAll('input[name="category"]');
    const sort = form.querySelector('select[name="sort"]');
    const dateFrom = form.querySelector('input[name="dateFrom"]');
    const dateTo = form.querySelector('input[name="dateTo"]');
    const clearButton = document.getElementById('search-clear');
    const clearFilters = document.getElementById('search-clear-filters');
    // Mobile drawer controls.
    const filtersPanel = document.getElementById('search-filters');
    const filtersToggle = document.getElementById('search-filters-toggle');
    const filtersBackdrop = document.getElementById('search-filters-backdrop');
    if (!input) return;

    const resultsUrl = form.dataset.resultsUrl || '/Search/Results';
    const action = form.getAttribute('action') || window.location.pathname;
    let timer;
    let controller;

    // Returns the currently selected category radio value, or '' for "all".
    function getCategory() {
        for (const radio of categoryRadios) {
            if (radio.checked) return radio.value;
        }
        return '';
    }

    // Fetch the results partial for the current query + filters and swap it in.
    // push=true adds a history entry (explicit submit); otherwise the URL is replaced
    // so typing doesn't flood the back stack.
    async function run(push) {
        const params = new URLSearchParams();
        const q = input.value.trim();
        const category = getCategory();
        const sortBy = sort ? sort.value : '';
        if (q) params.set('q', q);
        if (category) params.set('category', category);
        if (sortBy && sortBy !== '0') params.set('sort', sortBy);
        if (dateFrom?.value) params.set('dateFrom', dateFrom.value);
        if (dateTo?.value) params.set('dateTo', dateTo.value);

        if (controller) controller.abort();
        controller = new AbortController();
        results.setAttribute('aria-busy', 'true');
        results.classList.add('opacity-50', 'transition-opacity');

        try {
            const response = await fetch(`${resultsUrl}?${params}`, {
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                signal: controller.signal,
            });
            if (!response.ok) return;
            results.innerHTML = await response.text();
            const url = params.toString() ? `${action}?${params}` : action;
            window.history[push ? 'pushState' : 'replaceState'](null, '', url);
            // Toggle the inline "clear input" button visibility to track input state.
            if (clearButton) clearButton.classList.toggle('hidden', q.length === 0);
        } catch (error) {
            if (error.name !== 'AbortError') console.error('[instant-search]', error);
        } finally {
            results.setAttribute('aria-busy', 'false');
            results.classList.remove('opacity-50');
        }
    }

    // Debounce typing; react immediately to filter changes or explicit submit.
    input.addEventListener('input', () => {
        clearTimeout(timer);
        timer = setTimeout(() => run(false), 250);
    });
    categoryRadios.forEach((radio) => radio.addEventListener('change', () => run(false)));
    sort?.addEventListener('change', () => run(false));
    dateFrom?.addEventListener('change', () => run(false));
    dateTo?.addEventListener('change', () => run(false));
    form.addEventListener('submit', (event) => {
        event.preventDefault();
        clearTimeout(timer);
        run(true);
    });

    // Inline "X" inside the search input — clear the query and re-fetch immediately.
    clearButton?.addEventListener('click', () => {
        input.value = '';
        input.focus();
        run(false);
    });

    // "Clear all" link in the sidebar — reset every filter to its default and re-fetch
    // instead of letting the link's GET round-trip the page.
    clearFilters?.addEventListener('click', (event) => {
        event.preventDefault();
        categoryRadios.forEach((radio) => {
            radio.checked = radio.value === '';
        });
        if (sort) sort.value = '0';
        if (dateFrom) dateFrom.value = '';
        if (dateTo) dateTo.value = '';
        run(false);
    });

    // Mobile filter drawer: open via the navbar button, close via backdrop tap or Escape.
    // Manages focus + modal semantics so screen readers and keyboard users don't get stuck.
    function setFiltersOpen(open) {
        if (!filtersPanel) return;
        filtersPanel.classList.toggle('open', open);
        filtersBackdrop?.classList.toggle('open', open);
        document.body.style.overflow = open ? 'hidden' : '';
        if (open) {
            filtersPanel.setAttribute('role', 'dialog');
            filtersPanel.setAttribute('aria-modal', 'true');
            // Move focus into the panel; falls back to the panel itself if it has no
            // focusable descendant (shouldn't happen, but defensive).
            const firstFocusable = filtersPanel.querySelector(
                'a[href], button, input, select, textarea, [tabindex]:not([tabindex="-1"])'
            );
            firstFocusable?.focus();
        } else {
            filtersPanel.removeAttribute('role');
            filtersPanel.removeAttribute('aria-modal');
            // Return focus to the trigger so keyboard users keep their place.
            filtersToggle?.focus();
        }
    }
    filtersToggle?.addEventListener('click', () => setFiltersOpen(true));
    filtersBackdrop?.addEventListener('click', () => setFiltersOpen(false));
    document.addEventListener('keydown', (event) => {
        // Only react when the drawer is actually open — otherwise Escape would
        // unstick body overflow that an unrelated modal had set.
        if (event.key === 'Escape' && filtersPanel?.classList.contains('open')) {
            setFiltersOpen(false);
        }
    });

    // Back/forward: re-sync the inputs from the URL and refresh the results.
    window.addEventListener('popstate', () => {
        const params = new URLSearchParams(window.location.search);
        input.value = params.get('q') || '';
        const category = params.get('category') || '';
        categoryRadios.forEach((radio) => {
            radio.checked = radio.value === category;
        });
        if (sort) sort.value = params.get('sort') || '0';
        if (dateFrom) dateFrom.value = params.get('dateFrom') || '';
        if (dateTo) dateTo.value = params.get('dateTo') || '';
        run(false);
    });
})();
