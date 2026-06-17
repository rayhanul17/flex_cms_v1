# Module static assets

This folder is mounted at `/modules/{module-id-lowercase}/` by the host.

Recommended layout:

- `css/` — module-specific stylesheets
- `js/` — module-specific scripts
- `lib/` — third-party libraries (only what this module needs)
- `images/` — static images bundled with the module
- `uploads/` — runtime uploads land here (created automatically by `IFcmsFileUploadService`)

Example:
- File on disk: `modules/FlexCms.MyMod/wwwroot/css/my-mod.css`
- URL: `/modules/flexcms.mymod/css/my-mod.css`

The host's `IFcmsFileUploadService.SaveAsync(file, moduleId, subfolder, ...)` writes uploads under `uploads/{subfolder}/yyyy/MM/`.
