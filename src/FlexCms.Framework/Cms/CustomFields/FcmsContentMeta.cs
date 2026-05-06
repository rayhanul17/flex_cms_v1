using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Cms.CustomFields;

/// <summary>
/// Generic key/value meta storage attached to any entity (page, post,
/// module-defined). Type-aware via <see cref="ValueType"/> — the helper
/// methods on <see cref="ICustomFieldService"/> deserialize accordingly.
/// </summary>
public class FcmsContentMeta : BaseEfEntity
{
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }

    public string Key { get; set; } = "";

    /// <summary>One of: <c>string</c>, <c>int</c>, <c>decimal</c>, <c>bool</c>, <c>datetime</c>, <c>json</c>.</summary>
    public string ValueType { get; set; } = "string";

    /// <summary>Value as a string. JSON-serialized for non-primitives.</summary>
    public string Value { get; set; } = "";
}
