// ═══════════════════════════════════════════════════════════════════════════
// fcms.toast — Bootstrap 5 toasts with success / danger / warning / info
// Container #fcmsToastContainer in _FcmsConfirm.cshtml.
//
// Public API:
//   fcms.toast.success(message, options?)
//   fcms.toast.danger (message, options?)
//   fcms.toast.warning(message, options?)
//   fcms.toast.info   (message, options?)
//   fcms.toast.show   (message, variant, options?)
//
// Options:
//   duration       Number of milliseconds before auto-dismiss. 0 = sticky.
//                  Number argument (legacy) is treated as duration in ms.
//   closeButton    When false, hide the X button. Default true.
//   appendMessage  When true and the previous toast is still visible, append
//                  message text to it instead of creating a new toast.
// ═══════════════════════════════════════════════════════════════════════════

(function () {
    if (typeof window.fcms === 'undefined') window.fcms = {};

    const ICONS = {
        success: 'bi-check-circle-fill',
        danger:  'bi-exclamation-triangle-fill',
        warning: 'bi-exclamation-circle-fill',
        info:    'bi-info-circle-fill'
    };

    // Track the most recent toast per variant so appendMessage can find it.
    const _activeByVariant = {};

    function resolveOptions(opts) {
        if (opts == null) return { duration: 4000, closeButton: true, appendMessage: false };
        if (typeof opts === 'number') return { duration: opts, closeButton: true, appendMessage: false };
        return {
            duration:      typeof opts.duration === 'number' ? opts.duration : 4000,
            closeButton:   opts.closeButton !== false,
            appendMessage: opts.appendMessage === true
        };
    }

    function show(message, variant = 'info', opts) {
        const container = document.getElementById('fcmsToastContainer');
        if (!container) {
            console.warn('fcms: #fcmsToastContainer missing — _FcmsConfirm partial not included');
            return;
        }

        const v = ICONS[variant] ? variant : 'info';
        const options = resolveOptions(opts);

        // Append to the most recent toast of the same variant when requested
        // and that toast hasn't been dismissed yet.
        const previous = _activeByVariant[v];
        if (options.appendMessage && previous && previous.isConnected) {
            const body = previous.querySelector('.toast-body');
            if (body) {
                const sep = body.dataset.separator || ' | ';
                body.appendChild(document.createTextNode(sep + message));
                return;
            }
        }

        const closeBtn = options.closeButton
            ? '<button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>'
            : '';

        const wrap = document.createElement('div');
        wrap.className = `toast align-items-center text-bg-${v} border-0 show`;
        wrap.setAttribute('role', 'alert');
        wrap.setAttribute('aria-live', 'assertive');
        wrap.setAttribute('aria-atomic', 'true');
        wrap.innerHTML = `
            <div class="d-flex">
                <div class="toast-body">
                    <i class="bi ${ICONS[v]} me-2"></i><span></span>
                </div>
                ${closeBtn}
            </div>`;
        // Use textContent to keep the message escaping safe — no raw HTML injection.
        wrap.querySelector('.toast-body span').textContent = message;
        container.appendChild(wrap);
        _activeByVariant[v] = wrap;

        const autohide = options.duration > 0;
        const t = bootstrap.Toast.getOrCreateInstance(wrap, {
            delay: autohide ? options.duration : 999999,
            autohide
        });
        t.show();
        wrap.addEventListener('hidden.bs.toast', () => {
            if (_activeByVariant[v] === wrap) delete _activeByVariant[v];
            wrap.remove();
        });
    }

    fcms.toast = {
        success: (msg, opts) => show(msg, 'success', opts),
        danger:  (msg, opts) => show(msg, 'danger',  opts),
        warning: (msg, opts) => show(msg, 'warning', opts),
        info:    (msg, opts) => show(msg, 'info',    opts),

        // Generic — caller picks variant
        show: show
    };
})();
