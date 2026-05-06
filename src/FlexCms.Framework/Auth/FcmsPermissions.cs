namespace FlexCms.Framework.Auth;

/// <summary>
/// Compile-time constants for every core permission key. Use everywhere instead
/// of string literals — IntelliSense + rename safety + breaks the build if a
/// permission is misspelled.
/// </summary>
/// <remarks>
/// Modules should expose their own static class (e.g. <c>BlogPermissions</c>)
/// following the same pattern. Module keys conventionally start with the
/// module's table prefix (<c>blog.posts.create</c>, <c>shop.orders.refund</c>).
/// </remarks>
public static class FcmsPermissions
{
    // ── Pages ─────────────────────────────────────────────────────────────────
    public const string PagesCreate = "pages.create";
    public const string PagesEdit = "pages.edit";
    public const string PagesDelete = "pages.delete";

    // ── Posts ─────────────────────────────────────────────────────────────────
    public const string PostsCreate = "posts.create";
    public const string PostsEdit = "posts.edit";
    public const string PostsDelete = "posts.delete";

    // ── Categories (post taxonomy) ───────────────────────────────────────────
    public const string CategoriesCreate = "categories.create";
    public const string CategoriesEdit = "categories.edit";
    public const string CategoriesDelete = "categories.delete";

    // ── Media ─────────────────────────────────────────────────────────────────
    public const string MediaView = "media.view";
    public const string MediaUpload = "media.upload";
    public const string MediaEdit = "media.edit";
    public const string MediaDelete = "media.delete";
    public const string MediaFolders = "media.folders";

    // ── Redirects ────────────────────────────────────────────────────────────
    public const string RedirectsCreate = "redirects.create";
    public const string RedirectsEdit = "redirects.edit";
    public const string RedirectsDelete = "redirects.delete";

    // ── Admin: Roles ─────────────────────────────────────────────────────────
    public const string RolesCreate = "roles.create";
    public const string RolesEdit = "roles.edit";
    public const string RolesDelete = "roles.delete";
    public const string RolesManage = "roles.manage";
    public const string RolesPermissions = "roles.permissions";

    // ── Admin: Users ─────────────────────────────────────────────────────────
    public const string UsersCreate = "users.create";
    public const string UsersEdit = "users.edit";
    public const string UsersDelete = "users.delete";
    public const string UsersManage = "users.manage";

    // ── Admin: Audit Log ─────────────────────────────────────────────────────
    public const string AuditView = "audit.view";
    public const string AuditManage = "audit.manage";

    // ── Admin: Settings ──────────────────────────────────────────────────────
    public const string SettingsManage = "settings.manage";
    public const string SettingsView = "settings.view";

    // ── Admin: Messaging (Phase 8) ───────────────────────────────────────────
    public const string MessagingBroadcast = "messaging.broadcast";
    public const string MessagingView = "messaging.view";

    // ── Admin: Payments + Exports (Phase 12) ─────────────────────────────────
    public const string PaymentsView = "payments.view";
    public const string PaymentsManage = "payments.manage";
    public const string ExportsRequest = "exports.request";
    public const string ExportsView = "exports.view";
}
