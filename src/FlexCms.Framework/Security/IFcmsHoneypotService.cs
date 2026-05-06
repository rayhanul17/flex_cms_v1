namespace FlexCms.Framework.Security;

/// <summary>
/// Bot-trap helper. The form renders a CSS-hidden field
/// (<see cref="FieldName"/>) that real users never see; bots filling all
/// inputs trip the trap. Posts that arrive with a non-empty value should be
/// rejected silently (BadRequest) — never echo a "you're a bot" message back
/// or the bot operator iterates faster.
/// </summary>
public interface IFcmsHoneypotService
{
    /// <summary>Hidden form field name. Default <c>fcms_hp</c>.</summary>
    string FieldName { get; }

    /// <summary>True when the submitted form should pass (the honeypot field is empty / absent).</summary>
    bool IsLegit(IDictionary<string, Microsoft.Extensions.Primitives.StringValues> form);
}
