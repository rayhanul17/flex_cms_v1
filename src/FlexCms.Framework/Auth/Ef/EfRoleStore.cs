using FlexCms.Framework.Db.Ef;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace FlexCms.Framework.Auth.Ef;

public class EfRoleStore : RoleStore<FcmsRole, FcmsDbContext, Guid>
{
    public EfRoleStore(FcmsDbContext context, IdentityErrorDescriber? describer = null)
        : base(context, describer) { }
}
