# Phase 7 — i18n + Translation: Manual Test Cases

> **Automated coverage**: 23 unit tests (translator + middleware) + 18 integration
> tests (PageService/PostService translation flow on EF in-memory + MongoDB
> testcontainer). All passing. This doc covers manual end-to-end UI scenarios
> only — anything testable in code is already in the suite.

## Setup

1. Run `dotnet run --project src/FlexCms.Host` (or `dotnet watch run`).
2. Login as SuperAdmin.
3. Open Settings (`/admin/settings`) and confirm new fields appear:
   - **Default Language** dropdown (English / বাংলা).
   - **Language mode** dropdown (Cookie / URL prefix).

## 1. UI translation switcher (admin)

| # | Action | Expected |
|---|--------|----------|
| 1.1 | Top-bar language dropdown shows current code (`EN` or `BN`) with translate icon. | Visible on every admin page. |
| 1.2 | Click dropdown → both languages listed with active highlight on current. | Active item has `.active` class. |
| 1.3 | Switch to BN → all admin labels (Save, Cancel, Delete, Settings, etc.) render in Bengali. | Cookie `fcms_ui_lang=bn` set, page reload happens via POST + redirect. |
| 1.4 | Switch back to EN → labels restore. | Cookie updated to `en`. |
| 1.5 | Cookie persists across browser restart (`Expires` ~1 year). | DevTools → Application → Cookies. |
| 1.6 | Anonymous visitor on public page → top-bar switcher still works (no login required). | Cookie still set, fallback chain still applies. |

## 2. UI translation fallback

| # | Action | Expected |
|---|--------|----------|
| 2.1 | Visit admin in BN. Add a brand-new key only to `en.json` (e.g. via module). | Key resolves to its English value when shown in BN UI (fallback chain). |
| 2.2 | Lookup completely unknown key (`@Html.T("does.not.exist")`). | Returns the key string verbatim — never blank, never crash. |

## 3. Content translation (Page)

| # | Action | Expected |
|---|--------|----------|
| 3.1 | Create a page `slug=about`, English content. Visit `/about`. | English content shown. |
| 3.2 | Add a Bengali translation row via repository or admin form: `LanguageCode=bn`, `Slug=about-bn`, Bengali content. | Visit `/about-bn` while in any language → Bengali content. |
| 3.3 | Switch UI to BN, visit `/about` (base slug). | Translation overlay applied → Bengali content shown at base URL. |
| 3.4 | Switch UI to BN, visit `/about-no-bn-translation` (only EN exists). | English content shown (fallback, never 404). |
| 3.5 | Try to add a second BN translation for the same page → unique constraint hit. | `(PageId, LanguageCode)` violation → DB error surfaces as user-visible failure. |
| 3.6 | Try `Slug=about-bn` for a different page in BN → blocked by `(LanguageCode, Slug)` unique. | Same error. |
| 3.7 | Delete the page (soft-delete). | Translations cascade-delete via FK in EF; in Mongo they are orphaned but invisible (base lookup returns null). |

## 4. Content translation (Post) — `/blog/{slug}`

Same 3.1–3.7 against `/blog/about` etc.

## 5. URL-prefix mode

| # | Action | Expected |
|---|--------|----------|
| 5.1 | Settings → Language mode = "URL prefix" → Save. | SiteSettings.LanguageMode = "url-prefix". |
| 5.2 | Visit `/en/about` → English content; URL stays `/en/about` in browser. | Middleware strips `/en` before routing; controller sees path = `/about`. |
| 5.3 | Visit `/bn/about` → Bengali (translation overlay if exists, else fallback). | Same. |
| 5.4 | Visit `/about` (no prefix) → site default language served. | No 404. |
| 5.5 | Visit `/admin/posts` → admin loads normally. | "admin" is not a language → middleware leaves path alone. |
| 5.6 | Switch back to "Cookie" mode → `/en/about` now 404s (no longer a recognized prefix). | Expected: prefix mode is opt-in for SEO. |

## 6. Module-shipped translations (smoke test for module dev guide)

1. Create a module that ships an embedded resource at `Resources/i18n/bn.json` with one key (e.g. `mymodule.greeting = হ্যালো!`).
2. In the module's `RegisterServices`, resolve `IFcmsTranslator` via the service provider after build and call `LoadEmbeddedFromAssembly(typeof(MyModule).Assembly)`.
3. In a Razor view: `@Html.T("mymodule.greeting")` → renders Bengali when UI is in BN.
4. Override `mymodule.greeting` later via `_translator.AddOrOverride("bn", new Dictionary<string,string>{[\"mymodule.greeting\"]=\"হাই!\"})` → next request shows the override.

## 7. Database storage (EF + Mongo cross-check)

Already covered by [PageTranslationServiceTests](../tests/FlexCms.Tests.Integration/Phase7/PageTranslationServiceTests.cs), [PostTranslationServiceTests](../tests/FlexCms.Tests.Integration/Phase7/PostTranslationServiceTests.cs), and [MongoTranslationTests](../tests/FlexCms.Tests.Integration/Phase7/MongoTranslationTests.cs). For manual confirmation:

- **MySQL/Postgres**: `SELECT * FROM fcms_page_translations;` — rows present with `language_code`, `slug`, `title` columns.
- **MongoDB**: `db.fcms_page_translations.find({})` — documents with `pageId` (binary UUID), `languageCode`, etc.
- **Mongo indexes** (`db.fcms_page_translations.getIndexes()`): `ux_page_translations_page_lang` and `ux_page_translations_lang_slug` both present and `unique: true`.

## 8. Edge cases

| # | Action | Expected |
|---|--------|----------|
| 8.1 | Disable cookies in browser, cookie mode site → every request resolves to site default. | No crash, just no per-user preference. |
| 8.2 | Set cookie to `klingon` manually → middleware rejects unsupported value, falls back to default. | Verified by `Cookie_mode_ignores_unsupported_cookie_value` unit test. |
| 8.3 | DB momentarily down on first request → middleware swallows the exception and serves "en". | Verified by `Settings_failure_does_not_break_pipeline` unit test. |
| 8.4 | Switch language while editing a form → no surprise data loss; form re-renders with same values, only labels change. | Cookie set then `LocalRedirect(returnUrl)`. |

## 9. Out of scope (future phases)

- Per-page **slug regeneration** when translation slug changes (Phase 9 — Admin UX work).
- Admin **Translations tab inside Page/Post editor** with rich-text editor (Phase 9 — Admin UX).
- **RTL layout** for languages where `_meta.rtl = true` (Phase 11 — Themes).
- **Add/remove languages from admin UI** (currently file-driven; admin will get a JSON editor in Phase 9 or 11).
