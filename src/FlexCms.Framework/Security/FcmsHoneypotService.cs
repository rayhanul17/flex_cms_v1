using Microsoft.Extensions.Primitives;

namespace FlexCms.Framework.Security;

public sealed class FcmsHoneypotService : IFcmsHoneypotService
{
    public string FieldName => "fcms_hp";

    public bool IsLegit(IDictionary<string, StringValues> form)
    {
        if (form is null || !form.TryGetValue(FieldName, out var v)) return true;
        return string.IsNullOrEmpty(v.ToString());
    }
}
