using FlexCms.Framework.Db.Ef;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace FlexCms.Framework.Auth.Ef;

public class EfUserStore : UserStore<FcmsUser, FcmsRole, FcmsDbContext, Guid>
{
    public EfUserStore(FcmsDbContext context, IdentityErrorDescriber? describer = null)
        : base(context, describer) { }
}
