// Ajax modal system — vanilla JS port of the Backoffice jQuery modal pattern.
// Provides showAjaxViewModal(url, title) to fetch server-rendered partials
// and display them in the #ajax-modal dialog.

const loadingHtml = "<div class='flex flex-col items-center justify-center py-8'><span class='loading loading-spinner loading-lg text-primary'></span><p class='mt-4 text-base-content/60'>Loading...</p></div>";

// Show dialog by ID
function showDialog(dialogId) {
    const dialog = document.getElementById(dialogId);
    if (dialog && typeof dialog.showModal === 'function') {
        dialog.showModal();
    }
}

// Close dialog by ID
function closeDialog(dialogId) {
    const dialog = document.getElementById(dialogId);
    if (dialog && typeof dialog.close === 'function') {
        dialog.close();
    }
}

// Fetch a partial view via GET and inject into the ajax modal body
export function showAjaxViewModal(url, title) {
    const modal = document.getElementById('ajax-modal');
    if (!modal) return;

    modal.querySelector('.title-text').textContent = title;
    modal.querySelector('.modal-body').innerHTML = loadingHtml;

    showDialog('ajax-modal');

    fetch(url)
        .then(function(response) {
            if (!response.ok) throw new Error(response.statusText);
            return response.text();
        })
        .then(function(html) {
            const body = modal.querySelector('.modal-body');
            body.innerHTML = html;
            // innerHTML doesn't execute <script> tags — re-create them so they run
            body.querySelectorAll('script').forEach(function(orig) {
                const s = document.createElement('script');
                s.textContent = orig.textContent;
                orig.replaceWith(s);
            });
        })
        .catch(function(error) {
            modal.querySelector('.modal-body').innerHTML =
                '<div class="alert alert-error">' + (error.message || 'An error occurred, please try again later.') + '</div>';
        });
}
window.showAjaxViewModal = showAjaxViewModal;

// Close the ajax modal
export function closeAjaxModal() {
    closeDialog('ajax-modal');
}
window.closeAjaxModal = closeAjaxModal;

// Event delegation for trigger elements and close button
document.addEventListener('click', function(e) {
    // .show-ajax-view-modal trigger
    const viewTrigger = e.target.closest('.show-ajax-view-modal');
    if (viewTrigger) {
        e.preventDefault();
        showAjaxViewModal(viewTrigger.dataset.url, viewTrigger.dataset.title);
        return;
    }

    // .modal-close button inside #ajax-modal
    const closeBtn = e.target.closest('#ajax-modal .modal-close');
    if (closeBtn) {
        e.preventDefault();
        closeDialog('ajax-modal');
    }
});
