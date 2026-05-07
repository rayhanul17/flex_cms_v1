# Phase 16 + 17 — Performance / Accessibility / Editorial / Module API: Manual Test Cases

> **Automated coverage**: 24 unit tests across cache stampede, image
> optimizer, WCAG contrast, and module API registry. Project total:
> **416 unit + 247 EF integration**.
>
> Phase 16 is **partial** — entities/services are wired and tested; admin
> UI surfaces (search dashboard, contrast warnings in theme editor,
> editorial calendar, image-backfill job) are deferred. Phase 17 is
> **complete** for the reduced scope (Module API Registry only).

## 1. Cache stampede protection (Issue 104)

| # | Action | Expected |
|---|--------|----------|
| 1.1 | 50 concurrent `GetOrCreateAsync("hot-key", factory, 5min)` calls miss the cache → factory invoked exactly ONCE; the other 49 wait on the per-key semaphore + read the populated value. | `Concurrent_misses_for_same_key_invoke_factory_once`. |
| 1.2 | Two different keys (`"k1"`, `"k2"`) — both factories run concurrently (per-key isolation), peak in-flight ≥ 2. | `Different_keys_do_not_serialize_each_other`. |
| 1.3 | Factory throws `InvalidOperationException` → exception bubbles to caller; per-key semaphore RELEASED. Next call retries the factory (would hang forever if lock leaked). | `Throwing_factory_releases_lock_so_next_call_retries`. |
| 1.4 | `Evict(key)` → next read re-runs the factory. | `Evict_drops_the_entry`. |
| 1.5 | Use it in `PermissionService` / `MenuService` / `RedirectService` (refactor candidates listed in the interface XML doc) — load test 1000 concurrent on uncached endpoint → DB query count = 1. | Refactoring those services is the recommended next step; the primitive itself is shipped + tested. |

## 2. Image optimization (Issue 105)

| # | Action | Expected |
|---|--------|----------|
| 2.1 | Upload a 2000×1500 JPEG → `IImageOptimizer.OptimizeAsync` returns 4 byte arrays: `hero.webp` (full size), `hero-640w.webp`, `hero-1024w.webp`, `hero-1920w.webp`. | `Optimize_emits_full_webp_plus_smaller_variants`. |
| 2.2 | Upload an 800×600 image with default widths `[640, 1024, 1920]` → only `small.webp` + `small-640w.webp` are produced; `1024w` and `1920w` skipped to avoid upscaling. | `Optimize_skips_widths_larger_than_source`. |
| 2.3 | Empty input bytes / garbage bytes → returns empty dict, no exception. Caller falls back to serving the original. | `Garbage_input_returns_empty_dict_does_not_throw`. |
| 2.4 | Input filename `photos/holiday-2026.jpeg` → output keys strip the extension: `holiday-2026.webp`, `holiday-2026-640w.webp`. | `Output_filenames_strip_input_extension`. |
| 2.5 | Razor: `<fcms-picture src="/uploads/hero.jpg" alt="Hero" widths="640,1024,1920" />` → renders `<picture><source type="image/webp" srcset="/uploads/hero-640w.webp 640w, /uploads/hero-1024w.webp 1024w, /uploads/hero-1920w.webp 1920w" sizes="(max-width: 640px) 100vw, (max-width: 1024px) 75vw, 50vw" /><img src="/uploads/hero.jpg" alt="Hero" loading="lazy" decoding="async" /></picture>`. | `PictureTagHelper`. |
| 2.6 | `<fcms-picture src="/uploads/x.jpg" alt="X" />` (no widths) → single `<source srcset="/uploads/x.webp">` + lazy `<img>`. | Default fallback. |
| 2.7 | Lighthouse mobile score on a page with 5 images: optimize all → score ≥ 90 (was < 60 with raw JPEGs). | Manual benchmark; depends on the test page's other content. |

## 3. Search (Issue 106)

