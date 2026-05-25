/**
 * Compact number toggle for tables.
 *
 * Add data-compact-number to any <td> that contains a numeric value.
 * A toggle button (.btn-compact-toggle) switches between full and compact display.
 * State persists via localStorage.
 */

const STORAGE_KEY = 'compactNumbers';

function formatCompact(value) {
    const abs = Math.abs(value);
    if (abs >= 1e12) return (value / 1e12).toFixed(1) + 'T';
    if (abs >= 1e9) return (value / 1e9).toFixed(1) + 'B';
    if (abs >= 1e6) return (value / 1e6).toFixed(1) + 'M';
    if (abs >= 1e3) return (value / 1e3).toFixed(0) + 'K';
    return value.toLocaleString('en-US');
}

function applyFormat(compact) {
    document.querySelectorAll('[data-compact-number]').forEach(cell => {
        if (compact) {
            if (!cell.dataset.fullText) cell.dataset.fullText = cell.textContent;
            const raw = parseFloat(cell.dataset.compactNumber);
            if (isNaN(raw)) return;
            const prefix = cell.dataset.compactPrefix || '';
            cell.textContent = prefix + formatCompact(raw);
        } else if (cell.dataset.fullText) {
            cell.textContent = cell.dataset.fullText;
        }
    });

    document.querySelectorAll('.btn-compact-toggle').forEach(el => {
        el.checked = compact;
    });
}

function toggle() {
    const current = localStorage.getItem(STORAGE_KEY) === 'true';
    const next = !current;
    localStorage.setItem(STORAGE_KEY, next);
    applyFormat(next);
}

document.addEventListener('DOMContentLoaded', () => {
    if (localStorage.getItem(STORAGE_KEY) === 'true') {
        applyFormat(true);
    }
});

window.toggleCompactNumbers = toggle;
