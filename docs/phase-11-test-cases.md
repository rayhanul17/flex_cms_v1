# Phase 11 — Themes + Setup Wizard: Manual Test Cases

> **Setup Wizard** was already shipped earlier (4-step DB → Site Info →
> Admin Account → Done flow with two-path Program.cs and SeedService).
> See [phase-1-6-test-cases.md](phase-1-6-test-cases.md) §1.
>
> This phase delivers **theme infrastructure + dark/light/auto mode**
> built around the convention "default views are the built-in
> `FlexCms.Default` theme; modules ship a folder of override views with
> a `theme.json` manifest". The originally-planned separate theme
> projects (`FlexCms.Theme.AdminLte`, `FlexCms.Theme.Bootstrap`,
> `FlexCms.Theme.Tailwind`) are deferred — they are designed-asset
> bundles that need real visual design work, not framework code.
>
> **Automated coverage**: 16 unit tests (8 ThemeManager — discovery,
> built-in default presence, duplicate-id rejection, garbled-manifest
> survival, refresh; 8 ThemeMode — cookie roundtrip, unknown-value
> coercion to auto, Set/Resolve symmetry). Project total: 251 unit +
> 210 integration.

## 1. Theme discovery (`ThemeManager`)

| # | Action | Expected |
|---|--------|----------|
| 1.1 | Empty `themes/` folder → `IThemeManager.All` returns just the built-in `FlexCms.Default` entry. | Verified by `Empty_root_still_exposes_the_built_in_default`. |
| 1.2 | Create `themes/MyTheme/theme.json` with `{ "id": "MyTheme", "name": "My Theme" }` → restart → `IThemeManager.All` includes it. (Or call `Refresh()` for hot reload.) | Verified by `Discovers_disk_themes_alongside_built_in_default` + `Refresh_picks_up_themes_added_after_construction`. |
| 1.3 | Manifest with empty `id` → silently skipped. | Verified. |
| 1.4 | Manifest tries to claim `id = "FlexCms.Default"` → built-in wins. | Verified. |
| 1.5 | Manifest file is malformed JSON → that theme is skipped, others still load. | Verified by `Garbled_manifest_file_is_skipped_without_throwing`. |

## 2. Theme view resolution (`ThemeViewLocationExpander`)

| # | Action | Expected |
|---|--------|----------|
| 2.1 | Settings → Public theme = "MyTheme" → request hits `~/Themes/MyTheme/Views/Frontend/Page.cshtml` first; if missing, falls through to host's `~/Views/Frontend/Page.cshtml`. | Per `ExpandViewLocations` ordering. |
| 2.2 | Switch theme in Settings → next request renders the new theme's views without process restart. | Cache key includes `fcms-theme` value via `PopulateValues`. |
| 2.3 | Theme references a partial it doesn't override (e.g. `_NotificationBell`) → host's version renders. | Fallback works at the partial level too. |

## 3. Theme selector (admin Settings → Appearance)

| # | Action | Expected |
|---|--------|----------|
| 3.1 | `/admin/settings` → Appearance card shows `Public theme` dropdown listing all `SupportsPublic` themes; built-ins are flagged "(built-in)". | Populated from `IThemeManager.All`. |
| 3.2 | Save with a different theme → toast "Settings saved." → reload page → dropdown shows the new selection persisted. | `SiteSettings.PublicThemeId` written. |
| 3.3 | Save with empty value → coerced to `FlexCms.Default`. | Guard in `SettingsController.Index(POST)`. |

## 4. Dark / light / auto mode

| # | Action | Expected |
|---|--------|----------|
| 4.1 | Top bar shows mode toggle button (sun / moon / half-circle icon depending on current cookie). | `_ThemeModeToggle.cshtml`. |
| 4.2 | Click → dropdown with Light / Dark / Auto. Active option is highlighted. | Cookie value drives the active flag. |
| 4.3 | Click "Dark" → page reloads → `<html data-theme-mode="dark" data-bs-theme="dark">`. Bootstrap automatically applies its dark colour scheme via the `data-bs-theme` attribute. | Default views immediately switch palette. |
| 4.4 | Click "Light" → reloads → `data-theme-mode="light" data-bs-theme="light"`. | Same. |
| 4.5 | Click "Auto" → reloads → `data-theme-mode="auto" data-bs-theme="light"` (server doesn't know system preference; CSS / JS can read `prefers-color-scheme` if needed). | Cookie value is `auto`. |
| 4.6 | Cookie persists 1 year (DevTools → Application → Cookies → `fcms_theme_mode`). | `Expires = 1y` in `ThemeMode.Set`. |
| 4.7 | Anonymous visitor on public site can use the toggle (no auth required). | `ThemeController.SetMode` is unauthenticated. |

## 5. CSRF + cookie security

| # | Action | Expected |
|---|--------|----------|
| 5.1 | POST `/theme/mode` without antiforgery token → 400 BadRequest. | `[ValidateAntiForgeryToken]`. |
| 5.2 | Send cookie value `<script>` → coerced to `auto` on read; never echoed unsafely. | `Resolve` whitelist. |
| 5.3 | Cookie has `IsEssential=true`, `SameSite=Lax`, `Secure` matches request scheme, `HttpOnly=false` (JS needs to read for CSS toggling). | Verified at `ThemeMode.Set`. |

## 6. Building a custom theme (developer flow)

1. Create `themes/MyTheme/theme.json`:
   ```json
   {
     "id": "MyTheme",
     "name": "My Theme",
     "description": "Custom theme",
     "version": "1.0.0",
     "isBuiltIn": false,
     "supportsPublic": true,
     "supportsAdmin": false,
     "supportedModes": ["light", "dark", "auto"]
   }
   ```
2. Create `themes/MyTheme/Views/Shared/_PublicLayout.cshtml` (and any other views to override).
3. Restart the host (or call `IThemeManager.Refresh()`).
4. `/admin/settings` → Appearance → "My Theme" appears → save.
5. Public pages now use the new layout; admin pages still use host defaults.

## 7. Edge cases

| # | Action | Expected |
|---|--------|----------|
| 7.1 | `themes/` folder missing entirely → `ThemeManager` ctor doesn't throw; default still works. | Try/catch in `Refresh`. |
| 7.2 | Theme id contains path-traversal chars (`../foo`) → `ExpandViewLocations` hands a literal path string to Razor; Razor either finds it or doesn't. No filesystem-level traversal because `IThemeManager` only ever picks ids from manifests it loaded. | `ResolveThemeId` reads from settings (admin-controlled), not user input. |
| 7.3 | Settings DB unreachable on first request → `ThemeViewLocationExpander.ResolveThemeId` swallows + returns default. Page still renders. | `try/catch` fallback. |

## 8. Out of scope (deferred)

- **`FlexCms.Theme.AdminLte`** — full AdminLTE 3 sidebar / dashboard skin.
- **`FlexCms.Theme.Bootstrap`** — separate Bootstrap-specific theme project (current default views ARE Bootstrap, so this is largely redundant — would only differ in marketing-page template).
- **`FlexCms.Theme.Tailwind`** — Tailwind CSS variant of public layouts.
- **`_FcmsUi.cshtml` adapter pattern** for cross-theme toast/modal — current `fcms.toast` / `fcms.confirm` JS is theme-agnostic already.
- **Tailwind dark mode (class strategy)** — needs Tailwind CSS bundled first.
- **Theme-specific asset versioning + cache-busting** — Phase 14/15 work.
