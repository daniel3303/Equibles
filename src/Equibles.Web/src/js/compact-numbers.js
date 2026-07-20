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

// Some browsers expose `window.localStorage` as null (Brave with Shields up, embedded webviews,
// cookies fully blocked) or throw a SecurityError on first access in private mode. There an
// unguarded `localStorage.getItem` raises a TypeError, which killed the load-time restore and
// made the toggle button do nothing at all. Read and write through these instead: a blocked store
// degrades to "no persisted preference" and the toggle still works for the session.
function readPreference() {
    try {
        return window.localStorage?.getItem(STORAGE_KEY) === 'true';
    } catch {
        return false;
    }
}

function writePreference(value) {
    try {
        window.localStorage?.setItem(STORAGE_KEY, value);
    } catch {
        // Persisting is best-effort; the toggle already applied to the page.
    }
}

// The live choice, seeded from storage. Kept in memory so the toggle still flips both ways when the
// store is blocked — reading it back would always report "off" there and the button would stick on.
let compactEnabled = readPreference();

function toggle() {
    compactEnabled = !compactEnabled;
    writePreference(compactEnabled);
    applyFormat(compactEnabled);
}

document.addEventListener('DOMContentLoaded', () => {
    if (compactEnabled) {
        applyFormat(true);
    }
});

window.toggleCompactNumbers = toggle;
