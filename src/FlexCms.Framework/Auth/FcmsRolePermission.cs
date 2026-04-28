using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Auth;

public class FcmsRolePermission : BaseEfEntity
{
    public Guid RoleId { get; set; }
    public string PermissionKey { get; set; } = "";
}
