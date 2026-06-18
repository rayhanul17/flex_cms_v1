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
    public const string PagesView = "pages.view";
    public const string PagesCreate = "pages.create";
    public const string PagesEdit = "pages.edit";
    public const string PagesDelete = "pages.delete";

    public const string PostsView = "posts.view";
    public const string PostsCreate = "posts.create";
    public const string PostsEdit = "posts.edit";
    public const string PostsDelete = "posts.delete";

    public const string CategoriesView = "categories.view";
    public const string CategoriesCreate = "categories.create";
    public const string CategoriesEdit = "categories.edit";
    public const string CategoriesDelete = "categories.delete";

    public const string MediaView = "media.view";
    public const string MediaUpload = "media.upload";
    public const string MediaEdit = "media.edit";
    public const string MediaDelete = "media.delete";
    public const string MediaFolders = "media.folders";

    public const string RedirectsView = "redirects.view";
    public const string RedirectsCreate = "redirects.create";
    public const string RedirectsEdit = "redirects.edit";
    public const string RedirectsDelete = "redirects.delete";

    public const string NotificationsView = "notifications.view";
    public const string NotificationsManage = "notifications.manage";

    public const string RolesView = "roles.view";
    public const string RolesCreate = "roles.create";
    public const string RolesEdit = "roles.edit";
    public const string RolesDelete = "roles.delete";
    public const string RolesManage = "roles.manage";
    public const string RolesPermissions = "roles.permissions";

    public const string UsersView = "users.view";
    public const string UsersCreate = "users.create";
    public const string UsersEdit = "users.edit";
    public const string UsersDelete = "users.delete";
    public const string UsersManage = "users.manage";

    public const string AuditView = "audit.view";
    public const string AuditManage = "audit.manage";

    public const string SettingsManage = "settings.manage";
    public const string SettingsView = "settings.view";

    public const string MessagingBroadcast = "messaging.broadcast";
    public const string MessagingView = "messaging.view";

    public const string PaymentsView = "payments.view";
    public const string PaymentsManage = "payments.manage";
    public const string ExportsRequest = "exports.request";
    public const string ExportsView = "exports.view";

    public const string ApiTokensManage = "api.tokens.manage";
    public const string WebhooksManage = "webhooks.manage";
    public const string CommentsSubmit = "comments.submit";
    public const string CommentsModerate = "comments.moderate";
    public const string SubscribersManage = "subscribers.manage";

    public const string SystemManage = "system.manage";

    // Runtime module operations are code execution, not generic system
    // settings — split them off from system.manage so an operator can
    // hold "manage host settings" without being able to upload arbitrary
    // DLLs. ModulesUpload is the gate for the upload + scaffold actions;
    // ModulesManage covers everything non-destructive (list, activate,
    // deactivate, retry-seed). Destructive ops (uninstall with drop,
    // restart) require SuperAdmin in code, not a permission key.
    public const string ModulesManage = "modules.manage";
    public const string ModulesUpload = "modules.upload";
}
