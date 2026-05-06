// ═══════════════════════════════════════════════════════════════════════════
// fcms.confirm / fcms.alert / fcms.dialog — promise-based modal API
// Single shared Bootstrap 5 modal (#fcmsConfirmModal in _FcmsConfirm.cshtml).
// ═══════════════════════════════════════════════════════════════════════════

(function () {
    if (typeof window.fcms === 'undefined') window.fcms = {};

    const VARIANTS = ['success', 'danger', 'warning', 'info', 'primary', 'secondary'];

    function modalEl() {
        return document.getElementById('fcmsConfirmModal');
    }

    function buildButton(label, variant, onClick, autoClose = true) {
        const v = VARIANTS.includes(variant) ? variant : 'primary';
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = `btn btn-${v}`;
        btn.textContent = label;
        if (autoClose) btn.setAttribute('data-bs-dismiss', 'modal');
        if (onClick) btn.addEventListener('click', onClick);
        return btn;
    }

    function show({ title, message, footerButtons }) {
        const el = modalEl();
        if (!el) {
            console.warn('fcms: _FcmsConfirm partial missing from layout');
            return Promise.resolve(null);
        }
        document.getElementById('fcmsConfirmTitle').textContent = title || 'Confirm';
        document.getElementById('fcmsConfirmBody').innerHTML = message || '';

        const footer = document.getElementById('fcmsConfirmFooter');
        footer.innerHTML = '';
        footerButtons.forEach(b => footer.appendChild(b));

        const modal = bootstrap.Modal.getOrCreateInstance(el);
        modal.show();
        return modal;
    }

    // ── confirm({title, message, confirmLabel, confirmVariant, cancelLabel}) → Promise<bool>
    fcms.confirm = function (opts = {}) {
        return new Promise(resolve => {
            let resolved = false;
            const settle = (val) => { if (!resolved) { resolved = true; resolve(val); } };

            const cancelBtn = buildButton(
                opts.cancelLabel || 'Cancel',
                'secondary',
                () => settle(false),
                true
            );
            const confirmBtn = buildButton(
                opts.confirmLabel || 'Confirm',
                opts.confirmVariant || 'primary',
                () => settle(true),
                true
            );

            const el = modalEl();
            el.addEventListener('hidden.bs.modal', () => settle(false), { once: true });

            show({
                title: opts.title || 'Confirm',
                message: opts.message || 'Are you sure?',
                footerButtons: [cancelBtn, confirmBtn]
            });
        });
    };

    // ── alert({title, message, variant, buttonLabel}) → Promise<void>
    fcms.alert = function (opts = {}) {
        return new Promise(resolve => {
            let resolved = false;
            const settle = () => { if (!resolved) { resolved = true; resolve(); } };

            const okBtn = buildButton(
                opts.buttonLabel || 'OK',
                opts.variant || 'primary',
                settle,
                true
            );
            const el = modalEl();
            el.addEventListener('hidden.bs.modal', settle, { once: true });

            show({
                title: opts.title || 'Notice',
                message: opts.message || '',
                footerButtons: [okBtn]
            });
        });
    };

    // ── dialog({title, message, buttons: [{label, variant, value}]}) → Promise<value>
    fcms.dialog = function (opts = {}) {
        return new Promise(resolve => {
            let resolved = false;
            const settle = (val) => { if (!resolved) { resolved = true; resolve(val); } };

            const buttons = (opts.buttons || []).map(b =>
                buildButton(b.label, b.variant || 'primary', () => settle(b.value ?? null), true)
            );
            const el = modalEl();
            el.addEventListener('hidden.bs.modal', () => settle(null), { once: true });

            show({
                title: opts.title || '',
                message: opts.message || '',
                footerButtons: buttons
            });
        });
    };
})();
