using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Auth;

public class FcmsPermission : BaseEfEntity
{
    public string Key { get; set; } = "";         // e.g. "users.create"
    public string Group { get; set; } = "";       // e.g. "Users" — for accordion grouping
    public string DisplayName { get; set; } = ""; // e.g. "Create Users"
}
