using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.I18n;

/// <summary>
/// Admin-managed list of available languages. Phase 7 only supported the
/// hard-coded EN + BN pair via <c>SupportedLanguages</c>; Phase 15 / Issue
/// 98 lets the operator add Arabic / Hindi / etc. + flag RTL languages so
/// the theme can flip <c>dir="rtl"</c> automatically.
///
/// <para>
/// <see cref="Code"/> is the BCP-47 language tag — <c>en</c>, <c>bn</c>,
/// <c>ar</c>, <c>fr-CA</c>. Routing already respects the slug prefix
/// (Phase 7), and this just controls which prefixes are valid.
/// </para>
/// </summary>
public class FcmsLanguage : BaseEfEntity
{
    /// <summary>BCP-47 tag — used in URLs (<c>/{code}/posts/...</c>) + culture.</summary>
    public string Code { get; set; } = "";

    /// <summary>Native name shown in the language switcher (e.g. <c>বাংলা</c>, <c>العربية</c>).</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Right-to-left script — theme flips <c>dir="rtl"</c> + RTL CSS.</summary>
    public bool IsRtl { get; set; }

    /// <summary>Inactive languages are kept for history but excluded from the public switcher.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}
