namespace FlexCms.Framework.Cms;

/// <summary>
/// Marks an entity property to be excluded from the JSON snapshot saved into
/// <c>FcmsLog.Value</c>. Use for sensitive fields (passwords, tokens, API keys)
/// or any large/noisy field that shouldn't appear in audit logs.
///
/// <example>
/// public class FcmsApiKey
/// {
///     [FcmsLogIgnore] public string SecretKey { get; set; }
///     public string Name { get; set; }
/// }
/// </example>
///
/// <para>
/// Identity framework fields (PasswordHash, SecurityStamp, ConcurrencyStamp,
/// NormalizedUserName, NormalizedEmail, etc.) are stripped automatically by
/// <see cref="FcmsLogJsonResolver"/> — no need to mark them.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FcmsLogIgnoreAttribute : Attribute
{
}
