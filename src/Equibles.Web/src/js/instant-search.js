// Instant search — debounced as-you-type results on /Search, no full page reload.
// Progressive enhancement: if this doesn't run, the form still submits a normal GET.

(() => {
    const form = document.getElementById('global-search-form');
    const results = document.getElementById('search-results');
    if (!form || !results || !window.fetch || !window.AbortController) return;

    const input = form.querySelector('input[name="q"]');
    const scope = form.querySelector('select[name="category"]');
    if (!input) return;

    const resultsUrl = form.dataset.resultsUrl || '/Search/Results';
    const action = form.getAttribute('action') || window.location.pathname;
    let timer;
    let controller;

    // Fetch the results partial for the current query + scope and swap it in.
    // push=true adds a history entry (explicit submit); otherwise the URL is replaced
    // so typing doesn't flood the back stack.
    async function run(push) {
        const params = new URLSearchParams();
        const q = input.value.trim();
        const category = scope ? scope.value : '';
        if (q) params.set('q', q);
        if (category) params.set('category', category);

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
        } catch (error) {
            if (error.name !== 'AbortError') console.error('[instant-search]', error);
        } finally {
            results.setAttribute('aria-busy', 'false');
            results.classList.remove('opacity-50');
        }
    }

    // Debounce typing; react immediately to a scope change or explicit submit.
    input.addEventListener('input', () => {
        clearTimeout(timer);
        timer = setTimeout(() => run(false), 250);
    });
    scope?.addEventListener('change', () => run(false));
    form.addEventListener('submit', (event) => {
        event.preventDefault();
        clearTimeout(timer);
        run(true);
    });

    // Back/forward: re-sync the inputs from the URL and refresh the results.
    window.addEventListener('popstate', () => {
        const params = new URLSearchParams(window.location.search);
        input.value = params.get('q') || '';
        if (scope) scope.value = params.get('category') || '';
        run(false);
    });
})();
