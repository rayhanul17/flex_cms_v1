// ═══════════════════════════════════════════════════════════════════════════
// fcms-actions.js — Global click handler for [data-fcms-action] buttons.
// Wires up Edit (link, no JS), Toggle, Delete, Restore, Custom — auto-confirm
// + AJAX + toast + row update. No per-page JS needed for standard actions.
//
// Required button markup (TagHelper renders this):
//   <button data-fcms-action="delete"
//           data-url="/admin/users/{id}/delete"
//           data-confirm-title="Delete User?"
//           data-confirm-message="Move ..."
//           data-confirm-variant="danger"
//           data-confirm-label="Delete">
// ═══════════════════════════════════════════════════════════════════════════

(function () {
    if (typeof window.fcms === 'undefined') window.fcms = {};

    function csrfToken() {
        return document.querySelector('meta[name="csrf-token"]')?.content ?? '';
    }

    async function postJson(url) {
        const res = await fetch(url, {
            method: 'POST',
            headers: {
                'X-FlexCms-Csrf': csrfToken(),
                'X-Requested-With': 'XMLHttpRequest'
            }
        });
        try { return await res.json(); }
        catch { return { isSuccess: res.ok, message: res.statusText }; }
    }

    function findRow(btn) { return btn.closest('tr') || btn.closest('[data-fcms-row]'); }

    async function handleClick(e) {
        const btn = e.target.closest('[data-fcms-action]');
        if (!btn) return;

        e.preventDefault();
        const action = btn.dataset.fcmsAction;
        const url = btn.dataset.url;
        if (!url) { console.warn('fcms-action: missing data-url on', btn); return; }

        const confirmTitle = btn.dataset.confirmTitle;
        if (confirmTitle) {
            const ok = await fcms.confirm({
                title: confirmTitle,
                message: btn.dataset.confirmMessage || '',
                confirmLabel: btn.dataset.confirmLabel || 'Confirm',
                confirmVariant: btn.dataset.confirmVariant || 'primary'
            });
            if (!ok) return;
        }

        // Optional: disable button while in-flight
        btn.disabled = true;
        const res = await postJson(url);
        btn.disabled = false;

        if (res.isSuccess) {
            if (res.message) fcms.toast.success(res.message);

            switch (action) {
                case 'delete':
                    findRow(btn)?.remove();
                    break;

                case 'toggle-active': {
                    // Server returns { newStatus: "Active" | "InActive" }
                    const newStatus = res.data?.newStatus;
                    const statusCell = findRow(btn)?.querySelector('[data-fcms-status]');
                    if (statusCell && newStatus) {
                        statusCell.textContent = newStatus;
                        statusCell.className = 'badge text-bg-' + (newStatus === 'Active' ? 'success' : 'secondary');
                        statusCell.setAttribute('data-fcms-status', '');
                    }
                    // Toggle the button label/icon between Activate ⇄ Deactivate
                    const isActive = newStatus === 'Active';
                    btn.innerHTML = isActive
                        ? '<i class="bi bi-pause-circle"></i>'
                        : '<i class="bi bi-play-circle"></i>';
                    btn.setAttribute('title', isActive ? 'Deactivate' : 'Activate');
                    btn.classList.toggle('btn-outline-warning', isActive);
                    btn.classList.toggle('btn-outline-success', !isActive);
                    break;
                }

                case 'restore':
                    // Reload — restored row no longer belongs in trash list
                    findRow(btn)?.remove();
                    break;

                // 'custom' — caller handles via toast / page; row removal opt-in via data-remove-row
                default:
                    if (btn.dataset.removeRow === 'true') findRow(btn)?.remove();
                    break;
            }
        } else {
            fcms.toast.danger(res.message || 'Action failed.');
        }
    }

    document.addEventListener('click', handleClick);
})();
