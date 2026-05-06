// ═══════════════════════════════════════════════════════════════════════════
// fcms-datatable.js — thin wrapper around jQuery DataTables for FlexCMS.
//
// Initializes a server-side DataTable for the given selector. Auto-renders
// the action column from the JSON response's `permissions` flags, no per-page
// JS needed. Action buttons use the same data-fcms-action attributes that
// fcms-actions.js handles globally (delete/toggle/restore/custom).
//
// Usage (TagHelper emits this — developer never writes it manually):
//   fcms.dataTable('#tbl', {
//       url:       '/admin/pages/datatable',
//       baseUrl:   '/admin/pages',
//       columns:   [{field:'Title', sortable:true}, {field:'Status', type:'status'}, ...],
//       actions: {
//           edit:    { visible: true },
//           toggle:  { visible: true },
//           delete:  { visible: false },
//           restore: { visible: false },
//           custom: [
//               { label:'Publish', icon:'bi-globe', variant:'success', visible:true,
//                 urlTemplate:'/admin/pages/{id}/publish',
//                 confirmTitle:'Publish?', confirmMessage:'…' }
//           ]
//       },
//       confirmNameField: 'Title'
//   });
// ═══════════════════════════════════════════════════════════════════════════

(function () {
    if (typeof window.fcms === 'undefined') window.fcms = {};

    const STATUS_BADGE = {
        Active:   'text-bg-success',
        InActive: 'text-bg-secondary',
        Deleted:  'text-bg-danger'
    };

    function escapeHtml(s) {
        return String(s ?? '')
            .replaceAll('&', '&amp;').replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;').replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }

    function renderCell(value, type) {
        if (value === null || value === undefined) return '<span class="text-muted">—</span>';
        switch (type) {
            case 'status': {
                // Server may send int (1, 0, 404) OR string ("Active"/"InActive"/"Deleted")
                const name = (typeof value === 'number')
                    ? (value === 1 ? 'Active' : value === 0 ? 'InActive' : value === 404 ? 'Deleted' : String(value))
                    : String(value);
                return `<span class="badge ${STATUS_BADGE[name] ?? 'text-bg-light'}" data-fcms-status>${name}</span>`;
            }
            case 'date': {
                const d = new Date(value);
                if (isNaN(d.getTime())) return escapeHtml(value);
                return `<span class="text-muted small">${d.toLocaleString()}</span>`;
            }
            case 'bool':
                return value
                    ? '<i class="bi bi-check-lg text-success"></i>'
                    : '<i class="bi bi-x-lg text-muted"></i>';
            case 'code':
                return `<code class="small">${escapeHtml(value)}</code>`;
            default:
                return escapeHtml(value);
        }
    }

    function buildActionsCell(row, opts) {
        const a = opts.actions || {};
        const id = row.id ?? row.Id;
        const status = row.status ?? row.Status;
        const isDeleted = status === 404 || status === 'Deleted';
        const isActive  = status === 1   || status === 'Active';
        const baseUrl = (opts.baseUrl || '').replace(/\/$/, '');

        const name = opts.confirmNameField ? row[opts.confirmNameField] : '';
        const confirmName = name ? escapeHtml(name) : 'this item';

        const buttons = [];

        if (!isDeleted && a.edit?.visible)
            buttons.push(`<a class="btn btn-outline-info" href="${baseUrl}/${id}/edit" title="Edit"><i class="bi bi-pencil"></i></a>`);

        if (!isDeleted && a.toggle?.visible) {
            const label = isActive ? 'Deactivate' : 'Activate';
            const icon = isActive ? 'bi-pause-circle' : 'bi-play-circle';
            const variant = isActive ? 'warning' : 'success';
            buttons.push(`<button type="button" class="btn btn-outline-${variant}" title="${label}"
                data-fcms-action="toggle-active" data-url="${baseUrl}/${id}/toggle-active">
                <i class="bi ${icon}"></i></button>`);
        }

        if (!isDeleted && a.delete?.visible)
            buttons.push(`<button type="button" class="btn btn-outline-danger" title="Delete"
                data-fcms-action="delete" data-url="${baseUrl}/${id}/delete"
                data-confirm-title="Delete?" data-confirm-message="Move ${confirmName} to trash?"
                data-confirm-label="Delete" data-confirm-variant="danger">
                <i class="bi bi-trash"></i></button>`);

        if (isDeleted && (a.delete?.visible || a.restore?.visible))
            buttons.push(`<button type="button" class="btn btn-outline-success" title="Restore"
                data-fcms-action="restore" data-url="${baseUrl}/${id}/restore"
                data-confirm-title="Restore?" data-confirm-message="Restore ${confirmName}?"
                data-confirm-label="Restore" data-confirm-variant="success">
                <i class="bi bi-arrow-counterclockwise"></i></button>`);

        // Custom actions
        for (const c of (a.custom || [])) {
            if (!c.visible) continue;
            const url = (c.urlTemplate || '').replace('{id}', id);
            const variant = c.variant || 'secondary';
            const icon = c.icon || 'bi-box';
            const label = escapeHtml(c.label || '');
            let attrs = `data-fcms-action="custom" data-url="${escapeHtml(url)}"`;
            if (c.confirmTitle) {
                attrs += ` data-confirm-title="${escapeHtml(c.confirmTitle)}"`
                       + ` data-confirm-message="${escapeHtml(c.confirmMessage || '')}"`
                       + ` data-confirm-label="${escapeHtml(c.confirmLabel || c.label || 'Confirm')}"`
                       + ` data-confirm-variant="${variant}"`;
            }
            buttons.push(`<button type="button" class="btn btn-outline-${variant}" title="${label}" ${attrs}>
                <i class="bi ${icon}"></i></button>`);
        }

        if (buttons.length === 0) return '<span class="text-muted">—</span>';
        return `<div class="btn-group btn-group-sm" role="group">${buttons.join('')}</div>`;
    }

    fcms.dataTable = function (selector, opts) {
        const $tbl = jQuery(selector);
        if ($tbl.length === 0) { console.warn('fcms.dataTable: selector not found', selector); return; }

        const cols = (opts.columns || []).map(c => ({
            data: c.field,
            name: c.field,
            orderable: c.sortable !== false,
            searchable: c.searchable !== false,
            render: (data, type, row) =>
                type === 'display' ? renderCell(data, c.type) : data
        }));

        const hasActions = !!opts.actions;
        if (hasActions) {
            cols.push({
                data: null,
                orderable: false,
                searchable: false,
                className: 'text-end',
                render: (data, type, row) => buildActionsCell(row, opts)
            });
        }

        const csrf = document.querySelector('meta[name="csrf-token"]')?.content ?? '';

        // Default sort: first user-defined column, ascending — caller can override via opts.defaultSort
        const defaultSort = opts.defaultSort || [[0, 'asc']];

        return $tbl.DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: opts.url,
                type: 'POST',
                headers: { 'X-FlexCms-Csrf': csrf, 'X-Requested-With': 'XMLHttpRequest' }
            },
            columns: cols,
            order: defaultSort,
            pageLength: opts.pageLength || 25,
            lengthMenu: opts.lengthMenu || [10, 25, 50, 100],
            language: { search: '', searchPlaceholder: 'Search…' }
        });
    };
})();
