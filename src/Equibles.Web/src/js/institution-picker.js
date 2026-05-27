// Institution picker — turns the chip strip + search input in [data-institution-picker]
// into a typeahead multi-picker. The server pre-renders chips for any CIKs already in
// the URL; this module wires up search, chip add/remove, and keyboard navigation.
//
// Each chip owns its own <input type="hidden" name="ciks" value="<cik>">, so removing
// the chip removes the input and the form posts `?ciks=A&ciks=B&ciks=C` — the shape
// ASP.NET's default `string[]` model binding expects without any custom splitting.
//
// Backend contract: GET data-search-url?q=...&limit=20 returns
//   [{ cik, name, city, stateOrCountry }, ...]
// The controller's projection uses lowercase property names directly — the picker
// reads them without depending on the host's JSON serializer casing policy.

(() => {
    if (!window.fetch || !window.AbortController) return;
    // Bundler injects the script at the bottom of <body>, but be defensive: if the
    // module ever loads earlier, defer until the picker markup is in the DOM.
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot, { once: true });
    } else {
        boot();
    }

    function boot() {
        const pickers = document.querySelectorAll('[data-institution-picker]');
        pickers.forEach(initPicker);
    }

    function initPicker(root) {
        const searchUrl = root.dataset.searchUrl;
        const minPicks = parseInt(root.dataset.minPicks || '0', 10);
        const maxPicks = parseInt(root.dataset.maxPicks || '99', 10);
        const box = root.querySelector('[data-institution-picker-box]');
        const chipsHost = root.querySelector('[data-institution-picker-chips]');
        const input = root.querySelector('[data-institution-picker-search]');
        const results = root.querySelector('[data-institution-picker-results]');
        const hint = root.querySelector('[data-institution-picker-hint]');
        if (!searchUrl || !box || !chipsHost || !input || !results) return;

        // Wire delete buttons on chips the server rendered.
        chipsHost.querySelectorAll('[data-institution-chip]').forEach(wireChipRemove);

        // Clicking anywhere in the chip box focuses the input — feels like one wide field.
        box.addEventListener('click', (event) => {
            if (event.target.closest('[data-institution-chip-remove]')) return;
            if (event.target.closest('[data-institution-chip]')) return;
            input.focus();
        });

        let timer;
        let controller;
        let activeIndex = -1;

        input.addEventListener('input', () => {
            clearTimeout(timer);
            timer = setTimeout(fetchSuggestions, 180);
        });

        input.addEventListener('keydown', (event) => {
            // Backspace on empty input removes the last chip — classic chip-input behaviour.
            if (event.key === 'Backspace' && input.value === '') {
                const chips = chipsHost.querySelectorAll('[data-institution-chip]');
                const last = chips[chips.length - 1];
                if (last) {
                    removeChip(last);
                    event.preventDefault();
                }
                return;
            }
            // Enter with non-empty input should ALWAYS pick — never let the browser
            // submit the form mid-typeahead. If the dropdown is still loading, swallow
            // the keystroke too; we don't want to lose what the user typed.
            if (event.key === 'Enter' && input.value.trim() !== '') {
                event.preventDefault();
                const options = Array.from(results.querySelectorAll('[data-pick-option]'));
                const target = activeIndex >= 0 ? options[activeIndex] : options[0];
                if (target) selectOption(target);
                return;
            }
            const visible = !results.classList.contains('hidden');
            if (!visible) return;
            const options = Array.from(results.querySelectorAll('[data-pick-option]'));
            if (options.length === 0) return;

            if (event.key === 'ArrowDown') {
                event.preventDefault();
                activeIndex = (activeIndex + 1) % options.length;
                highlight(options);
            } else if (event.key === 'ArrowUp') {
                event.preventDefault();
                activeIndex = (activeIndex - 1 + options.length) % options.length;
                highlight(options);
            } else if (event.key === 'Escape') {
                hideResults();
            }
        });

        // Close the dropdown when focus leaves the picker, but let the user finish
        // clicking a result first (focusout fires before the click handler).
        document.addEventListener('click', (event) => {
            if (!root.contains(event.target)) hideResults();
        });

        async function fetchSuggestions() {
            const q = input.value.trim();
            if (q.length < 2) {
                hideResults();
                return;
            }
            if (atMaxPicks()) {
                results.innerHTML = '';
                renderMessage(`You can compare at most ${maxPicks} institutions.`);
                showResults();
                return;
            }
            if (controller) controller.abort();
            controller = new AbortController();
            try {
                const response = await fetch(
                    `${searchUrl}?q=${encodeURIComponent(q)}&limit=20`,
                    { signal: controller.signal, headers: { Accept: 'application/json' } }
                );
                if (!response.ok) {
                    hideResults();
                    return;
                }
                const rows = await response.json();
                renderResults(rows, q);
            } catch (error) {
                if (error.name !== 'AbortError') console.error('[institution-picker]', error);
            }
        }

        function renderResults(rows, query) {
            const picked = new Set(currentCiks());
            const available = rows.filter((row) => !picked.has(row.cik));
            results.innerHTML = '';
            activeIndex = -1;

            if (available.length === 0) {
                renderMessage(`No matches for "${query}"`);
                showResults();
                return;
            }

            available.forEach((row, index) => {
                const location = [row.city, row.stateOrCountry].filter(Boolean).join(', ');
                const li = document.createElement('li');
                li.setAttribute('data-pick-option', '');
                li.setAttribute('role', 'option');
                li.dataset.cik = row.cik || '';
                li.dataset.name = row.name || '';
                li.innerHTML = `
                    <button type="button" class="flex flex-col items-start gap-0.5 w-full text-left">
                        <span class="font-medium">${escapeHtml(row.name || '(unnamed)')}</span>
                        <span class="text-xs opacity-60 font-mono">
                            ${escapeHtml(row.cik || '')}${location ? ` · ${escapeHtml(location)}` : ''}
                        </span>
                    </button>`;
                li.addEventListener('mouseenter', () => {
                    activeIndex = index;
                    highlight(Array.from(results.querySelectorAll('[data-pick-option]')));
                });
                // mousedown fires before the document-level click that would hide the
                // dropdown, so the pick lands reliably.
                li.addEventListener('mousedown', (event) => {
                    event.preventDefault();
                    selectOption(li);
                });
                results.appendChild(li);
            });
            showResults();
        }

        function renderMessage(text) {
            const li = document.createElement('li');
            li.className = 'menu-disabled';
            li.innerHTML = `<span class="text-sm text-base-content/60">${escapeHtml(text)}</span>`;
            results.appendChild(li);
        }

        function highlight(options) {
            options.forEach((option, index) => {
                option.classList.toggle('bg-base-200', index === activeIndex);
            });
        }

        function selectOption(option) {
            const cik = option.dataset.cik;
            const name = option.dataset.name;
            if (!cik) return;
            addChip(cik, name);
            input.value = '';
            hideResults();
            input.focus();
        }

        function addChip(cik, name) {
            if (currentCiks().includes(cik)) return;
            if (atMaxPicks()) return;
            const chip = document.createElement('span');
            chip.className = 'badge badge-lg gap-1 badge-primary badge-soft';
            chip.setAttribute('data-institution-chip', '');
            chip.dataset.cik = cik;
            const displayName = name || '(name unknown)';
            chip.innerHTML = `
                <input type="hidden" name="ciks" />
                <span class="font-medium"></span>
                <span class="text-xs opacity-70 font-mono"></span>
                <button type="button" class="btn btn-ghost btn-xs btn-circle"
                        aria-label="Remove ${escapeAttr(displayName)}"
                        data-institution-chip-remove>
                    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24"
                         stroke-width="1.5" stroke="currentColor"
                         class="size-3 inline-block shrink-0">
                        <path stroke-linecap="round" stroke-linejoin="round" d="M6 18 18 6M6 6l12 12" />
                    </svg>
                </button>`;
            chip.querySelector('input[name="ciks"]').value = cik;
            chip.querySelector('.font-medium').textContent = displayName;
            chip.querySelector('.font-mono').textContent = cik;
            chipsHost.appendChild(chip);
            wireChipRemove(chip);
            updateHint();
            updateInputPlaceholder();
        }

        function wireChipRemove(chip) {
            const button = chip.querySelector('[data-institution-chip-remove]');
            button?.addEventListener('click', (event) => {
                event.preventDefault();
                event.stopPropagation();
                removeChip(chip);
            });
        }

        function removeChip(chip) {
            chip.remove();
            updateHint();
            updateInputPlaceholder();
        }

        function currentCiks() {
            return Array.from(chipsHost.querySelectorAll('[data-institution-chip]'))
                .map((chip) => chip.dataset.cik)
                .filter(Boolean);
        }

        function atMaxPicks() {
            return currentCiks().length >= maxPicks;
        }

        function updateInputPlaceholder() {
            input.placeholder = chipsHost.querySelector('[data-institution-chip]')
                ? 'Add another...'
                : 'Type an institution name or CIK...';
        }

        function updateHint() {
            if (!hint) return;
            const count = currentCiks().length;
            if (count < minPicks) {
                hint.textContent = `Pick at least ${minPicks} institutions (currently ${count}).`;
                hint.classList.add('text-warning');
                hint.classList.remove('text-base-content/50');
            } else if (count >= maxPicks) {
                hint.textContent = `Maximum ${maxPicks} institutions reached.`;
                hint.classList.add('text-warning');
                hint.classList.remove('text-base-content/50');
            } else {
                hint.textContent = `${count} selected. Type to add more (up to ${maxPicks}).`;
                hint.classList.remove('text-warning');
                hint.classList.add('text-base-content/50');
            }
        }

        function showResults() {
            results.classList.remove('hidden');
        }

        function hideResults() {
            results.classList.add('hidden');
            activeIndex = -1;
        }

        function escapeHtml(text) {
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }

        function escapeAttr(text) {
            return String(text).replace(/"/g, '&quot;');
        }

        updateHint();
    }
})();
