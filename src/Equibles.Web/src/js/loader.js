/* Loader overlay for form submissions */

function initLoader() {
    if (!document.querySelector('.loader')) {
        const loader = document.createElement('div');
        loader.className = 'loader';
        loader.innerHTML = '<div class="message"></div><div class="icon"><span class="loading loading-spinner loading-lg"></span></div>';
        document.body.appendChild(loader);
    }
}

export function showLoader(content) {
    initLoader();
    if (!content) content = 'Please wait...';
    document.querySelector('.loader .message').innerHTML = content;
    document.querySelector('.loader').style.display = 'block';
}

export function hideLoader() {
    const loader = document.querySelector('.loader');
    if (loader) {
        loader.querySelector('.message').innerHTML = '';
        loader.style.display = 'none';
    }
}

window.showLoader = showLoader;
window.hideLoader = hideLoader;

document.addEventListener('DOMContentLoaded', function() {
    initLoader();

    // Auto-show loader on form submit with data-submitting-message
    document.addEventListener('submit', function(e) {
        const form = e.target;
        if (form.hasAttribute('data-submitting-message')) {
            const message = form.getAttribute('data-submitting-message');
            if (message) showLoader(message);
        }
    });
});
