// Row-click navigation — any element with `data-href` becomes clickable; clicks on
// inner <a> pass through so native link semantics (middle-click, modifier keys, etc.)
// stay intact. Uses event delegation so dynamically-added rows are covered too.
document.addEventListener('click', function (e) {
    const row = e.target.closest('[data-href]');
    if (!row) return;
    if (e.target.closest('a')) return;
    window.location.href = row.dataset.href;
});