| # | Action | Expected |
|---|--------|----------|
| 3.1 | Module ships `services.AddScoped<IFcmsSearchableSource, ProductSource>()` → `IFcmsSearchProvider.SearchAsync("yarn")` includes Product hits alongside framework's Page+Post hits. | Fan-out across registered sources. |
| 3.2 | One source throws → other sources' results still returned; failing source contributes 0 hits. Logger.Warning records the source id + query. | `LikeSearchProvider.SearchAsync` per-source try/catch. |
| 3.3 | Empty / whitespace query → returns `SearchResults` with empty hits, total=0. | Defensive guard. |
| 3.4 | Query with zero hits → row appended to `fcms_search_queries` with `result_count=0`. Admin "No-Result Queries" panel surfaces it via `IFcmsSearchAnalytics.GetNoResultQueriesAsync`. | Best-effort analytics — tracking failure doesn't fail the search. |
| 3.5 | Same no-result query attempted 3 times → `NoResultEntry.Attempts = 3`, ordered by attempt count desc. | `GROUP BY query` in analytics. |
| 3.6 | Page size capped at 100 (anti-DoS). | `Math.Clamp(pageSize, 1, 100)`. |

## 4. Real-time admin notifications (Issue 107)

| # | Action | Expected |
|---|--------|----------|
| 4.1 | Two admin browser tabs open `/admin` (both connect to `/hubs/admin-notifications`). Trigger a notification in tab 1 (e.g. comment requires moderation). Tab 2's bell badge increments within 100ms — no 60s wait. | SignalR push via `IAdminNotificationPusher.PushToUserAsync(userId, payload)`. |
| 4.2 | Network tab DevTools after page load → zero `/admin/notifications/count` polling requests for 5 minutes (SignalR connected). | Polling scaled back when hub is up. |
| 4.3 | Disable SignalR (firewall / proxy block) → bell still works via 60s poll fallback. | Graceful degradation — polling code still present. |
| 4.4 | Anonymous user attempts to connect → hub aborts the connection. | `[Authorize]` on the hub. |
| 4.5 | Non-admin user (Subscriber) connects → joins `user:{id}` group only, NOT `admin:notifications` broadcast group. | Role check inside `OnConnectedAsync`. |

## 5. WCAG contrast (Issue 108)

| # | Action | Expected |
|---|--------|----------|
| 5.1 | `WcagContrast.Ratio("#000", "#fff")` → ~21.0 (max). | `Black_on_white_is_max_ratio_21`. |
| 5.2 | `WcagContrast.Ratio("#888", "#888")` → 1.0 (identical → no contrast). | Verified. |
| 5.3 | `WcagContrast.MeetsAa("#cccccc", "#ffffff")` → false (textbook fail). `MeetsAa("#555", "#fff")` → true. | AA = 4.5:1 floor. |
| 5.4 | Theme save with bg=#fff + text=#ccc → admin theme editor warning "Contrast 1.6:1 fails WCAG AA (need 4.5:1)". | Admin UI surfacing; backing helper shipped. |
| 5.5 | Malformed input (`"not-a-color"`, empty, `"#xyz"`) → returns 0 (admin treats as "unable to evaluate"). | `Malformed_hex_returns_zero`. |
| 5.6 | 3-char hex `#fff` produces same ratio as `#ffffff`. | Shorthand expansion. |
| 5.7 | `Evaluate(ratio)` returns `(AaNormal, AaLarge, AaaNormal, AaaLarge)` flags for 4 levels — used by the badge UI. | Verified at max ratio. |

## 6. Editorial workflow (Issue 109)

| # | Action | Expected |
|---|--------|----------|
| 6.1 | Author calls `SubmitForReviewAsync("FcmsPost", postId, authorId, assignToEditorId, autoPublish: true)` → row created with `ReviewStatus = Submitted`. | `EditorialService`. |
| 6.2 | Editor calls `ApproveAsync(reviewId, editorId, "Looks good")` on a review with `AutoPublishOnApproval=true` → review status → Approved + `FcmsPost.IsPublished=true` + `PublishedAt` set if missing. | Auto-publish branch. |
| 6.3 | Same with `AutoPublishOnApproval=false` → review status → Approved but the post stays `IsPublished=false` until manually published. | Approval gates the publish button. |
| 6.4 | `RequestChangesAsync(reviewId, editorId, "Fix the second paragraph")` → status → ChangesRequested + comment stored. Author re-edits + calls SubmitForReviewAsync again → new review row created (history preserved). | Append-only review log. |
| 6.5 | `RejectAsync(reviewId, editorId, "Off-topic")` → status → Rejected (terminal). | Terminal state. |
| 6.6 | `GetLatestAsync("FcmsPost", postId)` → most recent review by `CreatedAt`. Drives the admin badge. | Index `(EntityType, EntityId, CreatedAt)`. |
| 6.7 | `AddAnnotationAsync("FcmsPost", postId, reviewerId, anchorJson, "rephrase this")` → annotation row created. `GetAnnotationsAsync` returns it ordered by `CreatedAt`. | `FcmsContentAnnotation`. |
| 6.8 | `ResolveAnnotationAsync(annotationId, authorId)` → `IsResolved=true`, `ResolvedAt`, `ResolvedByUserId` set. | Author marks reviewer comments addressed. |
| 6.9 | Permission gate: Author has SubmitForReview but not PublishImmediate → admin UI hides [Publish] button, shows [Submit for Review]. | (Permission keys + UI deferred.) |

