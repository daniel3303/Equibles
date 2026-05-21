/* Copy-to-clipboard button wiring.
 *
 * Markup contract: any element with class `btn-copy` carrying a `data-value`
 * attribute. Click → copy the value to the clipboard, swap the icon to a
 * checkmark and the title to "Copied!" for ~1.5s, then revert. Falls back to
 * a hidden textarea + execCommand('copy') when navigator.clipboard isn't
 * available (insecure contexts).
 */

const CHECK_ICON_SVG = `<svg class="size-4 inline-block shrink-0" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>`;

async function writeToClipboard(value) {
    if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(value);
        return;
    }
    // Fallback for older browsers / http (non-secure) contexts.
    const ta = document.createElement('textarea');
    ta.value = value;
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    try {
        document.execCommand('copy');
    } finally {
        document.body.removeChild(ta);
    }
}

function flashCopied(btn) {
    const originalIconHtml = btn.innerHTML;
    const originalTitle = btn.getAttribute('title');
    btn.innerHTML = CHECK_ICON_SVG;
    btn.setAttribute('title', 'Copied!');
    btn.setAttribute('aria-label', 'Copied to clipboard');
    btn.classList.add('text-success');
    setTimeout(() => {
        btn.innerHTML = originalIconHtml;
        if (originalTitle) {
            btn.setAttribute('title', originalTitle);
            btn.setAttribute('aria-label', originalTitle);
        } else {
            btn.removeAttribute('aria-label');
        }
        btn.classList.remove('text-success');
    }, 1500);
}

document.addEventListener('click', async (e) => {
    const btn = e.target.closest('.btn-copy');
    if (!btn) return;

    // Buttons in the markup don't set type explicitly; defend against form
    // submission when one of these ever lands inside a <form>.
    e.preventDefault();

    const value = btn.getAttribute('data-value');
    if (value == null) return;

    try {
        await writeToClipboard(value);
        flashCopied(btn);
    } catch (err) {
        if (typeof window.showMessage === 'function') {
            window.showMessage('Could not copy to clipboard.', 'Error');
        }
    }
});
