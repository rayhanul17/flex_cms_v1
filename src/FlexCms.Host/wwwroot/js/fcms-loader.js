// ═══════════════════════════════════════════════════════════════════════════
// fcms-loader.js — Thin topbar progress-bar loader for all async operations.
//
// API:
//   fcms.loader.show()  — increment in-flight counter, show bar
//   fcms.loader.hide()  — decrement counter; hides bar when counter reaches 0
//
// Auto-patches window.fetch so every fetch() call automatically shows/hides
// the loader without per-call instrumentation. DataTables AJAX also hooks in
// via its beforeSend/complete callbacks in fcms-datatable.js.
// ═══════════════════════════════════════════════════════════════════════════

(function () {
    if (typeof window.fcms === 'undefined') window.fcms = {};

    let _count = 0;
    let _hideTimer = null;
    const BAR_ID = 'fcms-loader-bar';

    function getBar() {
        let bar = document.getElementById(BAR_ID);
        if (!bar) {
            bar = document.createElement('div');
            bar.id = BAR_ID;
            bar.setAttribute('role', 'progressbar');
            bar.setAttribute('aria-label', 'Loading');
            document.body.appendChild(bar);
        }
        return bar;
    }

    function show() {
        _count++;
        if (_hideTimer) { clearTimeout(_hideTimer); _hideTimer = null; }
        const bar = getBar();
        bar.classList.remove('fcms-loader-done', 'fcms-loader-hidden');
        bar.classList.add('fcms-loader-active');
    }

    function hide() {
        _count = Math.max(0, _count - 1);
        if (_count > 0) return;
        const bar = getBar();
        // Complete fill → short pause → fade out
        bar.classList.add('fcms-loader-done');
        _hideTimer = setTimeout(() => {
            bar.classList.remove('fcms-loader-active', 'fcms-loader-done');
            bar.classList.add('fcms-loader-hidden');
            _hideTimer = null;
        }, 400);
    }

    fcms.loader = { show, hide };

    // ── Patch window.fetch ────────────────────────────────────────────────
    // Wrap native fetch so every call participates in the loader counter
    // automatically. Same-origin and cross-origin requests both tracked.
    const _nativeFetch = window.fetch;
    window.fetch = function (...args) {
        show();
        return _nativeFetch.apply(this, args).finally(hide);
    };
})();
