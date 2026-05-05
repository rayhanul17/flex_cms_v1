// ═══════════════════════════════════════════════════════════════════════════
// fcms.toast — Bootstrap 5 toasts with success / danger / warning / info
// Container #fcmsToastContainer in _FcmsConfirm.cshtml.
// ═══════════════════════════════════════════════════════════════════════════

(function () {
    if (typeof window.fcms === 'undefined') window.fcms = {};

    const ICONS = {
        success: 'bi-check-circle-fill',
        danger:  'bi-exclamation-triangle-fill',
        warning: 'bi-exclamation-circle-fill',
        info:    'bi-info-circle-fill'
    };

    function show(message, variant = 'info', delay = 4000) {
        const container = document.getElementById('fcmsToastContainer');
        if (!container) {
            console.warn('fcms: #fcmsToastContainer missing — _FcmsConfirm partial not included');
            return;
        }

        const v = ICONS[variant] ? variant : 'info';
        const wrap = document.createElement('div');
        wrap.className = `toast align-items-center text-bg-${v} border-0 show`;
        wrap.setAttribute('role', 'alert');
        wrap.setAttribute('aria-live', 'assertive');
        wrap.setAttribute('aria-atomic', 'true');
        wrap.innerHTML = `
            <div class="d-flex">
                <div class="toast-body">
                    <i class="bi ${ICONS[v]} me-2"></i>${message}
                </div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
            </div>`;
        container.appendChild(wrap);

        const t = bootstrap.Toast.getOrCreateInstance(wrap, { delay, autohide: delay > 0 });
        t.show();
        wrap.addEventListener('hidden.bs.toast', () => wrap.remove());
    }

    fcms.toast = {
        success: (msg, delay)  => show(msg, 'success', delay),
        danger:  (msg, delay)  => show(msg, 'danger',  delay),
        warning: (msg, delay)  => show(msg, 'warning', delay),
        info:    (msg, delay)  => show(msg, 'info',    delay),

        // Generic — caller picks variant
        show: show
    };
})();
