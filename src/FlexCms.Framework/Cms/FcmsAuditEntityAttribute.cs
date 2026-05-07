namespace FlexCms.Framework.Cms;

/// <summary>
/// Overrides the action-name prefix used by <see cref="FcmsAuditInterceptor"/>
/// when auto-generating audit log entries for an entity type.
///
/// By default the interceptor derives the prefix from the CLR type name
/// (e.g. <c>FcmsPost</c> → <c>"Post"</c>), producing entries like
/// <c>"Post.Created"</c>, <c>"Post.Updated"</c>, <c>"Post.Deleted"</c>.
/// Use this attribute to supply a custom prefix instead.
/// </summary>
/// <example>
/// [FcmsAuditEntity("BlogPost")]
/// public class FcmsPost : BaseEfEntity { ... }
/// // produces: "BlogPost.Created", "BlogPost.Updated", "BlogPost.Deleted"
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class FcmsAuditEntityAttribute : Attribute
{
    public string ActionPrefix { get; }

    public FcmsAuditEntityAttribute(string actionPrefix)
    {
        ActionPrefix = actionPrefix;
    }
}

/// <summary>
/// Excludes an entity class from automatic audit logging by
/// <see cref="FcmsAuditInterceptor"/>. Use for append-only infrastructure
/// entities (audit logs themselves, sessions, background queue entries)
/// that produce infinite recursion or noise if auto-logged.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class FcmsAuditIgnoreEntityAttribute : Attribute { }