## 7. Module API Registry (Issue 110 — Phase 17)

| # | Action | Expected |
|---|--------|----------|
| 7.1 | Blog module exposes `[FcmsModuleApi("1.2.0")] interface IBlogPublicApi { Task<List<Post>> GetRecentAsync(int n); }` + registers `services.AddSingleton<IBlogPublicApi, BlogApiImpl>()`. E-commerce module calls `_registry.Get<IBlogPublicApi>()?.GetRecentAsync(5)` → returns posts. | `Get_returns_implementation_when_registered`. |
| 7.2 | Blog module deactivated (no DI registration) → `_registry.Get<IBlogPublicApi>()` → null. Consumer's null-conditional renders the page without crash. | `Get_returns_null_when_provider_module_not_registered`. |
| 7.3 | Consumer demands `Get<IBlogPublicApi>(">=1.0.0")` against declared `1.2.0` → returns implementation. | `Get_with_satisfied_constraint_returns_implementation`. |
| 7.4 | Consumer demands `>=2.0.0` against declared `1.2.0` → returns null + warning logged. Next major bump of the API requires consumers to update. | `Get_with_unsatisfied_constraint_returns_null`. |
| 7.5 | `^1.0.0` constraint → matches `1.x` (same major) but not `2.0.0`. `^1.5.0` matches `1.6.x` not `1.4.x`. | Caret semantics from Phase 15 SemVer. |
| 7.6 | Interface without `[FcmsModuleApi]` attribute → registry falls back to plain DI lookup. Defensive — typo in attribute placement still works. | `Get_without_attribute_falls_back_to_di_lookup`. |
| 7.7 | `Registry.List()` → enumerates all `[FcmsModuleApi]`-marked interfaces across loaded assemblies + their registered impls. Admin diagnostic. | Reflection-driven; one-shot for the admin page. |

## 8. Database storage cross-check

- **EF**: `SELECT result_count, COUNT(*) FROM fcms_search_queries GROUP BY result_count;` — index on `(result_count, created_at)`.
- **EF**: `SELECT review_status, COUNT(*) FROM fcms_content_reviews GROUP BY review_status;` — admin moderation queue uses `(EntityType, EntityId, CreatedAt)` for "latest review" lookup.
- **EF**: `SELECT entity_type, COUNT(*) FROM fcms_content_annotations WHERE is_resolved = 0;` — open annotations per entity type.

## 9. Out of scope (future / deferred / explicit drops)

**Deferred (Phase 16 admin UI / view layer):**
- Search admin dashboard (no-result queries panel, popular queries, [Rebuild Index] button).
- Theme editor inline contrast warning toast.
- Editorial calendar drag-drop view + side-by-side review diff (uses Phase 14 `IRevisionDiffService`).
- In-content annotation overlay (editor-component specific).
- Image-optimize backfill job for legacy uploads.
- Skip-link insertion in shared layout + axe-core CI integration.
- FULLTEXT / tsvector / FTS / Mongo-text-index concrete `IFcmsSearchProvider` implementations (the LIKE-based provider ships and works on any DB; vendor providers slot in via DI).

**Phase 17 — explicitly dropped (NOT deferred):**
- Cmd+K admin search, privacy analytics, PWA, WordPress importer, multi-step forms, AI provider, Prometheus metrics, module marketplace. See Phase 17 entry in plan.md for the rationale per item.
